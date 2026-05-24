#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [ValidateSet('Debug', 'Release', 'Beta')]
    [string] $Configuration,

    [string] $FrontendProject = '',

    [switch] $WriteGitHubOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
if (-not $scriptDir) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptDir '..')).Path
Import-Module (Join-Path $scriptDir 'Build.Common.psm1') -Force -DisableNameChecking
Set-Location -LiteralPath $repoRoot

if ([string]::IsNullOrWhiteSpace($FrontendProject)) {
    $FrontendProject = Join-Path $repoRoot 'ContextMenuMgr.Frontend\ContextMenuMgr.Frontend.csproj'
}

function Get-ReleaseVersionFromTag {
    param([Parameter(Mandatory)] [string] $Tag)

    if ($Tag -notmatch '^v(?<version>\d+(\.\d+){2,3})$') {
        return $null
    }

    try {
        return [version] $Matches['version']
    }
    catch {
        return $null
    }
}

function Get-PreviousReleaseTag {
    param([Parameter(Mandatory)] [string] $CurrentReleaseTag)

    $currentVersion = Get-ReleaseVersionFromTag -Tag $CurrentReleaseTag
    $releaseTags = git tag --sort=-creatordate |
        Where-Object { Get-ReleaseVersionFromTag -Tag $_ } |
        Where-Object { $_ -ne $CurrentReleaseTag }

    if ($null -ne $currentVersion) {
        $releaseTags = $releaseTags |
            Where-Object {
                $candidateVersion = Get-ReleaseVersionFromTag -Tag $_
                $null -ne $candidateVersion -and $candidateVersion -lt $currentVersion
            }
    }

    return $releaseTags |
        Sort-Object { Get-ReleaseVersionFromTag -Tag $_ } -Descending |
        Select-Object -First 1
}

function Get-PreviousAnyTag {
    param([Parameter(Mandatory)] [string] $CurrentReleaseTag)

    return git tag --sort=-creatordate |
        Where-Object { $_ -ne $CurrentReleaseTag } |
        Select-Object -First 1
}

git fetch --tags --force
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to fetch Git tags.'
}

$releaseVersion = Get-BuildVersion -ProjectPath $FrontendProject -Configuration $Configuration
$isPrerelease = [string]::Equals($Configuration, 'Beta', [System.StringComparison]::OrdinalIgnoreCase)
$shouldCreateRelease = -not [string]::Equals($Configuration, 'Debug', [System.StringComparison]::OrdinalIgnoreCase)
$releaseTag = "v$releaseVersion"

if ([string]::Equals($Configuration, 'Release', [System.StringComparison]::OrdinalIgnoreCase)) {
    $releaseName = "$releaseTag-Release"
    $previousTag = Get-PreviousReleaseTag -CurrentReleaseTag $releaseTag
}
elseif ($isPrerelease) {
    $releaseName = $releaseTag
    $previousTag = Get-PreviousAnyTag -CurrentReleaseTag $releaseTag
}
else {
    $releaseName = "$releaseTag-Debug"
    $previousTag = Get-PreviousAnyTag -CurrentReleaseTag $releaseTag
}

$outputs = [ordered]@{
    release_version = $releaseVersion
    release_tag = $releaseTag
    release_name = $releaseName
    prerelease = $isPrerelease.ToString().ToLowerInvariant()
    should_create_release = $shouldCreateRelease.ToString().ToLowerInvariant()
    previous_tag = $previousTag
}

foreach ($item in $outputs.GetEnumerator()) {
    Write-Host "$($item.Key): $($item.Value)"
}

$shouldWriteGitHubOutput = $WriteGitHubOutput -or -not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)
if ($shouldWriteGitHubOutput) {
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        throw 'GITHUB_OUTPUT is not set.'
    }

    foreach ($item in $outputs.GetEnumerator()) {
        "$($item.Key)=$($item.Value)" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    }
}
