[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [Parameter(Mandatory = $true)][string]$Title,
    [Parameter(Mandatory = $true)][string]$NotesPath,
    [Parameter(Mandatory = $true)][string[]]$AssetPaths,
    [bool]$Prerelease = $false
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Normalize-Text([string]$Value) {
    return ($Value -replace "`r`n", "`n").TrimEnd()
}

function Assert-DownloadedAsset([string]$AssetName, [string]$ExpectedPath) {
    $directory = Join-Path $env:RUNNER_TEMP ("github-release-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $directory | Out-Null
    try {
        gh release download $Tag --repo $Repository --pattern $AssetName --dir $directory
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to download GitHub asset $AssetName."
        }
        $actual = Join-Path $directory $AssetName
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $actual).Hash
        $expectedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExpectedPath).Hash
        if ($actualHash -ne $expectedHash) {
            throw "GitHub asset $AssetName conflicts with the CI artifact."
        }
    } finally {
        Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

foreach ($path in @($NotesPath) + $AssetPaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing release input: $path"
    }
}
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    throw "GH_TOKEN is required."
}

$releaseJson = gh release view $Tag `
    --repo $Repository `
    --json tagName,name,body,isDraft,isPrerelease,assets 2>$null
$releaseExists = $LASTEXITCODE -eq 0
if (-not $releaseExists) {
    $arguments = @(
        "release", "create", $Tag
    ) + $AssetPaths + @(
        "--repo", $Repository,
        "--title", $Title,
        "--notes-file", $NotesPath,
        "--verify-tag"
    )
    if ($Prerelease) {
        $arguments += "--prerelease"
    }
    & gh @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub Release creation failed for $Tag."
    }
} else {
    $release = $releaseJson | ConvertFrom-Json
    if ($release.tagName -ne $Tag -or
        $release.name -ne $Title -or
        $release.isDraft -or
        [bool]$release.isPrerelease -ne $Prerelease) {
        throw "Existing GitHub Release metadata conflicts with $Tag."
    }
    if ((Normalize-Text ([string]$release.body)) -ne
        (Normalize-Text (Get-Content -Raw -LiteralPath $NotesPath))) {
        throw "Existing GitHub Release notes conflict with the generated notes."
    }
    $names = @($release.assets | ForEach-Object { $_.name })
    foreach ($path in $AssetPaths) {
        $name = Split-Path -Leaf $path
        if ($names -contains $name) {
            Assert-DownloadedAsset $name $path
        } else {
            gh release upload $Tag $path --repo $Repository
            if ($LASTEXITCODE -ne 0) {
                throw "Unable to upload missing GitHub asset $name."
            }
        }
    }
}

$tagCommit = (git rev-list -n 1 $Tag).Trim()
if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $ExpectedCommit) {
    throw "GitHub tag commit changed."
}
foreach ($path in $AssetPaths) {
    Assert-DownloadedAsset (Split-Path -Leaf $path) $path
}
Write-Output "Verified immutable GitHub Release $Tag."
