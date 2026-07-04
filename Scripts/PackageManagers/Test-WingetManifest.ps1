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

Write-Host "Running winget validate for '$ManifestDirectory'."
$validationOutput = & winget validate --manifest $ManifestDirectory 2>&1
$validationExitCode = $LASTEXITCODE

foreach ($line in @($validationOutput)) {
    Write-Host $line
}

if ($validationExitCode -ne 0) {
    throw "winget validate failed for '$ManifestDirectory' with exit code $validationExitCode."
}

Write-Host "winget validate passed for '$ManifestDirectory'."
