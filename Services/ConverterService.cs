using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace SpectraGrab.Services;

public sealed record ConverterPreset(string Id, string Name, string Extension);

public interface IConverterService
{
    bool IsReady { get; }
    string Status { get; }
    IReadOnlyList<ConverterPreset> Presets { get; }
    Task<string> ConvertAsync(string inputPath, string presetId, int quality, string outputFolder, CancellationToken cancellationToken);
}

/// <summary>
/// The Converter tab previously had presets and a quality slider with no
/// command behind them. This is the real implementation: ffmpeg transcodes
/// one local file at a time. Drop-queue batch conversion is not claimed.
/// </summary>
public sealed class ConverterService(IToolLocator toolLocator) : IConverterService
{
    private static readonly IReadOnlyList<ConverterPreset> ConverterPresets =
    [
        new("mp4-h264-aac", "MP4 H.264 AAC", ".mp4"),
        new("mkv-h265-opus", "MKV H.265 Opus", ".mkv"),
        new("webm-av1-opus", "WebM AV1 Opus", ".webm"),
        new("gif-clip", "GIF clip", ".gif")
    ];

    public IReadOnlyList<ConverterPreset> Presets => ConverterPresets;

    public bool IsReady => FindFfmpeg() is not null;

    public string Status => IsReady
        ? $"Converter ready at {FindFfmpeg()}."
        : "ffmpeg was not found. Install ffmpeg to enable conversion.";

    private string? FindFfmpeg() => toolLocator.Find("ffmpeg.exe") ?? toolLocator.Find("ffmpeg");

    public async Task<string> ConvertAsync(string inputPath, string presetId, int quality, string outputFolder, CancellationToken cancellationToken)
    {
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException("ffmpeg was not found. Use 'Install missing tools' first.");
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            throw new InvalidOperationException("Choose an existing local file to convert.");
        }

        var preset = ConverterPresets.FirstOrDefault(item => item.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase))
            ?? ConverterPresets[0];
        Directory.CreateDirectory(outputFolder);
        var outputPath = CreateUniqueOutputPath(outputFolder, inputPath, preset.Extension);

        var arguments = new List<string> { "-hide_banner", "-loglevel", "warning", "-y", "-i", inputPath };
        if (preset.Id == "gif-clip")
        {
            arguments.AddRange(["-vf", "fps=15,scale=640:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse"]);
        }
        else if (preset.Id == "mkv-h265-opus")
        {
            arguments.AddRange(["-c:v", "libx265", "-crf", QualityToCrf(quality).ToString(CultureInfo.InvariantCulture), "-preset", "medium", "-c:a", "libopus", "-b:a", "192k"]);
        }
        else if (preset.Id == "webm-av1-opus")
        {
            arguments.AddRange(["-c:v", "libaom-av1", "-crf", QualityToAv1Crf(quality).ToString(CultureInfo.InvariantCulture), "-b:v", "0", "-c:a", "libopus", "-b:a", "192k"]);
        }
        else
        {
            arguments.AddRange(["-c:v", "libx264", "-crf", QualityToCrf(quality).ToString(CultureInfo.InvariantCulture), "-preset", "medium", "-c:a", "aac", "-b:a", "192k"]);
        }
        arguments.Add(outputPath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
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
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }

        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error)
                ? "No diagnostic output was returned."
                : string.Join(Environment.NewLine, error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(12));
            throw new InvalidOperationException($"ffmpeg conversion failed with exit code {process.ExitCode}. {detail}");
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            throw new InvalidOperationException("ffmpeg exited without creating a usable converted file.");
        }

        return outputPath;
    }

    private static string CreateUniqueOutputPath(string folder, string inputPath, string extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(inputPath) + "-converted";
        var outputPath = Path.Combine(folder, baseName + extension);
        for (var suffix = 2; File.Exists(outputPath); suffix++)
        {
            outputPath = Path.Combine(folder, $"{baseName}-{suffix}{extension}");
        }
        return outputPath;
    }

    private static int QualityToCrf(int quality) => Math.Clamp(35 - (quality / 5), 14, 32);
    private static int QualityToAv1Crf(int quality) => Math.Clamp(63 - (quality / 2), 20, 50);
}
