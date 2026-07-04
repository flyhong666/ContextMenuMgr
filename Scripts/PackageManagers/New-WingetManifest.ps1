#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string] $ReleaseMetadataJson,

    [Parameter(Mandatory)]
    [string] $AssetManifestJson,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [switch] $SkipWingetValidate
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

$metadata = Read-Json -Path $ReleaseMetadataJson
$assets = Read-Json -Path $AssetManifestJson

Assert-Asset -Assets $assets -Name 'wingetX64'
Assert-Asset -Assets $assets -Name 'wingetX86'
Assert-Asset -Assets $assets -Name 'wingetArm64'

$packageIdentifier = [string] $metadata.wingetPackageIdentifier
$packageVersion = [string] $metadata.packageVersion
$packageName = [string] $metadata.wingetPackageName
$releaseNotesUrl = "https://github.com/PLFJY/ContextMenuMgr/releases/tag/$($metadata.tagName)"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$versionManifest = Join-Path $OutputDirectory "$packageIdentifier.yaml"
$localeManifest = Join-Path $OutputDirectory "$packageIdentifier.locale.en-US.yaml"
$installerManifest = Join-Path $OutputDirectory "$packageIdentifier.installer.yaml"

Write-TextFile -Path $versionManifest -Lines @(
    "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json",
    "PackageIdentifier: $(ConvertTo-YamlScalar $packageIdentifier)",
    "PackageVersion: $(ConvertTo-YamlScalar $packageVersion)",
    "DefaultLocale: en-US",
    "ManifestType: version",
    "ManifestVersion: 1.12.0"
)

Write-TextFile -Path $localeManifest -Lines @(
    "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json",
    "PackageIdentifier: $(ConvertTo-YamlScalar $packageIdentifier)",
    "PackageVersion: $(ConvertTo-YamlScalar $packageVersion)",
    "PackageLocale: en-US",
    "Publisher: PLFJY",
    "PublisherUrl: https://github.com/PLFJY",
    "PublisherSupportUrl: https://github.com/PLFJY/ContextMenuMgr/issues",
    "PackageName: $(ConvertTo-YamlScalar $packageName)",
    "PackageUrl: https://github.com/PLFJY/ContextMenuMgr",
    "License: GPL-3.0",
    "LicenseUrl: https://github.com/PLFJY/ContextMenuMgr/blob/main/LICENSE",
    "ShortDescription: A Windows context menu management tool.",
    "Description: Context Menu Manager Plus manages Windows context menu entries, detects third-party additions, and lets users review unwanted menu items.",
    "Tags:",
    "- context-menu",
    "- windows",
    "- shell",
    "- registry",
    "- wpf",
    "ReleaseNotesUrl: $releaseNotesUrl",
    "ManifestType: defaultLocale",
    "ManifestVersion: 1.12.0"
)

Write-TextFile -Path $installerManifest -Lines @(
    "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json",
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
Test-RequiredYamlKeys -Path $localeManifest
Test-RequiredYamlKeys -Path $installerManifest

if ($SkipWingetValidate) {
    Write-Host 'winget validate skipped by request.'
}
elseif ($null -ne (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Host "Running winget validate for '$OutputDirectory'."
    $validationOutput = & winget validate --manifest $OutputDirectory 2>&1
    $validationExitCode = $LASTEXITCODE

    foreach ($line in @($validationOutput)) {
        Write-Host $line
    }

    if ($validationExitCode -ne 0) {
        throw "winget validate failed for '$OutputDirectory' with exit code $validationExitCode."
    }
}
else {
    Write-Host 'winget CLI was not found; deterministic YAML generation checks completed.'
}

Write-Host "winget manifests: $OutputDirectory"
Write-Output $OutputDirectory
