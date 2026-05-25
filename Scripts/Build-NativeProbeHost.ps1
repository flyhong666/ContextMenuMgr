param(
    [Parameter(Mandatory = $true)]
    [string] $Project,

    [Parameter(Mandatory = $true)]
    [string] $Configuration,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Win32", "x64", "ARM64")]
    [string] $Platform,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string] $IntermediateDirectory,

    [string] $ForceRebuild = "false"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-MSBuildPath {
    $command = Get-Command "MSBuild.exe" -ErrorAction SilentlyContinue
    if ($command -and $command.Source) {
        return $command.Source
    }

    $vswhereCandidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($vswhere in $vswhereCandidates) {
        if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
            continue
        }

        $installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($installationPath)) {
            $candidate = Join-Path $installationPath.Trim() "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }

    throw "Unable to locate MSBuild.exe. Install Visual Studio Build Tools with the C++ workload."
}

function Get-ArchitectureLabel {
    param([Parameter(Mandatory = $true)] [string] $Platform)

    switch ($Platform) {
        "Win32" { return "x86" }
        "x64" { return "x64" }
        "ARM64" { return "arm64" }
        default { return $Platform }
    }
}

function Test-NativeProbeHostUpToDate {
    param(
        [Parameter(Mandatory = $true)] [string] $ProjectPath,
        [Parameter(Mandatory = $true)] [string] $TargetExe
    )

    if (-not (Test-Path -LiteralPath $TargetExe -PathType Leaf)) {
        return $false
    }

    $projectDirectory = Split-Path -Parent $ProjectPath
    $sourceDirectory = Join-Path $projectDirectory "src"
    $jsonHeader = Join-Path $projectDirectory "third_party\nlohmann\json.hpp"
    $inputs = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    $inputs.Add((Get-Item -LiteralPath $ProjectPath)) | Out-Null

    if (Test-Path -LiteralPath $sourceDirectory -PathType Container) {
        Get-ChildItem -LiteralPath $sourceDirectory -Recurse -File -Include *.cpp,*.h |
            ForEach-Object { $inputs.Add($_) | Out-Null }
    }

    if (Test-Path -LiteralPath $jsonHeader -PathType Leaf) {
        $inputs.Add((Get-Item -LiteralPath $jsonHeader)) | Out-Null
    }

    if ($inputs.Count -eq 0) {
        return $false
    }

    $targetTimestamp = (Get-Item -LiteralPath $TargetExe).LastWriteTimeUtc
    $latestInputTimestamp = ($inputs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
    return $targetTimestamp -gt $latestInputTimestamp
}

$resolvedProject = (Resolve-Path -LiteralPath $Project).Path
$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$resolvedIntermediateDirectory = [System.IO.Path]::GetFullPath($IntermediateDirectory)
$architectureLabel = Get-ArchitectureLabel -Platform $Platform
$targetExe = Join-Path $resolvedOutputDirectory "ContextMenuMgr.ProbeHost.exe"
$forceRebuildEnabled = [System.Convert]::ToBoolean($ForceRebuild)

if ($forceRebuildEnabled) {
    if (Test-Path -LiteralPath $resolvedOutputDirectory) {
        Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force
    }

    if (Test-Path -LiteralPath $resolvedIntermediateDirectory) {
        Remove-Item -LiteralPath $resolvedIntermediateDirectory -Recurse -Force
    }
}

if (-not $forceRebuildEnabled -and (Test-NativeProbeHostUpToDate -ProjectPath $resolvedProject -TargetExe $targetExe)) {
    Write-Host "Native ProbeHost $architectureLabel is up to date; skipping build."
    exit 0
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $resolvedIntermediateDirectory -Force | Out-Null

$msbuild = Resolve-MSBuildPath
& $msbuild `
    $resolvedProject `
    /nologo `
    /m `
    /t:Build `
    "/p:Configuration=$Configuration" `
    "/p:Platform=$Platform" `
    "/p:OutDir=$resolvedOutputDirectory\" `
    "/p:IntDir=$resolvedIntermediateDirectory\"

if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed for native ProbeHost platform $Platform. ExitCode=$LASTEXITCODE"
}
