# SpectraGrab Build 5 Notes

Date: 2026-06-12

## Summary

SpectraGrab icon and package logo update using the claw/orb mark from the supplied Spectra-Grab 26 artwork.

## Implemented

- Added a square `Assets\spectragrab-icon.png` app icon derived from the SpectraGrab claw/orb logo, with the wordmark removed for small-icon readability.
- Added `Assets\spectragrab.ico` as the Windows executable icon.
- Added source package logo assets for `StoreLogo.png`, `Square44x44Logo.png`, and `Square150x150Logo.png`.
- Wired the executable `ApplicationIcon` to `Assets\spectragrab.ico`.
- Wired the WPF main window `Icon` to the same SpectraGrab icon.
- Incremented the MSIX manifest version to `0.1.0.5`.

## Verification

- `dotnet build -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish -c Release -r win-x64 --self-contained true -o artifacts\publish\win-x64`: passed.
- Published executable smoke test: launched successfully, main window title was `SpectraGrab`, process was responding.
- Build 5 MSIX package was created with `makeappx`.
- Build 5 MSIX package was signed with the local `CN=SpectraGrab Local` certificate.
- `signtool verify /pa /v` passed with 0 warnings and 0 errors.

## Artifacts

- `artifacts\packages\build-5\SpectraGrab-0.1.0-build5-x64.msix`
- `artifacts\packages\build-5\SpectraGrab-0.1.0-build5-portable-win-x64.zip`
- `artifacts\packages\build-5\SpectraGrab-Local.cer`
