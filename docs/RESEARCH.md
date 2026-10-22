# Capture experience research

Research checked on 22 October 2026. The reference is Ubuntu's built-in GNOME screenshot experience, introduced with GNOME 42 and Ubuntu 22.04. Snap adapts that interaction to a native Windows executable.

## Which Ubuntu experience?

Canonical's [Ubuntu 22.04 announcement](https://ubuntu.com/blog/ubuntu-22-04-lts-whats-new-linux-desktop) identifies GNOME 42 and its new screenshot tool. The [GNOME 42 release notes](https://release.gnome.org/42/) describe an interactive overlay combining screenshots and screen recording. That is the baseline for Snap's immediate launch behavior.

The older `gnome-screenshot` program is a different workflow. Its interactive invocation presents capture options, followed by a save dialog. It also offers delayed capture. Before the overlay, Print captured the desktop, Alt+Print captured the current window, Shift+Print selected an area, and adding Ctrl used the clipboard instead of saving. These differences are documented in a [GNOME developer discussion](https://bugzilla.gnome.org/show_bug.cgi?id=753894) and the [Ubuntu 20.04 manual](https://manpages.ubuntu.com/manpages/focal/man1/gnome-screenshot.1.html).

## User-visible reference

Ubuntu's [screenshot and screencast guide](https://help.ubuntu.com/stable/ubuntu-help/screen-shot-record.html.en) establishes the main flow:

- Print opens capture controls with a region selection.
- The user adjusts the region, chooses Screen, or chooses a window from an overview.
- The pointer control includes or excludes the pointer.
- A round button confirms capture.
- Images are saved in Pictures/Screenshots and placed on the clipboard automatically.
- Video mode offers a region or screen. Recording ends through the red indicator and saves in Videos/Screencasts.

The [official GNOME screenshot illustration](https://help.gnome.org/gnome-help/figures/screenshot-tool.png) is the visual reference. Snap should open directly into these controls when launched, without a landing page, file browser, or capture history screen.

## Selection and capture details

The [upstream screenshot implementation](https://raw.githubusercontent.com/GNOME/gnome-shell/main/js/ui/screenshot.js) provides details beyond the help page:

- A still screenshot uses a frozen desktop image taken before showing the controls.
- First launch selects a centered rectangle one quarter of the primary monitor's width and height. A valid selection is retained subsequently.
- Initial settings are Selection, still image, and pointer hidden.
- Interior dragging moves the region; edges and corners resize it. Outside dragging, right-dragging, and Ctrl+dragging begin a new region.
- Mouse release does not capture; confirmation remains explicit.
- Screen mode selects one monitor, initially primary. Region selection can span displays.
- Window mode arranges previews and initially selects the focused window.
- Switching to recording disables Window and returns a selected window to Selection.

Escape dismisses the modal grab through GNOME's [grab helper](https://raw.githubusercontent.com/GNOME/gnome-shell/main/js/ui/grabHelper.js). Snap maps that to cancelling its overlay.

## Controls and shortcuts

The [GNOME 42 release notes](https://release.gnome.org/42/) define the overlay shortcuts:

| Key | Effect |
| --- | --- |
| S | Select an area |
| C | Select a screen |
| W | Select a window |
| P | Toggle pointer inclusion |
| V | Switch image / video |
| Enter, Space, Ctrl+C | Confirm capture |

The [GNOME keyboard settings guide](https://teams.pages.gitlab.gnome.org/Websites/help.gnome.org/gnome-help/keyboard-shortcuts-set.html.kn) also lists Print for the overlay, Alt+Print for the current window, Shift+Print for a direct full-screen capture, and Ctrl+Alt+Shift+R for recording. The differing historical Shift+Print behavior is intentional; the modern reference uses it for full-screen capture.

## Visual treatment

GNOME's [screenshot styles](https://raw.githubusercontent.com/GNOME/gnome-shell/main/data/theme/gnome-shell-sass/widgets/_screenshot.scss) specify a rounded dark OSD panel, positioned above the bottom edge. Selection, Screen, and Window sit across the top with icons above labels. The lower row contains a photo/video capsule, a concentric circular shutter, and a pointer button. The close button sits outside the panel near its upper corner.

The selection has a white 2px border, white circular corner handles, and a 50% black shade outside it. The recording shutter has a red center. The panel becomes less prominent while the user drags. Ubuntu's theme can change accent colors and dimensions; these sources define an interaction reference, not a promise of identical pixels across every GNOME release.

## Native Windows implementation choices

Snap uses WPF and .NET 8 for Windows 10/11, with native Windows interop for capture and hotkeys. Still captures remain in memory until the user confirms. Confirming encodes a PNG and copies an image to the Windows clipboard. There is no post-capture editor or mandatory save dialog.

Videos use MP4/H.264 with no audio. This is a Windows implementation choice; the Ubuntu documentation's workflow and destination are the reference, not a requirement to reproduce its recording container or codec.

Background operation is opt-in with `--background`. Global shortcuts use `RegisterHotKey`, with Ctrl+Alt+S as an additional overlay shortcut. Microsoft documents that [registration can fail when a key combination is already registered](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), and specifically discusses the operating system's Print Screen shortcut. Snap should not imply exclusive ownership of that key or silently change system settings. `--quit` provides an explicit way to close the existing process. No startup or registry changes are part of this workflow.

## Capture constraints and verification

Window previews and capture are best effort. Microsoft's [PrintWindow documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow) explains that the owning application renders its window into the supplied device context. It also warns that this is a synchronous call that can make a UI thread unresponsive. A successful desktop capture does not prove that every application will supply a useful separate window image.

Windows allows applications to [restrict or exclude their windows from supported capture APIs](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity). Protected, capture-excluded, or otherwise unavailable content may be missing or blank. Snap cannot promise capture of every window or protected media.

WPF layout units and desktop pixels must be mapped consistently, including displays positioned to the left or above the primary display. Microsoft's [high-DPI guide](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) explains the difference between system and per-monitor DPI awareness. Real verification must include mixed scaling, monitor boundaries, negative coordinates, and display changes. Passing a synthetic crop test cannot establish that these cases work on hardware.

`--self-test` is deliberately separate from real capture: it operates on test data, writes only under `artifacts/self-test`, and must not capture the user's desktop. It can verify internal operations without writing test screenshots into Pictures or Videos. Manual QA should then verify region adjustment, pointer inclusion, clipboard paste, window previews, recording output, and whichever global shortcuts the current Windows session allows.
