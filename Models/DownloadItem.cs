using CommunityToolkit.Mvvm.ComponentModel;

namespace SpectraGrab.Models;

public sealed partial class DownloadItem : ObservableObject
{
    [ObservableProperty]
    private string title = "Waiting for media";

    [ObservableProperty]
    private string url = string.Empty;

    [ObservableProperty]
    private string thumbnailUrl = string.Empty;

    [ObservableProperty]
    private string format = "Best quality";

    [ObservableProperty]
    private string status = "Queued";

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string speed = "-";

    [ObservableProperty]
    private string eta = "-";

    [ObservableProperty]
    private string outputPath = string.Empty;
}
