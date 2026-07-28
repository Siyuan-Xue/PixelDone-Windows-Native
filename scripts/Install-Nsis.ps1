[CmdletBinding()]
param(
    [string]$Version = "3.12.0",
    [string]$Sha256 = "3BC2B06253A7E4957111BE152AC6A536E0C7478A706E19DA814038DB5D706495"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$makeNsis = "${env:ProgramFiles(x86)}\NSIS\makensis.exe"
if (Test-Path -LiteralPath $makeNsis -PathType Leaf) {
    & $makeNsis /VERSION
    if ($LASTEXITCODE -ne 0) {
        throw "The preinstalled makensis.exe is not usable."
    }
    exit 0
}

$releaseVersion = $Version -replace '\.0$', ''
$downloadUri =
    "https://downloads.sourceforge.net/project/nsis/NSIS%203/$releaseVersion/" +
    "nsis-$releaseVersion-setup.exe"
$temporaryRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [System.IO.Path]::GetTempPath()
} else {
    $env:RUNNER_TEMP
}
$installer = Join-Path $temporaryRoot "nsis-$releaseVersion-setup.exe"

try {
    Write-Output "Downloading NSIS $Version from its official release."
    Invoke-WebRequest `
        -Uri $downloadUri `
        -OutFile $installer `
        -MaximumRetryCount 3 `
        -RetryIntervalSec 5

    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash
    if ($actualHash -ne $Sha256) {
        throw "The downloaded NSIS installer failed SHA-256 verification."
    }

    $process = Start-Process `
        -FilePath $installer `
        -ArgumentList "/S" `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($process.ExitCode -notin 0, 3010) {
        throw "The NSIS installer exited with code $($process.ExitCode)."
    }

    if (-not (Test-Path -LiteralPath $makeNsis -PathType Leaf)) {
        throw "NSIS did not install makensis.exe."
    }
    & $makeNsis /VERSION
    if ($LASTEXITCODE -ne 0) {
        throw "The installed makensis.exe is not usable."
    }
} finally {
    Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
}
