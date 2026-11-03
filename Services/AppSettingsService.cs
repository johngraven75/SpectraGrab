using System.IO;
using System.Text.Json;

namespace SpectraGrab.Services;

/// <summary>
/// Small, honestly-wired app settings. These are the only Settings-tab
/// toggles that do real work: robots.txt, crawler delay, clipboard
/// monitoring, off-peak scheduled queue starts, and portable tool storage.
/// Controls that had no implementation behind them were removed from the
/// Settings tab rather than left as decorative checkboxes.
/// </summary>
public interface IAppSettingsService
{
    bool RespectRobotsTxt { get; set; }
    int CrawlerDelayMilliseconds { get; set; }
    bool MonitorClipboard { get; set; }
    bool ScheduledDownloads { get; set; }
    bool PortableTools { get; set; }
    void Save();
}

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly string settingsPath;
    private readonly object gate = new();
    private AppSettings settings = new();

    public AppSettingsService()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("SPECTRAGRAB_CONFIG_ROOT");
        var configRoot = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpectraGrab", "config")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(overrideRoot));
        settingsPath = Path.Combine(configRoot, "app-settings.json");
        Load();
    }

    public bool RespectRobotsTxt
    {
        get { lock (gate) { return settings.RespectRobotsTxt; } }
        set { lock (gate) { settings.RespectRobotsTxt = value; } }
    }

    public int CrawlerDelayMilliseconds
    {
        get { lock (gate) { return settings.CrawlerDelayMilliseconds; } }
        set { lock (gate) { settings.CrawlerDelayMilliseconds = Math.Clamp(value, 0, 2000); } }
    }

    public bool MonitorClipboard
    {
        get { lock (gate) { return settings.MonitorClipboard; } }
        set { lock (gate) { settings.MonitorClipboard = value; } }
    }

    public bool ScheduledDownloads
    {
        get { lock (gate) { return settings.ScheduledDownloads; } }
        set { lock (gate) { settings.ScheduledDownloads = value; } }
    }

    public bool PortableTools
    {
        get { lock (gate) { return settings.PortableTools; } }
        set { lock (gate) { settings.PortableTools = value; } }
    }

    public void Save()
    {
        lock (gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                var temporary = settingsPath + ".part";
                File.WriteAllText(temporary, json);
                File.Move(temporary, settingsPath, true);
            }
            catch
            {
                // Settings are a convenience, never a reason to fail boot.
                // The in-memory values remain in effect for this session.
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath));
            if (loaded is not null)
            {
                loaded.CrawlerDelayMilliseconds = Math.Clamp(loaded.CrawlerDelayMilliseconds, 0, 2000);
                settings = loaded;
            }
        }
        catch
        {
            // A corrupt settings file must not block boot; defaults win.
            settings = new AppSettings();
        }
    }

    public sealed class AppSettings
    {
        public bool RespectRobotsTxt { get; set; } = true;
        public int CrawlerDelayMilliseconds { get; set; } = 100;
        public bool MonitorClipboard { get; set; }
        public bool ScheduledDownloads { get; set; }
        public bool PortableTools { get; set; }
    }
}
