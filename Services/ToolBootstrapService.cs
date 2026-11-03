using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace SpectraGrab.Services;

/// <summary>
/// One-click recovery when yt-dlp and/or ffmpeg are missing at boot.
/// Order: winget when it is present (the cleanest Windows path), otherwise
/// a direct download into SpectraGrab's own user-writable bin folder.
/// Nothing is downloaded silently at boot: the UI calls this only when the
/// user presses "Install missing tools". Downloaded binaries are verified
/// by running --version / -version before the install is reported as done.
/// </summary>
public interface IToolBootstrapService
{
    IReadOnlyList<string> MissingTools { get; }
    string Status { get; }
    Task InstallMissingToolsAsync(IProgress<string>? progress, CancellationToken cancellationToken);
}

public sealed class ToolBootstrapService(IToolLocator toolLocator, IAppSettingsService settings) : IToolBootstrapService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    public IReadOnlyList<string> MissingTools
    {
        get
        {
            var missing = new List<string>();
            if (toolLocator.Find("yt-dlp.exe") is null && toolLocator.Find("yt-dlp") is null)
            {
                missing.Add("yt-dlp");
            }
            if (toolLocator.Find("ffmpeg.exe") is null && toolLocator.Find("ffmpeg") is null)
            {
                missing.Add("ffmpeg");
            }
            return missing;
        }
    }

    public string Status => MissingTools.Count == 0
        ? "yt-dlp and ffmpeg are available."
        : $"Missing tools: {string.Join(", ", MissingTools)}. Use 'Install missing tools' to fetch them.";

    private string TargetDirectory => settings.PortableTools
        ? Path.Combine(AppContext.BaseDirectory, "bin")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpectraGrab", "bin");

    public async Task InstallMissingToolsAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var missing = MissingTools;
        if (missing.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(TargetDirectory);
        var winget = toolLocator.Find("winget.exe");

        if (winget is not null)
        {
            foreach (var tool in missing.ToList())
            {
                progress?.Report($"Installing {tool} with winget...");
                var packageId = tool == "yt-dlp" ? "yt-dlp.yt-dlp" : "Gyan.FFmpeg";
                var installed = await RunAsync(winget,
                    ["install", "--id", packageId, "--exact", "--accept-package-agreements", "--accept-source-agreements"],
                    cancellationToken);
                if (!installed)
                {
                    progress?.Report($"Winget could not install {tool}; trying a direct download instead.");
                }
            }
        }

        missing = MissingTools;
        if (missing.Contains("yt-dlp"))
        {
            progress?.Report("Downloading yt-dlp...");
            var destination = Path.Combine(TargetDirectory, "yt-dlp.exe");
            await DownloadFileAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", destination, cancellationToken);
            if (new FileInfo(destination).Length < 100_000)
            {
                File.Delete(destination);
                throw new InvalidOperationException("The downloaded yt-dlp file was unexpectedly small; the download may have failed.");
            }
        }

        missing = MissingTools;
        if (missing.Contains("ffmpeg"))
        {
            progress?.Report("Downloading ffmpeg (essentials build, this can take a while)...");
            var temporaryZip = Path.Combine(Path.GetTempPath(), $"SpectraGrab-ffmpeg-{Guid.NewGuid():N}.zip");
            var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"SpectraGrab-ffmpeg-{Guid.NewGuid():N}");
            try
            {
                await DownloadFileAsync("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip", temporaryZip, cancellationToken);
                ZipFile.ExtractToDirectory(temporaryZip, temporaryDirectory);
                var ffmpeg = Directory.EnumerateFiles(temporaryDirectory, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidOperationException("ffmpeg.exe was not found inside the downloaded ffmpeg archive.");
                File.Copy(ffmpeg, Path.Combine(TargetDirectory, "ffmpeg.exe"), true);
            }
            finally
            {
                try { if (File.Exists(temporaryZip)) File.Delete(temporaryZip); } catch { /* best effort */ }
                try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true); } catch { /* best effort */ }
            }
        }

        missing = MissingTools;
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Still missing after install: {string.Join(", ", missing)}. Install them manually and restart SpectraGrab.");
        }

        // Verify the binaries actually run, not just that files exist.
        await VerifyToolAsync(toolLocator.Find("yt-dlp.exe") ?? toolLocator.Find("yt-dlp")!, ["--version"], cancellationToken);
        await VerifyToolAsync(toolLocator.Find("ffmpeg.exe") ?? toolLocator.Find("ffmpeg")!, ["-version"], cancellationToken);
        progress?.Report("Tools installed and verified.");
    }

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + ".part";
        try
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(temporary);
            await input.CopyToAsync(output, cancellationToken);
            File.Move(temporary, destination, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { /* best effort */ }
        }
    }

    private static async Task<bool> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        try
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
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task VerifyToolAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        if (!await RunAsync(fileName, arguments, timeout.Token))
        {
            throw new InvalidOperationException($"Installed tool did not run successfully: {fileName}");
        }
    }
}
