#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string] $ManifestPath,

    [string] $BucketRepository = 'PLFJY/scoop-bucket',

    [string] $TokenEnvVar = 'SCOOP_BUCKET_TOKEN',

    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Command {
    param([Parameter(Mandatory)] [string] $Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found."
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)] [string[]] $Arguments,
        [string] $WorkingDirectory = ''
    )

    $previousLocation = Get-Location
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            Set-Location -LiteralPath $WorkingDirectory
        }

        & git @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "git $($Arguments -join ' ') failed."
        }
    }
    finally {
        Set-Location -LiteralPath $previousLocation
    }
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Generated Scoop manifest was not found: $ManifestPath"
}

Assert-Command -Name 'git'

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$manifestName = Split-Path -Leaf $ManifestPath
$appName = [System.IO.Path]::GetFileNameWithoutExtension($manifestName)
$packageVersion = [string] $manifest.version

if ([string]::IsNullOrWhiteSpace($BucketRepository)) {
    throw 'Bucket repository is required.'
}

$token = [System.Environment]::GetEnvironmentVariable($TokenEnvVar)
if (-not $DryRun -and [string]::IsNullOrWhiteSpace($token)) {
    throw "Environment variable '$TokenEnvVar' is required when Scoop push is enabled."
}

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ContextMenuMgr-scoop-bucket-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
$clonePath = Join-Path $workRoot 'bucket-repo'

if ($DryRun) {
    $cloneUrl = "https://github.com/$BucketRepository.git"
}
else {
    $escapedToken = [System.Uri]::EscapeDataString($token)
    $cloneUrl = "https://x-access-token:$escapedToken@github.com/$BucketRepository.git"
}

Invoke-Git -Arguments @('clone', $cloneUrl, $clonePath)
Invoke-Git -Arguments @('config', 'user.name', 'github-actions[bot]') -WorkingDirectory $clonePath
Invoke-Git -Arguments @('config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com') -WorkingDirectory $clonePath

$bucketDirectory = Join-Path $clonePath 'bucket'
New-Item -ItemType Directory -Force -Path $bucketDirectory | Out-Null
Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $bucketDirectory $manifestName) -Force

Invoke-Git -Arguments @('status', '--short') -WorkingDirectory $clonePath
$status = git -C $clonePath status --porcelain
if ([string]::IsNullOrWhiteSpace(($status -join [Environment]::NewLine))) {
    Write-Host 'Scoop bucket already contains the generated manifest; skipping commit.'
    return
}

if ($DryRun) {
    Write-Host 'Dry run enabled; Scoop bucket diff follows.'
    git -C $clonePath diff -- bucket
    git -C $clonePath status --short
    return
}

$commitMessage = "Update $appName to $packageVersion"
Invoke-Git -Arguments @('add', 'bucket') -WorkingDirectory $clonePath
Invoke-Git -Arguments @('commit', '-m', $commitMessage) -WorkingDirectory $clonePath
Invoke-Git -Arguments @('push') -WorkingDirectory $clonePath
Write-Host "Pushed Scoop bucket update: $commitMessage"
