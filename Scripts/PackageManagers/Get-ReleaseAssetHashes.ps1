#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string] $OwnerRepo,

    [Parameter(Mandatory)]
    [string] $TagName,

    [Parameter(Mandatory)]
    [string] $AssetVersion,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [string] $DownloadDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Command {
    param([Parameter(Mandatory)] [string] $Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found."
    }
}

function Get-AssetDefinition {
    param([Parameter(Mandatory)] [string] $Version)

    return [ordered] @{
        wingetX64 = "ContextMenuMgrPlus-$Version-x64-framework-dependent-Setup.exe"
        wingetX86 = "ContextMenuMgrPlus-$Version-x86-framework-dependent-Setup.exe"
        wingetArm64 = "ContextMenuMgrPlus-$Version-arm64-framework-dependent-Setup.exe"
        scoopPortable = "ContextMenuMgrPlus-$Version-framework-dependent-portable.zip"
    }
}

if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    throw 'GH_TOKEN is required to download and hash GitHub Release assets.'
}

Assert-Command -Name 'gh'

if ([string]::IsNullOrWhiteSpace($DownloadDirectory)) {
    $DownloadDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("ContextMenuMgr-package-assets-" + [System.Guid]::NewGuid().ToString('N'))
}

New-Item -ItemType Directory -Force -Path $DownloadDirectory | Out-Null

$availableAssets = gh release view $TagName --repo $OwnerRepo --json assets | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) {
    throw "Failed to inspect release '$TagName' in '$OwnerRepo'."
}

$requiredAssets = Get-AssetDefinition -Version $AssetVersion
$assetOutput = [ordered] @{}

foreach ($item in $requiredAssets.GetEnumerator()) {
    $logicalName = $item.Key
    $assetName = $item.Value
    $match = @($availableAssets.assets | Where-Object { $_.name -eq $assetName })
    if ($match.Count -ne 1) {
        throw "Required release asset '$assetName' was not found on release '$TagName'."
    }

    gh release download $TagName --repo $OwnerRepo --pattern $assetName --dir $DownloadDirectory --clobber
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to download release asset '$assetName'."
    }

    $assetPath = Join-Path $DownloadDirectory $assetName
    if (-not (Test-Path -LiteralPath $assetPath)) {
        throw "Downloaded asset was not found at '$assetPath'."
    }

    $sha256 = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $assetOutput[$logicalName] = [ordered] @{
        name = $assetName
        url = "https://github.com/$OwnerRepo/releases/download/$TagName/$assetName"
        sha256 = $sha256
    }
}

$manifest = [ordered] @{
    releaseTag = $TagName
    assetVersion = $AssetVersion
    assets = $assetOutput
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json | Out-Null
Write-Host "Asset hash manifest: $OutputPath"
