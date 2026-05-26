namespace SpectraGrab.Services;

public sealed record CodecProfile(
    string Id,
    string Name,
    string VideoEncoder,
    string Container,
    bool RequiresTranscode);

public sealed record AudioCodecProfile(
    string Id,
    string Name,
    string AudioEncoder);

public interface ICodecProfileCatalog
{
    IReadOnlyList<CodecProfile> VideoCodecs { get; }
    IReadOnlyList<AudioCodecProfile> AudioCodecs { get; }
    CodecProfile ResolveVideo(string id);
    AudioCodecProfile ResolveAudio(string id);
}

public sealed class CodecProfileCatalog : ICodecProfileCatalog
{
    public IReadOnlyList<CodecProfile> VideoCodecs { get; } =
    [
        new("copy", "Original stream / no recode", "copy", "mp4", false),
        new("h264", "H.264 / AVC", "libx264", "mp4", true),
        new("h265", "H.265 / HEVC", "libx265", "mp4", true),
        new("av1", "AV1", "libsvtav1", "mkv", true),
        new("vp9", "VP9", "libvpx-vp9", "webm", true)
    ];

    public IReadOnlyList<AudioCodecProfile> AudioCodecs { get; } =
    [
        new("copy", "Original audio", "copy"),
        new("aac", "AAC", "aac"),
        new("opus", "Opus", "libopus"),
        new("mp3", "MP3", "libmp3lame"),
        new("flac", "FLAC", "flac")
    ];

    public CodecProfile ResolveVideo(string id)
    {
        return VideoCodecs.FirstOrDefault(codec => codec.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? VideoCodecs[0];
    }

    public AudioCodecProfile ResolveAudio(string id)
    {
        return AudioCodecs.FirstOrDefault(codec => codec.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? AudioCodecs[0];
    }
}
