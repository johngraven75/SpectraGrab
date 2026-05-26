# SpectraGrab Build 4 Notes

Date: 2026-05-26

## Summary

Cookie handling fix for Chrome cookie database lock errors.

## Implemented

- Added Netscape `cookies.txt` path support. When a cookie file path is provided, SpectraGrab passes `--cookies <file>` to `yt-dlp` instead of reading a live browser database.
- Kept browser-cookie support for Chrome, Edge, Firefox, Brave, Vivaldi, and Opera.
- Added clearer handling for `Could not copy Chrome cookie database` and Windows permission-denied cookie copy errors.
- Error guidance now tells the user to close the browser, choose another browser, or use a Netscape cookie export file.

## Verification

- `dotnet build -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish -c Release -r win-x64 --self-contained true`: passed.
- Published executable smoke test: launched successfully, main window title was `SpectraGrab`, process was responding.
- Signed MSIX package was created and SignTool verification passed.
- Build 4 was installed to `%LOCALAPPDATA%\Programs\SpectraGrab` and `C:\Users\johng\OneDrive\Documents\Desktop\SpectraGrab`.
- Desktop shortcut was updated and the Desktop install launched responsive with main window title `SpectraGrab`.
