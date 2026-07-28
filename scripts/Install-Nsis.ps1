[CmdletBinding()]
param(
    [string]$Version = "3.12.0",
    [ValidateRange(1, 5)][int]$MaximumAttempts = 3
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

for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
    Write-Output "Installing NSIS $Version (attempt $attempt of $MaximumAttempts)."
    choco install nsis `
        --version $Version `
        --yes `
        --no-progress
    if ($LASTEXITCODE -eq 0 -and
        (Test-Path -LiteralPath $makeNsis -PathType Leaf)) {
        & $makeNsis /VERSION
        if ($LASTEXITCODE -ne 0) {
            throw "The installed makensis.exe is not usable."
        }
        exit 0
    }

    if ($attempt -lt $MaximumAttempts) {
        Start-Sleep -Seconds (10 * $attempt)
    }
}

throw "Unable to install NSIS $Version after $MaximumAttempts attempts."
