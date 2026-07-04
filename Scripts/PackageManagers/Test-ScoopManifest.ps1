#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string] $ManifestPath,

    [Parameter(Mandatory)]
    [string] $ExpectedAppName,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Scoop manifest was not found: $ManifestPath"
}

$manifestName = [System.IO.Path]::GetFileNameWithoutExtension($ManifestPath)
Assert-True -Condition ($manifestName -eq $ExpectedAppName) -Message "Scoop manifest filename '$manifestName' does not match expected app '$ExpectedAppName'."

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json

Assert-True -Condition ([string] $manifest.version -eq $ExpectedVersion) -Message "Scoop version '$($manifest.version)' does not match expected version '$ExpectedVersion'."
Assert-True -Condition (-not [string]::IsNullOrWhiteSpace([string] $manifest.url)) -Message 'Scoop url is missing.'
Assert-True -Condition ([string] $manifest.hash -match '^[A-Fa-f0-9]{64}$') -Message 'Scoop hash is not a 64-character SHA256 hex string.'
Assert-True -Condition ([string] $manifest.license -eq 'GPL-3.0-only') -Message "Scoop license must be GPL-3.0-only."

$shortcutText = @($manifest.shortcuts) | ConvertTo-Json -Depth 10 -Compress
Assert-True -Condition ($shortcutText -match [regex]::Escape('ContextMenuManagerPlus.exe')) -Message 'Scoop shortcuts must reference ContextMenuManagerPlus.exe.'

Assert-True -Condition (@($manifest.persist) -contains 'Data') -Message 'Scoop persist must contain Data.'

$preInstall = (@($manifest.pre_install) -join "`n")
Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($preInstall)) -Message 'Scoop pre_install is missing.'

if ($ExpectedAppName -eq 'contextmenumgrplus-beta') {
    Assert-True -Condition ($preInstall -match 'contextmenumgrplus\\current') -Message 'Scoop beta pre_install must check for stable contextmenumgrplus.'
}
elseif ($ExpectedAppName -eq 'contextmenumgrplus') {
    Assert-True -Condition ($preInstall -match 'contextmenumgrplus-beta\\current') -Message 'Scoop stable pre_install must check for contextmenumgrplus-beta.'
}
else {
    throw "Unexpected Scoop app name '$ExpectedAppName'."
}

Write-Host "Scoop manifest validation passed: $ManifestPath"
