param(
    [string]$DictionaryPath = "ContextMenuMgr.Frontend\Resources\EnhanceMenusDic.xml",
    [string]$ReferencePath = "C:\Users\plfjy\Desktop\ContextMenuManager\ContextMenuManager\Properties\Resources\Texts\EnhanceMenusDic.xml"
)

$ErrorActionPreference = "Stop"

$forbiddenTokens = @(
    "ContextMenuMgr",
    "NamedPipe",
    "Pipe",
    "localhost",
    "Backend",
    "TrayHost"
)

$knownItems = @(
    "CopyContent",
    "GetHash",
    "WallPaperLocation",
    "WebSearch",
    "ShowDriveLetters",
    "BatteryReport",
    "VirtualMode",
    "FirewallRules"
)

function Load-XmlDocument([string]$Path) {
    $doc = New-Object System.Xml.XmlDocument
    $doc.PreserveWhitespace = $false
    $doc.Load((Resolve-Path $Path))
    return $doc
}

function Get-ElementChildren($Node) {
    @($Node.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element })
}

function Get-ElementKey($Node) {
    if ($Node.Name -match '^Item\d*$') {
        $key = $Node.GetAttribute("KeyName")
        if (-not [string]::IsNullOrWhiteSpace($key)) {
            return $key
        }
    }

    return $Node.Name
}

function Get-GroupRegistryPath($Group) {
    $regPath = Get-ElementChildren $Group |
        Where-Object { $_.Name -eq "RegPath" } |
        Select-Object -First 1
    if ($null -eq $regPath) {
        return ""
    }

    return $regPath.InnerText.Trim()
}

function Add-EnhanceItemCommands($Map, $GroupRegPath, $Parent, $Prefix) {
    foreach ($item in Get-ElementChildren $Parent | Where-Object { $_.Name -eq "Item" -or $_.Name -match '^Item\d+$' }) {
        $key = Get-ElementKey $item
        $path = if ([string]::IsNullOrEmpty($Prefix)) { "/$key" } else { "$Prefix/$key" }
        $commands = @($item.SelectNodes(".//Command") | ForEach-Object { ConvertTo-CanonicalCommandXml $_.OuterXml })
        $Map["$GroupRegPath|$path"] = $commands -join "`n"

        foreach ($nestedShellItems in @($item.SelectNodes("./SubKey/Shell/SubKey"))) {
            Add-EnhanceItemCommands $Map $GroupRegPath $nestedShellItems $path
        }
    }
}

function ConvertTo-CanonicalCommandXml([string]$Value) {
    $result = $Value
    $result = $result -replace '<FileName>cmd\.exe</FileName>', '<FileName>C:\Windows\System32\cmd.exe</FileName>'
    $result = $result -replace '<FileName>Cmd\.exe</FileName>', '<FileName>C:\Windows\System32\cmd.exe</FileName>'
    $result = $result -replace '<FileName>explorer\.exe</FileName>', '<FileName>C:\Windows\explorer.exe</FileName>'
    $result = $result -replace 'Default="cmd\.exe ', 'Default="C:\Windows\System32\cmd.exe '
    $result = $result -replace 'Default="cmd ', 'Default="C:\Windows\System32\cmd.exe '
    $result = $result -replace 'Default="explorer\.exe ', 'Default="C:\Windows\explorer.exe '
    $result = $result -replace 'Default="explorer ', 'Default="C:\Windows\explorer.exe '
    $result = $result -replace 'start explorer\.exe', 'start C:\Windows\explorer.exe'
    $result = $result -replace 'start explorer(?=&quot;)', 'start C:\Windows\explorer.exe'
    $result = $result -replace 'wsh\.Run\(&quot;explorer /select, &quot; &amp; path\)', 'wsh.Run(&quot;C:\Windows\explorer.exe /select, &quot; &amp; path)'
    return $result
}

function Get-CommandMap($Document) {
    $map = @{}
    foreach ($group in @($Document.DocumentElement.SelectNodes("Group"))) {
        $groupRegPath = Get-GroupRegistryPath $group
        $shell = Get-ElementChildren $group | Where-Object { $_.Name -eq "Shell" } | Select-Object -First 1
        if ($null -ne $shell) {
            Add-EnhanceItemCommands $map $groupRegPath $shell ""
        }
    }

    return $map
}

function Strip-MenuAcceleratorAmpersands([string]$Value) {
    if ([string]::IsNullOrEmpty($Value) -or $Value.StartsWith("@")) {
        return $Value
    }

    $escapedAmpersandToken = [string][char]0xF000
    return $Value.Replace("&&", $escapedAmpersandToken).Replace("&", "").Replace($escapedAmpersandToken, "&")
}

function Assert-MenuAcceleratorUnitCases() {
    $failures = New-Object System.Collections.Generic.List[string]
    $cases = @(
        @{ Input = "Op&en"; Expected = "Open" },
        @{ Input = "as &administrator"; Expected = "as administrator" },
        @{ Input = "Save && Exit"; Expected = "Save & Exit" },
        @{ Input = "@shell32.dll,-37444"; Expected = "@shell32.dll,-37444" }
    )

    foreach ($case in $cases) {
        $actual = Strip-MenuAcceleratorAmpersands $case.Input
        if ($actual -ne $case.Expected) {
            $failures.Add("Strip-MenuAcceleratorAmpersands failed for '$($case.Input)': expected '$($case.Expected)', got '$actual'.")
        }
    }

    return $failures
}

function Assert-NoBareCommandExecutables($Document) {
    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($fileName in @($Document.SelectNodes("//Command/FileName"))) {
        $value = $fileName.InnerText.Trim()
        if ($value -match '^(?i:cmd|cmd\.exe|explorer|explorer\.exe)$') {
            $failures.Add("Bare command executable in FileName: $($fileName.OuterXml)")
        }
    }

    foreach ($command in @($Document.SelectNodes("//Command[@Default]"))) {
        $value = $command.Attributes["Default"].Value.TrimStart()
        if ($value -match '^(?i:cmd|cmd\.exe|explorer|explorer\.exe)(\s|$)') {
            $failures.Add("Bare command executable prefix in Command Default: $($command.OuterXml)")
        }
    }

    foreach ($arguments in @($Document.SelectNodes("//Command/Arguments"))) {
        $value = $arguments.InnerXml
        if ($value -match '(?i)(^|[&|]\s*)start\s+explorer(\.exe)?(\s|$|&quot;)') {
            $failures.Add("Bare explorer launch in Arguments: $($arguments.OuterXml)")
        }
    }

    foreach ($createFile in @($Document.SelectNodes("//Command//*[self::FileName or self::Arguments]/CreateFile"))) {
        foreach ($attribute in @($createFile.Attributes)) {
            $hasBareStartExplorer = $attribute.Value -match '(?i)(^|\s|["])start\s+explorer(\.exe)?(\s|$|")'
            $hasBareWshExplorer = $attribute.Value -match '(?i)wsh\.Run\("explorer\s'
            if ($hasBareStartExplorer -or $hasBareWshExplorer) {
                $failures.Add("Bare explorer launch in CreateFile: $($createFile.OuterXml)")
            }
        }
    }

    return $failures
}

function Assert-NoForbiddenDependencies($Document) {
    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($command in @($Document.SelectNodes("//Command"))) {
        $parts = New-Object System.Collections.Generic.List[string]
        if ($null -ne $command.Attributes["Default"]) {
            $parts.Add($command.Attributes["Default"].Value)
        }

        foreach ($nodeName in @("FileName", "Arguments", "ShellExecute")) {
            foreach ($node in @($command.SelectNodes(".//$nodeName"))) {
                $parts.Add($node.InnerText)
                $parts.Add($node.OuterXml)
            }
        }

        foreach ($createFile in @($command.SelectNodes(".//CreateFile"))) {
            foreach ($attribute in @($createFile.Attributes)) {
                $parts.Add($attribute.Value)
            }
        }

        $haystack = $parts -join "`n"
        foreach ($token in $forbiddenTokens) {
            if ($haystack.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $failures.Add("Forbidden token '$token' in command: $($command.OuterXml)")
            }
        }
    }

    return $failures
}

function Add-Failures($Target, $Source) {
    foreach ($item in @($Source)) {
        if ($null -ne $item) {
            $Target.Add([string]$item)
        }
    }
}

$dictionary = Load-XmlDocument $DictionaryPath
$failures = New-Object System.Collections.Generic.List[string]
Add-Failures $failures (Assert-NoForbiddenDependencies $dictionary)
Add-Failures $failures (Assert-NoBareCommandExecutables $dictionary)
Add-Failures $failures (Assert-MenuAcceleratorUnitCases)

if (Test-Path $ReferencePath) {
    $reference = Load-XmlDocument $ReferencePath
    $actualMap = Get-CommandMap $dictionary
    $referenceMap = Get-CommandMap $reference

    foreach ($itemKey in $knownItems) {
        $actualKeys = @($actualMap.Keys | Where-Object { $_ -like "*|/$itemKey" -or $_ -like "*|/$itemKey/*" })
        if ($actualKeys.Count -eq 0) {
            $failures.Add("Known item '$itemKey' is not present in dictionary map.")
            continue
        }

        foreach ($actualKey in $actualKeys) {
            if (-not $referenceMap.ContainsKey($actualKey)) {
                $failures.Add("Known item '$itemKey' is not present in reference map at $actualKey.")
                continue
            }

            if ($actualMap[$actualKey] -ne $referenceMap[$actualKey]) {
                $failures.Add("Known item '$itemKey' command subtree differs from BluePointLilac at $actualKey.")
            }
        }
    }
}
else {
    Write-Warning "Reference XML was not found at '$ReferencePath'; skipped BluePointLilac known-item comparison."
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "EnhanceMenusDic validation passed."
