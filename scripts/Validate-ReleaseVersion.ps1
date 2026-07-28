[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Tag
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Tag -notmatch '^v(?<version>\d+\.\d+\.\d+(?:-beta\.\d+)?)$') {
    throw "Release tag must be vX.Y.Z or vX.Y.Z-beta.N: $Tag"
}
$version = $Matches.version
$repositoryRoot = Split-Path -Parent $PSScriptRoot

[xml]$project = Get-Content -Raw -LiteralPath (
    Join-Path $repositoryRoot "src\PixelDone.Windows\PixelDone.Windows.csproj")
$projectVersion = ([string]$project.SelectSingleNode(
    "/Project/PropertyGroup/Version").InnerText).Trim()
$displayVersion = ([string]$project.SelectSingleNode(
    "/Project/PropertyGroup/ApplicationDisplayVersion").InnerText).Trim()
if ($projectVersion -ne $version -or $displayVersion -ne $version) {
    throw "Tag $Tag does not match project versions $projectVersion / $displayVersion."
}

$escaped = [regex]::Escape($version)
$productVersion = Get-Content -Raw -LiteralPath (
    Join-Path $repositoryRoot "src\PixelDone.Core\ProductVersion.cs")
if ($productVersion -notmatch "public const string Version = `"$escaped`";") {
    throw "PixelDoneProduct.Version does not equal $version."
}

$nsis = Get-Content -Raw -LiteralPath (
    Join-Path $repositoryRoot "packaging\PixelDone.nsi")
if ($nsis -notmatch "(?m)^!define PRODUCT_VERSION `"$escaped`"`r?$") {
    throw "NSIS PRODUCT_VERSION does not equal $version."
}

$numericVersion = ($version -split '-', 2)[0] + ".0"
$manifest = Get-Content -Raw -LiteralPath (
    Join-Path $repositoryRoot "src\PixelDone.Windows\app.manifest")
if ($manifest -notmatch
    "<assemblyIdentity version=`"$([regex]::Escape($numericVersion))`"") {
    throw "app.manifest assemblyIdentity does not equal $numericVersion."
}

$expectedHeader = "# PixelDone for Windows $version"
$actualHeader = (Get-Content -LiteralPath (
    Join-Path $repositoryRoot "RELEASE_NOTES.md") -TotalCount 1).Trim()
if ($actualHeader -ne $expectedHeader) {
    throw "RELEASE_NOTES.md must start with '$expectedHeader'."
}

Write-Output "Validated Windows release identity $Tag."
