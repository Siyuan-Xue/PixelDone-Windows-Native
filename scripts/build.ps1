[CmdletBinding()]
param(
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'PixelDone-Windows-Native.slnx'
$appProject = Join-Path $repositoryRoot 'src\PixelDone.Windows\PixelDone.Windows.csproj'
$publishDirectory = Join-Path $repositoryRoot 'artifacts\publish\win-x64'
$installerDirectory = Join-Path $repositoryRoot 'artifacts\installer'
$windowsAppSdkVersion = '2.3.1'

$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($dotnetCommand) {
    $dotnet = $dotnetCommand.Source
} else {
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) {
        throw 'The .NET SDK is not installed.'
    }
}

& $dotnet restore $solution --runtime win-x64
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet restore failed.'
}

& $dotnet test (Join-Path $repositoryRoot 'tests\PixelDone.Core.Tests\PixelDone.Core.Tests.csproj') `
    --configuration Release `
    --no-restore
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet test failed.'
}

& $dotnet publish $appProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

# Windows App SDK 2.3.1's unpackaged self-contained publish currently omits
# this WinRT diagnostics resource even though AppNotificationManager loads it.
# Recover the signed Microsoft binary from the restored official runtime MSIX.
$nugetPackages = if ($env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES
} else {
    Join-Path $env:USERPROFILE '.nuget\packages'
}
$runtimeFrameworkMsix = Join-Path $nugetPackages (
    "microsoft.windowsappsdk.runtime\$windowsAppSdkVersion\tools\MSIX\" +
    "win10-x64\Microsoft.WindowsAppRuntime.2.msix")
$insightsResource = Join-Path $publishDirectory `
    'Microsoft.WindowsAppRuntime.Insights.Resource.dll'
if (-not (Test-Path $runtimeFrameworkMsix)) {
    throw "The restored Windows App SDK runtime package is missing: $runtimeFrameworkMsix"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$runtimeArchive = [System.IO.Compression.ZipFile]::OpenRead($runtimeFrameworkMsix)
try {
    $entry = $runtimeArchive.GetEntry(
        'Microsoft.WindowsAppRuntime.Insights.Resource.dll')
    if (-not $entry) {
        throw 'The Windows App SDK runtime MSIX has no Insights resource DLL.'
    }
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile(
        $entry,
        $insightsResource,
        $true)
} finally {
    $runtimeArchive.Dispose()
}

$publishedExecutable = Join-Path $publishDirectory 'PixelDone.exe'
if (-not (Test-Path $publishedExecutable)) {
    throw "Publish did not produce $publishedExecutable"
}

$requiredWinUiResources = @(
    'PixelDone.pri',
    'App.xbf',
    'MainWindow.xbf',
    'MainPage.xbf',
    'Microsoft.WindowsAppRuntime.Insights.Resource.dll'
)
foreach ($winUiResource in $requiredWinUiResources) {
    $publishedWinUiResource = Join-Path $publishDirectory $winUiResource
    if (-not (Test-Path $publishedWinUiResource)) {
        throw "Publish did not produce $publishedWinUiResource"
    }
}

if ($Installer) {
    $makeNsis = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if ($makeNsis) {
        $makeNsisPath = $makeNsis.Source
    } else {
        $defaultNsis = Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'
        if (Test-Path $defaultNsis) {
            $makeNsisPath = $defaultNsis
        } else {
            throw 'NSIS is not installed. Install it with: winget install NSIS.NSIS'
        }
    }

    New-Item -ItemType Directory -Force -Path $installerDirectory | Out-Null
    & $makeNsisPath `
        "/DAPP_SOURCE=$publishDirectory" `
        "/DOUTPUT_DIR=$installerDirectory" `
        (Join-Path $repositoryRoot 'packaging\PixelDone.nsi')
    if ($LASTEXITCODE -ne 0) {
        throw 'makensis failed.'
    }
}

Write-Host "PixelDone Windows publish: $publishDirectory"
