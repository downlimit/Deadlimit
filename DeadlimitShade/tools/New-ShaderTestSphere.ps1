[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputPath,

    [ValidateRange(3, 128)]
    [int] $Segments = 32,

    [ValidateRange(3, 64)]
    [int] $Rings = 16
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$lines = [System.Collections.Generic.List[string]]::new()

function Format-Number {
    param([double] $Value)
    return $Value.ToString('0.00000000', $culture)
}

function Add-Vertex {
    param(
        [double] $X,
        [double] $Y,
        [double] $Z,
        [double] $U,
        [double] $V
    )

    $lines.Add("v $(Format-Number $X) $(Format-Number $Y) $(Format-Number $Z)")
    $lines.Add("vt $(Format-Number $U) $(Format-Number $V)")
    $lines.Add("vn $(Format-Number $X) $(Format-Number $Y) $(Format-Number $Z)")
}

$lines.Add('# Deterministic Deadlimit Shade NPR test sphere')
$lines.Add('o DeadlimitShaderTestSphere')
$lines.Add('usemtl DeadlimitTest')

# Vertex 1 is the north pole. Intermediate rings duplicate their seam vertex.
Add-Vertex 0.0 1.0 0.0 0.5 0.0

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
        Add-Vertex $x $y $z $u $v
    }
}

$southPole = 2 + (($Rings - 1) * ($Segments + 1))
Add-Vertex 0.0 -1.0 0.0 0.5 1.0

function Add-Face {
    param([int] $A, [int] $B, [int] $C)
    $lines.Add("f $A/$A/$A $B/$B/$B $C/$C/$C")
}

$firstRing = 2
for ($segment = 0; $segment -lt $Segments; $segment++) {
    Add-Face 1 ($firstRing + $segment + 1) ($firstRing + $segment)
}

for ($ring = 0; $ring -lt ($Rings - 2); $ring++) {
    $current = $firstRing + ($ring * ($Segments + 1))
    $next = $current + $Segments + 1
    for ($segment = 0; $segment -lt $Segments; $segment++) {
        $a = $current + $segment
        $b = $current + $segment + 1
        $c = $next + $segment
        $d = $next + $segment + 1
        Add-Face $a $b $d
        Add-Face $a $d $c
    }
}

$lastRing = $firstRing + (($Rings - 2) * ($Segments + 1))
for ($segment = 0; $segment -lt $Segments; $segment++) {
    Add-Face ($lastRing + $segment) ($lastRing + $segment + 1) $southPole
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

[System.IO.File]::WriteAllText(
    $OutputPath,
    ($lines -join "`n") + "`n",
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Generated deterministic shader test sphere: $OutputPath"
