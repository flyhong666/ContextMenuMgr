#Requires -Version 5.1
param(
    [string] $ArtifactDirectory = 'build/dist',
    [string] $ReleaseBodyPath = 'build/release-body.md'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory was not found: $ArtifactDirectory"
}

$artifacts = Get-ChildItem -LiteralPath $ArtifactDirectory -File |
    Where-Object { $_.Extension -in '.exe', '.zip' } |
    Sort-Object Name

if (-not $artifacts) {
    throw "No .exe or .zip artifacts were found in $ArtifactDirectory."
}

$hashRows = foreach ($artifact in $artifacts) {
    $hash = Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256
    $fileName = $artifact.Name.Replace('|', '\|')
    '| {0} | {1} |' -f $fileName, $hash.Hash.ToLowerInvariant()
}

$releaseLines = @(
    '',
    '## SHA256',
    '',
    '| File | SHA256 |',
    '|---|---|'
) + $hashRows

$releaseLines | Add-Content -LiteralPath $ReleaseBodyPath -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $summaryLines = @(
        '### SHA256',
        '',
        '| File | SHA256 |',
        '|---|---|'
    ) + $hashRows

    $summaryLines | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding UTF8
}

Write-Host "SHA256 table appended to $ReleaseBodyPath"
