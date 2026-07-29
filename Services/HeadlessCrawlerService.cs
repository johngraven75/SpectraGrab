using HtmlAgilityPack;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

public sealed partial class HeadlessCrawlerService : ICrawlerService
{
    private static readonly string[] CandidateAttributes =
    [
        "href", "src", "data-src", "data-lazy-src", "data-video", "data-video-url", "data-url", "data-file",
        "data-source", "data-stream", "data-hls", "data-m3u8", "data-mpd", "data-mp4", "poster", "content"
    ];

    private readonly CrawlerService staticCrawler;
    private readonly IHeadlessBrowserService browser;

    public HeadlessCrawlerService(CrawlerService staticCrawler, IHeadlessBrowserService browser)
    {
        this.staticCrawler = staticCrawler;
        this.browser = browser;
    }

    public async Task<IReadOnlyList<DiscoveredMedia>> CrawlAsync(string url, int depth, CancellationToken cancellationToken)
    {
        var staticResultsTask = staticCrawler.CrawlAsync(url, depth, cancellationToken);
        var renderedHtmlTask = browser.RenderDomAsync(url, cancellationToken);

        await Task.WhenAll(staticResultsTask, renderedHtmlTask);

        var merged = staticResultsTask.Result
            .ToDictionary(item => item.Url, StringComparer.OrdinalIgnoreCase);

        var html = renderedHtmlTask.Result;
        if (!string.IsNullOrWhiteSpace(html) && Uri.TryCreate(url, UriKind.Absolute, out var sourcePage))
        {
            MergeRenderedDom(merged, html, sourcePage);
        }

        return merged.Values
            .OrderBy(item => MediaPriority(item.Type))
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void MergeRenderedDom(Dictionary<string, DiscoveredMedia> results, string html, Uri sourcePage)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        foreach (var node in document.DocumentNode.Descendants())
        {
            foreach (var attributeName in CandidateAttributes)
            {
                AddCandidate(results, sourcePage, node.GetAttributeValue(attributeName, string.Empty));
            }

            var srcset = node.GetAttributeValue("srcset", string.Empty);
            foreach (var part in srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddCandidate(results, sourcePage, part.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
            }
        }

        var decoded = WebUtility.HtmlDecode(html);
        foreach (Match match in RenderedMediaUrlRegex().Matches(decoded))
        {
            AddCandidate(results, sourcePage, match.Groups["url"].Value);
        }

        foreach (Match match in PlayerConfigRegex().Matches(decoded))
        {
            AddCandidate(results, sourcePage, match.Groups["url"].Value);
        }
    }

    private static void AddCandidate(Dictionary<string, DiscoveredMedia> results, Uri sourcePage, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var cleaned = value.Trim().Trim('"', '\'')
            .Replace("\\/", "/")
            .Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);

        if (!Uri.TryCreate(sourcePage, cleaned, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !IsMedia(uri))
        {
            return;
        }

        results.TryAdd(uri.AbsoluteUri, new DiscoveredMedia
        {
            Title = BuildTitle(uri),
            Url = uri.AbsoluteUri,
            Type = DetectType(uri),
            SourcePage = sourcePage.AbsoluteUri
        });
    }

    private static bool IsMedia(Uri uri)
    {
        var path = uri.AbsolutePath;
        var extension = Path.GetExtension(path);
        if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".avi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mpd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ism", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".opus", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".srt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var query = uri.Query;
        return query.Contains("m3u8", StringComparison.OrdinalIgnoreCase)
            || query.Contains("manifest", StringComparison.OrdinalIgnoreCase)
            || query.Contains(".mpd", StringComparison.OrdinalIgnoreCase)
            || query.Contains(".mp4", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectType(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return extension switch
        {
            ".mp3" or ".aac" or ".m4a" or ".opus" or ".flac" or ".wav" => "audio",
            ".srt" or ".vtt" => "subtitle",
            ".m3u8" or ".mpd" or ".m3u" or ".ism" => "manifest",
            _ when uri.Query.Contains("m3u8", StringComparison.OrdinalIgnoreCase)
                || uri.Query.Contains("manifest", StringComparison.OrdinalIgnoreCase)
                || uri.Query.Contains("mpd", StringComparison.OrdinalIgnoreCase) => "manifest",
            _ => "video"
        };
    }

    private static string BuildTitle(Uri uri)
    {
        var file = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(file) ? uri.Host : Uri.UnescapeDataString(file);
    }

    private static int MediaPriority(string type) => type switch
    {
        "video" => 0,
        "manifest" => 1,
        "audio" => 2,
        "subtitle" => 3,
        _ => 4
    };

    [GeneratedRegex("""(?<url>https?:\\?/\\?/[^\"'\\s<>]+(?:m3u8|mpd|mp4|webm|m4v|ts)(?:\\?[^\"'\\s<>]*)?)""", RegexOptions.IgnoreCase)]
    private static partial Regex RenderedMediaUrlRegex();

    [GeneratedRegex("""(?:file|src|source|videoUrl|video_url|stream|streamUrl|stream_url|hls|hlsUrl|hls_url|manifest|manifestUrl|manifest_url|playlist|playlistUrl|contentUrl)\\s*[:=]\\s*[\"'](?<url>https?:[^\"']+)[\"']""", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerConfigRegex();
}
