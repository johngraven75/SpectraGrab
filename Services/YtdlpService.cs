using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

public interface IYtdlpService
{
    Task<VideoMetadata> InspectAsync(string url, DownloadOptions options, CancellationToken cancellationToken);
    Task DownloadAsync(DownloadItem item, string outputFolder, string format, DownloadOptions options, CancellationToken cancellationToken);
    bool IsReady { get; }
    string Status { get; }
}

public sealed partial class YtdlpService(IToolLocator toolLocator, ISitePluginCatalog pluginCatalog, ICodecProfileCatalog codecProfiles) : IYtdlpService
{
    private readonly string? ytDlpPath = toolLocator.Find("yt-dlp.exe") ?? toolLocator.Find("yt-dlp");
    private readonly string? ffmpegPath = toolLocator.Find("ffmpeg.exe") ?? toolLocator.Find("ffmpeg");

    public bool IsReady => ytDlpPath is not null;

    public string Status => ytDlpPath is null
        ? "yt-dlp was not found. Install yt-dlp to enable real downloads."
        : $"yt-dlp ready at {ytDlpPath}";

    public async Task<VideoMetadata> InspectAsync(string url, DownloadOptions options, CancellationToken cancellationToken)
    {
        if (ytDlpPath is null)
        {
            return FallbackMetadata(url, "yt-dlp is missing");
        }

        var args = BuildSiteArgs(url, options);
        args.AddRange(["--dump-single-json", "--no-warnings", "--no-playlist", url]);
        string json;
        try
        {
            json = await RunCaptureAsync(ytDlpPath, args, cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsCookieDatabaseCopyError(ex.Message))
        {
            throw new InvalidOperationException(BuildCookieHelpMessage(options.CookieBrowser));
        }
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var title = root.GetPropertyOrDefault("title", "Untitled media");
        var uploader = root.GetPropertyOrDefault("uploader", root.GetPropertyOrDefault("channel", "Unknown source"));
        var thumbnail = root.GetPropertyOrDefault("thumbnail", string.Empty);
        var site = root.GetPropertyOrDefault("extractor_key", root.GetPropertyOrDefault("webpage_url_domain", "Direct"));
        var duration = FormatDuration(root.TryGetProperty("duration", out var durationNode) && durationNode.TryGetDouble(out var seconds) ? seconds : null);

        var formats = new List<MediaFormat>
        {
            new("best", "Best quality", "auto", "best video + audio", "maximum", "Smart default", null),
            new("bestvideo+bestaudio/best", "Best video + best audio", "auto", "lossless merge", "maximum", "DASH/HLS aware", null),
            new("bestaudio/best", "Audio only", "mp3", "best audio", "audio", "Extract audio", null)
        };

        if (root.TryGetProperty("formats", out var formatsNode) && formatsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var format in formatsNode.EnumerateArray().Take(120))
            {
                var id = format.GetPropertyOrDefault("format_id", string.Empty);
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var ext = format.GetPropertyOrDefault("ext", "auto");
                var note = format.GetPropertyOrDefault("format_note", format.GetPropertyOrDefault("format", "Stream"));
                var resolution = format.GetPropertyOrDefault("resolution", BuildResolution(format));
                var videoCodec = format.GetPropertyOrDefault("vcodec", string.Empty);
                var audioCodec = format.GetPropertyOrDefault("acodec", string.Empty);
                var codec = string.Join(" / ", new[] { videoCodec, audioCodec }.Where(value => !string.IsNullOrWhiteSpace(value) && value != "none"));
                var size = GetSize(format);
                formats.Add(new MediaFormat(id, $"{resolution} {ext}".Trim(), ext, string.IsNullOrWhiteSpace(codec) ? "unknown" : codec, resolution, note, size));
            }
        }

        return new VideoMetadata
        {
            Url = url,
            Title = title,
            Uploader = uploader,
            Duration = duration,
            ThumbnailUrl = thumbnail,
            Site = site,
            Formats = formats
        };
    }

    public async Task DownloadAsync(DownloadItem item, string outputFolder, string format, DownloadOptions options, CancellationToken cancellationToken)
    {
        if (ytDlpPath is null)
        {
            throw new InvalidOperationException("yt-dlp was not found.");
        }

        Directory.CreateDirectory(outputFolder);
        var outputTemplate = Path.Combine(outputFolder, "%(extractor)s", "%(uploader,Unknown)s_%(title).180s_%(resolution)s.%(ext)s");
        var args = BuildSiteArgs(item.Url, options);
        var videoCodec = codecProfiles.ResolveVideo(options.VideoCodecId);
        var audioCodec = codecProfiles.ResolveAudio(options.AudioCodecId);
        args.AddRange(
        [
            "--newline",
            "--progress",
            "--continue",
            "--retries",
            "10",
            "--fragment-retries",
            "10",
            "--extractor-retries",
            "5",
            "--concurrent-fragments",
            options.AdultSiteMode ? "6" : "3",
            "--embed-thumbnail",
            "--write-subs",
            "--write-auto-subs",
            "--output",
            outputTemplate,
            "--format",
            NormalizeFormat(format),
            "--merge-output-format",
            videoCodec.Container,
            item.Url
        ]);

        if (videoCodec.RequiresTranscode || audioCodec.AudioEncoder != "copy")
        {
            args.InsertRange(args.Count - 1, BuildCodecArgs(videoCodec, audioCodec, options.Quality));
        }

        if (ffmpegPath is not null)
        {
            args.InsertRange(0, ["--ffmpeg-location", ffmpegPath]);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        item.Status = "Downloading";
        process.Start();

        while (!process.StandardOutput.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                continue;
            }

            ApplyProgress(item, line);
        }

        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            item.Status = "Failed";
            if (IsCookieDatabaseCopyError(error))
            {
                throw new InvalidOperationException(BuildCookieHelpMessage(options.CookieBrowser));
            }

            throw new InvalidOperationException(error.Trim());
        }

        item.Progress = 100;
        item.Status = "Complete";
        item.Speed = "Done";
        item.Eta = "0s";
        item.OutputPath = outputFolder;
    }

    private List<string> BuildSiteArgs(string url, DownloadOptions options)
    {
        var profile = pluginCatalog.ResolveForUrl(url, options.SitePluginId);
        var args = new List<string>();
        if (options.AdultSiteMode)
        {
            args.AddRange(
            [
                "--referer",
                url,
                "--add-header",
                "User-Agent:Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36",
                "--add-header",
                "Accept-Language:en-US,en;q=0.9"
            ]);
        }

        args.AddRange(profile.YtdlpArguments);

        if (!string.IsNullOrWhiteSpace(options.CookieFilePath) && File.Exists(options.CookieFilePath))
        {
            args.AddRange(["--cookies", options.CookieFilePath]);
        }
        else if (options.UseBrowserCookies || profile.RequiresCookies)
        {
            args.AddRange(["--cookies-from-browser", options.CookieBrowser.Trim().ToLowerInvariant()]);
        }

        if (options.AllowInsecureCertificates)
        {
            args.Add("--no-check-certificate");
        }

        return args;
    }

    private static bool IsCookieDatabaseCopyError(string message)
    {
        return message.Contains("Could not copy Chrome cookie database", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) && message.Contains("Cookies", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCookieHelpMessage(string browser)
    {
        return $"Could not read {browser} cookies because the browser has the cookie database locked. Close all {browser} windows and try again, choose Edge/Firefox cookies, or export cookies to a Netscape cookies.txt file and enter that file path in SpectraGrab.";
    }

    private static List<string> BuildCodecArgs(CodecProfile videoCodec, AudioCodecProfile audioCodec, int quality)
    {
        var ffmpegArgs = new List<string>();
        if (videoCodec.RequiresTranscode)
        {
            ffmpegArgs.AddRange(["-c:v", videoCodec.VideoEncoder]);
            if (videoCodec.Id is "h264" or "h265")
            {
                ffmpegArgs.AddRange(["-crf", QualityToCrf(quality).ToString(CultureInfo.InvariantCulture), "-preset", "medium"]);
            }
            else if (videoCodec.Id == "av1")
            {
                ffmpegArgs.AddRange(["-crf", QualityToAv1Crf(quality).ToString(CultureInfo.InvariantCulture), "-preset", "8"]);
            }
            else if (videoCodec.Id == "vp9")
            {
                ffmpegArgs.AddRange(["-crf", QualityToVp9Crf(quality).ToString(CultureInfo.InvariantCulture), "-b:v", "0"]);
            }
        }
        else
        {
            ffmpegArgs.AddRange(["-c:v", "copy"]);
        }

        if (audioCodec.AudioEncoder == "copy")
        {
            ffmpegArgs.AddRange(["-c:a", "copy"]);
        }
        else
        {
            ffmpegArgs.AddRange(["-c:a", audioCodec.AudioEncoder]);
            if (audioCodec.Id is "aac" or "mp3")
            {
                ffmpegArgs.AddRange(["-b:a", QualityToAudioBitrate(quality)]);
            }
        }

        return ["--recode-video", videoCodec.Container, "--postprocessor-args", "ffmpeg:" + string.Join(' ', ffmpegArgs)];
    }

    private static int QualityToCrf(int quality) => Math.Clamp(35 - (quality / 5), 14, 32);

    private static int QualityToAv1Crf(int quality) => Math.Clamp(48 - (quality / 3), 18, 45);

    private static int QualityToVp9Crf(int quality) => Math.Clamp(55 - (quality / 3), 20, 50);

    private static string QualityToAudioBitrate(int quality)
    {
        var bitrate = Math.Clamp(96 + (quality * 2), 128, 320);
        return $"{bitrate}k";
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

        return format.Equals("Best quality", StringComparison.OrdinalIgnoreCase) ? "bestvideo+bestaudio/best" : format;
    }

    private static async Task<string> RunCaptureAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "yt-dlp could not inspect this URL." : error.Trim());
        }

        return output;
    }

    private static VideoMetadata FallbackMetadata(string url, string reason) => new()
    {
        Url = url,
        Title = "URL ready",
        Uploader = reason,
        Duration = "Unknown",
        Site = new Uri(url).Host,
        Formats =
        [
            new("best", "Best quality", "auto", "best", "maximum", reason, null),
            new("bestaudio/best", "Audio only", "mp3", "best audio", "audio", reason, null)
        ]
    };

    private static string BuildResolution(JsonElement format)
    {
        var width = format.TryGetProperty("width", out var widthNode) && widthNode.TryGetInt32(out var w) ? w : 0;
        var height = format.TryGetProperty("height", out var heightNode) && heightNode.TryGetInt32(out var h) ? h : 0;
        return width > 0 && height > 0 ? $"{width}x{height}" : format.GetPropertyOrDefault("format_note", "audio");
    }

    private static long? GetSize(JsonElement format)
    {
        if (format.TryGetProperty("filesize", out var exact) && exact.TryGetInt64(out var exactValue))
        {
            return exactValue;
        }

        return format.TryGetProperty("filesize_approx", out var approx) && approx.TryGetInt64(out var approxValue) ? approxValue : null;
    }

    private static string FormatDuration(double? seconds)
    {
        if (seconds is null)
        {
            return "Unknown";
        }

        var time = TimeSpan.FromSeconds(seconds.Value);
        return time.Hours > 0 ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static void ApplyProgress(DownloadItem item, string line)
    {
        var match = ProgressRegex().Match(line);
        if (match.Success && double.TryParse(match.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            item.Progress = Math.Clamp(percent, 0, 100);
        }

        var speedMatch = SpeedRegex().Match(line);
        if (speedMatch.Success)
        {
            item.Speed = speedMatch.Groups["speed"].Value.Trim();
        }

        var etaMatch = EtaRegex().Match(line);
        if (etaMatch.Success)
        {
            item.Eta = etaMatch.Groups["eta"].Value.Trim();
        }
    }

    [GeneratedRegex(@"\[download\]\s+(?<percent>\d+(?:\.\d+)?)%")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"\sat\s+(?<speed>[^\s]+/s)")]
    private static partial Regex SpeedRegex();

    [GeneratedRegex(@"\sETA\s+(?<eta>[0-9:\-]+)")]
    private static partial Regex EtaRegex();
}

internal static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string name, string fallback)
    {
        return element.TryGetProperty(name, out var node) && node.ValueKind != JsonValueKind.Null ? node.ToString() : fallback;
    }
}
