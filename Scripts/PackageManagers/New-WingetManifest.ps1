#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string] $ReleaseMetadataJson,

    [Parameter(Mandatory)]
    [string] $AssetManifestJson,

    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-Json {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "JSON file was not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Assert-Asset {
    param(
        [Parameter(Mandatory)] $Assets,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $Assets.assets.$Name) {
        throw "Asset manifest is missing assets.$Name."
    }
}

function ConvertTo-YamlScalar {
    param([AllowNull()] [string] $Value)

    if ($null -eq $Value) {
        return "''"
    }

    return "'" + ($Value -replace "'", "''") + "'"
}

function Write-TextFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $Lines
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    [System.IO.File]::WriteAllLines($Path, $Lines, [System.Text.UTF8Encoding]::new($false))
}

function Test-RequiredYamlKeys {
    param([Parameter(Mandatory)] [string] $Path)

    $text = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Generated YAML file is empty: $Path"
    }

    if ($text -notmatch '(?m)^ManifestVersion:\s*') {
        throw "Generated YAML file is missing ManifestVersion: $Path"
    }
}

function Get-LocaleDefinitions {
    param([Parameter(Mandatory)] [bool] $IsBeta)

    if ($IsBeta) {
        return @(
            [ordered] @{
                Locale = 'zh-CN'
                ManifestType = 'defaultLocale'
                ShortDescription = 'Context Menu Manager Plus 的 Beta 渠道版本。'
                Description = 'Context Menu Manager Plus Beta 用于提前验证新的右键菜单管理功能和修复，可能包含回归问题。Stable 和 Beta 是互斥渠道，安装一个会替换另一个。'
                Tags = @('右键菜单', '上下文菜单', 'Windows', 'Shell', '注册表', 'WPF', 'Beta', '预发布')
            },
            [ordered] @{
                Locale = 'zh-TW'
                ManifestType = 'locale'
                ShortDescription = 'Context Menu Manager Plus 的 Beta 渠道版本。'
                Description = 'Context Menu Manager Plus Beta 用於提前驗證新的右鍵選單管理功能和修復，可能包含回歸問題。Stable 和 Beta 是互斥渠道，安裝一個會替換另一個。'
                Tags = @('右鍵選單', '內容選單', 'Windows', 'Shell', '登錄檔', 'WPF', 'Beta', '預發布')
            },
            [ordered] @{
                Locale = 'en-US'
                ManifestType = 'locale'
                ShortDescription = 'Beta channel for Context Menu Manager Plus.'
                Description = 'Context Menu Manager Plus Beta is a prerelease channel for validating new context menu management features and fixes. It may contain regressions. Stable and Beta are mutually exclusive channels; installing one replaces the other.'
                Tags = @('context-menu', 'windows', 'shell', 'registry', 'wpf', 'beta', 'prerelease')
            }
        )
    }

    return @(
        [ordered] @{
            Locale = 'zh-CN'
            ManifestType = 'defaultLocale'
            ShortDescription = 'Windows 右键菜单管理工具。'
            Description = 'Context Menu Manager Plus 用于管理 Windows 右键菜单项，检测第三方新增项，并帮助用户审核不需要的菜单项。'
            Tags = @('右键菜单', '上下文菜单', 'Windows', 'Shell', '注册表', 'WPF')
        },
        [ordered] @{
            Locale = 'zh-TW'
            ManifestType = 'locale'
            ShortDescription = 'Windows 右鍵選單管理工具。'
            Description = 'Context Menu Manager Plus 用於管理 Windows 右鍵選單項目，偵測第三方新增項目，並協助使用者審核不需要的選單項目。'
            Tags = @('右鍵選單', '內容選單', 'Windows', 'Shell', '登錄檔', 'WPF')
        },
        [ordered] @{
            Locale = 'en-US'
            ManifestType = 'locale'
            ShortDescription = 'A Windows context menu management tool.'
            Description = 'Context Menu Manager Plus manages Windows context menu entries, detects third-party additions, and helps users review unwanted menu items.'
            Tags = @('context-menu', 'windows', 'shell', 'registry', 'wpf')
        }
    )
}

function New-LocaleManifestLines {
    param(
        [Parameter(Mandatory)] [string] $PackageIdentifier,
        [Parameter(Mandatory)] [string] $PackageVersion,
        [Parameter(Mandatory)] [string] $PackageName,
        [Parameter(Mandatory)] [string] $ReleaseNotesUrl,
        [Parameter(Mandatory)] $Definition
    )

    $lines = @(
        "PackageIdentifier: $(ConvertTo-YamlScalar $PackageIdentifier)",
        "PackageVersion: $(ConvertTo-YamlScalar $PackageVersion)",
        "PackageLocale: $($Definition.Locale)",
        "Publisher: PLFJY",
        "PublisherUrl: https://github.com/PLFJY",
        "PublisherSupportUrl: https://github.com/PLFJY/ContextMenuMgr/issues",
        "PackageName: $(ConvertTo-YamlScalar $PackageName)",
        "PackageUrl: https://github.com/PLFJY/ContextMenuMgr",
        "License: GPL-3.0",
        "LicenseUrl: https://github.com/PLFJY/ContextMenuMgr/blob/main/LICENSE",
        "ShortDescription: $(ConvertTo-YamlScalar $Definition.ShortDescription)",
        "Description: $(ConvertTo-YamlScalar $Definition.Description)",
        "Tags:"
    )

    foreach ($tag in @($Definition.Tags)) {
        $lines += "- $(ConvertTo-YamlScalar $tag)"
    }

    $lines += @(
        "ReleaseNotesUrl: $ReleaseNotesUrl",
        "ManifestType: $($Definition.ManifestType)",
        "ManifestVersion: 1.12.0"
    )

    return $lines
}

$metadata = Read-Json -Path $ReleaseMetadataJson
$assets = Read-Json -Path $AssetManifestJson

Assert-Asset -Assets $assets -Name 'wingetX64'
Assert-Asset -Assets $assets -Name 'wingetX86'
Assert-Asset -Assets $assets -Name 'wingetArm64'

$packageIdentifier = [string] $metadata.wingetPackageIdentifier
$packageVersion = [string] $metadata.packageVersion
$packageName = 'Context Menu Manager Plus'
$isBeta = $packageIdentifier -eq 'PLFJY.ContextMenuMgrPlus.Beta'
$releaseNotesUrl = "https://github.com/PLFJY/ContextMenuMgr/releases/tag/$($metadata.tagName)"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$versionManifest = Join-Path $OutputDirectory "$packageIdentifier.yaml"
$localeDefinitions = @(Get-LocaleDefinitions -IsBeta $isBeta)
$localeManifests = @()
$installerManifest = Join-Path $OutputDirectory "$packageIdentifier.installer.yaml"

Write-TextFile -Path $versionManifest -Lines @(
    "PackageIdentifier: $(ConvertTo-YamlScalar $packageIdentifier)",
    "PackageVersion: $(ConvertTo-YamlScalar $packageVersion)",
    "DefaultLocale: zh-CN",
    "ManifestType: version",
    "ManifestVersion: 1.12.0"
)

foreach ($definition in $localeDefinitions) {
    $localeManifest = Join-Path $OutputDirectory "$packageIdentifier.locale.$($definition.Locale).yaml"
    $localeManifests += $localeManifest
    Write-TextFile -Path $localeManifest -Lines (New-LocaleManifestLines `
            -PackageIdentifier $packageIdentifier `
            -PackageVersion $packageVersion `
            -PackageName $packageName `
            -ReleaseNotesUrl $releaseNotesUrl `
            -Definition $definition)
}

foreach ($requiredLocale in @('zh-CN', 'zh-TW', 'en-US')) {
    $requiredLocaleManifest = Join-Path $OutputDirectory "$packageIdentifier.locale.$requiredLocale.yaml"
    if (-not (Test-Path -LiteralPath $requiredLocaleManifest)) {
        throw "Required winget locale manifest was not generated: $requiredLocaleManifest"
    }
}

Write-TextFile -Path $installerManifest -Lines @(
    "PackageIdentifier: $(ConvertTo-YamlScalar $packageIdentifier)",
    "PackageVersion: $(ConvertTo-YamlScalar $packageVersion)",
    "InstallerType: inno",
    "Scope: machine",
    "UpgradeBehavior: install",
    "InstallModes:",
    "- silent",
    "- silentWithProgress",
    "InstallerSwitches:",
    "  Silent: /VERYSILENT /NORESTART",
    "  SilentWithProgress: /SILENT /NORESTART",
    "Installers:",
    "- Architecture: x64",
    "  InstallerUrl: $($assets.assets.wingetX64.url)",
    "  InstallerSha256: $($assets.assets.wingetX64.sha256)",
    "- Architecture: x86",
    "  InstallerUrl: $($assets.assets.wingetX86.url)",
    "  InstallerSha256: $($assets.assets.wingetX86.sha256)",
    "- Architecture: arm64",
    "  InstallerUrl: $($assets.assets.wingetArm64.url)",
    "  InstallerSha256: $($assets.assets.wingetArm64.sha256)",
    "ManifestType: installer",
    "ManifestVersion: 1.12.0"
)

Test-RequiredYamlKeys -Path $versionManifest
foreach ($localeManifest in $localeManifests) {
    Test-RequiredYamlKeys -Path $localeManifest
}
Test-RequiredYamlKeys -Path $installerManifest

Write-Host "winget manifests: $OutputDirectory"
Write-Output $OutputDirectory
