; Snap per-user Windows installer. Build with Inno Setup 6.7.3 or 7.1.0.
; The payload must be a self-contained win-x64 publish directory.
; ISCC.exe /DAppVersion=0.1.0 /DBuildSource="C:\path\to\Snap\dist\Snap" Snap.iss

#define ProjectRoot AddBackslash(SourcePath) + ".."
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef BuildSource
  #define BuildSource ProjectRoot + "\dist\Snap"
#endif

#if !FileExists(BuildSource + "\Snap.exe")
  #error "Snap.exe is missing. Run build.ps1 -SelfContained first."
#endif
#if !FileExists(BuildSource + "\coreclr.dll")
  #error "A self-contained win-x64 build is required; coreclr.dll is missing."
#endif

[Setup]
AppId={{846C016D-9C3B-4A28-B094-147734685855}
AppName=Snap
AppVersion={#AppVersion}
AppVerName=Snap {#AppVersion}
AppPublisher=Snap
AppComments=A simple screen capture tool for Windows.
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Snap Setup
DefaultDirName={localappdata}\Programs\Snap
DefaultGroupName=Snap
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
OutputDir={#ProjectRoot}\dist\installer
OutputBaseFilename=Snap-Setup-{#AppVersion}-x64
SetupIconFile={#ProjectRoot}\src\Snap\Assets\Snap-app.ico
UninstallDisplayIcon={app}\Snap.exe
UninstallDisplayName=Snap
WizardStyle=modern dynamic
DisableWelcomePage=no
DisableDirPage=auto
DisableReadyPage=no
#if FileExists(ProjectRoot + "\installer\assets\wizard-large.bmp")
WizardImageFile={#ProjectRoot}\installer\assets\wizard-large.bmp
#endif
#if FileExists(ProjectRoot + "\installer\assets\wizard-small.png")
WizardSmallImageFile={#ProjectRoot}\installer\assets\wizard-small.png
#endif
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=Snap.exe,*.dll
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to Snap
WelcomeLabel2=Capture a moment. Keep your flow.%n%nThis wizard will install Snap on your computer.%n%nSnap opens directly into a simple capture overlay. Choose a region, screen, or window and take your shot.
FinishedLabel=Snap is installed and ready to capture.%n%nOpen Snap from the Start menu whenever you need it. Screenshots and recordings are saved in your Pictures and Videos folders.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#BuildSource}\*"; DestDir: "{app}"; Excludes: "*.pdb,Snap.log"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Snap"; Filename: "{app}\Snap.exe"; WorkingDir: "{app}"; Comment: "Take a screenshot"
Name: "{group}\Snap (background mode)"; Filename: "{app}\Snap.exe"; Parameters: "--background"; WorkingDir: "{app}"; Comment: "Enable Snap's tray icon and capture shortcuts"
Name: "{autodesktop}\Snap"; Filename: "{app}\Snap.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Comment: "Take a screenshot"

[Run]
Filename: "{app}\Snap.exe"; Description: "&Launch Snap"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

; Only files recorded in Inno Setup's uninstall log are removed. In particular,
; do not recursively remove {app} or the user's screenshot/recording folders.

[Code]
const
  SnapMutex = 'Local\Snap.Native.Windows.v1';

function CloseRunningSnap: Boolean;
var
  QuitResult, Attempt: Integer;
  AppExecutable: String;
begin
  Result := True;
  if not CheckForMutexes(SnapMutex) then
    Exit;

  AppExecutable := ExpandConstant('{app}\Snap.exe');
  if FileExists(AppExecutable) then
  begin
    Log('Requesting a graceful shutdown of Snap.');
    { Do not wait on the helper process: an older version may show a dialog
      when IPC cannot be reached. The app mutex is the shutdown authority. }
    if not Exec(AppExecutable, '--quit', ExpandConstant('{app}'),
      SW_HIDE, ewNoWait, QuitResult) then
      Log('Could not start the Snap shutdown helper: ' + SysErrorMessage(QuitResult));
  end;

  { A recording may need time to flush its final frames to disk. Never kill
    the app or continue replacing/deleting its files while it is running. }
  for Attempt := 1 to 150 do
  begin
    if not CheckForMutexes(SnapMutex) then
      Exit;
    Sleep(100);
  end;
  Result := not CheckForMutexes(SnapMutex);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CloseRunningSnap then
    Result := 'Snap is still running. Finish your recording, choose Quit Snap from the tray menu, then click Retry to continue.';
end;

function InitializeUninstall: Boolean;
begin
  { Check before the uninstaller confirmation, without stopping a recording
    merely because the user opened the uninstaller and may cancel it. }
  Result := not CheckForMutexes(SnapMutex);
  if not Result then
    SuppressibleMsgBox('Snap is running. Finish your recording and choose Quit Snap from the tray menu, then run the uninstaller again.',
      mbError, MB_OK, IDOK);
end;
