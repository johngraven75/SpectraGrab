using HtmlAgilityPack;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

public interface ICrawlerService
{
    Task<IReadOnlyList<DiscoveredMedia>> CrawlAsync(string url, int depth, CancellationToken cancellationToken);
}

public sealed partial class CrawlerService : ICrawlerService
{
    private const int MaxPagesPerCrawl = 250;

    private static readonly string[] MediaExtensions =
    [
        ".mp4", ".mkv", ".webm", ".mov", ".avi", ".flv", ".m4v", ".f4v", ".ts", ".m3u8", ".mpd", ".m3u", ".ism",
        ".mp3", ".aac", ".m4a", ".opus", ".flac", ".wav", ".srt", ".vtt", ".jpg", ".jpeg", ".png", ".webp", ".gif"
    ];

    private static readonly string[] PlayerAttributeNames =
    [
        "href", "src", "data-src", "data-lazy-src", "data-video", "data-video-url", "data-url", "data-file",
        "data-source", "data-stream", "data-hls", "data-m3u8", "data-mpd", "data-mp4", "poster", "content"
    ];

    private readonly HttpClient httpClient = BuildClient();

    public async Task<IReadOnlyList<DiscoveredMedia>> CrawlAsync(string url, int depth, CancellationToken cancellationToken)
    {
        var root = new Uri(url);
        var normalizedDepth = Math.Clamp(depth, 0, 8);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(Uri Url, int Level)>();
        var results = new Dictionary<string, DiscoveredMedia>(StringComparer.OrdinalIgnoreCase);
        Enqueue(pending, queued, root, 0);

        while (pending.Count > 0 && visited.Count < MaxPagesPerCrawl)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, level) = pending.Dequeue();
            if (!visited.Add(NormalizePageKey(current)) || level > normalizedDepth)
            {
                continue;
            }

            string html;
            string? contentType;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Referrer = level == 0 ? null : root;
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                contentType = response.Content.Headers.ContentType?.MediaType;
                if (IsDirectMediaContentType(contentType))
                {
                    AddMediaResult(results, current, current, TypeFromContentType(contentType));
                    continue;
                }

                if (contentType is not null
                    && !contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
                    && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                    && !contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
                    && !contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                html = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch
            {
                continue;
            }

            var document = new HtmlDocument();
            document.LoadHtml(html);

            foreach (var link in ExtractLinks(document, current))
            {
                AddMediaResult(results, link, current);
                if (level < normalizedDepth && ShouldFollowPage(root, link))
                {
                    Enqueue(pending, queued, link, level + 1);
                }
            }

            foreach (var link in ExtractEmbeddedMediaLinks(html, current))
            {
                AddMediaResult(results, link, current);
            }

            foreach (var link in ExtractPlayerConfigLinks(html, current))
            {
                AddMediaResult(results, link, current);
                if (level < normalizedDepth && ShouldFollowPage(root, link) && LooksLikeEmbedPage(link))
                {
                    Enqueue(pending, queued, link, level + 1);
                }
            }

            foreach (var embed in ExtractEmbedPages(document, current))
            {
                if (level < normalizedDepth && ShouldFollowPage(root, embed))
                {
                    Enqueue(pending, queued, embed, level + 1);
                }
            }

            await Task.Delay(150, cancellationToken);
        }

        return results.Values
            .OrderBy(item => MediaPriority(item.Type))
            .ThenBy(item => item.Title)
            .ToList();
    }

    private static HttpClient BuildClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 SpectraGrab/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
        return client;
    }

    private static void Enqueue(Queue<(Uri Url, int Level)> pending, HashSet<string> queued, Uri uri, int level)
    {
        var key = NormalizePageKey(uri);
        if (queued.Add(key))
        {
            pending.Enqueue((uri, level));
        }
    }

    private static IEnumerable<Uri> ExtractLinks(HtmlDocument document, Uri baseUri)
    {
        foreach (var node in document.DocumentNode.Descendants())
        {
            foreach (var attribute in PlayerAttributeNames)
            {
                var value = node.GetAttributeValue(attribute, string.Empty);
                if (TryCreateUri(baseUri, value, out var uri))
                {
                    yield return uri;
                }
            }

            var srcset = node.GetAttributeValue("srcset", string.Empty);
            foreach (var candidate in ParseSrcSet(srcset))
            {
                if (TryCreateUri(baseUri, candidate, out var uri))
                {
                    yield return uri;
                }
            }
        }
    }

    private static IEnumerable<Uri> ExtractEmbedPages(HtmlDocument document, Uri baseUri)
    {
        foreach (var node in document.DocumentNode.SelectNodes("//iframe[@src]|//embed[@src]|//object[@data]") ?? [])
        {
            var value = node.GetAttributeValue("src", node.GetAttributeValue("data", string.Empty));
            if (TryCreateUri(baseUri, value, out var uri))
            {
                yield return uri;
            }
        }
    }

    private static IEnumerable<Uri> ExtractEmbeddedMediaLinks(string html, Uri baseUri)
    {
        var decoded = WebUtility.HtmlDecode(html);
        foreach (Match match in EmbeddedMediaRegex().Matches(decoded))
        {
            var value = UnescapeUrl(match.Groups["url"].Value);
            if (TryCreateUri(baseUri, value, out var uri))
            {
                yield return uri;
            }
        }
    }

    private static IEnumerable<Uri> ExtractPlayerConfigLinks(string html, Uri baseUri)
    {
        var decoded = WebUtility.HtmlDecode(html);
        foreach (Match match in PlayerConfigRegex().Matches(decoded))
        {
            var value = UnescapeUrl(match.Groups["url"].Value);
            if (TryCreateUri(baseUri, value, out var uri))
            {
                yield return uri;
            }
        }

        foreach (Match match in AbsoluteMediaUrlRegex().Matches(decoded))
        {
            var value = UnescapeUrl(match.Groups["url"].Value);
            if (TryCreateUri(baseUri, value, out var uri))
            {
                yield return uri;
            }
        }
    }

    private static IEnumerable<string> ParseSrcSet(string srcset)
    {
        if (string.IsNullOrWhiteSpace(srcset))
        {
            yield break;
        }

        foreach (var part in srcset.Split(','))
        {
            var candidate = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static void AddMediaResult(Dictionary<string, DiscoveredMedia> results, Uri link, Uri sourcePage, string? forcedType = null)
    {
        if (!IsMedia(link) && forcedType is null)
        {
            return;
        }

        var key = link.AbsoluteUri;
        results.TryAdd(key, new DiscoveredMedia
        {
            Title = BuildTitle(link),
            Url = key,
            Type = forcedType ?? DetectType(link),
            SourcePage = sourcePage.AbsoluteUri
        });
    }

    private static string BuildTitle(Uri link)
    {
        var file = Path.GetFileName(link.LocalPath);
        if (!string.IsNullOrWhiteSpace(file))
        {
            return Uri.UnescapeDataString(file);
        }

        return link.Host;
    }

    private static bool TryCreateUri(Uri baseUri, string value, out Uri uri)
    {
        uri = baseUri;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cleaned = UnescapeUrl(value.Trim().Trim('"', '\''));
        if (cleaned.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || cleaned.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || cleaned.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || cleaned.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        return Uri.TryCreate(baseUri, cleaned, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string UnescapeUrl(string value) => value
        .Replace("\\/", "/")
        .Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase)
        .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldFollowPage(Uri root, Uri candidate)
    {
        if (IsMedia(candidate) || candidate.Scheme is not ("http" or "https"))
        {
            return false;
        }

        return IsSameSite(root.Host, candidate.Host)
            || LooksLikeEmbedPage(candidate)
            || LooksVideoOriented(candidate);
    }

    private static bool IsSameSite(string rootHost, string candidateHost)
    {
        rootHost = rootHost.TrimStart('.').ToLowerInvariant();
        candidateHost = candidateHost.TrimStart('.').ToLowerInvariant();
        return candidateHost.Equals(rootHost, StringComparison.OrdinalIgnoreCase)
            || candidateHost.EndsWith("." + rootHost, StringComparison.OrdinalIgnoreCase)
            || rootHost.EndsWith("." + candidateHost, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeEmbedPage(Uri uri)
    {
        var value = uri.AbsoluteUri;
        return value.Contains("embed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("player", StringComparison.OrdinalIgnoreCase)
            || value.Contains("iframe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksVideoOriented(Uri uri)
    {
        var value = uri.AbsolutePath + uri.Query;
        return value.Contains("video", StringComparison.OrdinalIgnoreCase)
            || value.Contains("watch", StringComparison.OrdinalIgnoreCase)
            || value.Contains("view", StringComparison.OrdinalIgnoreCase)
            || value.Contains("clip", StringComparison.OrdinalIgnoreCase)
            || value.Contains("movie", StringComparison.OrdinalIgnoreCase)
            || value.Contains("playlist", StringComparison.OrdinalIgnoreCase)
            || value.Contains("channel", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePageKey(Uri uri)
    {
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static bool IsDirectMediaContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("dash+xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase);
    }

    private static string TypeFromContentType(string contentType)
    {
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return "audio";
        }

        if (contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("dash+xml", StringComparison.OrdinalIgnoreCase))
        {
            return "manifest";
        }

        return "video";
    }

    private static bool IsMedia(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        if (MediaExtensions.Any(path.EndsWith))
        {
            return true;
        }

        var query = uri.Query;
        return query.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || query.Contains(".mpd", StringComparison.OrdinalIgnoreCase)
            || query.Contains(".mp4", StringComparison.OrdinalIgnoreCase)
            || query.Contains("manifest", StringComparison.OrdinalIgnoreCase)
            || query.Contains("playlist", StringComparison.OrdinalIgnoreCase) && query.Contains("m3u", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectType(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return extension switch
        {
            ".mp3" or ".aac" or ".m4a" or ".opus" or ".flac" or ".wav" => "audio",
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "image",
            ".srt" or ".vtt" => "subtitle",
            ".m3u8" or ".mpd" or ".m3u" or ".ism" => "manifest",
            _ when uri.Query.Contains("m3u8", StringComparison.OrdinalIgnoreCase)
                || uri.Query.Contains("manifest", StringComparison.OrdinalIgnoreCase)
                || uri.Query.Contains("mpd", StringComparison.OrdinalIgnoreCase) => "manifest",
            _ => "video"
        };
    }

    private static int MediaPriority(string type) => type switch
    {
        "video" => 0,
        "manifest" => 1,
        "audio" => 2,
        "subtitle" => 3,
        "image" => 4,
        _ => 5
    };

    [GeneratedRegex(@"(?<url>https?:\\/\\/[^\"'\s<>]+?\.(?:mp4|mkv|webm|mov|avi|flv|m4v|f4v|ts|m3u8|mpd|m3u|ism|mp3|aac|m4a|opus|flac|wav|srt|vtt|jpg|jpeg|png|webp|gif)(?:\?[^\"'\s<>]*)?)", RegexOptions.IgnoreCase)]
    private static partial Regex EmbeddedMediaRegex();

    [GeneratedRegex(@"(?:file|src|source|videoUrl|video_url|stream|streamUrl|stream_url|hls|hlsUrl|hls_url|manifest|manifestUrl|manifest_url|playlist|playlistUrl|contentUrl)\s*[:=]\s*[\"'](?<url>https?:[^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerConfigRegex();

    [GeneratedRegex(@"(?<url>https?:\\?/\\?/[^\"'\s<>]+(?:m3u8|mpd|mp4|webm|m4v|ts)(?:\?[^\"'\s<>]*)?)", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteMediaUrlRegex();
}
