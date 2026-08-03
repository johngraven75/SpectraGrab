using CommunityToolkit.Mvvm.ComponentModel;

namespace SpectraGrab.Models;

public sealed record CapturePreset(
    string Id,
    string Name,
    string Extension,
    string Muxer,
    string Description);

public sealed record CaptureRequest(
    string SourceUrl,
    string OutputFolder,
    string PresetId);

public sealed record CaptureProgress(
    string OutputPath,
    TimeSpan Elapsed,
    long BytesWritten,
    string Speed);

public sealed record CaptureResult(
    string OutputPath,
    TimeSpan Duration,
    long BytesWritten);

public sealed partial class CaptureSession : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string SourceUrl { get; init; }

    public required string PresetName { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;

    [ObservableProperty]
    private string outputPath = string.Empty;

    [ObservableProperty]
    private string status = "Starting";

    [ObservableProperty]
    private string elapsed = "00:00:00";

    [ObservableProperty]
    private string size = "0 B";

    [ObservableProperty]
    private string speed = "-";
}
