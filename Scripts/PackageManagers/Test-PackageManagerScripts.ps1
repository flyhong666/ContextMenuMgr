#Requires -Version 5.1
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
if (-not $scriptDir) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        [AllowNull()] $Actual,
        [AllowNull()] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Write-Json {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] $Value
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function New-SampleAssets {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Tag,
        [Parameter(Mandatory)] [string] $AssetVersion
    )

    $hashA = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
    $hashB = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
    $hashC = 'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'
    $hashD = 'dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd'

    Write-Json -Path $Path -Value ([ordered] @{
        releaseTag = $Tag
        assetVersion = $AssetVersion
        assets = [ordered] @{
            wingetX64 = [ordered] @{
                name = "ContextMenuMgrPlus-$AssetVersion-x64-framework-dependent-Setup.exe"
                url = "https://github.com/PLFJY/ContextMenuMgr/releases/download/$Tag/ContextMenuMgrPlus-$AssetVersion-x64-framework-dependent-Setup.exe"
                sha256 = $hashA
            }
            wingetX86 = [ordered] @{
                name = "ContextMenuMgrPlus-$AssetVersion-x86-framework-dependent-Setup.exe"
                url = "https://github.com/PLFJY/ContextMenuMgr/releases/download/$Tag/ContextMenuMgrPlus-$AssetVersion-x86-framework-dependent-Setup.exe"
                sha256 = $hashB
            }
            wingetArm64 = [ordered] @{
                name = "ContextMenuMgrPlus-$AssetVersion-arm64-framework-dependent-Setup.exe"
                url = "https://github.com/PLFJY/ContextMenuMgr/releases/download/$Tag/ContextMenuMgrPlus-$AssetVersion-arm64-framework-dependent-Setup.exe"
                sha256 = $hashC
            }
            scoopPortable = [ordered] @{
                name = "ContextMenuMgrPlus-$AssetVersion-framework-dependent-portable.zip"
                url = "https://github.com/PLFJY/ContextMenuMgr/releases/download/$Tag/ContextMenuMgrPlus-$AssetVersion-framework-dependent-portable.zip"
                sha256 = $hashD
            }
        }
    })
}

function Invoke-GenerationCase {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Tag,
        [Parameter(Mandatory)] [bool] $Prerelease,
        [Parameter(Mandatory)] [string] $PublishedAt,
        [Parameter(Mandatory)] [string] $ExpectedPackageVersion,
        [Parameter(Mandatory)] [string] $ExpectedWingetId,
        [Parameter(Mandatory)] [string] $ExpectedScoopFile
    )

    $caseRoot = Join-Path $Root $Name
    New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null

    $eventPath = Join-Path $caseRoot 'release-event.json'
    $metadataPath = Join-Path $caseRoot 'release-metadata.json'
    $assetPath = Join-Path $caseRoot 'assets.json'
    $scoopOut = Join-Path $caseRoot 'scoop'
    $wingetOut = Join-Path $caseRoot 'winget'
    $assetVersion = $Tag -replace '^[vV]', ''

    Write-Json -Path $eventPath -Value ([ordered] @{
        release = [ordered] @{
            tag_name = $Tag
            name = $Tag
            prerelease = $Prerelease
            published_at = $PublishedAt
            html_url = "https://github.com/PLFJY/ContextMenuMgr/releases/tag/$Tag"
        }
    })

    New-SampleAssets -Path $assetPath -Tag $Tag -AssetVersion $assetVersion

    & (Join-Path $scriptDir 'Resolve-PackageRelease.ps1') -ReleaseEventJson $eventPath -OutputPath $metadataPath
    & (Join-Path $scriptDir 'New-ScoopManifest.ps1') -ReleaseMetadataJson $metadataPath -AssetManifestJson $assetPath -OutputDirectory $scoopOut | Out-Null
    & (Join-Path $scriptDir 'New-WingetManifest.ps1') -ReleaseMetadataJson $metadataPath -AssetManifestJson $assetPath -OutputDirectory $wingetOut | Out-Null

    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    Assert-Equal -Actual $metadata.packageVersion -Expected $ExpectedPackageVersion -Message "$Name package version mismatch."
    Assert-Equal -Actual $metadata.wingetPackageIdentifier -Expected $ExpectedWingetId -Message "$Name winget PackageIdentifier mismatch."

    $scoopPath = Join-Path $scoopOut $ExpectedScoopFile
    Assert-True -Condition (Test-Path -LiteralPath $scoopPath) -Message "$Name Scoop manifest was not created."
    $scoop = Get-Content -LiteralPath $scoopPath -Raw | ConvertFrom-Json
    Assert-Equal -Actual $scoop.license -Expected 'GPL-3.0-only' -Message "$Name Scoop license mismatch."
    Assert-Equal -Actual $scoop.persist -Expected 'Data' -Message "$Name Scoop persist mismatch."

    $preInstall = ($scoop.pre_install -join "`n")
    if ($Prerelease) {
        Assert-True -Condition ($preInstall -match 'contextmenumgrplus\\current') -Message 'Scoop Beta manifest does not check the stable app.'
    }
    else {
        Assert-True -Condition ($preInstall -match 'contextmenumgrplus-beta\\current') -Message 'Scoop Stable manifest does not check the beta app.'
    }

    $versionManifestPath = Join-Path $wingetOut "$ExpectedWingetId.yaml"
    $zhCnLocaleManifestPath = Join-Path $wingetOut "$ExpectedWingetId.locale.zh-CN.yaml"
    $zhTwLocaleManifestPath = Join-Path $wingetOut "$ExpectedWingetId.locale.zh-TW.yaml"
    $enUsLocaleManifestPath = Join-Path $wingetOut "$ExpectedWingetId.locale.en-US.yaml"
    $installerManifestPath = Join-Path $wingetOut "$ExpectedWingetId.installer.yaml"

    foreach ($path in @($versionManifestPath, $zhCnLocaleManifestPath, $zhTwLocaleManifestPath, $enUsLocaleManifestPath, $installerManifestPath)) {
        Assert-True -Condition (Test-Path -LiteralPath $path) -Message "$Name missing winget manifest: $path"
        $content = Get-Content -LiteralPath $path -Raw
        $firstLine = Get-Content -LiteralPath $path -First 1
        Assert-True -Condition ($firstLine -match '^PackageIdentifier:') -Message "$Name winget manifest should start with PackageIdentifier: $path"
        Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($content)) -Message "$Name winget manifest is empty: $path"
        Assert-True -Condition ($content -match 'ManifestVersion: 1\.12\.0') -Message "$Name winget manifest is missing ManifestVersion: $path"
    }

    $versionManifest = Get-Content -LiteralPath $versionManifestPath -Raw
    $zhCnLocaleManifest = Get-Content -LiteralPath $zhCnLocaleManifestPath -Raw
    $zhTwLocaleManifest = Get-Content -LiteralPath $zhTwLocaleManifestPath -Raw
    $enUsLocaleManifest = Get-Content -LiteralPath $enUsLocaleManifestPath -Raw
    $installerManifest = Get-Content -LiteralPath $installerManifestPath -Raw

    Assert-True -Condition ($versionManifest -match [regex]::Escape("PackageIdentifier: '$ExpectedWingetId'")) -Message "$Name version manifest has wrong PackageIdentifier."
    Assert-True -Condition ($versionManifest -match 'DefaultLocale: zh-CN') -Message "$Name version manifest does not use zh-CN as DefaultLocale."
    Assert-True -Condition ($zhCnLocaleManifest -match 'PackageLocale: zh-CN') -Message "$Name zh-CN locale manifest has wrong PackageLocale."
    Assert-True -Condition ($zhCnLocaleManifest -match 'ManifestType: defaultLocale') -Message "$Name zh-CN locale manifest is not defaultLocale."
    Assert-True -Condition ($zhTwLocaleManifest -match 'PackageLocale: zh-TW') -Message "$Name zh-TW locale manifest has wrong PackageLocale."
    Assert-True -Condition ($zhTwLocaleManifest -match 'ManifestType: locale') -Message "$Name zh-TW locale manifest is not locale."
    Assert-True -Condition ($enUsLocaleManifest -match 'PackageLocale: en-US') -Message "$Name en-US locale manifest has wrong PackageLocale."
    Assert-True -Condition ($enUsLocaleManifest -match 'ManifestType: locale') -Message "$Name en-US locale manifest is not locale."
    foreach ($localeContent in @($zhCnLocaleManifest, $zhTwLocaleManifest, $enUsLocaleManifest)) {
        Assert-True -Condition ($localeContent -match 'License: GPL-3\.0') -Message "$Name winget locale manifest has wrong license."
        Assert-True -Condition ($localeContent -match [regex]::Escape("PackageName: 'Context Menu Manager Plus'")) -Message "$Name winget PackageName should remain Context Menu Manager Plus."
    }
    Assert-True -Condition ($installerManifest -match 'Architecture: x64') -Message "$Name installer manifest is missing x64."
    Assert-True -Condition ($installerManifest -match 'Architecture: x86') -Message "$Name installer manifest is missing x86."
    Assert-True -Condition ($installerManifest -match 'Architecture: arm64') -Message "$Name installer manifest is missing arm64."

    Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json | Out-Null
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) ("ContextMenuMgr-package-manager-tests-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null

try {
    Invoke-GenerationCase `
        -Root $root `
        -Name 'stable' `
        -Tag 'v1.7.2' `
        -Prerelease $false `
        -PublishedAt '2026-07-04T13:58:22Z' `
        -ExpectedPackageVersion '1.7.2' `
        -ExpectedWingetId 'PLFJY.ContextMenuMgrPlus' `
        -ExpectedScoopFile 'contextmenumgrplus.json'

    Invoke-GenerationCase `
        -Root $root `
        -Name 'beta' `
        -Tag 'v1.7.2-Beta+abcdef0' `
        -Prerelease $true `
        -PublishedAt '2026-07-04T13:58:22Z' `
        -ExpectedPackageVersion '1.7.2-beta.20260704135822' `
        -ExpectedWingetId 'PLFJY.ContextMenuMgrPlus.Beta' `
        -ExpectedScoopFile 'contextmenumgrplus-beta.json'

    Write-Host "Package manager script tests passed. Fixture root: $root"
}
catch {
    Write-Error $_
    exit 1
}
