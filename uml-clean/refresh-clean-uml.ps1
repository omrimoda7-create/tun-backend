$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$files = Get-ChildItem -Path $root -Recurse -Filter *.puml |
    Where-Object { $_.Name -notin @("include.puml", "style.puml") }

$primitiveTypes = @(
    "Guid",
    "Guid?",
    "DateTime",
    "DateTime?",
    "bool",
    "bool?",
    "int",
    "int?",
    "long",
    "long?",
    "double",
    "double?",
    "float",
    "float?",
    "decimal",
    "decimal?",
    "string",
    "string?"
)

function Get-StyleIncludePath {
    param([string]$filePath)

    $relativeDir = Split-Path -Parent ($filePath.Substring($root.Length).TrimStart('\'))
    if ([string]::IsNullOrWhiteSpace($relativeDir)) {
        return '.\style.puml'
    }

    $depth = ($relativeDir -split '\\').Count
    return ('..\'+('..\' * ($depth - 1)) + 'style.puml').Replace('..\..\','..\..\')
}

function Normalize-InheritanceLine {
    param([string]$line)

    $normalized = $line.Trim()
    $normalized = [regex]::Replace(
        $normalized,
        '^"(?<base>[^"]+)`1"\s+"<(?<inner>[^"]+)>"\s+<\|--\s+(?<child>.+)$',
        '${base}<${inner}> <|-- ${child}')

    return $normalized
}

function Get-CollectionTypeFromLabel {
    param([string]$label, [string]$helperType)

    if ($label -match '^(?<name>[^<]+)<(?<inner>.+)>$') {
        return @{
            Name = $Matches.name.Trim()
            Type = "$helperType<$($Matches.inner.Trim())>"
        }
    }

    return @{
        Name = $label.Trim()
        Type = $helperType
    }
}

function Build-CleanDiagram {
    param([System.IO.FileInfo]$file)

    $content = Get-Content -Raw $file.FullName
    $lines = $content -split "`r?`n"
    $mainClass = $file.BaseName

    $classBodies = @{}
    $currentClass = $null
    $currentBody = New-Object System.Collections.Generic.List[string]
    $inheritanceLines = New-Object System.Collections.Generic.List[string]
    $associationLines = New-Object System.Collections.Generic.List[string]

    foreach ($rawLine in $lines) {
        $line = $rawLine.Trim()
        if (-not $line -or $line -in @('@startuml', '@enduml')) {
            continue
        }

        if ($null -ne $currentClass) {
            if ($line -eq '}') {
                $classBodies[$currentClass] = @($currentBody)
                $currentClass = $null
                $currentBody = New-Object System.Collections.Generic.List[string]
                continue
            }

            $currentBody.Add($line)
            continue
        }

        if ($line -match '^class\s+(?<name>.+?)\s*\{$') {
            $currentClass = $Matches.name.Trim()
            continue
        }

        if ($line -like '*<|--*') {
            $inheritanceLines.Add((Normalize-InheritanceLine $line))
            continue
        }

        if ($line -match "^$([regex]::Escape($mainClass))\s+(?<arrow>o->|-->)\s+`"(?<label>[^`"]+)`"\s+(?<target>.+)$") {
            $associationLines.Add($line)
        }
    }

    $mainBodyLines = New-Object System.Collections.Generic.List[string]
    $relationLines = New-Object System.Collections.Generic.List[string]

    if ($classBodies.ContainsKey($mainClass)) {
        foreach ($bodyLine in $classBodies[$mainClass]) {
            $mainBodyLines.Add($bodyLine)
        }
    }

    foreach ($relationLine in $associationLines) {
        $match = [regex]::Match($relationLine, "^$([regex]::Escape($mainClass))\s+(?<arrow>o->|-->)\s+`"(?<label>[^`"]+)`"\s+(?<target>.+)$")
        if (-not $match.Success) {
            continue
        }

        $arrow = $match.Groups['arrow'].Value
        $label = $match.Groups['label'].Value.Trim()
        $target = $match.Groups['target'].Value.Trim().Trim('"')

        if ($target -in @('ICollection`1', 'List`1', 'DbSet`1')) {
            $helperType = $target -replace '`1', ''
            $collectionInfo = Get-CollectionTypeFromLabel -label $label -helperType $helperType
            $mainBodyLines.Add("+ $($collectionInfo.Name) : $($collectionInfo.Type)")
            continue
        }

        if ($primitiveTypes -contains $target) {
            $mainBodyLines.Add("+ $label : $target")
            continue
        }

        $mainBodyLines.Add("+ $label : $target")
        $cleanArrow = if ($arrow -eq 'o->') { 'o--' } else { '-->' }
        $relationLines.Add("$mainClass $cleanArrow $target : $label")
    }

    $uniqueBody = $mainBodyLines |
        Where-Object { $_ -and $_.Trim().Length -gt 0 } |
        Select-Object -Unique

    $uniqueRelations = $relationLines | Select-Object -Unique
    $uniqueInheritance = $inheritanceLines | Select-Object -Unique

    $styleInclude = Get-StyleIncludePath -filePath $file.FullName
    $output = New-Object System.Collections.Generic.List[string]

    $output.Add('@startuml')
    $output.Add("!include $styleInclude")
    $output.Add("title $mainClass")
    $output.Add('')
    $output.Add("class $mainClass {")

    foreach ($bodyLine in $uniqueBody) {
        $output.Add("    $bodyLine")
    }

    $output.Add('}')

    foreach ($inheritLine in $uniqueInheritance) {
        if ($inheritLine -notmatch 'ICollection|List`1|DbSet`1') {
            $output.Add($inheritLine)
        }
    }

    foreach ($relationLine in $uniqueRelations) {
        $output.Add($relationLine)
    }

    $output.Add('@enduml')

    Set-Content -Path $file.FullName -Value ($output -join [Environment]::NewLine)
}

foreach ($file in $files) {
    Build-CleanDiagram -file $file
}
