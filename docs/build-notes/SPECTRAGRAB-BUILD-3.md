# SpectraGrab Build 3 Notes

Date: 2026-05-26

## Summary

Added adaptive adult-site and hoster plugin behavior inside SpectraGrab without requiring external handoff.

## Implemented

- Added internal site plugin catalog with broad presets for adaptive hosters, adult sites, member-cookie pages, HLS/direct embeds, DASH manifests, social video, streaming platforms, enterprise players, cloud hosters, audio pages, and JDownloader-style hoster behavior.
- Added explicit site profiles and auto-routing for Pornhub, RedTube, xHamster, XNXX, typo-tolerant XXNX input, and BoyfriendTV adaptive handling.
- Kept downloads inside SpectraGrab by default through `yt-dlp`, crawler discovery, hoster-friendly headers, referrer handling, geo bypass, retries, fragment retries, and HLS behavior.
- Added browser-cookie controls for Chrome, Edge, Firefox, Brave, Vivaldi, and Opera profiles.
- Added optional K-Lite Codec Pack detection/status. K-Lite is not required for downloading because ffmpeg remains the merge/conversion engine.
- Added video codec choices: original stream/no recode, H.264/AVC, H.265/HEVC, AV1, and VP9.
- Added audio codec choices: original audio, AAC, Opus, MP3, and FLAC.
- Added codec quality control that maps to ffmpeg CRF/bitrate settings when transcoding is selected.
- Expanded crawler discovery for common embedded media attributes and direct video/audio/manifest extensions.

## Verification

- `dotnet build -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish -c Release -r win-x64 --self-contained true`: passed.
- Published executable smoke test: launched successfully, main window title was `SpectraGrab`, process was responding.
- Signed MSIX package was created and SignTool verification passed.
- Local portable install copied to `%LOCALAPPDATA%\Programs\SpectraGrab`, Desktop shortcut was updated, and the installed executable launched responsive with main window title `SpectraGrab`.
- Local `yt-dlp --list-extractors` confirmed dedicated extractors for Pornhub, RedTube, xHamster, and XNXX. No dedicated BoyfriendTV extractor was listed, so BoyfriendTV uses the adaptive hoster/crawler path.
- K-Lite Codec Pack Full 19.7.0 was installed with Winget and verified under `C:\Program Files (x86)\K-Lite Codec Pack`.
