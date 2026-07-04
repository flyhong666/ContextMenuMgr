#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string] $ManifestDirectory,

    [string] $ValidationOutputPath = ''
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

if (-not (Test-Path -LiteralPath $ManifestDirectory -PathType Container)) {
    throw "winget manifest directory was not found: $ManifestDirectory"
}

$installerManifestPath = Get-ChildItem -LiteralPath $ManifestDirectory -Filter '*.installer.yaml' -File | Select-Object -First 1
if ($null -eq $installerManifestPath) {
    throw "winget installer manifest was not found in '$ManifestDirectory'."
}

$installerManifest = Get-Content -LiteralPath $installerManifestPath.FullName -Raw
Assert-True -Condition ($installerManifest -match '(?m)^-\s*Architecture:\s*x64\s*$') -Message 'winget installer manifest is missing x64 installer entry.'
Assert-True -Condition ($installerManifest -match '(?m)^-\s*Architecture:\s*x86\s*$') -Message 'winget installer manifest is missing x86 installer entry.'
Assert-True -Condition ($installerManifest -match '(?m)^-\s*Architecture:\s*arm64\s*$') -Message 'winget installer manifest is missing arm64 installer entry.'
Assert-True -Condition ($installerManifest -match 'x64-self-contained-Setup\.exe') -Message 'winget installer manifest must use x64 self-contained setup.'
Assert-True -Condition ($installerManifest -match 'x86-self-contained-Setup\.exe') -Message 'winget installer manifest must use x86 self-contained setup.'
Assert-True -Condition ($installerManifest -match 'arm64-self-contained-Setup\.exe') -Message 'winget installer manifest must use arm64 self-contained setup.'
Assert-True -Condition ($installerManifest -notmatch 'framework-dependent') -Message 'winget installer manifest must not reference framework-dependent assets.'

if ($null -eq (Get-Command winget -ErrorAction SilentlyContinue)) {
    throw 'winget command was not found. Install winget before validating generated manifests.'
}

Write-Host 'winget version:'
$wingetVersionOutput = & winget --version 2>&1
foreach ($line in @($wingetVersionOutput)) {
    Write-Host $line
}

Write-Host "Running winget validate for '$ManifestDirectory' with --ignore-warnings."
$validationOutput = & winget validate --manifest $ManifestDirectory --ignore-warnings 2>&1
$validationExitCode = $LASTEXITCODE

foreach ($line in @($validationOutput)) {
    Write-Host $line
}

if ([string]::IsNullOrWhiteSpace($ValidationOutputPath)) {
    $ValidationOutputPath = Join-Path (Split-Path -Parent $ManifestDirectory) 'winget-validation-output.txt'
}

$outputParent = Split-Path -Parent $ValidationOutputPath
if (-not [string]::IsNullOrWhiteSpace($outputParent)) {
    New-Item -ItemType Directory -Force -Path $outputParent | Out-Null
}

@(
    'winget --version'
    @($wingetVersionOutput)
    ''
    "winget validate --manifest $ManifestDirectory --ignore-warnings"
    @($validationOutput)
    ''
    "ExitCode: $validationExitCode"
) | Set-Content -LiteralPath $ValidationOutputPath -Encoding UTF8
Write-Host "Wrote winget validation output to '$ValidationOutputPath'."

if ($validationExitCode -ne 0) {
    throw "winget validate failed for '$ManifestDirectory' with exit code $validationExitCode."
}

Write-Host "winget validate passed for '$ManifestDirectory'."
