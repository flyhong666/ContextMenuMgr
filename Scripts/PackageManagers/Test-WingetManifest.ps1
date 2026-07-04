#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string] $ManifestDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ManifestDirectory -PathType Container)) {
    throw "winget manifest directory was not found: $ManifestDirectory"
}

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

$validationOutputPath = Join-Path (Split-Path -Parent $ManifestDirectory) 'winget-validation-output.txt'
@(
    'winget --version'
    @($wingetVersionOutput)
    ''
    "winget validate --manifest $ManifestDirectory --ignore-warnings"
    @($validationOutput)
    ''
    "ExitCode: $validationExitCode"
) | Set-Content -LiteralPath $validationOutputPath -Encoding UTF8
Write-Host "Wrote winget validation output to '$validationOutputPath'."

if ($validationExitCode -ne 0) {
    throw "winget validate failed for '$ManifestDirectory' with exit code $validationExitCode."
}

Write-Host "winget validate passed for '$ManifestDirectory'."
