[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputPath,

    [ValidateRange(3, 128)]
    [int] $Segments = 32,

    [ValidateRange(3, 64)]
    [int] $Rings = 16,

    [ValidateRange(0.000001, 1.0)]
    [double] $OutlineWidth = 0.04,

    [switch] $HeroOnly
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$vertices = [System.Collections.Generic.List[object]]::new()
$faces = [System.Collections.Generic.List[object]]::new()
$lines = [System.Collections.Generic.List[string]]::new()

function Format-Number {
    param([double] $Value)
    return $Value.ToString('0.00000000', $culture)
}

function Add-SourceVertex {
    param(
        [double] $X,
        [double] $Y,
        [double] $Z,
        [double] $U,
        [double] $V
    )

    $vertices.Add([pscustomobject]@{
        X = $X
        Y = $Y
        Z = $Z
        U = $U
        V = $V
    })
}

function Add-SourceFace {
    param([int] $A, [int] $B, [int] $C)
    $faces.Add(@($A, $B, $C))
}

# Vertex 1 is the north pole. Intermediate rings duplicate their seam vertex.
Add-SourceVertex 0.0 1.0 0.0 0.5 0.0

for ($ring = 1; $ring -lt $Rings; $ring++) {
    $v = $ring / [double] $Rings
    $phi = [Math]::PI * $v
    $y = [Math]::Cos($phi)
    $radius = [Math]::Sin($phi)

    for ($segment = 0; $segment -le $Segments; $segment++) {
        $u = $segment / [double] $Segments
        $theta = 2.0 * [Math]::PI * $u
        $x = $radius * [Math]::Cos($theta)
        $z = $radius * [Math]::Sin($theta)
        Add-SourceVertex $x $y $z $u $v
    }
}

$southPole = 2 + (($Rings - 1) * ($Segments + 1))
Add-SourceVertex 0.0 -1.0 0.0 0.5 1.0

$firstRing = 2
for ($segment = 0; $segment -lt $Segments; $segment++) {
    Add-SourceFace 1 ($firstRing + $segment + 1) ($firstRing + $segment)
}

for ($ring = 0; $ring -lt ($Rings - 2); $ring++) {
    $current = $firstRing + ($ring * ($Segments + 1))
    $next = $current + $Segments + 1
    for ($segment = 0; $segment -lt $Segments; $segment++) {
        $a = $current + $segment
        $b = $current + $segment + 1
        $c = $next + $segment
        $d = $next + $segment + 1
        Add-SourceFace $a $b $d
        Add-SourceFace $a $d $c
    }
}

$lastRing = $firstRing + (($Rings - 2) * ($Segments + 1))
for ($segment = 0; $segment -lt $Segments; $segment++) {
    Add-SourceFace ($lastRing + $segment) ($lastRing + $segment + 1) $southPole
}

$sourceVertexCount = $vertices.Count
$shellScale = 1.0 + $OutlineWidth

$lines.Add($(if ($HeroOnly) {
    '# Deterministic Deadlimit Shade hero-only shader test sphere'
} else {
    '# Deterministic Deadlimit Shade two-material NPR and outline test sphere'
}))
if (-not $HeroOnly) {
    $lines.Add("# outline_width=$(Format-Number $OutlineWidth)")
}
$lines.Add('# source vertices are emitted first and remain identical for every width')
$lines.Add('o DeadlimitShaderTestSphere')

foreach ($vertex in $vertices) {
    $lines.Add("v $(Format-Number $vertex.X) $(Format-Number $vertex.Y) $(Format-Number $vertex.Z)")
}
if (-not $HeroOnly) {
    foreach ($vertex in $vertices) {
        $lines.Add("v $(Format-Number ($vertex.X * $shellScale)) $(Format-Number ($vertex.Y * $shellScale)) $(Format-Number ($vertex.Z * $shellScale))")
    }
}

foreach ($vertex in $vertices) {
    $lines.Add("vt $(Format-Number $vertex.U) $(Format-Number $vertex.V)")
}
if (-not $HeroOnly) {
    foreach ($vertex in $vertices) {
        $lines.Add("vt $(Format-Number $vertex.U) $(Format-Number $vertex.V)")
    }
}

foreach ($vertex in $vertices) {
    $lines.Add("vn $(Format-Number $vertex.X) $(Format-Number $vertex.Y) $(Format-Number $vertex.Z)")
}
if (-not $HeroOnly) {
    foreach ($vertex in $vertices) {
        $lines.Add("vn $(Format-Number $vertex.X) $(Format-Number $vertex.Y) $(Format-Number $vertex.Z)")
    }
}

$lines.Add('g DeadlimitHero')
$lines.Add('usemtl DeadlimitTest')
foreach ($face in $faces) {
    $lines.Add("f $($face[0])/$($face[0])/$($face[0]) $($face[1])/$($face[1])/$($face[1]) $($face[2])/$($face[2])/$($face[2])")
}

if (-not $HeroOnly) {
    $lines.Add('g DeadlimitOutlineShell')
    $lines.Add('usemtl __deadlimit_outline')
    foreach ($face in $faces) {
        $a = $sourceVertexCount + $face[0]
        $b = $sourceVertexCount + $face[1]
        $c = $sourceVertexCount + $face[2]
        $lines.Add("f $a/$a/$a $c/$c/$c $b/$b/$b")
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

[System.IO.File]::WriteAllText(
    $OutputPath,
    ($lines -join "`n") + "`n",
    [System.Text.UTF8Encoding]::new($false))

if ($HeroOnly) {
    Write-Output "Generated deterministic hero-only shader test sphere: $OutputPath"
    Write-Output "Hero vertices: $sourceVertexCount"
}
else {
    Write-Output "Generated deterministic two-material shader test sphere: $OutputPath"
    Write-Output "Hero vertices: $sourceVertexCount; shell vertices: $sourceVertexCount; outline width: $(Format-Number $OutlineWidth)"
}
