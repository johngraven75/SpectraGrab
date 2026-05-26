# SpectraGrab Build 2 Notes

Date: 2026-05-26

## Summary

Logo and splash screen update for SpectraGrab.

## Implemented

- Added the supplied `spectrasgrab.png` artwork to the app as `Assets\spectrasgrab.png`.
- Wired the artwork as the WPF startup splash screen.
- Updated the left navigation branding to show the logo inside the program.
- Updated the main dashboard title to match the artwork: `Spectra-Grab 26 Ultimate Edition`.

## Verification

- `dotnet build -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish -c Release -r win-x64 --self-contained true`: passed.
- Published executable smoke test: launched successfully, main window title was `SpectraGrab`, process was responding.
- Signed MSIX package was created and SignTool verification passed.
- Local portable install copied to `%LOCALAPPDATA%\Programs\SpectraGrab`, Desktop shortcut was updated, and the installed executable launched responsive with main window title `SpectraGrab`.
- `Add-AppxPackage` MSIX installation is still blocked by Windows trust policy (`0x800B0109`) unless the local signing certificate is trusted in the machine root store. Importing into `Cert:\LocalMachine\Root` requires elevation on this machine, so the active installed Build 2 is the portable install.
