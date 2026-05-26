using System.IO;

namespace SpectraGrab.Services;

public interface ICodecPackService
{
    string Status { get; }
}

public sealed class CodecPackService : ICodecPackService
{
    private static readonly string[] KnownKLitePaths =
    [
        @"C:\Program Files (x86)\K-Lite Codec Pack",
        @"C:\Program Files\K-Lite Codec Pack"
    ];

    public string Status
    {
        get
        {
            var installPath = KnownKLitePaths.FirstOrDefault(Directory.Exists);
            return installPath is null
                ? "K-Lite Codec Pack not detected. Optional for playback previews; ffmpeg remains the download/merge engine."
                : $"K-Lite Codec Pack detected at {installPath}. Optional playback codec support is available.";
        }
    }
}
