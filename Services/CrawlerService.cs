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
    private static readonly string[] MediaExtensions =
    [
        ".mp4", ".mkv", ".webm", ".mov", ".avi", ".flv", ".m4v", ".f4v", ".ts", ".m3u8", ".mpd", ".m3u", ".ism",
        ".mp3", ".aac", ".m4a", ".opus", ".flac", ".wav", ".srt", ".vtt", ".jpg", ".jpeg", ".png", ".webp", ".gif"
    ];

    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<IReadOnlyList<DiscoveredMedia>> CrawlAsync(string url, int depth, CancellationToken cancellationToken)
    {
        var root = new Uri(url);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(Uri Url, int Level)>();
        var results = new Dictionary<string, DiscoveredMedia>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, level) = pending.Dequeue();
            if (!visited.Add(current.AbsoluteUri) || level > depth)
            {
                continue;
            }

            string html;
            try
            {
                html = await httpClient.GetStringAsync(current, cancellationToken);
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
                if (!IsMedia(link) && level < depth && link.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase))
                {
                    pending.Enqueue((link, level + 1));
                }
            }

            foreach (var link in ExtractEmbeddedMediaLinks(html, current))
            {
                AddMediaResult(results, link, current);
            }

            await Task.Delay(350, cancellationToken);
        }

        return results.Values.OrderBy(item => item.Type).ThenBy(item => item.Title).ToList();
    }

    private static IEnumerable<Uri> ExtractLinks(HtmlDocument document, Uri baseUri)
    {
        var attributes = new[] { "href", "src", "data-src", "data-video", "data-url", "data-hls", "data-mp4", "poster", "content" };
        foreach (var node in document.DocumentNode.Descendants())
        {
            foreach (var attribute in attributes)
            {
                var value = node.GetAttributeValue(attribute, string.Empty);
                if (TryCreateUri(baseUri, value, out var uri))
                {
                    yield return uri;
                }
            }
        }
    }

    private static IEnumerable<Uri> ExtractEmbeddedMediaLinks(string html, Uri baseUri)
    {
        foreach (Match match in EmbeddedMediaRegex().Matches(WebUtility.HtmlDecode(html)))
        {
            var value = match.Groups["url"].Value.Replace("\\/", "/");
            if (TryCreateUri(baseUri, value, out var uri))
            {
                yield return uri;
            }
        }
    }

    private static void AddMediaResult(Dictionary<string, DiscoveredMedia> results, Uri link, Uri sourcePage)
    {
        if (!IsMedia(link))
        {
            return;
        }

        results.TryAdd(link.AbsoluteUri, new DiscoveredMedia
        {
            Title = Path.GetFileName(link.LocalPath),
            Url = link.AbsoluteUri,
            Type = DetectType(link),
            SourcePage = sourcePage.AbsoluteUri
        });
    }

    private static bool TryCreateUri(Uri baseUri, string value, out Uri uri)
    {
        uri = baseUri;
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Uri.TryCreate(baseUri, value.Trim(), out uri!);
    }

    private static bool IsMedia(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        return MediaExtensions.Any(path.EndsWith);
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
            _ => "video"
        };
    }

    [GeneratedRegex(@"(?<url>https?:\\/\\/[^""'\s<>]+?\.(?:mp4|mkv|webm|mov|avi|flv|m4v|f4v|ts|m3u8|mpd|m3u|ism|mp3|aac|m4a|opus|flac|wav|srt|vtt|jpg|jpeg|png|webp|gif)(?:\?[^""'\s<>]*)?)", RegexOptions.IgnoreCase)]
    private static partial Regex EmbeddedMediaRegex();
}
