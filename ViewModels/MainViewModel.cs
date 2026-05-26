using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpectraGrab.Models;
using SpectraGrab.Services;

namespace SpectraGrab.ViewModels;

public sealed partial class MainViewModel(IYtdlpService downloader, ICrawlerService crawler) : ObservableObject
{
    private readonly CancellationTokenSource lifetime = new();

    [ObservableProperty]
    private string url = string.Empty;

    [ObservableProperty]
    private string crawlerUrl = string.Empty;

    [ObservableProperty]
    private int crawlDepth = 1;

    [ObservableProperty]
    private string selectedFormat = "Best quality";

    [ObservableProperty]
    private string outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SpectraGrab");

    [ObservableProperty]
    private VideoMetadata? currentMetadata;

    [ObservableProperty]
    private string statusMessage = "Paste a URL to inspect formats, queue a download, or crawl a page for media.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private int parallelDownloads = 2;

    public ObservableCollection<DownloadItem> Queue { get; } = [];
    public ObservableCollection<DiscoveredMedia> CrawlResults { get; } = [];
    public ObservableCollection<string> Themes { get; } = ["Midnight Aurora", "Candy Pop", "Forest Morning", "OLED Black", "High Contrast"];
    public ObservableCollection<string> DeviceProfiles { get; } = ["Best quality", "Recommended for PC", "iPhone", "Android", "TV", "Audio only"];
    public ObservableCollection<string> FormatOptions { get; } = ["Best quality", "Best video + best audio", "Audio only"];

    public string ToolStatus => downloader.Status;
    public int TotalDownloaded => Queue.Count(item => item.Status == "Complete");
    public int ActiveDownloads => Queue.Count(item => item.Status == "Downloading");
    public int FoundMediaCount => CrawlResults.Count;

    [RelayCommand]
    private async Task InspectAsync()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
        {
            StatusMessage = "Enter a full URL first.";
            return;
        }

        await RunBusyAsync(async token =>
        {
            StatusMessage = "Inspecting media streams with yt-dlp...";
            CurrentMetadata = await downloader.InspectAsync(Url, token);
            FormatOptions.Clear();
            foreach (var format in CurrentMetadata.Formats.Take(24).Select(format => format.Label).Distinct())
            {
                FormatOptions.Add(format);
            }

            SelectedFormat = FormatOptions.FirstOrDefault() ?? "Best quality";
            StatusMessage = $"Ready: {CurrentMetadata.Title}";
        });
    }

    [RelayCommand]
    private void AddCurrentToQueue()
    {
        if (CurrentMetadata is null)
        {
            StatusMessage = "Inspect a URL before adding it to the queue.";
            return;
        }

        Queue.Add(new DownloadItem
        {
            Title = CurrentMetadata.Title,
            Url = CurrentMetadata.Url,
            ThumbnailUrl = CurrentMetadata.ThumbnailUrl,
            Format = SelectedFormat,
            Status = "Queued"
        });
        OnPropertyChanged(nameof(ActiveDownloads));
        StatusMessage = "Added to queue.";
    }

    [RelayCommand]
    private async Task DownloadCurrentAsync()
    {
        AddCurrentToQueue();
        await StartQueueAsync();
    }

    [RelayCommand]
    private async Task StartQueueAsync()
    {
        var items = Queue.Where(item => item.Status is "Queued" or "Paused" or "Failed").ToList();
        if (items.Count == 0)
        {
            StatusMessage = "No queued downloads to start.";
            return;
        }

        await RunBusyAsync(async token =>
        {
            using var limiter = new SemaphoreSlim(Math.Clamp(ParallelDownloads, 1, 10));
            var tasks = items.Select(async item =>
            {
                await limiter.WaitAsync(token);
                try
                {
                    await downloader.DownloadAsync(item, OutputFolder, item.Format, token);
                }
                catch (Exception ex)
                {
                    item.Status = "Failed";
                    StatusMessage = ex.Message;
                }
                finally
                {
                    limiter.Release();
                    OnPropertyChanged(nameof(TotalDownloaded));
                    OnPropertyChanged(nameof(ActiveDownloads));
                }
            });

            StatusMessage = "Download queue running.";
            await Task.WhenAll(tasks);
            StatusMessage = "Queue finished.";
        });
    }

    [RelayCommand]
    private void PauseSelected(DownloadItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.Status = "Paused";
        StatusMessage = $"Paused {item.Title}.";
    }

    [RelayCommand]
    private void RemoveSelected(DownloadItem? item)
    {
        if (item is null)
        {
            return;
        }

        Queue.Remove(item);
        StatusMessage = "Removed queue item.";
        OnPropertyChanged(nameof(TotalDownloaded));
        OnPropertyChanged(nameof(ActiveDownloads));
    }

    [RelayCommand]
    private async Task CrawlAsync()
    {
        if (!Uri.TryCreate(CrawlerUrl, UriKind.Absolute, out _))
        {
            StatusMessage = "Enter a full crawler URL first.";
            return;
        }

        await RunBusyAsync(async token =>
        {
            StatusMessage = "Crawling page links and direct media...";
            CrawlResults.Clear();
            var results = await crawler.CrawlAsync(CrawlerUrl, Math.Clamp(CrawlDepth, 0, 4), token);
            foreach (var result in results)
            {
                CrawlResults.Add(result);
            }

            OnPropertyChanged(nameof(FoundMediaCount));
            StatusMessage = $"Crawler found {CrawlResults.Count} media links.";
        });
    }

    [RelayCommand]
    private void AddCrawlResult(DiscoveredMedia? media)
    {
        if (media is null)
        {
            return;
        }

        Queue.Add(new DownloadItem
        {
            Title = media.Title,
            Url = media.Url,
            Format = media.Type == "audio" ? "Audio only" : "Best quality",
            Status = "Queued"
        });
        StatusMessage = "Crawler result added to queue.";
    }

    [RelayCommand]
    private void AddAllCrawlResults()
    {
        foreach (var media in CrawlResults)
        {
            AddCrawlResult(media);
        }

        StatusMessage = $"Added {CrawlResults.Count} discovered items to the queue.";
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> work)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await work(lifetime.Token);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
