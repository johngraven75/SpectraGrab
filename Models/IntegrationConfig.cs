using System.Text.Json.Nodes;

namespace SpectraGrab.Models;

public sealed class ProviderConfig
{
    public int SchemaVersion { get; set; } = 2;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Version { get; set; } = 2;
    public JsonObject Settings { get; set; } = [];
}

public sealed class PluginConfig
{
    public int SchemaVersion { get; set; } = 2;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Version { get; set; } = 2;
    public JsonObject Settings { get; set; } = [];
}

public sealed record IntegrationConfigReport(
    string ConfigRoot,
    int ProviderCount,
    int PluginCount,
    int CreatedCount,
    int UpgradedCount,
    int RepairedCount);
