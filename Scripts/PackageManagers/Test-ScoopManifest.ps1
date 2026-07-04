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

$rawJson = Get-Content -LiteralPath $ManifestPath -Raw
$manifest = $rawJson | ConvertFrom-Json -NoEnumerate

Assert-True -Condition ([string] $manifest.version -eq $ExpectedVersion) -Message "Scoop version '$($manifest.version)' does not match expected version '$ExpectedVersion'."
Assert-True -Condition (-not [string]::IsNullOrWhiteSpace([string] $manifest.url)) -Message 'Scoop url is missing.'
Assert-True -Condition ([string] $manifest.hash -match '^[A-Fa-f0-9]{64}$') -Message 'Scoop hash is not a 64-character SHA256 hex string.'
Assert-True -Condition ([string] $manifest.license -eq 'GPL-3.0-only') -Message "Scoop license must be GPL-3.0-only."

$expectedShortcutName = switch ($ExpectedAppName) {
    'contextmenumgrplus' { 'Context Menu Manager Plus' }
    'contextmenumgrplus-beta' { 'Context Menu Manager Plus Beta' }
    default { throw "Unexpected Scoop app name '$ExpectedAppName'." }
}

Assert-True -Condition ($null -ne $manifest.shortcuts) -Message 'Scoop shortcuts is missing.'
$shortcuts = $manifest.shortcuts
Assert-True -Condition ($shortcuts.Count -ge 1) -Message 'Scoop shortcuts must contain at least one shortcut entry.'
Assert-True -Condition ($shortcuts[0] -is [System.Collections.IList]) -Message 'Scoop first shortcut entry must be an array.'

$firstShortcut = $shortcuts[0]
Assert-True -Condition ($firstShortcut.Count -ge 2) -Message 'Scoop first shortcut entry must contain target and shortcut name.'
Assert-True -Condition ([string] $firstShortcut[0] -eq 'ContextMenuManagerPlus.exe') -Message 'Scoop first shortcut target must be ContextMenuManagerPlus.exe.'
Assert-True -Condition ([string] $firstShortcut[1] -eq $expectedShortcutName) -Message "Scoop first shortcut name must be '$expectedShortcutName'."

$shortcutJson = ConvertTo-Json -InputObject $manifest.shortcuts -Depth 10 -Compress
Assert-True -Condition ($shortcutJson.StartsWith('[[')) -Message "Scoop shortcuts must serialize as a nested array, got: $shortcutJson"

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
