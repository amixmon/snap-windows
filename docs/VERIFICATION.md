# Verification

Verified on 22 October 2026 on the current Windows workstation.

`./build.ps1 -SelfContained -Test` publishes a Windows x64 application with the .NET Desktop Runtime included. The Release build completed without warnings or errors. All 11 built-in checks passed; the detailed report is generated at `artifacts/self-test/results.txt`.

The checks cover selection initialization, negative desktop coordinates, clamping, move/resize behavior, reverse dragging, pixel rounding, invalid capture dimensions, PNG pixel preservation, native H.264 encoding, MP4 finalization, and native Windows decoding. The decoded test video has a 320 × 180 visible image, with the expected red top and blue bottom; Windows reports a padded 320 × 192 storage surface, which the test handles through the display aperture.

The live standalone application was also opened and inspected through Windows accessibility tools. Region drawing kept the overlay open after mouse release; window mode displayed selectable previews; video mode disabled window selection and changed the shutter to red. A real region capture produced a 262 × 175 PNG in Pictures/Screenshots. Escape dismissed the final standalone build and ended the process.

Clipboard persistence across destination applications, real recording stop interaction, global shortcuts from another application, and mixed-DPI multi-monitor hardware remain manual checks. The automated recording test uses generated pixels; it does not record the desktop. Window preview fallbacks are visible for applications that cannot render through PrintWindow, as described in the research notes.

The packaged `dist/Snap-Windows-x64.zip` contains the `Snap` output folder. Keep its files together when moving it to another Windows x64 machine.

## Branding and setup wizard

The generated source logo has a transparent background and measures 1254 × 1254 pixels. Windows icon frames cover 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels. The embedded executable icon was extracted and visually checked at Windows icon size. The source and setup use the same mark.

`build-installer.ps1 -Test` completed with Inno Setup 7.1.0, producing the offline, per-user x64 setup. The compiler download's SHA-256 matched its official release asset and its Authenticode signature was valid for Pyrsys B.V. The generated Snap setup is unsigned.

The actual setup executable passed four smoke-test groups on Windows 11: fresh installation with bundled runtime, notices, shortcuts and uninstall registration; all 11 app self-tests from the installed executable; reinstall/update with graceful closure of background mode; and uninstall with removal of tracked payload/shortcuts/registration while preserving a user-created file. These tests used a temporary project directory and the real setup/uninstaller. Logs and results are under `artifacts/installer-smoke`.

No clean Windows 10 machine or code-signing certificate was available for testing. The installer declares Windows 10 build 19041 or later on x64; other architectures are rejected. The setup wizard's generated artwork is checked separately from its installation behavior.
