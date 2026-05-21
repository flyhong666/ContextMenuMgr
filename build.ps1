#Requires -Version 5.1
param(
    [string] $Configuration = "Release",
    [string] $AppId = "45156332-3408-47B7-B5D2-2567E5888F64",
    [string[]] $Platforms = @("win-x64", "win-x86", "win-arm64"),
    [string[]] $DistributionModes = @("self-contained", "framework-dependent"),
    [int] $MaxParallel = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ScriptDirectory {
    if ($PSScriptRoot) { return $PSScriptRoot }

    $path = $MyInvocation.MyCommand.Path
    if ($path) { return (Split-Path -Parent $path) }

    throw "Cannot determine script directory. Please run this as a .ps1 file."
}

function Invoke-External {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter()] [string[]] $Arguments = @(),
        [Parameter()] [string] $ErrorMessage = "External command failed."
    )

    Write-Host ">> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_ }

    if ($LASTEXITCODE -ne 0) {
        throw "$ErrorMessage (ExitCode=$LASTEXITCODE): $FilePath $($Arguments -join ' ')"
    }
}

function Test-PropertyGroupMatchesConfiguration {
    param(
        [Parameter(Mandatory)] [object] $PropertyGroup,
        [Parameter(Mandatory)] [string] $Configuration
    )

    $condition = [string] $PropertyGroup.GetAttribute("Condition")
    if ([string]::IsNullOrWhiteSpace($condition)) {
        return $true
    }

    $expanded = $condition.Replace('$(Configuration)', $Configuration)
    $match = [regex]::Match(
        $expanded,
        "^\s*['""]?(?<left>[^'""]*)['""]?\s*(?<operator>==|!=)\s*['""]?(?<right>[^'""]*)['""]?\s*$")

    if (-not $match.Success) {
        return $false
    }

    $left = $match.Groups["left"].Value.Trim()
    $right = $match.Groups["right"].Value.Trim()
    $equals = [string]::Equals($left, $right, [System.StringComparison]::OrdinalIgnoreCase)

    if ($match.Groups["operator"].Value -eq "==") {
        return $equals
    }

    return -not $equals
}

function Get-ProjectPropertyValue {
    param(
        [Parameter(Mandatory)] [string] $ProjectPath,
        [Parameter(Mandatory)] [string] $PropertyName,
        [Parameter(Mandatory)] [string] $Configuration
    )

    [xml] $projectXml = Get-Content -LiteralPath $ProjectPath
    $propertyGroups = @($projectXml.Project.PropertyGroup)
    $value = $null

    foreach ($group in $propertyGroups) {
        if (-not (Test-PropertyGroupMatchesConfiguration -PropertyGroup $group -Configuration $Configuration)) {
            continue
        }

        $property = $group.SelectSingleNode($PropertyName)
        if ($property) {
            $value = [string] $property.InnerText
        }
    }

    return $value
}

function Get-FrontendVersion {
    param(
        [Parameter(Mandatory)] [string] $ProjectPath,
        [Parameter(Mandatory)] [string] $Configuration
    )

    $informationalVersion = Get-ProjectPropertyValue -ProjectPath $ProjectPath -PropertyName "InformationalVersion" -Configuration $Configuration
    if (-not [string]::IsNullOrWhiteSpace($informationalVersion)) {
        return $informationalVersion
    }

    $fileVersion = Get-ProjectPropertyValue -ProjectPath $ProjectPath -PropertyName "FileVersion" -Configuration $Configuration
    if (-not [string]::IsNullOrWhiteSpace($fileVersion)) {
        return $fileVersion
    }

    return "0.0.0"
}

function Ensure-FileExists {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description is missing: $Path"
    }
}

function Remove-DirectoryIfExists {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    try {
        [System.IO.Directory]::Delete((Resolve-Path -LiteralPath $Path).Path, $true)
        return
    }
    catch {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
        }

        Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
            ForEach-Object {
                try {
                    $_.Attributes = [System.IO.FileAttributes]::Normal
                }
                catch {
                }

                Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
            }

        Get-ChildItem -LiteralPath $Path -Recurse -Force -Directory -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            ForEach-Object {
                Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
            }

        Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
    }
}

function Resolve-IsccPath {
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $candidates = @(
        (Join-Path $RepoRoot "Installer\Inno Setup 6\ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command -and $command.Source) {
        return $command.Source
    }

    throw "Unable to locate ISCC.exe. Install Inno Setup 6 or place it under Installer\Inno Setup 6\ISCC.exe."
}

$ScriptDir = Get-ScriptDirectory
Set-Location -Path $ScriptDir

$RepoRoot = $ScriptDir
$SolutionPath = Join-Path $RepoRoot "ContextMenuMgr.slnx"
$FrontendProject = Join-Path $RepoRoot "ContextMenuMgr.Frontend\ContextMenuMgr.Frontend.csproj"
$BackendProject = Join-Path $RepoRoot "ContextMenuMgr.Backend\ContextMenuMgr.Backend.csproj"
$TrayHostProject = Join-Path $RepoRoot "ContextMenuMgr.TrayHost\ContextMenuMgr.TrayHost.csproj"
$NuGetConfig = Join-Path $RepoRoot "NuGet.Config"
$PublishRoot = Join-Path $RepoRoot "build\publish"
$DistRoot = Join-Path $RepoRoot "build\dist"
$Version = Get-FrontendVersion -ProjectPath $FrontendProject -Configuration $Configuration
$ArtifactProductName = "ContextMenuMgrPlus"
$IsccPath = Resolve-IsccPath -RepoRoot $RepoRoot
$InstallerIss = Join-Path $RepoRoot "Installer\build_Installer.iss"

Remove-DirectoryIfExists -Path $PublishRoot
Remove-DirectoryIfExists -Path $DistRoot

New-Item -ItemType Directory -Path $PublishRoot | Out-Null
New-Item -ItemType Directory -Path $DistRoot | Out-Null

Ensure-FileExists -Path $IsccPath -Description "Inno Setup compiler"
Ensure-FileExists -Path $InstallerIss -Description "Inno Setup script"

Invoke-External -FilePath "dotnet" -Arguments @(
    "restore",
    $SolutionPath,
    "--configfile", $NuGetConfig
) -ErrorMessage "dotnet restore failed"

$buildTasks = New-Object System.Collections.Generic.List[object]
foreach ($distributionMode in $DistributionModes) {
    foreach ($platform in $Platforms) {
        $buildTasks.Add([pscustomobject]@{
            Kind = "installer"
            DistributionMode = $distributionMode
            Platform = $platform
        }) | Out-Null
    }
}

$buildTasks.Add([pscustomobject]@{
    Kind = "portable-framework-dependent"
    DistributionMode = "framework-dependent"
    Platform = "anycpu"
}) | Out-Null

[void] (Get-Command "Start-Job" -ErrorAction Stop)
Write-Host "Using Windows PowerShell 5.1 Start-Job for parallel build tasks." -ForegroundColor DarkGray

function Test-HasRunningJobForBuildPlatform {
    param(
        [Parameter()] [object[]] $Jobs = @(),
        [Parameter(Mandatory)] [string] $Platform
    )

    if ($Platform -eq "anycpu") {
        return $false
    }

    return @(
        $Jobs | Where-Object {
            $_.State -eq 'Running' -and $_.Name.EndsWith("-$Platform", [System.StringComparison]::OrdinalIgnoreCase)
        }
    ).Count -gt 0
}

$jobInitScript = {
    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'

    function Invoke-External {
        param(
            [Parameter(Mandatory)] [string] $FilePath,
            [Parameter()] [string[]] $Arguments = @(),
            [Parameter()] [string] $ErrorMessage = "External command failed."
        )

        Write-Host ">> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
        & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_ }

        if ($LASTEXITCODE -ne 0) {
            throw "$ErrorMessage (ExitCode=$LASTEXITCODE): $FilePath $($Arguments -join ' ')"
        }
    }

    function Get-RuntimeIdentifier {
        param([Parameter(Mandatory)] [string] $Platform)

        switch ($Platform.ToLowerInvariant()) {
            "anycpu" { return "" }
            "win-x64" { return "win-x64" }
            "win-x86" { return "win-x86" }
            "win-arm64" { return "win-arm64" }
            default { throw "Unsupported platform '$Platform'. Supported values: anycpu, win-x64, win-x86, win-arm64." }
        }
    }

    function Get-ArtifactPlatformLabel {
        param([Parameter(Mandatory)] [string] $Platform)

        switch ($Platform.ToLowerInvariant()) {
            "anycpu" { return "anycpu" }
            "win-x64" { return "x64" }
            "win-x86" { return "x86" }
            "win-arm64" { return "arm64" }
            default { throw "Unsupported platform '$Platform'. Supported values: anycpu, win-x64, win-x86, win-arm64." }
        }
    }

    function Ensure-FileExists {
        param(
            [Parameter(Mandatory)] [string] $Path,
            [Parameter(Mandatory)] [string] $Description
        )

        if (-not (Test-Path -LiteralPath $Path)) {
            throw "$Description is missing: $Path"
        }
    }

    function Remove-DirectoryIfExists {
        param([Parameter(Mandatory)] [string] $Path)

        if (-not (Test-Path -LiteralPath $Path)) {
            return
        }

        try {
            [System.IO.Directory]::Delete((Resolve-Path -LiteralPath $Path).Path, $true)
            return
        }
        catch {
            try {
                Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
                return
            }
            catch {
            }

            Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
                ForEach-Object {
                    try {
                        $_.Attributes = [System.IO.FileAttributes]::Normal
                    }
                    catch {
                    }

                    Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
                }

            Get-ChildItem -LiteralPath $Path -Recurse -Force -Directory -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending |
                ForEach-Object {
                    Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
                }

            Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
        }
    }

    function Get-InstallerArchitectureOptions {
        param([Parameter(Mandatory)] [string] $Platform)

        switch ($Platform.ToLowerInvariant()) {
            "win-x64" {
                return @{
                    Allowed = "x64compatible"
                    InstallIn64BitMode = "x64compatible"
                }
            }
            "win-x86" {
                return @{
                    Allowed = "x86compatible"
                    InstallIn64BitMode = ""
                }
            }
            "win-arm64" {
                return @{
                    Allowed = "arm64"
                    InstallIn64BitMode = "arm64"
                }
            }
            default {
                throw "Unsupported installer platform '$Platform'. Supported values: win-x64, win-x86, win-arm64."
            }
        }
    }

    function Get-DistributionModeOptions {
        param([Parameter(Mandatory)] [string] $DistributionMode)

        switch ($DistributionMode.ToLowerInvariant()) {
            "self-contained" {
                return @{
                    SelfContained = "true"
                    InstallerSuffix = "self-contained"
                    UseDotNetDependencyInstaller = "0"
                }
            }
            "framework-dependent" {
                return @{
                    SelfContained = "false"
                    InstallerSuffix = "framework-dependent"
                    UseDotNetDependencyInstaller = "1"
                }
            }
            default {
                throw "Unsupported distribution mode '$DistributionMode'. Supported values: self-contained, framework-dependent."
            }
        }
    }

    function New-DotNetRestoreArguments {
        param(
            [Parameter(Mandatory)] [string] $ProjectPath,
            [Parameter()] [string] $RuntimeIdentifier = "",
            [Parameter(Mandatory)] [string] $NuGetConfig,
            [Parameter(Mandatory)] [string] $ArtifactsPath
        )

        $arguments = @("restore", $ProjectPath)

        if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
            $arguments += @("-r", $RuntimeIdentifier)
        }

        $arguments += @(
            "--configfile", $NuGetConfig,
            "--artifacts-path", $ArtifactsPath
        )

        return $arguments
    }

    function New-DotNetPublishArguments {
        param(
            [Parameter(Mandatory)] [string] $ProjectPath,
            [Parameter(Mandatory)] [string] $Configuration,
            [Parameter()] [string] $RuntimeIdentifier = "",
            [Parameter(Mandatory)] [string] $SelfContained,
            [Parameter(Mandatory)] [string] $OutputPath,
            [Parameter(Mandatory)] [string] $ArtifactsPath
        )

        $arguments = @(
            "publish", $ProjectPath,
            "-c", $Configuration
        )

        if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
            $arguments += @("-r", $RuntimeIdentifier)
        }

        $arguments += @(
            "--self-contained", $SelfContained,
            "--no-restore",
            "--artifacts-path", $ArtifactsPath,
            "-p:UseAppHost=true",
            "-o", $OutputPath
        )

        if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier) -and $SelfContained -eq "false") {
            $arguments += @("-p:PlatformTarget=AnyCPU")
        }

        return $arguments
    }

    function Publish-Application {
        param(
            [Parameter(Mandatory)] [string] $Configuration,
            [Parameter(Mandatory)] [string] $DistributionMode,
            [Parameter(Mandatory)] [string] $Platform,
            [Parameter(Mandatory)] [string] $FrontendProject,
            [Parameter(Mandatory)] [string] $BackendProject,
            [Parameter(Mandatory)] [string] $TrayHostProject,
            [Parameter(Mandatory)] [string] $PublishRoot,
            [Parameter(Mandatory)] [string] $NuGetConfig,
            [Parameter(Mandatory)] [string] $PublishGroup
        )

        $distributionOptions = Get-DistributionModeOptions -DistributionMode $DistributionMode
        $runtimeIdentifier = Get-RuntimeIdentifier -Platform $Platform
        $platformLabel = Get-ArtifactPlatformLabel -Platform $Platform

        $publishDir = Join-Path $PublishRoot (Join-Path $PublishGroup (Join-Path $DistributionMode $platformLabel))
        $taskArtifactsRoot = Join-Path $PublishRoot (Join-Path "_artifacts" (Join-Path $PublishGroup (Join-Path $DistributionMode $platformLabel)))

        Remove-DirectoryIfExists -Path $publishDir

        New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
        New-Item -ItemType Directory -Path $taskArtifactsRoot -Force | Out-Null

        $frontendRestoreArguments = New-DotNetRestoreArguments `
            -ProjectPath $FrontendProject `
            -RuntimeIdentifier $runtimeIdentifier `
            -NuGetConfig $NuGetConfig `
            -ArtifactsPath $taskArtifactsRoot

        $frontendPublishArguments = New-DotNetPublishArguments `
            -ProjectPath $FrontendProject `
            -Configuration $Configuration `
            -RuntimeIdentifier $runtimeIdentifier `
            -SelfContained $distributionOptions.SelfContained `
            -OutputPath $publishDir `
            -ArtifactsPath $taskArtifactsRoot

        $backendRestoreArguments = New-DotNetRestoreArguments `
            -ProjectPath $BackendProject `
            -RuntimeIdentifier $runtimeIdentifier `
            -NuGetConfig $NuGetConfig `
            -ArtifactsPath $taskArtifactsRoot

        $backendPublishArguments = New-DotNetPublishArguments `
            -ProjectPath $BackendProject `
            -Configuration $Configuration `
            -RuntimeIdentifier $runtimeIdentifier `
            -SelfContained $distributionOptions.SelfContained `
            -OutputPath $publishDir `
            -ArtifactsPath $taskArtifactsRoot

        $trayHostRestoreArguments = New-DotNetRestoreArguments `
            -ProjectPath $TrayHostProject `
            -RuntimeIdentifier $runtimeIdentifier `
            -NuGetConfig $NuGetConfig `
            -ArtifactsPath $taskArtifactsRoot

        $trayHostPublishArguments = New-DotNetPublishArguments `
            -ProjectPath $TrayHostProject `
            -Configuration $Configuration `
            -RuntimeIdentifier $runtimeIdentifier `
            -SelfContained $distributionOptions.SelfContained `
            -OutputPath $publishDir `
            -ArtifactsPath $taskArtifactsRoot

        Invoke-External -FilePath "dotnet" -Arguments $frontendRestoreArguments -ErrorMessage "dotnet restore failed for frontend ($platformLabel, $DistributionMode)"
        Invoke-External -FilePath "dotnet" -Arguments $frontendPublishArguments -ErrorMessage "dotnet publish failed for frontend ($platformLabel, $DistributionMode)"

        Invoke-External -FilePath "dotnet" -Arguments $backendRestoreArguments -ErrorMessage "dotnet restore failed for backend ($platformLabel, $DistributionMode)"
        Invoke-External -FilePath "dotnet" -Arguments $backendPublishArguments -ErrorMessage "dotnet publish failed for backend ($platformLabel, $DistributionMode)"

        Invoke-External -FilePath "dotnet" -Arguments $trayHostRestoreArguments -ErrorMessage "dotnet restore failed for tray host ($platformLabel, $DistributionMode)"
        Invoke-External -FilePath "dotnet" -Arguments $trayHostPublishArguments -ErrorMessage "dotnet publish failed for tray host ($platformLabel, $DistributionMode)"

        Ensure-FileExists -Path (Join-Path $publishDir "ContextMenuManagerPlus.exe") -Description "Frontend executable"
        Ensure-FileExists -Path (Join-Path $publishDir "ContextMenuManagerPlus.Service.exe") -Description "Backend service executable"
        Ensure-FileExists -Path (Join-Path $publishDir "ContextMenuManagerPlus.Service.dll") -Description "Backend service DLL"
        Ensure-FileExists -Path (Join-Path $publishDir "ContextMenuManagerPlus.TrayHost.exe") -Description "Tray host executable"

        return $publishDir
    }

    function New-PortableArchive {
        param(
            [Parameter(Mandatory)] [string] $PublishDir,
            [Parameter(Mandatory)] [string] $DistRoot,
            [Parameter(Mandatory)] [string] $ArtifactProductName,
            [Parameter(Mandatory)] [string] $Version,
            [Parameter(Mandatory)] [string] $DistributionMode,
            [Parameter(Mandatory)] [string] $Platform
        )

        $platformLabel = Get-ArtifactPlatformLabel -Platform $Platform

        if ($DistributionMode -eq "framework-dependent" -and $platformLabel -eq "anycpu") {
            $archiveFileName = "$ArtifactProductName-$Version-framework-dependent-portable.zip"
        }
        else {
            $archiveFileName = "$ArtifactProductName-$Version-$platformLabel-$DistributionMode-portable.zip"
        }

        $archivePath = Join-Path $DistRoot $archiveFileName

        if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }

        Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $archivePath -CompressionLevel Optimal -Force
        Ensure-FileExists -Path $archivePath -Description "Portable package"

        return $archivePath
    }

    function Invoke-InstallerBuildTarget {
        param(
            [Parameter(Mandatory)] [string] $Configuration,
            [Parameter(Mandatory)] [string] $DistributionMode,
            [Parameter(Mandatory)] [string] $Platform,
            [Parameter(Mandatory)] [string] $FrontendProject,
            [Parameter(Mandatory)] [string] $BackendProject,
            [Parameter(Mandatory)] [string] $TrayHostProject,
            [Parameter(Mandatory)] [string] $PublishRoot,
            [Parameter(Mandatory)] [string] $DistRoot,
            [Parameter(Mandatory)] [string] $Version,
            [Parameter(Mandatory)] [string] $ArtifactProductName,
            [Parameter(Mandatory)] [string] $IsccPath,
            [Parameter(Mandatory)] [string] $InstallerIss,
            [Parameter(Mandatory)] [string] $AppId,
            [Parameter(Mandatory)] [string] $NuGetConfig
        )

        $distributionOptions = Get-DistributionModeOptions -DistributionMode $DistributionMode
        $platformLabel = Get-ArtifactPlatformLabel -Platform $Platform
        $publishDir = Publish-Application `
            -Configuration $Configuration `
            -DistributionMode $DistributionMode `
            -Platform $Platform `
            -FrontendProject $FrontendProject `
            -BackendProject $BackendProject `
            -TrayHostProject $TrayHostProject `
            -PublishRoot $PublishRoot `
            -NuGetConfig $NuGetConfig `
            -PublishGroup "installer"

        $installerOptions = Get-InstallerArchitectureOptions -Platform $Platform
        $setupBaseName = "$ArtifactProductName-$Version-$platformLabel-$($distributionOptions.InstallerSuffix)-Setup"

        $isccArguments = @(
            "/DMyArchitecturesAllowed=$($installerOptions.Allowed)",
            "/DMyArchitecturesInstallIn64BitMode=$($installerOptions.InstallIn64BitMode)",
            "/DMyUseDotNetDependencyInstaller=$($distributionOptions.UseDotNetDependencyInstaller)",
            "/DMyAppId=$AppId",
            "/DMyAppBuildDir=$publishDir",
            "/DMyOutputDir=$DistRoot",
            "/DMyAppSetupName=$setupBaseName",
            $InstallerIss
        )

        Invoke-External -FilePath $IsccPath -Arguments $isccArguments -ErrorMessage "Inno Setup packaging failed for $platformLabel ($DistributionMode)"

        $artifacts = New-Object System.Collections.Generic.List[string]
        $installerPath = Join-Path $DistRoot ($setupBaseName + ".exe")
        Ensure-FileExists -Path $installerPath -Description "Installer package"
        $artifacts.Add($installerPath) | Out-Null

        if ($DistributionMode -eq "self-contained") {
            $portableArchive = New-PortableArchive `
                -PublishDir $publishDir `
                -DistRoot $DistRoot `
                -ArtifactProductName $ArtifactProductName `
                -Version $Version `
                -DistributionMode $DistributionMode `
                -Platform $Platform
            $artifacts.Add($portableArchive) | Out-Null
        }

        return $artifacts.ToArray()
    }

    function Invoke-PortableFrameworkDependentBuildTarget {
        param(
            [Parameter(Mandatory)] [string] $Configuration,
            [Parameter(Mandatory)] [string] $FrontendProject,
            [Parameter(Mandatory)] [string] $BackendProject,
            [Parameter(Mandatory)] [string] $TrayHostProject,
            [Parameter(Mandatory)] [string] $PublishRoot,
            [Parameter(Mandatory)] [string] $DistRoot,
            [Parameter(Mandatory)] [string] $Version,
            [Parameter(Mandatory)] [string] $ArtifactProductName,
            [Parameter(Mandatory)] [string] $NuGetConfig
        )

        $publishDir = Publish-Application `
            -Configuration $Configuration `
            -DistributionMode "framework-dependent" `
            -Platform "anycpu" `
            -FrontendProject $FrontendProject `
            -BackendProject $BackendProject `
            -TrayHostProject $TrayHostProject `
            -PublishRoot $PublishRoot `
            -NuGetConfig $NuGetConfig `
            -PublishGroup "portable"

        return New-PortableArchive `
            -PublishDir $publishDir `
            -DistRoot $DistRoot `
            -ArtifactProductName $ArtifactProductName `
            -Version $Version `
            -DistributionMode "framework-dependent" `
            -Platform "anycpu"
    }
}

$jobWorkerPath = Join-Path $PublishRoot "_build-worker.ps1"
$jobWorkerScript = @'
param(
    [Parameter(Mandatory)] [string] $Kind,
    [Parameter(Mandatory)] [string] $Configuration,
    [Parameter(Mandatory)] [string] $DistributionMode,
    [Parameter(Mandatory)] [string] $Platform,
    [Parameter(Mandatory)] [string] $FrontendProject,
    [Parameter(Mandatory)] [string] $BackendProject,
    [Parameter(Mandatory)] [string] $TrayHostProject,
    [Parameter(Mandatory)] [string] $PublishRoot,
    [Parameter(Mandatory)] [string] $DistRoot,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $ArtifactProductName,
    [Parameter(Mandatory)] [string] $IsccPath,
    [Parameter(Mandatory)] [string] $InstallerIss,
    [Parameter(Mandatory)] [string] $AppId,
    [Parameter(Mandatory)] [string] $NuGetConfig
)

__JOB_INIT_SCRIPT__

switch ($Kind) {
    "installer" {
        Invoke-InstallerBuildTarget `
            -Configuration $Configuration `
            -DistributionMode $DistributionMode `
            -Platform $Platform `
            -FrontendProject $FrontendProject `
            -BackendProject $BackendProject `
            -TrayHostProject $TrayHostProject `
            -PublishRoot $PublishRoot `
            -DistRoot $DistRoot `
            -Version $Version `
            -ArtifactProductName $ArtifactProductName `
            -IsccPath $IsccPath `
            -InstallerIss $InstallerIss `
            -AppId $AppId `
            -NuGetConfig $NuGetConfig
    }
    "portable-framework-dependent" {
        Invoke-PortableFrameworkDependentBuildTarget `
            -Configuration $Configuration `
            -FrontendProject $FrontendProject `
            -BackendProject $BackendProject `
            -TrayHostProject $TrayHostProject `
            -PublishRoot $PublishRoot `
            -DistRoot $DistRoot `
            -Version $Version `
            -ArtifactProductName $ArtifactProductName `
            -NuGetConfig $NuGetConfig
    }
    default {
        throw "Unsupported build task kind '$Kind'."
    }
}
'@

$jobWorkerScript = $jobWorkerScript.Replace("__JOB_INIT_SCRIPT__", $jobInitScript.ToString())
Set-Content -LiteralPath $jobWorkerPath -Value $jobWorkerScript -Encoding UTF8

$artifacts = New-Object System.Collections.Generic.List[string]

$runningJobs = @()
$failed = $false

foreach ($task in $buildTasks) {
    while (
        @($runningJobs | Where-Object { $_.State -eq 'Running' }).Count -ge $MaxParallel `
        -or (Test-HasRunningJobForBuildPlatform -Jobs $runningJobs -Platform $task.Platform)
    ) {
        $finishedJob = Wait-Job -Job $runningJobs -Any

        try {
            $jobOutput = Receive-Job -Job $finishedJob -ErrorAction Stop
            foreach ($item in @($jobOutput)) {
                if (-not [string]::IsNullOrWhiteSpace($item)) {
                    $artifacts.Add([string] $item) | Out-Null
                }
            }
        }
        catch {
            $failed = $true
            Write-Host ""
            Write-Host "Parallel build job failed: $($finishedJob.Name)" -ForegroundColor Red
            Write-Host $_
        }
        finally {
            Remove-Job -Job $finishedJob -Force
            $runningJobs = @($runningJobs | Where-Object { $_.Id -ne $finishedJob.Id })
        }

        if ($failed) {
            break
        }
    }

    if ($failed) {
        break
    }

    if ($task.Kind -eq "installer") {
        $jobName = "$($task.Kind)-$($task.DistributionMode)-$($task.Platform)"
    }
    else {
        $jobName = "$($task.Kind)-$($task.Platform)"
    }

    $jobArguments = @(
        $task.Kind,
        $Configuration,
        $task.DistributionMode,
        $task.Platform,
        $FrontendProject,
        $BackendProject,
        $TrayHostProject,
        $PublishRoot,
        $DistRoot,
        $Version,
        $ArtifactProductName,
        $IsccPath,
        $InstallerIss,
        $AppId,
        $NuGetConfig
    )

    $job = Start-Job -Name $jobName -FilePath $jobWorkerPath -ArgumentList $jobArguments

    $runningJobs += $job
}

while (-not $failed -and $runningJobs.Count -gt 0) {
    $finishedJob = Wait-Job -Job $runningJobs -Any

    try {
        $jobOutput = Receive-Job -Job $finishedJob -ErrorAction Stop
        foreach ($item in @($jobOutput)) {
            if (-not [string]::IsNullOrWhiteSpace($item)) {
                $artifacts.Add([string] $item) | Out-Null
            }
        }
    }
    catch {
        $failed = $true
        Write-Host ""
        Write-Host "Parallel build job failed: $($finishedJob.Name)" -ForegroundColor Red
        Write-Host $_
    }
    finally {
        Remove-Job -Job $finishedJob -Force
        $runningJobs = @($runningJobs | Where-Object { $_.Id -ne $finishedJob.Id })
    }
}

if ($failed) {
    throw "One or more parallel build jobs failed."
}

$manifestPath = Join-Path $DistRoot "artifacts.txt"
$artifacts = $artifacts | Sort-Object
$artifacts | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "Build completed successfully." -ForegroundColor Green
Write-Host "Version: $Version"
Write-Host "AppId: $AppId"
Write-Host "Artifacts:"
foreach ($artifact in $artifacts) {
    Write-Host "  $artifact"
}
