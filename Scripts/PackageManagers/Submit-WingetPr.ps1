#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string] $ManifestDirectory,

    [Parameter(Mandatory)]
    [string] $WingetForkRepository,

    [string] $TargetRepository = 'microsoft/winget-pkgs',

    [Parameter(Mandatory)]
    [string] $PackageIdentifier,

    [Parameter(Mandatory)]
    [string] $PackageVersion,

    [Parameter(Mandatory)]
    [string] $ReleaseTag,

    [string] $TokenEnvVar = 'WINGET_PR_TOKEN',

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

function Get-WingetManifestRelativePath {
    param(
        [Parameter(Mandatory)] [string] $Identifier,
        [Parameter(Mandatory)] [string] $Version
    )

    $segments = $Identifier.Split('.')
    if ($segments.Count -lt 2) {
        throw "Invalid winget package identifier '$Identifier'."
    }

    $publisher = $segments[0]
    $firstLetter = $publisher.Substring(0, 1).ToLowerInvariant()
    return Join-Path (Join-Path 'manifests' $firstLetter) (Join-Path ($segments -join [System.IO.Path]::DirectorySeparatorChar) $Version)
}

if (-not (Test-Path -LiteralPath $ManifestDirectory)) {
    throw "Generated winget manifest directory was not found: $ManifestDirectory"
}

if ([string]::IsNullOrWhiteSpace($WingetForkRepository)) {
    throw 'Winget fork repository is required.'
}

Assert-Command -Name 'git'
if (-not $DryRun) {
    Assert-Command -Name 'gh'
}

$token = [System.Environment]::GetEnvironmentVariable($TokenEnvVar)
if (-not $DryRun -and [string]::IsNullOrWhiteSpace($token)) {
    throw "Environment variable '$TokenEnvVar' is required when winget PR submission is enabled."
}

if (-not $DryRun) {
    $env:GH_TOKEN = $token
}

$branchPrefix = 'contextmenumgrplus'
if ($PackageIdentifier -like '*.Beta') {
    $branchPrefix = 'contextmenumgrplus-beta'
}

$safeVersion = $PackageVersion -replace '[^A-Za-z0-9._-]', '-'
$branchName = "$branchPrefix-$safeVersion"
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ContextMenuMgr-winget-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
$clonePath = Join-Path $workRoot 'winget-pkgs'

if ($DryRun) {
    $cloneUrl = "https://github.com/$WingetForkRepository.git"
}
else {
    $escapedToken = [System.Uri]::EscapeDataString($token)
    $cloneUrl = "https://x-access-token:$escapedToken@github.com/$WingetForkRepository.git"
}

Invoke-Git -Arguments @('clone', $cloneUrl, $clonePath)
Invoke-Git -Arguments @('config', 'user.name', 'github-actions[bot]') -WorkingDirectory $clonePath
Invoke-Git -Arguments @('config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com') -WorkingDirectory $clonePath

Invoke-Git -Arguments @('checkout', '-B', $branchName) -WorkingDirectory $clonePath
$targetRelativePath = Get-WingetManifestRelativePath -Identifier $PackageIdentifier -Version $PackageVersion
$targetPath = Join-Path $clonePath $targetRelativePath
New-Item -ItemType Directory -Force -Path $targetPath | Out-Null
Get-ChildItem -LiteralPath $ManifestDirectory -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $targetPath -Force
}

$status = git -C $clonePath status --porcelain
if ([string]::IsNullOrWhiteSpace(($status -join [Environment]::NewLine))) {
    Write-Host 'winget fork already contains the generated manifest; checking for an existing PR.'
}
else {
    if ($DryRun) {
        Write-Host "Dry run enabled; winget manifest path would be '$targetRelativePath'."
        git -C $clonePath diff -- $targetRelativePath
        git -C $clonePath status --short
        return
    }

    Invoke-Git -Arguments @('add', $targetRelativePath) -WorkingDirectory $clonePath
    Invoke-Git -Arguments @('commit', '-m', "New version: $PackageIdentifier version $PackageVersion") -WorkingDirectory $clonePath
    Invoke-Git -Arguments @('push', '--force-with-lease', 'origin', $branchName) -WorkingDirectory $clonePath
}

if ($DryRun) {
    Write-Host 'Dry run enabled; winget PR was not opened.'
    return
}

$existingPrJson = gh pr list --repo $TargetRepository --head "$($WingetForkRepository.Split('/')[0]):$branchName" --state open --json url --limit 1
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($existingPrJson)) {
    $existingPr = $existingPrJson | ConvertFrom-Json
    if ($existingPr.Count -gt 0) {
        Write-Host "Existing winget PR: $($existingPr[0].url)"
        return
    }
}

$body = @"
Generated from PLFJY/ContextMenuMgr release $ReleaseTag.

Stable and Beta are mutually exclusive channels and share the same installer AppId and service identity intentionally.

Beta package versions are normalized from the GitHub Release publish time.
"@

$title = "New version: $PackageIdentifier version $PackageVersion"
gh pr create --repo $TargetRepository --head "$($WingetForkRepository.Split('/')[0]):$branchName" --base master --title $title --body $body
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to create winget PR.'
}
