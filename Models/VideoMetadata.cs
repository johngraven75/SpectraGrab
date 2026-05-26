namespace SpectraGrab.Models;

public sealed class VideoMetadata
{
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = "Untitled media";
    public string Uploader { get; init; } = "Unknown source";
    public string Duration { get; init; } = "Unknown";
    public string ThumbnailUrl { get; init; } = string.Empty;
    public string Site { get; init; } = "Direct";
    public IReadOnlyList<MediaFormat> Formats { get; init; } = [];
}
