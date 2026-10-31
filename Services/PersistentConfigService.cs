using System.IO;
using System.Text.Json;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

public interface IPersistentConfigService
{
    string Status { get; }
    IReadOnlyList<ProviderConfig> Providers { get; }
    IReadOnlyList<PluginConfig> Plugins { get; }
    IntegrationConfigReport EnsureInitialized();
    ProviderConfig LoadProviderConfig(string id);
    PluginConfig LoadPluginConfig(string id);
    void SaveProviderConfig(ProviderConfig config);
    void SavePluginConfig(PluginConfig config);
}

public sealed class PersistentConfigService : IPersistentConfigService
{
    private readonly ISitePluginCatalog sitePluginCatalog;
    private readonly object gate = new();
    private readonly string configRoot;
    private List<ProviderConfig> providers = [];
    private List<PluginConfig> plugins = [];
    private IntegrationConfigReport? lastReport;

    public PersistentConfigService(ISitePluginCatalog sitePluginCatalog)
    {
        this.sitePluginCatalog = sitePluginCatalog;
        var overrideRoot = Environment.GetEnvironmentVariable("SPECTRAGRAB_CONFIG_ROOT");
        configRoot = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpectraGrab", "config")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(overrideRoot));
    }

    public string Status { get; private set; } = "Persistent integration configuration has not been initialized.";

    public IReadOnlyList<ProviderConfig> Providers
    {
        get
        {
            lock (gate)
            {
                return providers.Select(Clone).ToList();
            }
        }
    }

    public IReadOnlyList<PluginConfig> Plugins
    {
        get
        {
            lock (gate)
            {
                return plugins.Select(Clone).ToList();
            }
        }
    }

    public IntegrationConfigReport EnsureInitialized()
    {
        lock (gate)
        {
            if (lastReport is not null)
            {
                return lastReport;
            }

            try
            {
                var providerDirectory = Path.Combine(configRoot, "providers");
                var pluginDirectory = Path.Combine(configRoot, "plugins");
                Directory.CreateDirectory(providerDirectory);
                Directory.CreateDirectory(pluginDirectory);

                var counters = new ConfigCounters();
                providers = IntegrationConfigDefaults.LoadProviderDefaults()
                    .Select(item => LoadProvider(providerDirectory, item, counters))
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                plugins = IntegrationConfigDefaults.LoadPluginDefaults(sitePluginCatalog)
                    .Select(item => LoadPlugin(pluginDirectory, item, counters))
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                lastReport = new IntegrationConfigReport(
                    configRoot,
                    providers.Count,
                    plugins.Count,
                    counters.Created,
                    counters.Upgraded,
                    counters.Repaired);
                Status = $"Verified {providers.Count} provider and {plugins.Count} plugin configs at {configRoot}. "
                    + $"Created {counters.Created}, upgraded {counters.Upgraded}, repaired {counters.Repaired}.";
                return lastReport;
            }
            catch (Exception ex)
            {
                Status = $"Persistent integration configuration verification failed: {ex.Message}";
                throw;
            }
        }
    }

    public ProviderConfig LoadProviderConfig(string id)
    {
        EnsureInitialized();
        lock (gate)
        {
            return Clone(providers.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Unknown provider configuration: {id}."));
        }
    }

    public PluginConfig LoadPluginConfig(string id)
    {
        EnsureInitialized();
        lock (gate)
        {
            return Clone(plugins.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Unknown plugin configuration: {id}."));
        }
    }

    public void SaveProviderConfig(ProviderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureInitialized();
        lock (gate)
        {
            var defaults = IntegrationConfigDefaults.LoadProviderDefaults()
                .FirstOrDefault(item => item.Id.Equals(config.Id, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Unknown provider configuration: {config.Id}.");
            var normalized = Normalize(defaults, config);
            AtomicWrite(Path.Combine(configRoot, "providers", $"{normalized.Id}.json"), normalized);
            Replace(providers, normalized);
        }
    }

    public void SavePluginConfig(PluginConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureInitialized();
        lock (gate)
        {
            var defaults = IntegrationConfigDefaults.LoadPluginDefaults(sitePluginCatalog)
                .FirstOrDefault(item => item.Id.Equals(config.Id, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Unknown plugin configuration: {config.Id}.");
            var normalized = Normalize(defaults, config);
            AtomicWrite(Path.Combine(configRoot, "plugins", $"{normalized.Id}.json"), normalized);
            Replace(plugins, normalized);
        }
    }

    private static ProviderConfig LoadProvider(string directory, ProviderConfig defaults, ConfigCounters counters)
    {
        var path = Path.Combine(directory, $"{defaults.Id}.json");
        if (!File.Exists(path))
        {
            var created = Normalize(defaults, defaults);
            AtomicWrite(path, created);
            counters.Created++;
            return created;
        }

        try
        {
            var original = File.ReadAllText(path);
            var current = JsonSerializer.Deserialize<ProviderConfig>(original, IntegrationConfigDefaults.JsonOptions)
                ?? throw new InvalidDataException($"Provider configuration {defaults.Id} is empty.");
            var normalized = Normalize(defaults, current);
            var serialized = Serialize(normalized);
            if (!JsonEquivalent(original, serialized))
            {
                AtomicWrite(path, normalized);
                counters.Upgraded++;
            }
            return normalized;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            BackupInvalid(path);
            var repaired = Normalize(defaults, defaults);
            AtomicWrite(path, repaired);
            counters.Repaired++;
            return repaired;
        }
    }

    private static PluginConfig LoadPlugin(string directory, PluginConfig defaults, ConfigCounters counters)
    {
        var path = Path.Combine(directory, $"{defaults.Id}.json");
        if (!File.Exists(path))
        {
            var created = Normalize(defaults, defaults);
            AtomicWrite(path, created);
            counters.Created++;
            return created;
        }

        try
        {
            var original = File.ReadAllText(path);
            var current = JsonSerializer.Deserialize<PluginConfig>(original, IntegrationConfigDefaults.JsonOptions)
                ?? throw new InvalidDataException($"Plugin configuration {defaults.Id} is empty.");
            var normalized = Normalize(defaults, current);
            var serialized = Serialize(normalized);
            if (!JsonEquivalent(original, serialized))
            {
                AtomicWrite(path, normalized);
                counters.Upgraded++;
            }
            return normalized;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            BackupInvalid(path);
            var repaired = Normalize(defaults, defaults);
            AtomicWrite(path, repaired);
            counters.Repaired++;
            return repaired;
        }
    }

    private static ProviderConfig Normalize(ProviderConfig defaults, ProviderConfig current)
    {
        if (!defaults.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Provider id {current.Id} does not match {defaults.Id}.");
        }

        var normalized = new ProviderConfig
        {
            SchemaVersion = IntegrationConfigDefaults.CurrentSchemaVersion,
            Id = defaults.Id,
            Name = string.IsNullOrWhiteSpace(current.Name) ? defaults.Name : current.Name,
            Enabled = current.Enabled,
            Version = Math.Max(IntegrationConfigDefaults.CurrentSchemaVersion, current.Version),
            Settings = IntegrationConfigValidator.MergeSettings(defaults.Settings, current.Settings)
        };
        IntegrationConfigValidator.Validate(normalized.Id, normalized.Id, normalized.Name, normalized.Settings);
        return normalized;
    }

    private static PluginConfig Normalize(PluginConfig defaults, PluginConfig current)
    {
        if (!defaults.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Plugin id {current.Id} does not match {defaults.Id}.");
        }

        var normalized = new PluginConfig
        {
            SchemaVersion = IntegrationConfigDefaults.CurrentSchemaVersion,
            Id = defaults.Id,
            Name = string.IsNullOrWhiteSpace(current.Name) ? defaults.Name : current.Name,
            Enabled = current.Enabled,
            Version = Math.Max(IntegrationConfigDefaults.CurrentSchemaVersion, current.Version),
            Settings = IntegrationConfigValidator.MergeSettings(defaults.Settings, current.Settings)
        };
        IntegrationConfigValidator.Validate(normalized.Id, normalized.Id, normalized.Name, normalized.Settings);
        return normalized;
    }

    private static void Replace<T>(List<T> items, T replacement) where T : class
    {
        var replacementId = replacement switch
        {
            ProviderConfig provider => provider.Id,
            PluginConfig plugin => plugin.Id,
            _ => throw new InvalidOperationException("Unsupported integration configuration type.")
        };
        var index = items.FindIndex(item => item switch
        {
            ProviderConfig provider => provider.Id.Equals(replacementId, StringComparison.OrdinalIgnoreCase),
            PluginConfig plugin => plugin.Id.Equals(replacementId, StringComparison.OrdinalIgnoreCase),
            _ => false
        });
        if (index < 0)
        {
            throw new KeyNotFoundException($"Unknown integration configuration: {replacementId}.");
        }
        items[index] = replacement;
    }

    private static void BackupInvalid(string path)
    {
        var backupPath = $"{path}.invalid-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        File.Move(path, backupPath);
    }

    private static void AtomicWrite<T>(string path, T config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Cannot resolve configuration directory for {path}."));
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, Serialize(config));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, IntegrationConfigDefaults.JsonOptions) + Environment.NewLine;

    private static bool JsonEquivalent(string left, string right)
    {
        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonSerializer.Serialize(leftDocument.RootElement)
            .Equals(JsonSerializer.Serialize(rightDocument.RootElement), StringComparison.Ordinal);
    }

    private static ProviderConfig Clone(ProviderConfig config) =>
        JsonSerializer.Deserialize<ProviderConfig>(Serialize(config), IntegrationConfigDefaults.JsonOptions)
        ?? throw new InvalidDataException($"Unable to clone provider configuration {config.Id}.");

    private static PluginConfig Clone(PluginConfig config) =>
        JsonSerializer.Deserialize<PluginConfig>(Serialize(config), IntegrationConfigDefaults.JsonOptions)
        ?? throw new InvalidDataException($"Unable to clone plugin configuration {config.Id}.");

    private sealed class ConfigCounters
    {
        public int Created { get; set; }
        public int Upgraded { get; set; }
        public int Repaired { get; set; }
    }
}
