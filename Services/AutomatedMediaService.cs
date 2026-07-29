using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.Json;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

public sealed record AutomatedMediaResult(
    string Status,
    string? MediaPath,
    bool AdultMedia,
    string? PosterPath,
    string? NfoPath,
    string AiModel,
    string AiPlan,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> Warnings);

public interface IAutomatedMediaService
{
    Task<string> PlanAsync(string url, CancellationToken cancellationToken);
    Task<AutomatedMediaResult> FinalizeAsync(DownloadItem item, CancellationToken cancellationToken);
}

public sealed class AutomatedMediaService : IAutomatedMediaService
{
    public const string Model = "Qwen/Qwen3-4B-Instruct-2507";
    private const long MaxPosterBytes = 25L * 1024L * 1024L;
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<string> PlanAsync(string url, CancellationToken cancellationToken)
    {
        var fallback = JsonSerializer.Serialize(new
        {
            mediaKind = IsManifest(url) ? "adaptive_stream" : "web_media",
            likelyAdult = IsAdult(url),
            metadataPriority = IsAdult(url) ? new[] { "ThePornDB", "StashDB", "embedded", "thumbnail" } : new[] { "embedded", "thumbnail" },
            posterStrategy = "provider_then_extractor_thumbnail",
            protectedContentPolicy = "reject_drm_paywall_access_control_and_automated_captcha_bypass"
        });

        var token = FirstEnvironmentValue("SPECTRAGRAB_HF_TOKEN", "HF_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            return fallback;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://router.huggingface.co/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new
            {
                model = Model,
                messages = new object[]
                {
                    new { role = "system", content = "Classify lawful media downloads. Return compact JSON only. Never recommend DRM, paywall, access-control, credential, or CAPTCHA bypass." },
                    new { role = "user", content = $"Plan a fully automated download, metadata, and poster workflow for: {url}" }
                },
                temperature = 0.1,
                max_tokens = 220
            });
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return fallback;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public async Task<AutomatedMediaResult> FinalizeAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var providers = new List<string>();
        var mediaPath = ResolveMediaPath(item.OutputPath);
        var adult = IsAdult(item.Url) || IsAdult(mediaPath ?? string.Empty);
        var aiPlan = await PlanAsync(item.Url, cancellationToken);

        if (mediaPath is null)
        {
            warnings.Add("The final downloaded file could not be resolved for metadata sidecars.");
            return new("download_complete_metadata_pending", null, adult, null, null, Model, aiPlan, providers, warnings);
        }

        var metadata = new ProviderMetadata(item.Title, null, item.ThumbnailUrl, null, "Adult", null);
        if (adult)
        {
            var tpdbKey = FirstEnvironmentValue("SPECTRAGRAB_TPDB_API_KEY", "TPDB_API_KEY");
            if (!string.IsNullOrWhiteSpace(tpdbKey))
            {
                providers.Add("ThePornDB");
                try
                {
                    metadata = Merge(await FetchTpdbAsync(item.Title, tpdbKey, cancellationToken), metadata);
                }
                catch (Exception ex)
                {
                    warnings.Add($"ThePornDB: {ex.Message}");
                }
            }

            var stashKey = FirstEnvironmentValue("SPECTRAGRAB_STASHDB_API_KEY", "STASHDB_API_KEY");
            if (!string.IsNullOrWhiteSpace(stashKey))
            {
                providers.Add("StashDB");
                try
                {
                    metadata = Merge(await FetchStashDbAsync(item.Title, stashKey, cancellationToken), metadata);
                }
                catch (Exception ex)
                {
                    warnings.Add($"StashDB: {ex.Message}");
                }
            }
        }

        string? posterPath = null;
        if (!string.IsNullOrWhiteSpace(metadata.PosterUrl))
        {
            try
            {
                posterPath = await DownloadPosterAsync(metadata.PosterUrl, mediaPath, cancellationToken);
            }
            catch (Exception ex)
            {
                warnings.Add($"Poster: {ex.Message}");
            }
        }

        var nfoPath = WriteNfo(mediaPath, metadata with { PosterUrl = posterPath });
        return new(
            warnings.Count == 0 ? "verified" : "verified_with_warnings",
            mediaPath,
            adult,
            posterPath,
            nfoPath,
            Model,
            aiPlan,
            providers,
            warnings);
    }

    private async Task<ProviderMetadata> FetchTpdbAsync(string title, string apiKey, CancellationToken token)
    {
        var searchUrl = $"https://api.theporndb.net/scenes?parse={Uri.EscapeDataString(title)}&hash=&year=";
        using var searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        searchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var searchResponse = await client.SendAsync(searchRequest, token);
        searchResponse.EnsureSuccessStatusCode();
        using var search = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync(token));
        var first = search.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault();
        var id = first.ValueKind == JsonValueKind.Object
            ? GetString(first, "uuid") ?? GetString(first, "UUID")
            : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return ProviderMetadata.Empty;
        }

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api.theporndb.net/scenes/{id}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var detailResponse = await client.SendAsync(detailRequest, token);
        detailResponse.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync(token));
        var detail = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
        return new(
            GetString(detail, "title"),
            GetString(detail, "description") ?? GetString(detail, "details"),
            NestedString(detail, "posters", "large") ?? GetString(detail, "poster") ?? NestedString(detail, "background", "large"),
            ParseYear(GetString(detail, "date") ?? GetString(detail, "release_date")),
            JoinTags(detail),
            GetString(detail, "uuid"));
    }

    private async Task<ProviderMetadata> FetchStashDbAsync(string title, string apiKey, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://stashdb.org/graphql");
        request.Headers.Add("ApiKey", apiKey.Trim());
        request.Content = JsonContent.Create(new
        {
            query = "query($title:String!){ queryScenes(input:{title:$title, per_page:1, page:1, direction:DESC, sort:DATE}) { scenes { title details release_date images { url width height } tags { name } } } }",
            variables = new { title }
        });
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var scenes = document.RootElement.GetProperty("data").GetProperty("queryScenes").GetProperty("scenes");
        var scene = scenes.EnumerateArray().FirstOrDefault();
        if (scene.ValueKind != JsonValueKind.Object)
        {
            return ProviderMetadata.Empty;
        }

        var poster = scene.TryGetProperty("images", out var images)
            ? images.EnumerateArray().Select(image => GetString(image, "url")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            : null;
        return new(
            GetString(scene, "title"),
            GetString(scene, "details"),
            poster,
            ParseYear(GetString(scene, "release_date")),
            JoinTags(scene),
            null);
    }

    private async Task<string> DownloadPosterAsync(string url, string mediaPath, CancellationToken token)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxPosterBytes)
        {
            throw new InvalidOperationException("poster exceeds the 25 MB safety limit");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        if (bytes.LongLength > MaxPosterBytes || !IsImage(bytes))
        {
            throw new InvalidOperationException("provider response is not a supported image");
        }

        var extension = bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) ? ".png"
            : bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" ? ".webp"
            : ".jpg";
        var posterPath = Path.Combine(Path.GetDirectoryName(mediaPath)!, $"{Path.GetFileNameWithoutExtension(mediaPath)}-poster{extension}");
        var temporary = posterPath + ".part";
        await File.WriteAllBytesAsync(temporary, bytes, token);
        File.Move(temporary, posterPath, true);
        return posterPath;
    }

    private static string WriteNfo(string mediaPath, ProviderMetadata metadata)
    {
        var path = Path.ChangeExtension(mediaPath, ".nfo");
        var xml = $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<movie>
  <title>{Escape(metadata.Title ?? Path.GetFileNameWithoutExtension(mediaPath))}</title>
  <year>{metadata.Year?.ToString() ?? string.Empty}</year>
  <plot>{Escape(metadata.Overview)}</plot>
  <genre>{Escape(metadata.Genre ?? "Adult")}</genre>
  <uniqueid type="adult-provider">{Escape(metadata.ProviderId)}</uniqueid>
  <thumb aspect="poster">{Escape(metadata.PosterUrl)}</thumb>
</movie>
""";
        var temporary = path + ".part";
        File.WriteAllText(temporary, xml, new UTF8Encoding(false));
        File.Move(temporary, path, true);
        return path;
    }

    private static ProviderMetadata Merge(ProviderMetadata incoming, ProviderMetadata fallback) => new(
        incoming.Title ?? fallback.Title,
        incoming.Overview ?? fallback.Overview,
        incoming.PosterUrl ?? fallback.PosterUrl,
        incoming.Year ?? fallback.Year,
        incoming.Genre ?? fallback.Genre,
        incoming.ProviderId ?? fallback.ProviderId);

    private static string? ResolveMediaPath(string value)
    {
        if (File.Exists(value))
        {
            return value;
        }
        if (!Directory.Exists(value))
        {
            return null;
        }

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".webm", ".mov", ".m4v", ".avi", ".wmv", ".ts", ".m2ts"
        };
        return Directory.EnumerateFiles(value, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool IsAdult(string value)
    {
        var lower = value.ToLowerInvariant();
        return new[] { "adult", "porn", "xxx", "nsfw", "xvideos", "xnxx", "pornhub", "redtube", "youporn", "xhamster", "spankbang", "boyfriend.tv" }
            .Any(lower.Contains);
    }

    private static bool IsManifest(string value) =>
        value.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
        || value.Contains(".mpd", StringComparison.OrdinalIgnoreCase);

    private static bool IsImage(byte[] bytes) =>
        bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })
        || bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
        || (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP");

    private static string? FirstEnvironmentValue(params string[] names) =>
        names.Select(Environment.GetEnvironmentVariable).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? GetString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;

    private static string? NestedString(JsonElement value, string parent, string child) =>
        value.TryGetProperty(parent, out var nested) ? GetString(nested, child) : null;

    private static int? ParseYear(string? value) =>
        value is { Length: >= 4 } && int.TryParse(value[..4], out var year) ? year : null;

    private static string? JoinTags(JsonElement value) =>
        value.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
            ? string.Join(", ", tags.EnumerateArray().Select(tag => GetString(tag, "name")).Where(name => !string.IsNullOrWhiteSpace(name)).Take(8))
            : null;

    private static string Escape(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private sealed record ProviderMetadata(string? Title, string? Overview, string? PosterUrl, int? Year, string? Genre, string? ProviderId)
    {
        public static ProviderMetadata Empty { get; } = new(null, null, null, null, null, null);
    }
}
