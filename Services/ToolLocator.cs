using System.Diagnostics;
using System.IO;

namespace SpectraGrab.Services;

public interface IToolLocator
{
    string? Find(string executableName);
}

public sealed class ToolLocator : IToolLocator
{
    public string? Find(string executableName)
    {
        var besideApplication = Path.Combine(AppContext.BaseDirectory, executableName);
        if (File.Exists(besideApplication))
        {
            return besideApplication;
        }

        var direct = RunWhere(executableName);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetLink = Path.Combine(localAppData, "Microsoft", "WinGet", "Links", executableName);
        return File.Exists(wingetLink) ? wingetLink : null;
    }

    private static string? RunWhere(string executableName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                ArgumentList = { executableName },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit(2500);
            return process?.StandardOutput.ReadLine();
        }
        catch
        {
            return null;
        }
    }
}
