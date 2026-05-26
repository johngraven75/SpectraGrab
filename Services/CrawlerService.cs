using HtmlAgilityPack;
using System.IO;
using System.Net.Http;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

public interface ICrawlerService
{
    Task<IReadOnlyList<DiscoveredMedia>> CrawlAsync(string url, int depth, CancellationToken cancellationToken);
}

public sealed class CrawlerService : ICrawlerService
{
    private static readonly string[] MediaExtensions =
    [
        ".mp4", ".mkv", ".webm", ".mov", ".avi", ".flv", ".m3u8", ".mpd",
        ".mp3", ".aac", ".opus", ".flac", ".wav", ".srt", ".vtt", ".jpg", ".jpeg", ".png", ".webp", ".gif"
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
                if (IsMedia(link))
                {
                    results.TryAdd(link.AbsoluteUri, new DiscoveredMedia
                    {
                        Title = Path.GetFileName(link.LocalPath),
                        Url = link.AbsoluteUri,
                        Type = DetectType(link),
                        SourcePage = current.AbsoluteUri
                    });
                }
                else if (level < depth && link.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase))
                {
                    pending.Enqueue((link, level + 1));
                }
            }

            await Task.Delay(350, cancellationToken);
        }

        return results.Values.OrderBy(item => item.Type).ThenBy(item => item.Title).ToList();
    }

    private static IEnumerable<Uri> ExtractLinks(HtmlDocument document, Uri baseUri)
    {
        var attributes = new[] { "href", "src", "data-src", "poster", "content" };
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
            ".mp3" or ".aac" or ".opus" or ".flac" or ".wav" => "audio",
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "image",
            ".srt" or ".vtt" => "subtitle",
            ".m3u8" or ".mpd" => "manifest",
            _ => "video"
        };
    }
}
