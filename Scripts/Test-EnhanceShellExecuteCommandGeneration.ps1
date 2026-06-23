param()

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ContextMenuMgr-EnhanceShellExecute-" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null

    $projectFile = Join-Path $testRoot "EnhanceShellExecuteTest.csproj"
    $programFile = Join-Path $testRoot "Program.cs"
    $backendProject = Join-Path $repoRoot "ContextMenuMgr.Backend\ContextMenuMgr.Backend.csproj"

    Set-Content -Path $projectFile -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$backendProject" />
  </ItemGroup>
</Project>
"@

    Set-Content -Path $programFile -Encoding UTF8 -Value @'
using System.Reflection;
using System.Xml.Linq;
using ContextMenuMgr.Backend.Services;

var catalogType = typeof(ContextMenuRegistryCatalog);
var compileMethod = catalogType.GetMethod(
    "CompileEnhanceCommandValue",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("CompileEnhanceCommandValue was not found.");

static string Compile(MethodInfo compileMethod, string xml)
{
    var result = compileMethod.Invoke(null, new object[] { XElement.Parse(xml), "en-US" })
        ?? throw new InvalidOperationException("CompileEnhanceCommandValue returned null.");
    var commandProperty = result.GetType().GetProperty("Command")
        ?? throw new InvalidOperationException("Command property was not found.");
    return (string)(commandProperty.GetValue(result) ?? string.Empty);
}

static void AssertContains(string command, string expected, string name)
{
    if (!command.Contains(expected, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{name}: expected command to contain '{expected}', got '{command}'.");
    }
}

static void AssertNotContains(string command, string forbidden, string name)
{
    if (command.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{name}: command must not contain '{forbidden}', got '{command}'.");
    }
}

static void AssertNoForbiddenRuntimeDependency(string command, string name)
{
    foreach (var token in new[] { "ContextMenuMgr", "Backend", "TrayHost", "NamedPipe", "pipe" })
    {
        AssertNotContains(command, token, name);
    }
}

var cases = new[]
{
    new
    {
        Name = "empty ShellExecute powershell direct",
        Xml = """
            <Command>
              <ShellExecute />
              <FileName>powershell.exe</FileName>
              <Arguments>-noexit get-filehash "%1"</Arguments>
            </Command>
            """,
        MustContain = "powershell.exe -noexit get-filehash \"%1\"",
        MustNotContain = "mshta"
    },
    new
    {
        Name = "open ShellExecute explorer direct",
        Xml = """
            <Command>
              <ShellExecute Verb="open" />
              <FileName>explorer</FileName>
              <Arguments>"https://www.bing.com"</Arguments>
            </Command>
            """,
        MustContain = "C:\Windows\\explorer.exe \"https://www.bing.com\"",
        MustNotContain = "vbscript"
    },
    new
    {
        Name = "window style powershell direct",
        Xml = """
            <Command>
              <ShellExecute WindowStyle="3" />
              <FileName>powershell.exe</FileName>
              <Arguments>-noexit get-date</Arguments>
            </Command>
            """,
        MustContain = "powershell.exe -noexit get-date",
        MustNotContain = "mshta"
    },
    new
    {
        Name = "window style cmd direct",
        Xml = """
            <Command>
              <ShellExecute WindowStyle="0" />
              <FileName>cmd.exe</FileName>
              <Arguments>/d /c echo hi</Arguments>
            </Command>
            """,
        MustContain = "C:\Windows\\System32\\cmd.exe /d /c echo hi",
        MustNotContain = "mshta"
    },
    new
    {
        Name = "absolute path with spaces quoted",
        Xml = """
            <Command>
              <ShellExecute />
              <FileName>C:\Program Files\Test App\tool.exe</FileName>
              <Arguments>--flag "%1"</Arguments>
            </Command>
            """,
        MustContain = "\"C:\\Program Files\\Test App\\tool.exe\" --flag \"%1\"",
        MustNotContain = "mshta"
    },
    new
    {
        Name = "runas ShellExecute remains wrapped",
        Xml = """
            <Command>
              <ShellExecute Verb="runas" />
              <FileName>cmd.exe</FileName>
              <Arguments>/d /c whoami /groups</Arguments>
            </Command>
            """,
        MustContain = "mshta vbscript:createobject",
        MustNotContain = "ContextMenuMgr"
    }
};

foreach (var testCase in cases)
{
    var command = Compile(compileMethod, testCase.Xml);
    AssertContains(command, testCase.MustContain, testCase.Name);
    AssertNotContains(command, testCase.MustNotContain, testCase.Name);
    AssertNoForbiddenRuntimeDependency(command, testCase.Name);
}

Console.WriteLine("Enhance ShellExecute command generation tests passed.");
'@

    dotnet run --project $projectFile
}
finally {
    if (Test-Path $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
