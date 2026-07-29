using System.Diagnostics;
using System.IO;

namespace SpectraGrab.Services;

public interface IHeadlessBrowserService
{
    bool IsAvailable { get; }
    string? BrowserPath { get; }
    Task<string?> RenderDomAsync(string url, CancellationToken cancellationToken);
}

public sealed class HeadlessBrowserService : IHeadlessBrowserService
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(30);

    public HeadlessBrowserService()
    {
        BrowserPath = FindBrowser();
    }

    public bool IsAvailable => BrowserPath is not null;

    public string? BrowserPath { get; }

    public async Task<string?> RenderDomAsync(string url, CancellationToken cancellationToken)
    {
        if (BrowserPath is null
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var profileDirectory = Path.Combine(Path.GetTempPath(), $"SpectraGrab-headless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(profileDirectory);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RenderTimeout);

            var startInfo = new ProcessStartInfo
            {
                FileName = BrowserPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--headless=new");
            startInfo.ArgumentList.Add("--disable-gpu");
            startInfo.ArgumentList.Add("--disable-extensions");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--no-default-browser-check");
            startInfo.ArgumentList.Add($"--user-data-dir={profileDirectory}");
            startInfo.ArgumentList.Add("--virtual-time-budget=8000");
            startInfo.ArgumentList.Add("--dump-dom");
            startInfo.ArgumentList.Add(uri.AbsoluteUri);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return null;
            }

            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
                await process.WaitForExitAsync(timeoutCts.Token);
                var html = await stdoutTask;
                _ = await stderrTask;

                return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(html)
                    ? html
                    : null;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return null;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Browser cleanup is best-effort.
            }
            catch (UnauthorizedAccessException)
            {
                // Browser cleanup is best-effort.
            }
        }
    }

    private static string? FindBrowser()
    {
        var configured = Environment.GetEnvironmentVariable("SPECTRAGRAB_BROWSER_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe")
        };

        var absoluteMatch = candidates.FirstOrDefault(File.Exists);
        if (absoluteMatch is not null)
        {
            return absoluteMatch;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var executable in new[] { "msedge.exe", "chrome.exe", "chromium.exe" })
            {
                try
                {
                    var candidate = Path.Combine(directory, executable);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        return null;
    }
}
