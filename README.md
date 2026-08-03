# SpectraGrab

SpectraGrab is a Windows media downloader prototype built with WPF and .NET 8.

Tagline: Capture Everything. Any Format. Any Site.

## Current build

- Native WPF desktop UI with a vibrant dashboard, download inspector, live Capture module, queue, crawler, converter surface, and settings surface.
- `yt-dlp` integration for metadata inspection and real download execution.
- `ffmpeg` integration through `yt-dlp` for merge/conversion support when available.
- Dedicated FFmpeg live-stream capture with MKV, fragmented MP4, and MPEG-TS stream-copy presets, session progress, safe stop/finalization, and unique output naming.
- Persistent, versioned JSON configuration for every provider, extractor profile, and the Emby, Jellyfin, Plex, Local AI, and QuickConnect add-ins.
- Static page crawler for direct media discovery across internal links up to a selected depth.
- Async MVVM-style command flow with dependency injection.
- Self-contained Win-x64 publish, portable ZIP, and MSIX packaging flow.

## Requirements

The app can launch by itself from the self-contained build. Real downloads use `yt-dlp`; merging, conversion, and the Capture module use `ffmpeg`. SpectraGrab discovers these tools from the application directory, the Windows `PATH`, and WinGet executable links.

## Live Capture

Open **Capture**, enter a full HTTP or HTTPS HLS or compatible live-stream URL, choose an output preset, and select **Start Capture**. Each session reports elapsed time, bytes written, FFmpeg speed, output path, and final status. **Stop** asks FFmpeg to close the container cleanly before SpectraGrab ends the process.

Capture is intended for public streams and media the user is authorized to record. It does not bypass DRM, paywalls, access controls, authentication, or CAPTCHA challenges.

## Persistent provider and add-in configuration

Packaged defaults are shipped in `ConfigDefaults/providers` and `ConfigDefaults/plugins`. At startup, SpectraGrab verifies them and creates writable copies under `%LOCALAPPDATA%\SpectraGrab\config`. Missing settings are merged into existing files during upgrades, so user choices remain intact. Invalid files are retained with an `.invalid-<timestamp>` suffix before a safe default is restored, and saves use atomic file replacement.

The five media add-ins are Emby, Jellyfin, Plex, Local AI, and QuickConnect. Credentials are never stored in these JSON files; credential fields name environment variables such as `SPECTRAGRAB_PLEX_TOKEN` instead.

## Privacy

SpectraGrab has no telemetry code. URLs are sent only to the sites the user chooses to inspect, crawl, or download from.
