# SpectraGrab

SpectraGrab is a Windows media downloader prototype built with WPF and .NET 8.

Tagline: Capture Everything. Any Format. Any Site.

## Current build

- Native WPF desktop UI with a vibrant dashboard, download inspector, queue, crawler, converter surface, and settings surface.
- `yt-dlp` integration for metadata inspection and real download execution.
- `ffmpeg` integration through `yt-dlp` for merge/conversion support when available.
- Static page crawler for direct media discovery across internal links up to a selected depth.
- Async MVVM-style command flow with dependency injection.
- Self-contained Win-x64 publish, portable ZIP, and MSIX packaging flow.

## Requirements

The app can launch by itself from the self-contained build. Real downloads use `yt-dlp` and `ffmpeg`; this machine already has both installed.

## Privacy

SpectraGrab has no telemetry code. URLs are sent only to the sites the user chooses to inspect, crawl, or download from.

## Cross-platform releases

The shared product version and feature parity contract are tracked in `FEATURE_PARITY.json` and `docs/CROSS_PLATFORM_SYNC.md`. Windows CI does not publish releases; the coordinated release workflow blocks publishing until the Windows, Android, and iOS versions, parity states, and current platform CI runs are verified.
