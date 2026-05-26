namespace SpectraGrab.Models;

public sealed class DiscoveredMedia
{
    public string Title { get; init; } = "Discovered media";
    public string Url { get; init; } = string.Empty;
    public string Type { get; init; } = "media";
    public string SourcePage { get; init; } = string.Empty;
    public string Size { get; init; } = "Unknown";
}
