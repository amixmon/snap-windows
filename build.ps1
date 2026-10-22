param([switch]$SelfContained, [switch]$Test)
$ErrorActionPreference = 'Stop'
$sdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$project = Join-Path $PSScriptRoot 'src\Snap\Snap.csproj'
$output = Join-Path $PSScriptRoot 'dist\Snap'
& $sdk publish $project -c Release -r win-x64 --self-contained $SelfContained.IsPresent -o $output -p:PublishSingleFile=false "-p:RestoreConfigFile=$PSScriptRoot\NuGet.Config"
if ($LASTEXITCODE -ne 0) { throw 'Snap build failed.' }
$projectLicense = Join-Path $PSScriptRoot 'LICENSE'
if (Test-Path -LiteralPath $projectLicense) {
    Copy-Item -LiteralPath $projectLicense -Destination (Join-Path $output 'LICENSE.txt')
}
if ($SelfContained) {
    $config = Get-Content -LiteralPath (Join-Path $output 'Snap.runtimeconfig.json') -Raw | ConvertFrom-Json
    $runtimeVersion = ($config.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
    $desktopVersion = ($config.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App').version
    $notices = Join-Path $output 'ThirdParty'
    New-Item -ItemType Directory -Path $notices -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $env:NUGET_PACKAGES "microsoft.netcore.app.runtime.win-x64\$runtimeVersion\LICENSE.TXT") -Destination (Join-Path $notices 'dotnet-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $env:NUGET_PACKAGES "microsoft.netcore.app.runtime.win-x64\$runtimeVersion\THIRD-PARTY-NOTICES.TXT") -Destination (Join-Path $notices 'dotnet-THIRD-PARTY-NOTICES.txt')
    Copy-Item -LiteralPath (Join-Path $env:NUGET_PACKAGES "microsoft.windowsdesktop.app.runtime.win-x64\$desktopVersion\LICENSE") -Destination (Join-Path $notices 'windowsdesktop-LICENSE.txt')
}
if ($Test) {
    $testProcess = Start-Process -FilePath (Join-Path $output 'Snap.exe') -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden -WorkingDirectory $PSScriptRoot
    if ($testProcess.ExitCode -ne 0) { throw "Self-test failed; see artifacts/self-test/results.txt" }
    Get-Content (Join-Path $PSScriptRoot 'artifacts\self-test\results.txt')
}
Write-Output "Ready: $output\Snap.exe"
