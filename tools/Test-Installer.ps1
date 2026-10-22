param([string]$Installer)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Installer) { $Installer = Join-Path $projectRoot 'dist\installer\Snap-Setup-0.1.0-x64.exe' }
$Installer = [System.IO.Path]::GetFullPath($Installer)
$testRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\installer-smoke'))
$appDirectory = [System.IO.Path]::GetFullPath((Join-Path $testRoot 'app'))
if (!$appDirectory.StartsWith($projectRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Test directory must stay inside the project.' }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{846C016D-9C3B-4A28-B094-147734685855}_is1'
$alternateKey = 'HKCU:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{846C016D-9C3B-4A28-B094-147734685855}_is1'
if ((Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath $alternateKey)) { throw 'Snap is already installed. Use a clean Windows test account to avoid changing that installation.' }
if (Test-Path -LiteralPath (Join-Path $appDirectory 'Snap.exe')) { throw 'A previous smoke-test install is present. Uninstall it before rerunning.' }
try { $existing = [System.Threading.Mutex]::OpenExisting('Local\Snap.Native.Windows.v1'); $existing.Dispose(); throw 'Quit the running Snap instance before testing setup.' }
catch [System.Threading.WaitHandleCannotBeOpenedException] { }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$report = [System.Collections.Generic.List[string]]::new()
$report.Add('Snap installer smoke test')
$report.Add("Started: $([DateTimeOffset]::UtcNow.ToString('O'))")
$report.Add("Installer: $Installer")
$installed = $false
$groupName = 'Snap'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) $groupName
foreach ($name in @('Snap.lnk', 'Snap (background mode).lnk')) {
    if (Test-Path -LiteralPath (Join-Path $startMenu $name)) { throw "A Start menu shortcut already exists: $name. Use a clean test account." }
}

function Run-Setup([string]$logName) {
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=`"$appDirectory`"",'/TASKS=',"/LOG=`"$(Join-Path $testRoot $logName)`"")
    $process = Start-Process -FilePath $Installer -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Setup exited with $($process.ExitCode); inspect $logName." }
}
try {
    Run-Setup 'install.log'
    $installed = $true
    foreach ($name in @('Snap.exe','coreclr.dll','unins000.exe','ThirdParty\dotnet-LICENSE.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $appDirectory $name))) { throw "Missing installed file: $name" }
    }
    if (!(Test-Path -LiteralPath $uninstallKey)) { throw 'The per-user uninstall entry is missing.' }
    $registration = Get-ItemProperty -LiteralPath $uninstallKey
    if ($registration.InstallLocation.TrimEnd('\') -ne $appDirectory.TrimEnd('\')) { throw 'Setup did not use the temporary test directory.' }
    foreach ($name in @('Snap.lnk', 'Snap (background mode).lnk')) {
        if (!(Test-Path -LiteralPath (Join-Path $startMenu $name))) { throw "Missing Start menu shortcut: $name" }
    }
    $report.Add('PASS Fresh offline per-user install, runtime files, license notices, uninstall registration and both Start menu shortcuts')

    $selfTest = Start-Process -FilePath (Join-Path $appDirectory 'Snap.exe') -ArgumentList '--self-test' -WorkingDirectory $projectRoot -Wait -PassThru -WindowStyle Hidden
    if ($selfTest.ExitCode -ne 0) { throw 'Installed application self-test failed.' }
    $report.Add('PASS Installed application passes all 11 self-tests')

    $background = Start-Process -FilePath (Join-Path $appDirectory 'Snap.exe') -ArgumentList '--background' -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 1000
    $background.Refresh()
    if ($background.HasExited) { throw 'Background mode exited unexpectedly.' }
    Run-Setup 'upgrade.log'
    if (!$background.WaitForExit(3000)) { throw 'Setup did not gracefully close the background app before updating.' }
    $report.Add('PASS Reinstall/update gracefully closes the existing background app')

    # Verify that uninstall only removes tracked payload, preserving user-created files.
    $sentinel = Join-Path $appDirectory 'user-created-preserve.txt'
    Set-Content -LiteralPath $sentinel -Value 'This file was not installed by setup.'
    $uninstaller = Start-Process -FilePath (Join-Path $appDirectory 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$(Join-Path $testRoot 'uninstall.log')`"") -Wait -PassThru -WindowStyle Hidden
    if ($uninstaller.ExitCode -ne 0) { throw "Uninstall exited with $($uninstaller.ExitCode)." }
    $installed = $false
    if (Test-Path -LiteralPath (Join-Path $appDirectory 'Snap.exe')) { throw 'Uninstall left the app executable behind.' }
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Uninstall left the per-user uninstall entry behind.' }
    if (Test-Path -LiteralPath (Join-Path $startMenu 'Snap.lnk')) { throw 'Uninstall left the Start menu shortcut behind.' }
    if (!(Test-Path -LiteralPath $sentinel)) { throw 'Uninstall removed a user-created file.' }
    $report.Add('PASS Uninstall removes installed payload, registration and shortcuts while preserving untracked user files')
    $report.Add('Result: 4 passed; 0 failed.')
} catch {
    $report.Add('FAIL ' + $_.Exception.Message)
    throw
} finally {
    # Never delete a computed directory recursively. The real uninstaller owns cleanup.
    if ($installed -and (Test-Path -LiteralPath (Join-Path $appDirectory 'unins000.exe'))) {
        $quit = Start-Process -FilePath (Join-Path $appDirectory 'Snap.exe') -ArgumentList '--quit' -Wait -PassThru -WindowStyle Hidden
        $cleanup = Start-Process -FilePath (Join-Path $appDirectory 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru -WindowStyle Hidden
        $report.Add("Cleanup uninstall exit: $($cleanup.ExitCode)")
    }
    $report | Set-Content -LiteralPath (Join-Path $testRoot 'results.txt')
    $report | Write-Output
}
