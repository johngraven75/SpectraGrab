using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpectraGrab.Models;
using SpectraGrab.Services;

namespace SpectraGrab.ViewModels;

public sealed partial class MainViewModel(IYtdlpService downloader, IAutomatedMediaService automation, ICrawlerService crawler, ISitePluginCatalog pluginCatalog, ICodecPackService codecPack, ICodecProfileCatalog codecProfiles, IStreamCaptureService captureService, IPersistentConfigService persistentConfigs) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<DownloadItem, CancellationTokenSource> activeDownloadTokens = [];
    private readonly object activeDownloadGate = new();
    private readonly Dictionary<CaptureSession, CancellationTokenSource> activeCaptureTokens = [];
    private readonly object activeCaptureGate = new();
    private bool disposed;

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

    [ObservableProperty]
    private bool adultSiteMode = true;

    [ObservableProperty]
    private bool useBrowserCookies;

    [ObservableProperty]
    private string cookieBrowser = "chrome";

    [ObservableProperty]
    private string cookieFilePath = string.Empty;

    [ObservableProperty]
    private bool allowInsecureCertificates;

    [ObservableProperty]
    private string selectedSitePluginId = "adaptive-hoster";

    [ObservableProperty]
    private string selectedVideoCodecId = "copy";

    [ObservableProperty]
    private string selectedAudioCodecId = "copy";

    [ObservableProperty]
    private int codecQuality = 82;

    [ObservableProperty]
    private string captureUrl = string.Empty;

    [ObservableProperty]
    private string selectedCapturePresetId = "mkv-copy";

    public ObservableCollection<DownloadItem> Queue { get; } = [];
    public ObservableCollection<DiscoveredMedia> CrawlResults { get; } = [];
    public ObservableCollection<CaptureSession> CaptureSessions { get; } = [];
    public ObservableCollection<string> Themes { get; } = ["Midnight Aurora", "Candy Pop", "Forest Morning", "OLED Black", "High Contrast"];
    public ObservableCollection<string> DeviceProfiles { get; } = ["Best quality", "Recommended for PC", "iPhone", "Android", "TV", "Audio only"];
    public ObservableCollection<string> FormatOptions { get; } = ["Best quality", "Best video + best audio", "Audio only"];
    public ObservableCollection<string> CookieBrowsers { get; } = ["chrome", "edge", "firefox", "brave", "vivaldi", "opera"];
    public ObservableCollection<SitePluginProfile> SitePlugins { get; } = new(pluginCatalog.Profiles);
    public ObservableCollection<CodecProfile> VideoCodecs { get; } = new(codecProfiles.VideoCodecs);
    public ObservableCollection<AudioCodecProfile> AudioCodecs { get; } = new(codecProfiles.AudioCodecs);
    public ObservableCollection<CapturePreset> CapturePresets { get; } = new(captureService.Presets);

    public string ToolStatus => downloader.Status;
    public string CodecStatus => codecPack.Status;
    public string CaptureToolStatus => captureService.Status;
    public string IntegrationConfigStatus => persistentConfigs.Status;
    public int TotalDownloaded => Queue.Count(item => item.Status == "Complete");
    public int ActiveDownloads => Queue.Count(item => IsActiveStatus(item.Status));
    public int ActiveCaptureCount
    {
        get
        {
            lock (activeCaptureGate)
            {
                return activeCaptureTokens.Count;
            }
        }
    }
    public int FoundMediaCount => CrawlResults.Count;

    [RelayCommand]
    private async Task InspectAsync()
    {
        if (!IsHttpUrl(Url))
        {
            StatusMessage = "Enter a full HTTP or HTTPS URL first.";
            return;
        }

        await RunBusyAsync(async token =>
        {
            StatusMessage = "Inspecting media streams with yt-dlp...";
            CurrentMetadata = await downloader.InspectAsync(Url, CurrentDownloadOptions(), token);
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
        NotifyQueueCounts();
        StatusMessage = "Added to queue.";
    }

    [RelayCommand]
    private async Task DownloadCurrentAsync()
    {
        if (CurrentMetadata is null)
        {
            if (!IsHttpUrl(Url))
            {
                StatusMessage = "Enter a full HTTP or HTTPS URL first.";
                return;
            }

            await RunBusyAsync(async token =>
            {
                StatusMessage = "AI automation is inspecting and planning the submitted link...";
                CurrentMetadata = await downloader.InspectAsync(Url, CurrentDownloadOptions(), token);
                SelectedFormat = CurrentMetadata.Formats.FirstOrDefault()?.Label ?? "Best quality";
            });
            if (CurrentMetadata is null)
            {
                return;
            }
        }

        AddCurrentToQueue();
        await StartQueueAsync();
    }

    [RelayCommand]
    private async Task StartQueueAsync()
    {
        var items = Queue.Where(item => item.Status is "Queued" or "Paused" or "Failed" or "CAPTCHA required").ToList();
        if (items.Count == 0)
        {
            StatusMessage = "No queued downloads to start.";
            return;
        }

        await RunBusyAsync(async token =>
        {
            using var limiter = new SemaphoreSlim(Math.Clamp(ParallelDownloads, 1, 10));
            var tasks = items.Select(item => RunDownloadItemAsync(item, limiter, token));
            StatusMessage = "Download queue running.";
            await Task.WhenAll(tasks);
            StatusMessage = "Queue finished.";
        });
    }

    private async Task RunDownloadItemAsync(DownloadItem item, SemaphoreSlim limiter, CancellationToken queueToken)
    {
        await limiter.WaitAsync(queueToken);
        CancellationTokenSource? itemToken = null;
        try
        {
            itemToken = CancellationTokenSource.CreateLinkedTokenSource(queueToken);
            lock (activeDownloadGate)
            {
                activeDownloadTokens[item] = itemToken;
            }

            var aiPlan = await automation.PlanAsync(item.Url, itemToken.Token);
            item.Status = "AI planned · downloading";
            await downloader.DownloadAsync(item, OutputFolder, item.Format, CurrentDownloadOptions(), itemToken.Token);
            item.Status = "Verifying metadata and poster";
            var automationResult = await automation.FinalizeAsync(item, itemToken.Token);
            item.Status = "Complete";
            StatusMessage = automationResult.Warnings.Count == 0
                ? $"AI download complete: metadata and poster verified with {automationResult.AiModel}."
                : $"Download complete with metadata warnings: {string.Join("; ", automationResult.Warnings)}";
            _ = aiPlan;
        }
        catch (OperationCanceledException) when (itemToken?.IsCancellationRequested == true && !queueToken.IsCancellationRequested)
        {
            item.Status = "Paused";
            item.Speed = "Paused";
            item.Eta = "—";
        }
        catch (OperationCanceledException) when (queueToken.IsCancellationRequested)
        {
            item.Status = "Cancelled";
            throw;
        }
        catch (Exception ex)
        {
            if (item.Status != "CAPTCHA required" && item.Status != "DRM protected")
            {
                item.Status = "Failed";
            }
            StatusMessage = ex.Message;
        }
        finally
        {
            lock (activeDownloadGate)
            {
                activeDownloadTokens.Remove(item);
            }
            itemToken?.Dispose();
            limiter.Release();
            NotifyQueueCounts();
        }
    }

    [RelayCommand]
    private void PauseSelected(DownloadItem? item)
    {
        if (item is null)
        {
            return;
        }

        CancellationTokenSource? token;
        lock (activeDownloadGate)
        {
            activeDownloadTokens.TryGetValue(item, out token);
        }

        if (token is null)
        {
            if (item.Status == "Queued")
            {
                item.Status = "Paused";
                StatusMessage = $"Paused {item.Title}.";
            }
            else
            {
                StatusMessage = $"{item.Title} is not actively downloading.";
            }
            return;
        }

        token.Cancel();
        StatusMessage = $"Pausing {item.Title}...";
    }

    [RelayCommand]
    private void RemoveSelected(DownloadItem? item)
    {
        if (item is null)
        {
            return;
        }

        lock (activeDownloadGate)
        {
            if (activeDownloadTokens.TryGetValue(item, out var token))
            {
                token.Cancel();
            }
        }

        Queue.Remove(item);
        StatusMessage = "Removed queue item.";
        NotifyQueueCounts();
    }

    [RelayCommand]
    private async Task CrawlAsync()
    {
        if (!IsHttpUrl(CrawlerUrl))
        {
            StatusMessage = "Enter a full HTTP or HTTPS crawler URL first.";
            return;
        }

        await RunBusyAsync(async token =>
        {
            StatusMessage = "Deep-crawling pages, embeds, manifests, and direct media...";
            CrawlResults.Clear();
            var results = await crawler.CrawlAsync(CrawlerUrl, Math.Clamp(CrawlDepth, 0, 8), token);
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
        NotifyQueueCounts();
        StatusMessage = "Crawler result added to queue.";
    }

    [RelayCommand]
    private void AddAllCrawlResults()
    {
        var existing = Queue.Select(item => item.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var media in CrawlResults)
        {
            if (!existing.Add(media.Url))
            {
                continue;
            }

            Queue.Add(new DownloadItem
            {
                Title = media.Title,
                Url = media.Url,
                Format = media.Type == "audio" ? "Audio only" : "Best quality",
                Status = "Queued"
            });
            added++;
        }

        NotifyQueueCounts();
        StatusMessage = $"Added {added} new discovered items to the queue.";
    }

    [RelayCommand]
    private async Task StartCaptureAsync()
    {
        if (!IsHttpUrl(CaptureUrl))
        {
            StatusMessage = "Enter a full HTTP or HTTPS stream URL before starting capture.";
            return;
        }

        if (!captureService.IsReady)
        {
            StatusMessage = captureService.Status;
            return;
        }

        var preset = CapturePresets.FirstOrDefault(item => item.Id.Equals(SelectedCapturePresetId, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            StatusMessage = "Choose a valid capture preset.";
            return;
        }

        var session = new CaptureSession
        {
            SourceUrl = CaptureUrl.Trim(),
            PresetName = preset.Name
        };
        var sessionToken = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        lock (activeCaptureGate)
        {
            activeCaptureTokens.Add(session, sessionToken);
        }

        CaptureSessions.Insert(0, session);
        OnPropertyChanged(nameof(ActiveCaptureCount));
        StatusMessage = $"Starting live capture with {preset.Name}.";

        var progress = new Progress<CaptureProgress>(update =>
        {
            session.OutputPath = update.OutputPath;
            session.Elapsed = FormatElapsed(update.Elapsed);
            session.Size = FormatBytes(update.BytesWritten);
            session.Speed = update.Speed;
            session.Status = "Capturing";
        });

        try
        {
            var result = await captureService.CaptureAsync(
                new CaptureRequest(session.SourceUrl, OutputFolder, preset.Id),
                progress,
                sessionToken.Token);
            session.OutputPath = result.OutputPath;
            session.Elapsed = FormatElapsed(result.Duration);
            session.Size = FormatBytes(result.BytesWritten);
            session.Speed = "Complete";
            session.Status = "Complete";
            StatusMessage = $"Capture complete: {Path.GetFileName(result.OutputPath)}";
        }
        catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
        {
            session.Status = string.IsNullOrWhiteSpace(session.OutputPath) ? "Stopped" : "Stopped · output finalized";
            session.Speed = "Stopped";
            StatusMessage = "Capture stopped safely.";
        }
        catch (Exception ex)
        {
            session.Status = "Failed";
            session.Speed = "-";
            StatusMessage = ex.Message;
        }
        finally
        {
            lock (activeCaptureGate)
            {
                activeCaptureTokens.Remove(session);
            }
            sessionToken.Dispose();
            OnPropertyChanged(nameof(ActiveCaptureCount));
        }
    }

    [RelayCommand]
    private void StopCapture(CaptureSession? session)
    {
        if (session is null)
        {
            return;
        }

        CancellationTokenSource? token;
        lock (activeCaptureGate)
        {
            activeCaptureTokens.TryGetValue(session, out token);
        }

        if (token is null)
        {
            StatusMessage = "That capture is not running.";
            return;
        }

        session.Status = "Stopping";
        token.Cancel();
        StatusMessage = "Stopping capture and finalizing its output file...";
    }

    [RelayCommand]
    private void RemoveCapture(CaptureSession? session)
    {
        if (session is null)
        {
            return;
        }

        lock (activeCaptureGate)
        {
            if (activeCaptureTokens.ContainsKey(session))
            {
                StatusMessage = "Stop the active capture before removing it from the list.";
                return;
            }
        }

        CaptureSessions.Remove(session);
        StatusMessage = "Capture entry removed. The output file was not deleted.";
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
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            StatusMessage = "Operation cancelled.";
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

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsActiveStatus(string status) =>
        status.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase)
        || status.StartsWith("Trying discovered", StringComparison.OrdinalIgnoreCase)
        || status.Equals("HLS fallback", StringComparison.OrdinalIgnoreCase);

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var display = (double)value;
        var unit = 0;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        return $"{display:0.##} {units[unit]}";
    }

    private void NotifyQueueCounts()
    {
        OnPropertyChanged(nameof(TotalDownloaded));
        OnPropertyChanged(nameof(ActiveDownloads));
    }

    private DownloadOptions CurrentDownloadOptions() => new(
        AdultSiteMode,
        UseBrowserCookies,
        CookieBrowser,
        CookieFilePath,
        AllowInsecureCertificates,
        SelectedSitePluginId,
        SelectedVideoCodecId,
        SelectedAudioCodecId,
        CodecQuality);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        lock (activeDownloadGate)
        {
            foreach (var token in activeDownloadTokens.Values)
            {
                token.Cancel();
            }
        }
        lock (activeCaptureGate)
        {
            foreach (var token in activeCaptureTokens.Values)
            {
                token.Cancel();
            }
        }
        lifetime.Dispose();
    }
}
