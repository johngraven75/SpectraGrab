using System.Diagnostics;
using System.IO;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

/// <summary>
/// Adds resilient HLS handling around the primary yt-dlp service.
/// The primary service remains responsible for normal extractor-backed downloads,
/// format selection, codecs, and post-processing. This wrapper adds an FFmpeg-backed
/// HLS fallback for direct/adaptive streams and normalizes DRM failures into a clear
/// user-facing error instead of repeatedly retrying an unsupported protected stream.
/// </summary>
public sealed class HlsYtdlpService(
    YtdlpService inner,
    IToolLocator toolLocator) : IYtdlpService
{
    private readonly string? ytDlpPath = toolLocator.Find("yt-dlp.exe") ?? toolLocator.Find("yt-dlp");
    private readonly string? ffmpegPath = toolLocator.Find("ffmpeg.exe") ?? toolLocator.Find("ffmpeg");

    public bool IsReady => inner.IsReady;

    public string Status => inner.Status + (ffmpegPath is null
        ? " | FFmpeg not found: native HLS remains available, but FFmpeg HLS fallback is disabled."
        : " | HLS: native fragments + FFmpeg fallback ready.");

    public async Task<VideoMetadata> InspectAsync(string url, DownloadOptions options, CancellationToken cancellationToken)
    {
        try
        {
            return await inner.InspectAsync(url, options, cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsDrmFailure(ex.Message))
        {
            throw BuildDrmException(ex);
        }
    }

    public async Task DownloadAsync(
        DownloadItem item,
        string outputFolder,
        string format,
        DownloadOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            await inner.DownloadAsync(item, outputFolder, format, options, cancellationToken);
            return;
        }
        catch (InvalidOperationException ex) when (IsDrmFailure(ex.Message))
        {
            item.Status = "DRM protected";
            throw BuildDrmException(ex);
        }
        catch (InvalidOperationException ex) when (ShouldTryHlsFallback(item.Url, ex.Message))
        {
            if (ytDlpPath is null || ffmpegPath is null)
            {
                throw new InvalidOperationException(
                    "The primary HLS download failed and FFmpeg fallback is unavailable. Install FFmpeg and retry. " + ex.Message,
                    ex);
            }

            item.Status = "HLS fallback";
            await DownloadWithFfmpegHlsAsync(item, outputFolder, format, options, cancellationToken);
        }
    }

    private async Task DownloadWithFfmpegHlsAsync(
        DownloadItem item,
        string outputFolder,
        string format,
        DownloadOptions options,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);
        var outputTemplate = Path.Combine(
            outputFolder,
            "%(extractor)s",
            "%(uploader,Unknown)s_%(title).180s_%(resolution)s.%(ext)s");

        var args = new List<string>
        {
            "--ffmpeg-location", ffmpegPath!,
            "--newline",
            "--progress",
            "--continue",
            "--retries", "20",
            "--fragment-retries", "20",
            "--extractor-retries", "8",
            "--retry-sleep", "fragment:exp=1:20",
            "--concurrent-fragments", "4",
            "--hls-prefer-ffmpeg",
            "--downloader", "m3u8:ffmpeg",
            "--downloader", "m3u8_native:ffmpeg",
            "--write-subs",
            "--write-auto-subs",
            "--output", outputTemplate,
            "--format", NormalizeFormat(format),
            "--merge-output-format", "mp4"
        };

        AddAuthorizedSessionArgs(args, item.Url, options);
        args.Add(item.Url);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        item.Status = "Downloading HLS";
        process.Start();

        var stdoutTask = PumpProgressAsync(process.StandardOutput, item, cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, process.WaitForExitAsync(cancellationToken));
        var error = await stderrTask;

        if (process.ExitCode != 0)
        {
            if (IsDrmFailure(error))
            {
                item.Status = "DRM protected";
                throw BuildDrmException(new InvalidOperationException(error.Trim()));
            }

            item.Status = "Failed";
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "FFmpeg-backed HLS fallback failed."
                    : error.Trim());
        }

        item.Progress = 100;
        item.Status = "Complete";
        item.Speed = "Done";
        item.Eta = "0s";
        item.OutputPath = outputFolder;
    }

    private static async Task PumpProgressAsync(
        StreamReader reader,
        DownloadItem item,
        CancellationToken cancellationToken)
    {
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // yt-dlp's normal output is still useful during live HLS recording even
            // when a percentage is unavailable. Keep status visible rather than
            // presenting the queue as stalled.
            if (line.Contains("[download]", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = "Downloading HLS";
            }
        }
    }

    private static void AddAuthorizedSessionArgs(List<string> args, string url, DownloadOptions options)
    {
        args.AddRange([
            "--referer", url,
            "--add-header", "User-Agent:Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36",
            "--add-header", "Accept:application/vnd.apple.mpegurl,application/x-mpegURL,video/*;q=0.9,*/*;q=0.8",
            "--add-header", "Accept-Language:en-US,en;q=0.9"
        ]);

        if (!string.IsNullOrWhiteSpace(options.CookieFilePath) && File.Exists(options.CookieFilePath))
        {
            args.AddRange(["--cookies", options.CookieFilePath]);
        }
        else if (options.UseBrowserCookies)
        {
            args.AddRange(["--cookies-from-browser", options.CookieBrowser.Trim().ToLowerInvariant()]);
        }

        if (options.AllowInsecureCertificates)
        {
            args.Add("--no-check-certificate");
        }
    }

    private static bool ShouldTryHlsFallback(string url, string error)
    {
        if (IsDrmFailure(error))
        {
            return false;
        }

        return IsLikelyHlsUrl(url)
            || error.Contains("m3u8", StringComparison.OrdinalIgnoreCase)
            || error.Contains("HLS", StringComparison.OrdinalIgnoreCase)
            || error.Contains("fragment", StringComparison.OrdinalIgnoreCase)
            || error.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase)
            || error.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Unable to download video data", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Unable to open resource", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyHlsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Contains("m3u8", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Contains("manifest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDrmFailure(string message)
    {
        return message.Contains("DRM", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Widevine", StringComparison.OrdinalIgnoreCase)
            || message.Contains("PlayReady", StringComparison.OrdinalIgnoreCase)
            || message.Contains("FairPlay", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SAMPLE-AES", StringComparison.OrdinalIgnoreCase)
            || message.Contains("encrypted media", StringComparison.OrdinalIgnoreCase)
            || message.Contains("This video is DRM protected", StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidOperationException BuildDrmException(Exception innerException)
    {
        return new InvalidOperationException(
            "DRM-protected stream detected. SpectraGrab will not bypass Widevine, FairPlay, PlayReady, SAMPLE-AES DRM, license servers, or access controls. " +
            "Use an unprotected stream or an authorized offline-download method supplied by the content provider.",
            innerException);
    }

    private static string NormalizeFormat(string format)
    {
        if (format.Contains("Audio", StringComparison.OrdinalIgnoreCase))
        {
            return "bestaudio/best";
        }

        if (format.Contains("video + best audio", StringComparison.OrdinalIgnoreCase))
        {
            return "bestvideo+bestaudio/best";
        }

        return format.Equals("Best quality", StringComparison.OrdinalIgnoreCase)
            ? "bestvideo+bestaudio/best"
            : format;
    }
}
