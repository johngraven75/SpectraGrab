using System.Diagnostics;
using SpectraGrab.Models;

namespace SpectraGrab.Services;

/// <summary>
/// Detects CAPTCHA / human-verification failures from the media pipeline and
/// performs a safe human-in-the-loop handoff. It never attempts to solve,
/// bypass, or defeat a CAPTCHA. Instead, it opens the source page in the
/// user's default browser so the challenge can be completed normally, then
/// the existing browser-cookie flow can be used on retry.
/// </summary>
public sealed class CaptchaAwareYtdlpService(HlsYtdlpService inner) : IYtdlpService
{
    public bool IsReady => inner.IsReady;

    public string Status => inner.Status + " | CAPTCHA: detection + browser handoff ready.";

    public async Task<VideoMetadata> InspectAsync(
        string url,
        DownloadOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inner.InspectAsync(url, options, cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsCaptchaFailure(ex.Message))
        {
            OpenForHumanVerification(url);
            throw BuildCaptchaException(url, options, ex);
        }
    }

    public async Task DownloadAsync(
        DownloadItem item,
        string outputFolder,
        string format,
        DownloadOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            await inner.DownloadAsync(item, outputFolder, format, options, cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsCaptchaFailure(ex.Message))
        {
            item.Status = "CAPTCHA required";
            item.Speed = "User action";
            item.Eta = "—";
            OpenForHumanVerification(item.Url);
            throw BuildCaptchaException(item.Url, options, ex);
        }
    }

    private static bool IsCaptchaFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("captcha", StringComparison.OrdinalIgnoreCase)
            || message.Contains("verify you are human", StringComparison.OrdinalIgnoreCase)
            || message.Contains("human verification", StringComparison.OrdinalIgnoreCase)
            || message.Contains("challenge page", StringComparison.OrdinalIgnoreCase)
            || message.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cf-chl", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cloudflare challenge", StringComparison.OrdinalIgnoreCase)
            || message.Contains("checking your browser", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase)
            || message.Contains("robot check", StringComparison.OrdinalIgnoreCase)
            || message.Contains("recaptcha", StringComparison.OrdinalIgnoreCase)
            || message.Contains("hcaptcha", StringComparison.OrdinalIgnoreCase)
            || message.Contains("turnstile", StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenForHumanVerification(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // Browser launch is convenience only. The actionable error still
            // tells the user how to complete verification manually.
        }
    }

    private static InvalidOperationException BuildCaptchaException(
        string url,
        DownloadOptions options,
        Exception innerException)
    {
        var cookieGuidance = options.UseBrowserCookies
            ? $"After completing the challenge in {options.CookieBrowser}, retry the item so SpectraGrab can reuse the refreshed browser session."
            : "Complete the challenge in your browser, enable 'Use browser cookies' for that browser (or provide an updated cookies.txt), then retry the item.";

        return new InvalidOperationException(
            $"Human verification is required for {url}. SpectraGrab opened the page in your default browser when possible. " +
            cookieGuidance + " CAPTCHA solving or bypass is not automated.",
            innerException);
    }
}
