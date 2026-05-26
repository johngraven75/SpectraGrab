namespace SpectraGrab.Services;

public sealed record DownloadOptions(
    bool AdultSiteMode,
    bool UseBrowserCookies,
    string CookieBrowser,
    bool AllowInsecureCertificates,
    string SitePluginId,
    string VideoCodecId,
    string AudioCodecId,
    int Quality)
{
    public static DownloadOptions Default { get; } = new(true, false, "chrome", false, "adaptive-hoster", "copy", "copy", 82);
}
