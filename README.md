<img src="src/Snap/Assets/Snap-logo.png" width="96" height="96" alt="Snap logo">

# Snap

**Open. Select. Capture.** A simple, native Windows screenshot and screen recording tool inspired by the modern Ubuntu / GNOME capture overlay.

Opening Snap takes you straight to a dimmed desktop with selection handles and a small capture bar. Choose an area, screen, or window, adjust the frame, and capture. Screenshots save automatically and copy to the clipboard, so you can get back to what you were doing.

Built for people who want quick captures without an editor or a save dialog. Snap is written in C# with WPF and .NET 8.

## Download and install

**[Download the latest release](https://github.com/amixmon/snap-windows/releases/latest)**

Requires **Windows 10 version 2004 (build 19041) or later, or Windows 11, on an x64 PC**. ARM64 and 32-bit Windows are not supported by these packages.

| Download | How to use it |
| --- | --- |
| `Snap-Setup-0.1.0-x64.exe` | Run the setup wizard. Installs for your Windows account with Start menu shortcuts, an optional desktop shortcut, and an uninstaller. |
| `Snap-Windows-x64.zip` | Extract the whole ZIP and open `Snap/Snap.exe`. Keep all files in the extracted folder together. |

Both release packages include the .NET runtime and work offline. The app and installer are currently **unsigned**; Windows may display an unknown-publisher or SmartScreen reputation warning. Check the release source and published SHA-256 checksum before installing.

Uninstall through **Settings → Apps → Installed apps → Snap**. Your screenshots and recordings are preserved.

## Capture in seconds

1. Open **Snap** from Start, a desktop shortcut, or `Snap.exe`.
2. Choose **Selection**, **Screen**, or **Window**. Draw a selection, drag inside it to move it, or resize with its edges and corner handles.
3. Click the round capture button or press **Enter**. Press **Esc** to cancel.

Releasing the mouse keeps the overlay open so you can refine the selection. The pointer button controls whether the mouse pointer appears in your capture.

- **Screenshots:** PNG files saved in your Windows **Pictures/Screenshots** folder and copied to the clipboard for pasting.
- **Recordings:** MP4 files with H.264 video, **without audio**, saved in **Videos/Screencasts**. Switch to recording mode, select an area or screen, and start. Use the floating stop control to finish.
- **Local files:** Captures are saved on your computer. Snap does not require an account or upload captures to a service.

| Overlay shortcut | Action |
| --- | --- |
| S / C / W | Selection / Screen / Window |
| P | Toggle mouse pointer |
| V | Switch screenshot / recording mode |
| Enter / Space / Ctrl+C | Capture or start recording |
| Esc | Cancel |

## Optional tray mode

For shortcuts while other apps are active, open **Snap (background mode)** from Start, or run:

```powershell
.\Snap.exe --background
```

| Global shortcut | Action |
| --- | --- |
| Print Screen or Ctrl+Alt+S | Open the capture overlay |
| Alt+Print Screen | Capture the focused window |
| Shift+Print Screen | Capture the whole desktop directly |
| Ctrl+Alt+Shift+R | Open recording mode, or stop an active recording |

Windows or another app may already use a shortcut. If registration fails, open Snap from the tray instead. Background mode is optional and does not configure automatic startup or change Windows' Print Screen setting.

Quit from the tray menu, or run `Snap.exe --quit`.

## Build from source

On Windows, install the **.NET 8 SDK**, open PowerShell in the repository root, then run:

```powershell
.\build.ps1 -SelfContained -Test
.\dist\Snap\Snap.exe
```

This publishes an x64 build with the runtime included and runs the built-in checks. Omit `-SelfContained` for a smaller build that requires the **.NET 8 Desktop Runtime** on the target computer. Build outputs and locally installed tools are excluded from Git.

To build the setup wizard, install the official **Inno Setup compiler**; versions **6.7.3** and **7.1.0** are supported. The compiler is not bundled in this repository. Then run:

```powershell
.\build-installer.ps1 -Test
```

If needed, supply the compiler location with `-Compiler 'C:\Program Files\Inno Setup 7\ISCC.exe'`. The output and checksum are written to `dist/installer`. See the [installer guide](installer/README.md) for packaging and release checks.

## Status and limitations

Snap is an early release. The current Windows workstation passed **11 app self-tests** and **four installer smoke-test groups**. Real region capture and Escape dismissal were also checked. The tests do not establish compatibility with every display or application; see the [verification notes](docs/VERIFICATION.md) for the exact coverage and remaining manual checks.

- Window previews and captures are best effort. Some applications cannot render correctly through Windows' `PrintWindow` API, and protected or capture-excluded content may be unavailable.
- Window recording, audio recording, image editing, and annotations are not implemented.
- Clipboard persistence across destination apps, real recording stop interaction, global shortcuts, and mixed-DPI multi-monitor setups need further manual testing. A clean Windows 10 machine has not yet been tested.
- Snap uses a floating recording stop indicator and appears in the taskbar during selection to fit Windows interaction and accessibility behavior.

The workflow follows Ubuntu 22.04 and later's GNOME overlay. Snap is an independent Windows project. See [research and platform notes](docs/RESEARCH.md) for the intended behavior and primary references.

## Contribute

Bug reports, practical Windows compatibility feedback, and focused improvements are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) to build, verify changes, and report issues without exposing sensitive desktop content.

## License and artwork

Snap is available under the **MIT License**; see [LICENSE](LICENSE). Bundled .NET runtime components retain their own license and third-party notices in release packages.

The logo was created with ImageGen. Source artwork and the generation prompt are documented in [branding notes](docs/BRANDING.md).
