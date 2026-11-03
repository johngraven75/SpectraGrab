# SpectraGrab

SpectraGrab is a Windows media downloader prototype built with WPF and .NET 8.

Tagline: Capture public, unprotected media from sites yt-dlp supports, plus discovered HLS/DASH and direct media.

## Current build

- Native WPF desktop UI with a vibrant dashboard, download inspector, live Capture module, queue, crawler, working single-file converter (ffmpeg presets: MP4 H.264 AAC, MKV H.265 Opus, WebM AV1 Opus, GIF clip), and settings that actually persist (robots.txt, crawler delay, clipboard monitoring, off-peak scheduled queue starts, portable tool storage).
- `yt-dlp` integration for metadata inspection and real download execution.
- `ffmpeg` integration through `yt-dlp` for merge/conversion support when available.
- Dedicated FFmpeg live-stream capture with MKV, fragmented MP4, and MPEG-TS stream-copy presets, session progress, safe stop/finalization, and unique output naming.
- Persistent, versioned JSON configuration for every provider, extractor profile, and the Emby, Jellyfin, Plex, Local AI, and QuickConnect add-ins.
- Static page crawler for direct media discovery across internal links up to a selected depth.
- Async MVVM-style command flow with dependency injection.
- Self-contained Win-x64 publish, portable ZIP, and MSIX packaging flow.

## Requirements

The app can launch by itself from the self-contained build. Real downloads use `yt-dlp`; merging, conversion, and the Capture module use `ffmpeg`. SpectraGrab discovers these tools from the application directory, its `bin` subfolder, `%LOCALAPPDATA%\SpectraGrab\bin`, the Windows `PATH`, and WinGet executable links.

### First run: tools

At boot the app shows a clear warning if `yt-dlp` or `ffmpeg` is missing, instead of letting Inspect return a placeholder. Press **Install missing tools** on the New Download tab: SpectraGrab tries winget first (`yt-dlp.yt-dlp`, `Gyan.FFmpeg`), otherwise downloads `yt-dlp.exe` from the yt-dlp GitHub release and the ffmpeg essentials build from gyan.dev into `%LOCALAPPDATA%\SpectraGrab\bin` (or the app `bin` folder when portable tools are enabled in Settings), then verifies both binaries by running `--version` / `-version`. Tools installed after launch are picked up without a restart, because tool paths are resolved per operation, not cached at construction.

Inspect now fails with a plain "yt-dlp was not found" error when yt-dlp is missing, and a video+audio merge format fails with a plain ffmpeg-missing error, instead of a fake "URL ready" result.

## Live Capture

Open **Capture**, enter a full HTTP or HTTPS HLS or compatible live-stream URL, choose an output preset, and select **Start Capture**. Each session reports elapsed time, bytes written, FFmpeg speed, output path, and final status. **Stop** asks FFmpeg to close the container cleanly before SpectraGrab ends the process.

Capture is intended for public streams and media the user is authorized to record. It does not bypass DRM, paywalls, access controls, authentication, or CAPTCHA challenges.

## Persistent provider and add-in configuration

Packaged defaults are shipped in `ConfigDefaults/providers` and `ConfigDefaults/plugins`. At startup, SpectraGrab verifies them and creates writable copies under `%LOCALAPPDATA%\SpectraGrab\config`. Missing settings are merged into existing files during upgrades, so user choices remain intact. Invalid files are retained with an `.invalid-<timestamp>` suffix before a safe default is restored, and saves use atomic file replacement.

The five media add-ins are Emby, Jellyfin, Plex, Local AI, and QuickConnect. Credentials are never stored in these JSON files; credential fields name environment variables such as `SPECTRAGRAB_PLEX_TOKEN` instead.

## Privacy

SpectraGrab has no telemetry code. URLs are sent only to the sites the user chooses to inspect, crawl, or download from.

## Notes on old stray files

The former root `build` (an unapplied HLS patch email) and `code` (a tree listing) files were removed: they were never compiled, and the project file no longer carries a `Compile Remove` entry for a `HlsPacketDownloader.cs` that does not exist in the tree. Real HLS handling is `yt-dlp` with the ffmpeg fallback in `Services/HlsYtdlpService.cs`, plus ffmpeg live capture in `Services/StreamCaptureService.cs`.

## Cross-platform releases

The shared product version and feature parity contract are tracked in `FEATURE_PARITY.json` and `docs/CROSS_PLATFORM_SYNC.md`. Windows CI does not publish releases; the coordinated release workflow blocks publishing until the Windows, Android, and iOS versions, parity states, and current platform CI runs are verified.
