# SpectraGrab Cross-Platform Sync Contract

SpectraGrab is maintained as one product across three platform repositories:

- Windows: `johngraven75/SpectraGrab`
- Android: `johngraven75/SpectraGrab-Android`
- iOS: `johngraven75/SpectraGrab-iOS`

## Source of truth

The Windows repository is the feature-definition source of truth. A Windows feature change that affects product behavior must include a parity impact entry for Android and iOS before release.

Platform-specific implementation may differ, but user-visible behavior and feature capability should remain equivalent where the operating system permits it.

## Shared feature contract

The three applications must stay aligned on:

1. URL inspection and metadata discovery.
2. yt-dlp-compatible extractor behavior where supported by the platform runtime.
3. Generic provider fallback and embedded-player discovery.
4. Direct media downloads.
5. HLS (`m3u8`) and DASH (`mpd`) discovery/download support.
6. Playlist and multi-entry download support.
7. Deep crawler behavior, bounded recursion, deduplication, and media prioritization.
8. Authenticated browser/session import only where the platform permits authorized session sharing.
9. CAPTCHA detection with human-in-the-loop completion; no automated solving or bypass.
10. Queue management, pause/cancel/resume semantics, progress, retries, and error reporting.
11. Video/audio format and quality selection.
12. FFmpeg-backed merge/transcode behavior where packaged and supported.
13. Provider profiles and generic adult-media routing for public or authorized content.
14. No DRM, paywall, credential, access-control, or CAPTCHA circumvention.

## Platform constraints

### Windows

Windows can use locally installed or bundled `yt-dlp` and `ffmpeg`, browser-cookie extraction, unrestricted filesystem output, and WPF desktop process execution.

### Android

Android must use Android-compatible binaries/libraries, app-scoped storage or Storage Access Framework destinations, foreground services for long-running downloads, notification progress, network-security policy, and lifecycle-safe cancellation/resume. APK and AAB builds must be reproducible in CI.

### iOS

iOS must comply with sandboxing, background-transfer limits, app-group/keychain rules, platform networking, and App Store policy. Arbitrary desktop executable launching is not available; media extraction/downloading must use iOS-compatible libraries or a permitted service architecture. IPA/TestFlight builds require Apple signing credentials and provisioning.

## Version synchronization

All repositories use the same product version. A release tag is green only when the same version is validated on Windows, Android, and iOS.

Recommended release tag format:

`vMAJOR.MINOR.PATCH`

Each platform artifact must include the same semantic version plus platform build metadata where required.

## Green-light release gate

A product release is allowed only when:

- Windows Release build succeeds.
- Android Release APK/AAB build succeeds.
- iOS Release archive build succeeds on macOS.
- Shared feature-parity checks succeed.
- No unresolved release-blocking defects remain.
- Platform artifacts are generated successfully.
- Signing/publishing credentials required by the target store are present.

A failed platform blocks the coordinated product release until corrected or explicitly excluded by a documented platform limitation.

## Change propagation workflow

For every Windows feature PR:

1. Assign a shared feature/change identifier.
2. Mark whether Android and iOS are affected.
3. Open or update matching work in both mobile repositories.
4. Implement platform-equivalent behavior.
5. Run each repository's native CI.
6. Run the cross-platform parity gate.
7. Publish only after all required jobs are green.

## Mobile repository baseline

Each mobile repository should include:

- `README.md` describing platform constraints and parity status.
- `FEATURE_PARITY.json` with shared feature IDs and implementation state.
- native source project and tests.
- CI workflow for restore/build/test.
- release workflow producing APK/AAB on Android and signed/unsigned archive artifacts on iOS as appropriate.
- dependency/tool verification for media extraction, HLS/DASH, merge/transcode, and storage.
- issue/PR templates requiring a Windows parity reference.

## Publishing

Android publishing requires a signing keystore and, for Play Store publishing, configured Play Console credentials.

iOS publishing requires an Apple Developer account, certificate/signing identity, provisioning profile or automatic signing configuration, and App Store Connect credentials.

CI must never embed these secrets in source control; they must be stored as repository/environment secrets.
