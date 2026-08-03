using System.Diagnostics;
using System.Globalization;
using System.IO;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

public interface IStreamCaptureService
{
    bool IsReady { get; }
    string Status { get; }
    IReadOnlyList<CapturePreset> Presets { get; }

    Task<CaptureResult> CaptureAsync(
        CaptureRequest request,
        IProgress<CaptureProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class StreamCaptureService : IStreamCaptureService
{
    private static readonly IReadOnlyList<CapturePreset> CapturePresets =
    [
        new("mkv-copy", "MKV · stream copy", ".mkv", "matroska", "Recommended for resilient live capture without re-encoding."),
        new("mp4-copy", "MP4 · fragmented stream copy", ".mp4", "mp4", "Creates a broadly compatible fragmented MP4 when the source codecs permit it."),
        new("mpegts-copy", "MPEG-TS · stream copy", ".ts", "mpegts", "Keeps transport-stream output for maximum HLS compatibility.")
    ];

    private readonly string? ffmpegPath;

    public StreamCaptureService(IToolLocator toolLocator)
    {
        ffmpegPath = toolLocator.Find("ffmpeg.exe") ?? toolLocator.Find("ffmpeg");
    }

    public bool IsReady => ffmpegPath is not null;

    public string Status => ffmpegPath is null
        ? "FFmpeg was not found. Install or bundle FFmpeg to enable live capture."
        : $"FFmpeg capture ready at {ffmpegPath}";

    public IReadOnlyList<CapturePreset> Presets => CapturePresets;

    public async Task<CaptureResult> CaptureAsync(
        CaptureRequest request,
        IProgress<CaptureProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (ffmpegPath is null)
        {
            throw new InvalidOperationException("FFmpeg was not found. Install FFmpeg or place ffmpeg.exe beside SpectraGrab.exe, then try again.");
        }

        if (!TryGetHttpUri(request.SourceUrl, out var sourceUri))
        {
            throw new ArgumentException("Capture source must be a full HTTP or HTTPS URL.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.OutputFolder))
        {
            throw new ArgumentException("Choose an output folder before starting capture.", nameof(request));
        }

        var preset = CapturePresets.FirstOrDefault(item => item.Id.Equals(request.PresetId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("The selected capture preset is unavailable.", nameof(request));

        var outputFolder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputFolder.Trim()));
        Directory.CreateDirectory(outputFolder);
        var outputPath = CreateUniqueOutputPath(outputFolder, sourceUri, preset.Extension);
        progress?.Report(new CaptureProgress(outputPath, TimeSpan.Zero, 0, "Starting"));

        using var process = CreateProcess(ffmpegPath, sourceUri.AbsoluteUri, outputPath, preset);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("FFmpeg could not be started.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("FFmpeg could not be started. Verify that the executable is available and permitted to run.", ex);
        }

        var progressState = new CaptureProgressState(outputPath);
        var progressTask = ReadProgressAsync(process.StandardOutput, progressState, progress);
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopProcessAsync(process);
            await AwaitReaderTasksAsync(progressTask, errorTask);
            throw new OperationCanceledException(cancellationToken);
        }

        await progressTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            TryDeleteEmptyOutput(outputPath);
            throw new InvalidOperationException(BuildFailureMessage(process.ExitCode, error));
        }

        var bytesWritten = File.Exists(outputPath) ? new FileInfo(outputPath).Length : progressState.BytesWritten;
        if (!File.Exists(outputPath) || bytesWritten <= 0)
        {
            throw new InvalidOperationException("FFmpeg exited without creating a usable capture file.");
        }

        return new CaptureResult(outputPath, progressState.Elapsed, bytesWritten);
    }

    private static Process CreateProcess(string executable, string sourceUrl, string outputPath, CapturePreset preset)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "warning",
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "5",
            "-i", sourceUrl,
            "-map", "0:v?",
            "-map", "0:a?",
            "-c", "copy"
        };

        if (preset.Id == "mp4-copy")
        {
            arguments.AddRange(["-movflags", "+frag_keyframe+empty_moov+default_base_moof"]);
        }

        arguments.AddRange([
            "-progress", "pipe:1",
            "-nostats",
            "-f", preset.Muxer,
            "-n",
            outputPath
        ]);

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        return process;
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        CaptureProgressState state,
        IProgress<CaptureProgress>? progress)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..].Trim();
            switch (key)
            {
                case "out_time" when TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var elapsed):
                    state.Elapsed = elapsed;
                    break;
                case "total_size" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes):
                    state.BytesWritten = Math.Max(0, bytes);
                    break;
                case "speed":
                    state.Speed = string.IsNullOrWhiteSpace(value) ? "-" : value;
                    break;
                case "progress":
                    progress?.Report(new CaptureProgress(state.OutputPath, state.Elapsed, state.BytesWritten, state.Speed));
                    break;
            }
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            await process.StandardInput.WriteLineAsync("q");
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (TimeoutException)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            KillProcessTree(process);
        }
        catch (IOException)
        {
            KillProcessTree(process);
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort after graceful shutdown failed.
        }
    }

    private static async Task AwaitReaderTasksAsync(Task progressTask, Task<string> errorTask)
    {
        try
        {
            await Task.WhenAll(progressTask, errorTask);
        }
        catch (IOException)
        {
            // Process shutdown can close redirected streams before a pending read completes.
        }
    }

    private static string CreateUniqueOutputPath(string folder, Uri sourceUri, string extension)
    {
        var host = string.IsNullOrWhiteSpace(sourceUri.Host) ? "capture" : sourceUri.Host;
        var safeHost = new string(host.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character).ToArray()).Trim('-', '.');
        if (string.IsNullOrWhiteSpace(safeHost))
        {
            safeHost = "capture";
        }

        var baseName = $"{safeHost}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var outputPath = Path.Combine(folder, baseName + extension);
        for (var suffix = 2; File.Exists(outputPath); suffix++)
        {
            outputPath = Path.Combine(folder, $"{baseName}-{suffix}{extension}");
        }

        return outputPath;
    }

    private static bool TryGetHttpUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var candidate)
            && (candidate.Scheme == Uri.UriSchemeHttp || candidate.Scheme == Uri.UriSchemeHttps))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string BuildFailureMessage(int exitCode, string error)
    {
        var detail = string.IsNullOrWhiteSpace(error)
            ? "No diagnostic output was returned."
            : string.Join(Environment.NewLine, error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(12));
        return $"FFmpeg capture failed with exit code {exitCode}. {detail}";
    }

    private static void TryDeleteEmptyOutput(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length == 0)
            {
                File.Delete(outputPath);
            }
        }
        catch
        {
            // Preserve the original FFmpeg failure if cleanup is not possible.
        }
    }

    private sealed class CaptureProgressState(string outputPath)
    {
        public string OutputPath { get; } = outputPath;
        public TimeSpan Elapsed { get; set; }
        public long BytesWritten { get; set; }
        public string Speed { get; set; } = "-";
    }
}
