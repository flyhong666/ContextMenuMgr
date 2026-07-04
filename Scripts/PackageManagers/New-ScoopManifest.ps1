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

function Get-PreInstallLines {
    param([Parameter(Mandatory)] [string] $Channel)

    if ($Channel -eq 'beta') {
        return @(
            'if (Test-Path "$scoopdir\apps\contextmenumgrplus\current") {',
            "    error 'Context Menu Manager Plus stable is already installed. Uninstall contextmenumgrplus before installing the beta package.'",
            '    exit 1',
            '}'
        )
    }

    return @(
        'if (Test-Path "$scoopdir\apps\contextmenumgrplus-beta\current") {',
        "    error 'Context Menu Manager Plus Beta is already installed. Uninstall contextmenumgrplus-beta before installing the stable package.'",
        '    exit 1',
        '}'
    )
}

$metadata = Read-Json -Path $ReleaseMetadataJson
$assets = Read-Json -Path $AssetManifestJson

if ($null -eq $assets.assets.scoopPortable) {
    throw 'Asset manifest is missing assets.scoopPortable.'
}

$notes = @(
    'This framework-dependent portable package requires the .NET 10 Desktop Runtime.',
    'Context Menu Manager Plus may ask to install or repair its Windows service for elevated menu operations.'
)

if ($metadata.channel -eq 'beta') {
    $notes += 'Beta builds may contain regressions; use them only when you want to validate prerelease changes.'
}

$manifest = [ordered] @{
    version = [string] $metadata.packageVersion
    description = 'A Windows context menu management tool.'
    homepage = 'https://github.com/PLFJY/ContextMenuMgr'
    license = 'GPL-3.0-only'
    url = [string] $assets.assets.scoopPortable.url
    hash = [string] $assets.assets.scoopPortable.sha256
    shortcuts = @(
        @('ContextMenuManagerPlus.exe', [string] $metadata.scoopShortcutName)
    )
    persist = 'Data'
    notes = $notes
    pre_install = @(Get-PreInstallLines -Channel ([string] $metadata.channel))
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$manifestPath = Join-Path $OutputDirectory ([string] $metadata.scoopManifestFile)
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json | Out-Null

Write-Host "Scoop manifest: $manifestPath"
Write-Output $manifestPath
