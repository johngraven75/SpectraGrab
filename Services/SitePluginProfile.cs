namespace SpectraGrab.Services;

public sealed record SitePluginProfile(
    string Id,
    string Name,
    string Engine,
    string Description,
    bool RequiresCookies,
    IReadOnlyList<string> YtdlpArguments);

public interface ISitePluginCatalog
{
    IReadOnlyList<SitePluginProfile> Profiles { get; }
    SitePluginProfile Resolve(string id);
    SitePluginProfile ResolveForUrl(string url, string selectedId);
}

public sealed class SitePluginCatalog : ISitePluginCatalog
{
    public IReadOnlyList<SitePluginProfile> Profiles { get; } =
    [
        new(
            "adaptive-hoster",
            "Adaptive hoster plugin",
            "Internal",
            "Recommended. Uses supported extractors first, then hoster-friendly referrer, HLS, retries, cookies, and manifest behavior.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "youtube-family",
            "YouTube and Google video",
            "yt-dlp",
            "YouTube, Shorts, playlists, channels, memberships when browser cookies are enabled.",
            false,
            ["--geo-bypass"]),
        new(
            "social-video",
            "Social video sites",
            "yt-dlp",
            "TikTok, Instagram, Facebook, X/Twitter, Reddit, Pinterest, Snapchat-style public video pages.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "streaming-platforms",
            "Streaming platforms",
            "yt-dlp",
            "Vimeo, Dailymotion, Twitch clips/VODs, Bilibili, Rumble, PeerTube, Odysee, and similar extractor-backed sites.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "embed-players",
            "Embedded player pages",
            "Internal",
            "JWPlayer, Video.js, Flowplayer, Plyr, iframe embeds, and pages where media is exposed through player config.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "enterprise-players",
            "Enterprise video players",
            "yt-dlp",
            "Brightcove, Kaltura, Wistia, Cloudflare Stream, Vimeo embeds, and common business video players.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "cloud-hoster",
            "Cloud file hosters",
            "Internal",
            "Generic file-host pages that expose direct downloads, HLS manifests, or embedded video files.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "generic-adult",
            "Generic adult video site",
            "yt-dlp",
            "Adds referrer, desktop user agent, retries, HLS handling, and geo bypass.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "pornhub",
            "Pornhub",
            "yt-dlp",
            "Dedicated Pornhub extractor support with referrer, HLS, retries, and optional cookies.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "redtube",
            "RedTube",
            "yt-dlp",
            "Dedicated RedTube extractor support with referrer, HLS, retries, and optional cookies.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "xhamster",
            "xHamster",
            "yt-dlp",
            "Dedicated xHamster extractor support including embed pages.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "xnxx",
            "XNXX / XXNX",
            "yt-dlp",
            "Dedicated XNXX-family routing with typo-tolerant XXNX host matching, referrer headers, HLS handling, retries, and optional cookies.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "xvideos",
            "XVideos",
            "yt-dlp",
            "Dedicated XVideos routing using yt-dlp extraction with referrer headers, HLS handling, retries, and optional cookies.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "boyfriendtv",
            "Boyfriend.tv adaptive",
            "Internal",
            "Boyfriend.tv page handling with page referrer, desktop user agent, optional browser/cookies.txt authentication, HLS fragment downloading, and FFmpeg fallback for unprotected streams.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "members-cookies",
            "Members area with browser cookies",
            "yt-dlp",
            "Use for paid or logged-in pages after signing in through a browser profile.",
            true,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "direct-hls",
            "Direct HLS or MP4 embed",
            "yt-dlp",
            "Use for pages where the crawler finds m3u8, mpd, mp4, or ts media links.",
            false,
            ["--hls-use-mpegts"]),
        new(
            "dash-manifest",
            "DASH or fragmented streams",
            "yt-dlp",
            "MPD manifests, fragmented MP4, adaptive video plus audio, and sites requiring ffmpeg merge.",
            false,
            ["--geo-bypass"]),
        new(
            "browser-auth",
            "Browser authenticated sites",
            "yt-dlp",
            "Logged-in sites using Chrome, Edge, Firefox, Brave, Vivaldi, or Opera cookies.",
            true,
            ["--geo-bypass", "--hls-use-mpegts"]),
        new(
            "audio-podcast",
            "Audio and podcast pages",
            "yt-dlp",
            "Podcast, music, direct audio, and pages where the best stream is audio-only.",
            false,
            ["--geo-bypass"]),
        new(
            "jdownloader-hoster",
            "JDownloader 2 style hoster profile",
            "Internal",
            "Keeps the workflow inside SpectraGrab while using JD-style referrer, retry, HLS, and cookie-ready behavior.",
            false,
            ["--geo-bypass", "--hls-use-mpegts"])
    ];

    public SitePluginProfile Resolve(string id)
    {
        return Profiles.FirstOrDefault(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? Profiles[0];
    }

    public SitePluginProfile ResolveForUrl(string url, string selectedId)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("pornhub"))
            {
                return Resolve("pornhub");
            }

            if (host.Contains("redtube"))
            {
                return Resolve("redtube");
            }

            if (host.Contains("xhamster"))
            {
                return Resolve("xhamster");
            }

            if (host.Contains("xnxx") || host.Contains("xxnx"))
            {
                return Resolve("xnxx");
            }

            if (host.Equals("xvideos.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".xvideos.com", StringComparison.OrdinalIgnoreCase)
                || host.Contains("xvideos", StringComparison.OrdinalIgnoreCase))
            {
                return Resolve("xvideos");
            }

            if (host.Equals("boyfriend.tv", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".boyfriend.tv", StringComparison.OrdinalIgnoreCase)
                || host.Contains("boyfriendtv", StringComparison.OrdinalIgnoreCase))
            {
                return Resolve("boyfriendtv");
            }
        }

        return Resolve(selectedId);
    }
}
