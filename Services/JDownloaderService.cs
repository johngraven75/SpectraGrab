using System.Net.Http;

namespace SpectraGrab.Services;

public interface IJDownloaderService
{
    Task SendToJDownloaderAsync(string url, CancellationToken cancellationToken);
}

public sealed class JDownloaderService : IJDownloaderService
{
    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public async Task SendToJDownloaderAsync(string url, CancellationToken cancellationToken)
    {
        var endpoint = "http://127.0.0.1:9666/flash/add";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["urls"] = url,
            ["source"] = "SpectraGrab"
        });

        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"JDownloader 2 rejected the link: HTTP {(int)response.StatusCode}.");
        }
    }
}
