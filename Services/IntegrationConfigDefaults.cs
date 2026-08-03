using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

public static class IntegrationConfigDefaults
{
    public const int CurrentSchemaVersion = 2;

    public static IReadOnlyList<ProviderConfig> LoadProviderDefaults()
    {
        var fallbacks = new List<ProviderConfig>
        {
            Provider("huggingface", "Hugging Face automation", new JsonObject
            {
                ["endpoint"] = "https://router.huggingface.co/v1/chat/completions",
                ["model"] = AutomatedMediaService.Model,
                ["credentialEnvironmentVariable"] = "SPECTRAGRAB_HF_TOKEN",
                ["credentialRequired"] = false
            }),
            Provider("theporndb", "ThePornDB", new JsonObject
            {
                ["baseUrl"] = "https://api.theporndb.net",
                ["credentialEnvironmentVariable"] = "SPECTRAGRAB_TPDB_API_KEY",
                ["credentialRequired"] = true,
                ["posterDownload"] = true
            }),
            Provider("stashdb", "StashDB", new JsonObject
            {
                ["endpoint"] = "https://stashdb.org/graphql",
                ["credentialEnvironmentVariable"] = "SPECTRAGRAB_STASHDB_API_KEY",
                ["credentialRequired"] = true,
                ["posterDownload"] = true
            }),
            Provider("extractor-metadata", "Extractor metadata", new JsonObject
            {
                ["source"] = "yt-dlp",
                ["credentialRequired"] = false,
                ["enabledForAllSites"] = true
            }),
            Provider("extractor-thumbnail", "Extractor thumbnail", new JsonObject
            {
                ["source"] = "yt-dlp",
                ["credentialRequired"] = false,
                ["posterFallback"] = true
            })
        };

        return LoadPackaged("providers", fallbacks);
    }

    public static IReadOnlyList<PluginConfig> LoadPluginDefaults(ISitePluginCatalog sitePluginCatalog)
    {
        var fallbacks = new List<PluginConfig>
        {
            Plugin("emby", "Emby", new JsonObject
            {
                ["baseUrl"] = "http://localhost:8096",
                ["credentialEnvironmentVariable"] = "SPECTRAGRAB_EMBY_API_KEY",
                ["syncEnabled"] = true,
                ["enabledAtStartup"] = true
            }),
            Plugin("jellyfin", "Jellyfin", new JsonObject
            {
                ["baseUrl"] = "http://localhost:8096",
                ["credentialEnvironmentVariable"] = "SPECTRAGRAB_JELLYFIN_API_KEY",
                ["syncEnabled"] = true,
                ["enabledAtStartup"] = true
            }),
            Plugin("plex", "Plex", new JsonObject
            {
                ["baseUrl"] = "http://localhost:32400",
                ["credentialEnvironmentVariable"] = "SPECTRAGRAB_PLEX_TOKEN",
                ["syncEnabled"] = true,
                ["enabledAtStartup"] = true
            }),
            Plugin("localai", "Local AI", new JsonObject
            {
                ["endpoint"] = "http://localhost:8080",
                ["model"] = "default",
                ["enabledForMetadata"] = true,
                ["enabledAtStartup"] = true
            }),
            Plugin("quickconnect", "QuickConnect", new JsonObject
            {
                ["baseUrl"] = "https://quickconnect.to",
                ["serverId"] = string.Empty,
                ["enabledAtStartup"] = true
            })
        };

        var packaged = LoadPackaged("plugins", fallbacks).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var profile in sitePluginCatalog.Profiles)
        {
            if (packaged.ContainsKey(profile.Id))
            {
                continue;
            }

            packaged[profile.Id] = Plugin(profile.Id, profile.Name, new JsonObject
            {
                ["kind"] = "site-extractor",
                ["requiresCookies"] = profile.RequiresCookies,
                ["description"] = profile.Description,
                ["ytDlpArguments"] = new JsonArray(profile.YtdlpArguments
                    .Select(argument => (JsonNode?)JsonValue.Create(argument))
                    .ToArray())
            });
        }

        return packaged.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ProviderConfig Provider(string id, string name, JsonObject settings) => new()
    {
        Id = id,
        Name = name,
        Settings = settings
    };

    private static PluginConfig Plugin(string id, string name, JsonObject settings) => new()
    {
        Id = id,
        Name = name,
        Settings = settings
    };

    private static IReadOnlyList<T> LoadPackaged<T>(string kind, IReadOnlyList<T> fallbacks) where T : class
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ConfigDefaults", kind);
        if (!Directory.Exists(directory))
        {
            return fallbacks;
        }

        try
        {
            var loaded = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions))
                .Where(item => item is not null)
                .Cast<T>()
                .ToList();
            return loaded.Count == 0 ? fallbacks : loaded;
        }
        catch (Exception)
        {
            return fallbacks;
        }
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
