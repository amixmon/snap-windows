# Contributing to Snap

Snap aims to keep screen capture immediate and simple: open the app, select, capture, and continue. Focused fixes, Windows compatibility reports, and improvements to this flow are welcome.

## Build and verify

Use a Windows x64 computer with the .NET 8 SDK. From the repository root:

```powershell
.\build.ps1 -SelfContained -Test
.\dist\Snap\Snap.exe
```

The built-in self-tests use generated pixels and write results to `artifacts/self-test/results.txt`; they do not capture your desktop. For UI or capture changes, also exercise the affected interaction on real displays and inspect the saved image or recording. See [verification notes](docs/VERIFICATION.md) for known coverage gaps.

For installer changes, install Inno Setup 6.7.3 or 7.1.0 and run:

```powershell
.\build-installer.ps1 -Test
.\tools\Test-Installer.ps1
```

Use a clean Windows test account for installer smoke tests. They install and uninstall the real package in a temporary project folder, temporarily creating per-user Start menu shortcuts and uninstall registration. The script refuses to run if it finds a real Snap installation or running instance. Read the [installer guide](installer/README.md) before running it.

## Report a bug

Include the Snap version, Windows version/build, steps to reproduce, expected result, and actual result. For capture or overlay issues, include monitor count, resolutions, scaling percentages, and the application being captured. Mention whether you used the installer or portable package and normal or background mode.

Only attach screenshots, recordings, or log excerpts after checking them for private information. Reproduce issues on a neutral desktop or test window when possible; avoid sharing personal desktop captures, account details, or confidential content.

## Send a change

Keep each pull request focused and explain the user-visible behavior it changes. Include the relevant build/self-test result and any manual verification, especially for capture, scaling, shortcuts, or installation. Add meaningful regression coverage where it helps verify a real failure. Do not commit generated build outputs, local tools, logs, captures, or credentials.

Contributions are made under the project's [MIT License](LICENSE).
