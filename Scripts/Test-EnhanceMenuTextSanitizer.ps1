$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$contractsProject = Join-Path $repoRoot "ContextMenuMgr.Contracts\ContextMenuMgr.Contracts.csproj"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ContextMenuMgr-EnhanceMenuTextSanitizer-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $projectFile = Join-Path $testRoot "EnhanceMenuTextSanitizerTest.csproj"
    $programFile = Join-Path $testRoot "Program.cs"

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$contractsProject" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $projectFile -Encoding UTF8

    @'
using ContextMenuMgr.Contracts;

var cases = new (string Input, string Expected)[]
{
    ("Op&en", "Open"),
    ("as &administrator", "as administrator"),
    ("Save && Exit", "Save & Exit"),
    ("@shell32.dll,-37444", "@shell32.dll,-37444")
};

foreach (var testCase in cases)
{
    var actual = EnhanceMenuTextSanitizer.StripMenuAcceleratorAmpersands(testCase.Input);
    if (!string.Equals(actual, testCase.Expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"StripMenuAcceleratorAmpersands failed for '{testCase.Input}': expected '{testCase.Expected}', got '{actual}'.");
    }
}

Console.WriteLine("EnhanceMenuTextSanitizer tests passed.");
'@ | Set-Content -LiteralPath $programFile -Encoding UTF8

    dotnet run --project $projectFile | Out-Host
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
