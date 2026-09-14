[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceMesh,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [Parameter(Mandatory)]
    [ValidateScript({ [double]::IsFinite($_) -and $_ -gt 0.0 })]
    [double] $OutlineWidth,

    [ValidateSet('WeldedPosition', 'SourcePosition', 'SplitRenderNormal')]
    [string] $ExpansionMode = 'SplitRenderNormal'
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$sourcePath = (Resolve-Path -LiteralPath $SourceMesh).Path
$destinationPath = [IO.Path]::GetFullPath($OutputPath)

if ([string]::Equals($sourcePath, $destinationPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'SourceMesh and OutputPath must be different; preview meshes never replace source assets in place.'
}

function Parse-Number {
    param([string] $Value, [string] $Context)

    $parsed = 0.0
    if (-not [double]::TryParse(
            $Value,
            [Globalization.NumberStyles]::Float,
            $culture,
            [ref] $parsed) -or
        -not [double]::IsFinite($parsed)) {
        throw "Invalid number '$Value' in $Context."
    }
    return $parsed
}

function Format-Number {
    param([double] $Value)
    return $Value.ToString('0.00000000', $culture)
}

function Resolve-ObjIndex {
    param([int] $Index, [int] $Count, [string] $Context)

    if ($Index -eq 0) {
        throw "OBJ index 0 is invalid in $Context."
    }
    $resolved = if ($Index -gt 0) { $Index } else { $Count + $Index + 1 }
    if ($resolved -lt 1 -or $resolved -gt $Count) {
        throw "OBJ index $Index is out of range in $Context."
    }
    return $resolved
}

$sourceLines = [IO.File]::ReadAllLines($sourcePath)
$positions = [Collections.Generic.List[double[]]]::new()
$normals = [Collections.Generic.List[double[]]]::new()
$faces = [Collections.Generic.List[object]]::new()
$activeMaterial = $null

for ($lineIndex = 0; $lineIndex -lt $sourceLines.Count; $lineIndex++) {
    $lineNumber = $lineIndex + 1
    $content = ($sourceLines[$lineIndex] -split '#', 2)[0].Trim()
    if ($content.Length -eq 0) {
        continue
    }

    $parts = $content -split '\s+'
    switch ($parts[0]) {
        'v' {
            if ($parts.Count -lt 4) {
                throw "Vertex at line $lineNumber has fewer than three coordinates."
            }
            $positions.Add(@(
                (Parse-Number $parts[1] "vertex at line $lineNumber"),
                (Parse-Number $parts[2] "vertex at line $lineNumber"),
                (Parse-Number $parts[3] "vertex at line $lineNumber")))
        }
        'vn' {
            if ($parts.Count -lt 4) {
                throw "Normal at line $lineNumber has fewer than three coordinates."
            }
            $normal = @(
                (Parse-Number $parts[1] "normal at line $lineNumber"),
                (Parse-Number $parts[2] "normal at line $lineNumber"),
                (Parse-Number $parts[3] "normal at line $lineNumber"))
            $length = [Math]::Sqrt(
                $normal[0] * $normal[0] +
                $normal[1] * $normal[1] +
                $normal[2] * $normal[2])
            if ($length -le 1e-12) {
                throw "Normal at line $lineNumber has zero length."
            }
            $normals.Add(@(
                ($normal[0] / $length),
                ($normal[1] / $length),
                ($normal[2] / $length)))
        }
        'usemtl' {
            $activeMaterial = $content.Substring(6).Trim()
            if ($activeMaterial -eq '__deadlimit_outline') {
                throw 'Source mesh already uses the reserved material __deadlimit_outline.'
            }
        }
        'f' {
            if ($parts.Count -lt 4) {
                throw "Face at line $lineNumber has fewer than three corners."
            }
            $faces.Add([pscustomobject]@{
                LineNumber = $lineNumber
                Material = $activeMaterial
                Tokens = @($parts[1..($parts.Count - 1)])
                PositionCount = $positions.Count
                NormalCount = $normals.Count
            })
        }
    }
}

if ($positions.Count -eq 0 -or $faces.Count -eq 0) {
    throw 'Source mesh must contain vertices and faces.'
}
if ($normals.Count -eq 0) {
    throw 'Source mesh has no render normals; safe outline displacement is undefined.'
}

$shellVertices = [Collections.Generic.List[double[]]]::new()
$shellFaces = [Collections.Generic.List[string[]]]::new()
$cornerMap = @{}
$weldedNormals = @{}
$positionWeldKeys = [Collections.Generic.List[string]]::new()
foreach ($position in $positions) {
    $positionWeldKeys.Add(
        ($position | ForEach-Object { $_.ToString('R', $culture) }) -join '|')
}

if ($ExpansionMode -ne 'SplitRenderNormal') {
    foreach ($face in $faces) {
        foreach ($token in $face.Tokens) {
            $indices = $token -split '/'
            if ($indices.Count -lt 3 -or [string]::IsNullOrWhiteSpace($indices[2])) {
                throw "Face at line $($face.LineNumber) has no per-corner render normal."
            }
            $positionRaw = 0
            $normalRaw = 0
            if (-not [int]::TryParse($indices[0], [ref] $positionRaw) -or
                -not [int]::TryParse($indices[2], [ref] $normalRaw)) {
                throw "Face at line $($face.LineNumber) contains an invalid vertex/normal index."
            }
            $positionIndex = Resolve-ObjIndex $positionRaw $face.PositionCount "face at line $($face.LineNumber)"
            $normalIndex = Resolve-ObjIndex $normalRaw $face.NormalCount "face at line $($face.LineNumber)"
            $weldKey = if ($ExpansionMode -eq 'WeldedPosition') {
                $positionWeldKeys[$positionIndex - 1]
            }
            else {
                [string] $positionIndex
            }
            if (-not $weldedNormals.ContainsKey($weldKey)) {
                $weldedNormals[$weldKey] = @(0.0, 0.0, 0.0)
            }
            $sum = $weldedNormals[$weldKey]
            $normal = $normals[$normalIndex - 1]
            $sum[0] += $normal[0]
            $sum[1] += $normal[1]
            $sum[2] += $normal[2]
        }
    }

    foreach ($weldKey in @($weldedNormals.Keys)) {
        $sum = $weldedNormals[$weldKey]
        $length = [Math]::Sqrt(
            $sum[0] * $sum[0] +
            $sum[1] * $sum[1] +
            $sum[2] * $sum[2])
        if ($length -le 1e-12) {
            throw "Averaged render normal cancels to zero for weld group '$weldKey'."
        }
        $weldedNormals[$weldKey] = @(
            ($sum[0] / $length),
            ($sum[1] / $length),
            ($sum[2] / $length))
    }
}

foreach ($face in $faces) {
    $shellTokens = [Collections.Generic.List[string]]::new()
    foreach ($token in $face.Tokens) {
        $indices = $token -split '/'
        if ($indices.Count -lt 3 -or [string]::IsNullOrWhiteSpace($indices[2])) {
            throw "Face at line $($face.LineNumber) has no per-corner render normal."
        }

        $positionRaw = 0
        $normalRaw = 0
        if (-not [int]::TryParse($indices[0], [ref] $positionRaw) -or
            -not [int]::TryParse($indices[2], [ref] $normalRaw)) {
            throw "Face at line $($face.LineNumber) contains an invalid vertex/normal index."
        }
        $positionIndex = Resolve-ObjIndex $positionRaw $face.PositionCount "face at line $($face.LineNumber)"
        $normalIndex = Resolve-ObjIndex $normalRaw $face.NormalCount "face at line $($face.LineNumber)"
        $key = if ($ExpansionMode -eq 'WeldedPosition') {
            $positionWeldKeys[$positionIndex - 1]
        }
        elseif ($ExpansionMode -eq 'SourcePosition') {
            [string] $positionIndex
        }
        else {
            "$positionIndex/$normalIndex"
        }

        if (-not $cornerMap.ContainsKey($key)) {
            $position = $positions[$positionIndex - 1]
            $normal = if ($ExpansionMode -ne 'SplitRenderNormal') {
                $weldedNormals[$key]
            }
            else {
                $normals[$normalIndex - 1]
            }
            $shellVertices.Add(@(
                ($position[0] + $normal[0] * $OutlineWidth),
                ($position[1] + $normal[1] * $OutlineWidth),
                ($position[2] + $normal[2] * $OutlineWidth)))
            $cornerMap[$key] = $positions.Count + $shellVertices.Count
        }

        $texturePart = if ($indices.Count -ge 2 -and $indices[1].Length -gt 0) {
            $textureRaw = 0
            if (-not [int]::TryParse($indices[1], [ref] $textureRaw)) {
                throw "Face at line $($face.LineNumber) contains an invalid texture index."
            }
            $textureRaw
        }
        else {
            $null
        }
        $shellVertexIndex = $cornerMap[$key]
        $shellToken = if ($null -ne $texturePart) {
            "$shellVertexIndex/$texturePart/$normalIndex"
        }
        else {
            "$shellVertexIndex//$normalIndex"
        }
        $shellTokens.Add($shellToken)
    }
    $reversed = @($shellTokens)
    [array]::Reverse($reversed)
    $shellFaces.Add($reversed)
}

$outputLines = [Collections.Generic.List[string]]::new()
foreach ($line in $sourceLines) {
    $outputLines.Add($line)
}
$outputLines.Add('')
$outputLines.Add('# Deadlimit Shade derived preview-only outline shell')
$outputLines.Add("# outline_width=$(Format-Number $OutlineWidth)")
$outputLines.Add("# expansion_mode=$ExpansionMode")
foreach ($vertex in $shellVertices) {
    $outputLines.Add(
        "v $(Format-Number $vertex[0]) $(Format-Number $vertex[1]) $(Format-Number $vertex[2])")
}
$outputLines.Add('o __deadlimit_outline')
$outputLines.Add('usemtl __deadlimit_outline')
foreach ($face in $shellFaces) {
    $outputLines.Add('f ' + ($face -join ' '))
}

$destinationDirectory = Split-Path -Parent $destinationPath
if ($destinationDirectory -and -not (Test-Path -LiteralPath $destinationDirectory)) {
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
}
[IO.File]::WriteAllLines(
    $destinationPath,
    $outputLines,
    [Text.UTF8Encoding]::new($false))

Write-Output "Generated derived outline preview mesh: $destinationPath"
Write-Output "Source vertices: $($positions.Count); shell vertices: $($shellVertices.Count); faces: $($faces.Count); outline width: $(Format-Number $OutlineWidth); expansion mode: $ExpansionMode"
