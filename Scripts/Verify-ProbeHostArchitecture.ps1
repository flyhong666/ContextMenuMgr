param(
    [Parameter(Mandatory = $true)]
    [string] $Root,

    [string[]] $Labels = @("x86", "x64", "arm64")
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

foreach ($label in $Labels) {
    $path = Join-Path $Root (Join-Path $label "ContextMenuMgr.ProbeHost.exe")
    $machine = Get-PeMachine -Path $path
    $expected = [uint16] $expectedMachines[$label]
    if ($machine -ne $expected) {
        throw "ProbeHost architecture mismatch: expected $label at $path, but detected 0x$($machine.ToString('X4'))."
    }

    Write-Host "ProbeHost architecture verified: $label => 0x$($machine.ToString('X4'))"
}
