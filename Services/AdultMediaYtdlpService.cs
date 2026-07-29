using SpectraGrab.Models;

namespace SpectraGrab.Services;

/// <summary>
/// Provider-agnostic adult-media orchestration for public or authorized media.
/// It keeps yt-dlp/HLS as the primary path and falls back to bounded deep-crawl
/// discovery when a page is not directly understood by an extractor.
/// It does not bypass DRM, paywalls, login requirements, or CAPTCHA challenges.
/// </summary>
public sealed class AdultMediaYtdlpService(
    HlsYtdlpService inner,
    ICrawlerService crawler) : IYtdlpService
{
    private const int MaxFallbackCandidates = 12;

    public bool IsReady => inner.IsReady;

    public string Status => inner.Status + " | Adult media: extractor + HLS/DASH + deep-discovery fallback ready.";

    public async Task<VideoMetadata> InspectAsync(
        string url,
        DownloadOptions options,
        CancellationToken cancellationToken)
    {
        var effectiveOptions = NormalizeAdultOptions(options);
        try
        {
            return await inner.InspectAsync(url, effectiveOptions, cancellationToken);
        }
        catch (InvalidOperationException ex) when (effectiveOptions.AdultSiteMode && ShouldTryDiscoveryFallback(ex.Message))
        {
            var candidates = await DiscoverCandidatesAsync(url, 2, cancellationToken);
            foreach (var candidate in candidates)
            {
                try
                {
                    var metadata = await inner.InspectAsync(candidate.Url, effectiveOptions, cancellationToken);
                    return CopyMetadataWithOriginalUrl(metadata, url);
                }
                catch (InvalidOperationException candidateEx) when (ShouldTryNextCandidate(candidateEx.Message))
                {
                    // Try the next discovered playable candidate.
                }
            }

            throw new InvalidOperationException(
                "The provider page was not directly supported and deep discovery did not find an unprotected downloadable media stream. " +
                "Try browser cookies for content you are authorized to access, or use the Crawler tab to review discovered media links.",
                ex);
        }
    }

    public async Task DownloadAsync(
        DownloadItem item,
        string outputFolder,
        string format,
        DownloadOptions options,
        CancellationToken cancellationToken)
    {
        var effectiveOptions = NormalizeAdultOptions(options);
        try
        {
            await inner.DownloadAsync(item, outputFolder, format, effectiveOptions, cancellationToken);
            return;
        }
        catch (InvalidOperationException ex) when (effectiveOptions.AdultSiteMode && ShouldTryDiscoveryFallback(ex.Message))
        {
            var originalUrl = item.Url;
            var candidates = await DiscoverCandidatesAsync(originalUrl, 3, cancellationToken);

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Status = $"Trying discovered {candidate.Type}";
                item.Url = candidate.Url;

                try
                {
                    await inner.DownloadAsync(item, outputFolder, format, effectiveOptions, cancellationToken);
                    item.Url = originalUrl;
                    return;
                }
                catch (InvalidOperationException candidateEx) when (ShouldTryNextCandidate(candidateEx.Message))
                {
                    item.Url = originalUrl;
                }
            }

            item.Url = originalUrl;
            item.Status = "No playable media found";
            throw new InvalidOperationException(
                "SpectraGrab tried the provider extractor, HLS/DASH handling, and bounded deep-crawl discovery but did not find an unprotected downloadable stream. " +
                "Authenticated content requires an authorized browser/cookies.txt session. DRM, paywall, access-control, and CAPTCHA bypass are not performed.",
                ex);
        }
    }

    private async Task<IReadOnlyList<DiscoveredMedia>> DiscoverCandidatesAsync(
        string url,
        int depth,
        CancellationToken cancellationToken)
    {
        var discovered = await crawler.CrawlAsync(url, depth, cancellationToken);
        return discovered
            .Where(item => item.Type is "manifest" or "video" or "audio")
            .OrderBy(item => item.Type == "manifest" ? 0 : item.Type == "video" ? 1 : 2)
            .ThenBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFallbackCandidates)
            .ToList();
    }

    private static DownloadOptions NormalizeAdultOptions(DownloadOptions options)
    {
        if (!options.AdultSiteMode)
        {
            return options;
        }

        // The default adaptive profile remains useful for known hosts, while
        // unknown adult providers benefit from the generic extractor profile.
        return options.SitePluginId.Equals("adaptive-hoster", StringComparison.OrdinalIgnoreCase)
            ? options with { SitePluginId = "generic-adult" }
            : options;
    }

    private static VideoMetadata CopyMetadataWithOriginalUrl(VideoMetadata metadata, string originalUrl) => new()
    {
        Url = originalUrl,
        Title = metadata.Title,
        Uploader = metadata.Uploader,
        Duration = metadata.Duration,
        ThumbnailUrl = metadata.ThumbnailUrl,
        Site = metadata.Site,
        Formats = metadata.Formats
    };

    private static bool ShouldTryDiscoveryFallback(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || IsProtectedOrUserActionFailure(message))
        {
            return false;
        }

        return message.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)
            || message.Contains("No video formats", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unable to extract", StringComparison.OrdinalIgnoreCase)
            || message.Contains("could not find", StringComparison.OrdinalIgnoreCase)
            || message.Contains("video data", StringComparison.OrdinalIgnoreCase)
            || message.Contains("requested format is not available", StringComparison.OrdinalIgnoreCase)
            || message.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("HTTP Error 404", StringComparison.OrdinalIgnoreCase)
            || message.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldTryNextCandidate(string message) =>
        !IsProtectedOrUserActionFailure(message);

    private static bool IsProtectedOrUserActionFailure(string message)
    {
        return message.Contains("DRM", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Widevine", StringComparison.OrdinalIgnoreCase)
            || message.Contains("PlayReady", StringComparison.OrdinalIgnoreCase)
            || message.Contains("FairPlay", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SAMPLE-AES", StringComparison.OrdinalIgnoreCase)
            || message.Contains("captcha", StringComparison.OrdinalIgnoreCase)
            || message.Contains("verify you are human", StringComparison.OrdinalIgnoreCase)
            || message.Contains("human verification", StringComparison.OrdinalIgnoreCase)
            || message.Contains("login required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("sign in", StringComparison.OrdinalIgnoreCase)
            || message.Contains("subscription", StringComparison.OrdinalIgnoreCase)
            || message.Contains("members only", StringComparison.OrdinalIgnoreCase)
            || message.Contains("premium", StringComparison.OrdinalIgnoreCase)
            || message.Contains("paywall", StringComparison.OrdinalIgnoreCase);
    }
}
