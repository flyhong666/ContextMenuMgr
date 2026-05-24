#Requires -Version 5.1
param(
    [string] $PreviousTag = '',
    [string] $OutputPath = 'build/release-body.md'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$parent = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}

$lines = @(
    '## Changes',
    ''
)

if ([string]::IsNullOrWhiteSpace($PreviousTag)) {
    $lines += '- Initial release'
}
else {
    $lines += ('Previous tag: `{0}`' -f $PreviousTag)
    $lines += ''

    $revisionRange = '{0}..HEAD' -f $PreviousTag
    $commitLines = & git log '--pretty=format:- %s (%h)' $revisionRange
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to collect release notes from Git range '$revisionRange'."
    }

    if (-not $commitLines) {
        $lines += '- No commits since the previous tag'
    }
    else {
        $lines += $commitLines
    }
}

$lines | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "Release notes written to $OutputPath"
