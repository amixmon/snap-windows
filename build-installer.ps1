param(
    [switch]$SkipBuild,
    [switch]$Test,
    [string]$Compiler
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'tools\Prepare-BrandAssets.ps1')
if (!$SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -SelfContained -Test:$Test
}
$publishDirectory = Join-Path $PSScriptRoot 'dist\Snap'
$configuration = Join-Path $publishDirectory 'Snap.runtimeconfig.json'
if (!(Test-Path -LiteralPath $configuration) -or !(Test-Path -LiteralPath (Join-Path $publishDirectory 'coreclr.dll'))) {
    throw 'Publish a self-contained Windows x64 build first.'
}
$runtimeConfig = Get-Content -LiteralPath $configuration -Raw | ConvertFrom-Json
if (!$runtimeConfig.runtimeOptions.includedFrameworks) { throw 'The installer requires a bundled .NET runtime.' }
if (!(Test-Path -LiteralPath (Join-Path $publishDirectory 'ThirdParty\dotnet-LICENSE.txt'))) {
    throw 'Runtime redistribution notices are missing. Run build.ps1 -SelfContained.'
}
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\Snap\Snap.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'A numeric Windows release version is required.' }
$exeVersion = (Get-Item -LiteralPath (Join-Path $publishDirectory 'Snap.exe')).VersionInfo.ProductVersion.Split('+')[0]
if ($exeVersion -ne $version) { throw "Published version $exeVersion does not match project version $version." }
if (!$Compiler) {
    $candidates = @(
        (Join-Path $PSScriptRoot '.tools\inno\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $Compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$Compiler) {
        $available = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($available) { $Compiler = $available.Source }
    }
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) {
    throw 'Install the official Inno Setup compiler (https://jrsoftware.org/isdl.php), or pass -Compiler C:\path\ISCC.exe.'
}
& $Compiler '/Q' "/DAppVersion=$version" "/DBuildSource=$publishDirectory" (Join-Path $PSScriptRoot 'installer\Snap.iss')
if ($LASTEXITCODE -ne 0) { throw "Setup compilation failed with exit code $LASTEXITCODE." }
$installer = Join-Path $PSScriptRoot "dist\installer\Snap-Setup-$version-x64.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'dist\installer\SHA256SUMS.txt') -Value "$hash  $([System.IO.Path]::GetFileName($installer))" -Encoding ascii
Write-Output "Setup ready: $installer"
Write-Output "SHA256: $hash"
Write-Output 'This build is unsigned. A verified publisher requires a code-signing certificate from the release owner.'
