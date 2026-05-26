# SpectraGrab Build 1 Notes

Date: 2026-05-26

## Summary

Initial Windows build of SpectraGrab, a WPF/.NET 8 media downloader prototype.

## Implemented

- Created the WPF app shell and modern SpectraGrab interface.
- Added dependency injection and MVVM command flow.
- Added `yt-dlp` metadata inspection and queue download service.
- Added `ffmpeg` location handoff to `yt-dlp`.
- Added static crawler service for direct media links and internal crawl depth.
- Added queue progress parsing, output folder defaults, device/format surfaces, converter surface, settings surface, README, and privacy policy.

## Verification

- `dotnet build -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish -c Release -r win-x64 --self-contained true`: passed.
- Published executable smoke test: launched successfully, main window title was `SpectraGrab`, process was responding.
- Signed MSIX package was created and SignTool verification passed.
- Local portable install copied to `%LOCALAPPDATA%\Programs\SpectraGrab`, Desktop shortcut was created, and the installed executable launched responsive with main window title `SpectraGrab`.
- Local tools found: `yt-dlp` 2026.03.17 and ffmpeg 8.1.1.

## Known limits

- Crawler Build 1 handles static HTML media discovery. JavaScript network interception with Playwright is planned for a future build.
- Browser extension, scheduler, full converter workflow, and cookie vault UI are represented in the app surface but are not fully implemented yet.
- `Add-AppxPackage` MSIX installation was blocked by Windows trust policy because importing the local signing certificate into `Cert:\LocalMachine\Root` requires elevation on this machine. The MSIX itself is signed and verified; the portable install is the active local installation for Build 1.
