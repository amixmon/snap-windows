# Snap installer

`Snap.iss` creates a single offline setup wizard for the self-contained Windows x64 build. It installs for the current Windows account under `%LocalAppData%\Programs\Snap`, with no administrator request. It targets x64 Windows 10 version 2004 (build 19041) or later, including Windows 11. ARM64 and 32-bit Windows are not supported by this package.

The wizard provides a Start menu entry, a separate **Snap (background mode)** shortcut for optional tray/hotkey use, an optional desktop shortcut, and a **Launch Snap** checkbox. It does not register an automatic startup task. Uninstall through **Settings > Apps > Installed apps > Snap**. Your screenshots in Pictures/Screenshots and recordings in Videos/Screencasts are preserved.

## Build

Use the official Inno Setup compiler, then run from the repository root:

```powershell
./build-installer.ps1
```

The resulting file is `dist/installer/Snap-Setup-0.1.0-x64.exe`. Share this one file with other users; the .NET runtime is bundled and no internet connection is needed to install it.

The build script finds a local portable compiler at `.tools/inno/ISCC.exe`, a standard Inno Setup installation, or an explicit `-Compiler` path. It regenerates branding, publishes the self-contained app, copies runtime notices, and writes `dist/installer/SHA256SUMS.txt`. Add `-Test` to run the app checks before packaging. Inno Setup's own installer supports `/PORTABLE=1 /CURRENTUSER` for project-local compiler tooling; its [source](https://github.com/jrsoftware/issrc/blob/main/isportable.iss) describes this mode. Check the compiler's license terms for your use.

For direct compilation after publishing the app:

```powershell
./build.ps1 -SelfContained
& 'C:\Program Files\Inno Setup 7\ISCC.exe' installer/Snap.iss
```

The script accepts two optional compiler definitions:

| Definition | Default | Meaning |
| --- | --- | --- |
| `/DAppVersion=0.1.0` | `0.1.0` | Display/file version and output filename. Use a numeric Windows version, such as `0.1.0` or `0.1.1`. |
| `/DBuildSource=C:\absolute\publish\folder` | Repository `dist\Snap` | Full self-contained publish folder containing `Snap.exe` and `coreclr.dll`. Pass this as one quoted argument when its path contains spaces. |

Branding comes from `src/Snap/Assets/Snap-app.ico`, with `installer/assets/wizard-large.bmp` and transparent `wizard-small.png`. The large image uses a 164:314 aspect ratio; the small image is square. The wizard follows the Windows light/dark preference. The installer copies the publish folder, including runtime redistribution notices, and excludes debugging symbols and the application error log.

The fixed `AppId` is the upgrade identity. Keep it unchanged between releases so updates reuse the installed directory and uninstall record. Setup requests a graceful shutdown through Snap's existing `--quit` command and waits up to 15 seconds for its named mutex to disappear. If Snap remains busy, setup stops before replacing files and offers Retry. Uninstall asks the user to finish recording and quit Snap first if it is running, then stops before removing files. Neither flow force-kills a recording process.

## Compiler and authenticity

The [official Inno Setup download page](https://jrsoftware.org/isdl.php), checked on 22 October 2026, offers **Inno Setup 7.1.0 x64** (12 August 2026), and also **6.7.3**. This script uses features supported by both. The current x64 compiler installer is [innosetup-7.1.0-x64.exe](https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe). Its [official release asset listing](https://github.com/jrsoftware/issrc/releases/expanded_assets/is-7_1_0) reports SHA-256 `0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f`.

Follow the publisher's [verification instructions](https://jrsoftware.org/isdl-verify.php) before running downloaded compiler tooling. Its Authenticode publisher is **Pyrsys B.V.**; GitHub release attestations and Inno Setup signature verification are also available. The [official `.issig` file](https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe.issig) contains the compiler download's SHA-256 value. The compiler's signature does not sign the Snap setup that it generates. A public Snap release needs the owner's Windows code-signing identity to display a verified publisher; this project does not invent one.

## Release checks

Before distributing a release, run the app's self-test and test the setup on a clean supported Windows account: install, launch from Start, capture, enable background mode, upgrade while background mode is running, and uninstall. Confirm the uninstall removes installed files/shortcuts while keeping captured images and videos. Check the wizard and shortcuts at common display scaling values. Test silent installation with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` when needed; silent mode does not auto-launch Snap.

`tools/Test-Installer.ps1` tests install, installed self-tests, background-mode update and uninstall in `artifacts/installer-smoke/app`. It refuses to run when a real Snap installation or running instance already exists. It temporarily creates the per-user uninstall entry and Start menu shortcuts, then uses the real uninstaller to remove them. Its report is `artifacts/installer-smoke/results.txt`.

Primary implementation references: [non-administrative install mode](https://jrsoftware.org/ishelp/topic_admininstallmode.htm), [architecture restrictions](https://jrsoftware.org/ishelp/topic_setup_architecturesallowed.htm), [closing applications](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm), [installer lifecycle hooks](https://jrsoftware.org/ishelp/topic_scriptevents.htm), and [mutex detection](https://jrsoftware.org/ishelp/topic_isxfunc_checkformutexes.htm).
