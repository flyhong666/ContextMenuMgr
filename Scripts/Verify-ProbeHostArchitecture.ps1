param(
    [Parameter(Mandatory = $true, ParameterSetName = "Root")]
    [string] $Root,

    [Parameter(ParameterSetName = "Root")]
    [string[]] $Labels = @("x86", "x64", "arm64"),

    [Parameter(ParameterSetName = "Root", ValueFromRemainingArguments = $true)]
    [string[]] $AdditionalLabels = @(),

    [Parameter(Mandatory = $true, ParameterSetName = "File")]
    [string] $Path,

    [Parameter(Mandatory = $true, ParameterSetName = "File")]
    [ValidateSet("x86", "x64", "arm64")]
    [string] $Label,

    [Parameter(ParameterSetName = "File")]
    [string] $MSBuildPlatform = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-PeMachine {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "ProbeHost executable is missing: $Path"
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        if ($stream.Length -lt 0x40) {
            throw "ProbeHost executable is too small to contain a PE header: $Path"
        }

        $reader = [System.IO.BinaryReader]::new($stream)
        $stream.Position = 0x3C
        $peHeaderOffset = $reader.ReadInt32()
        if ($peHeaderOffset -le 0 -or $peHeaderOffset + 6 -gt $stream.Length) {
            throw "ProbeHost executable has an invalid PE header offset: $Path"
        }

        $stream.Position = $peHeaderOffset
        $signature = $reader.ReadUInt32()
        if ($signature -ne 0x00004550) {
            throw "ProbeHost executable is missing a PE signature: $Path"
        }

        return $reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

$expectedMachines = @{
    x86 = 0x014C
    x64 = 0x8664
    arm64 = 0xAA64
}

function Get-ExpectedMachine {
    param([Parameter(Mandatory = $true)] [string] $ArchitectureLabel)

    if (-not $expectedMachines.ContainsKey($ArchitectureLabel)) {
        throw "Unsupported ProbeHost architecture label '$ArchitectureLabel'. Expected one of: x86, x64, arm64."
    }

    return [uint16] $expectedMachines[$ArchitectureLabel]
}

if ($PSCmdlet.ParameterSetName -eq "File") {
    $machine = Get-PeMachine -Path $Path
    $expected = Get-ExpectedMachine -ArchitectureLabel $Label
    if ($machine -ne $expected) {
        throw @"
Native ProbeHost architecture mismatch after build:
Label=$Label
MSBuildPlatform=$MSBuildPlatform
ExpectedMachine=0x$($expected.ToString('X4'))
ActualMachine=0x$($machine.ToString('X4'))
Path=$Path
"@
    }

    Write-Host "Native ProbeHost architecture verified after build: Label=$Label MSBuildPlatform=$MSBuildPlatform Machine=0x$($machine.ToString('X4')) Path=$Path"
    exit 0
}

if ($AdditionalLabels.Count -gt 0) {
    $Labels = @($Labels) + @($AdditionalLabels)
}

foreach ($label in $Labels) {
    $path = Join-Path $Root (Join-Path $label "ContextMenuMgr.ProbeHost.exe")
    $machine = Get-PeMachine -Path $path
    $expected = Get-ExpectedMachine -ArchitectureLabel $label
    if ($machine -ne $expected) {
        throw "ProbeHost architecture mismatch: expected $label at $path, expected machine 0x$($expected.ToString('X4')), but detected 0x$($machine.ToString('X4'))."
    }

    Write-Host "ProbeHost architecture verified: $label => 0x$($machine.ToString('X4'))"
}
