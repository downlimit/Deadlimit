[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("deadlimit-outline-contract-" + [Guid]::NewGuid().ToString('N'))
$generator = Join-Path $PSScriptRoot '..\tools\New-ShaderTestSphere.ps1'
$meshA = Join-Path $testRoot 'sphere-width-a.obj'
$meshARepeat = Join-Path $testRoot 'sphere-width-a-repeat.obj'
$meshB = Join-Path $testRoot 'sphere-width-b.obj'
$widthA = 0.03
$widthB = 0.08

function Assert-True {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Read-ObjContract {
    param([string] $Path)

    $vertices = [System.Collections.Generic.List[double[]]]::new()
    $textureCoordinates = 0
    $normals = 0
    $materials = [System.Collections.Generic.List[string]]::new()
    $heroFaces = [System.Collections.Generic.List[int[]]]::new()
    $shellFaces = [System.Collections.Generic.List[int[]]]::new()
    $activeMaterial = $null

    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ($line.StartsWith('v ')) {
            $parts = $line.Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries)
            $vertices.Add(@(
                [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture),
                [double]::Parse($parts[2], [Globalization.CultureInfo]::InvariantCulture),
                [double]::Parse($parts[3], [Globalization.CultureInfo]::InvariantCulture)
            ))
        }
        elseif ($line.StartsWith('vt ')) {
            $textureCoordinates++
        }
        elseif ($line.StartsWith('vn ')) {
            $normals++
        }
        elseif ($line.StartsWith('usemtl ')) {
            $activeMaterial = $line.Substring(7)
            $materials.Add($activeMaterial)
        }
        elseif ($line.StartsWith('f ')) {
            $indices = $line.Substring(2).Split(' ') | ForEach-Object {
                [int]($_.Split('/')[0])
            }
            if ($activeMaterial -eq 'DeadlimitTest') {
                $heroFaces.Add($indices)
            }
            elseif ($activeMaterial -eq '__deadlimit_outline') {
                $shellFaces.Add($indices)
            }
            else {
                throw "Face in '$Path' has unexpected material '$activeMaterial'."
            }
        }
    }

    return [pscustomobject]@{
        Vertices = $vertices
        TextureCoordinateCount = $textureCoordinates
        NormalCount = $normals
        Materials = $materials
        HeroFaces = $heroFaces
        ShellFaces = $shellFaces
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    & $generator -OutputPath $meshA -Segments 12 -Rings 8 -OutlineWidth $widthA | Out-Null
    & $generator -OutputPath $meshARepeat -Segments 12 -Rings 8 -OutlineWidth $widthA | Out-Null
    & $generator -OutputPath $meshB -Segments 12 -Rings 8 -OutlineWidth $widthB | Out-Null

    $bytesA = [System.IO.File]::ReadAllBytes($meshA)
    $bytesARepeat = [System.IO.File]::ReadAllBytes($meshARepeat)
    Assert-True ($bytesA.Length -eq $bytesARepeat.Length) 'Repeated output length changed.'
    Assert-True ([Convert]::ToHexString($bytesA) -eq [Convert]::ToHexString($bytesARepeat)) 'Generation is not byte deterministic.'

    $contractA = Read-ObjContract $meshA
    $contractB = Read-ObjContract $meshB
    $sourceVertexCount = [int]($contractA.Vertices.Count / 2)

    Assert-True (($contractA.Vertices.Count % 2) -eq 0) 'Hero and shell vertex counts differ.'
    Assert-True ($contractA.TextureCoordinateCount -eq $contractA.Vertices.Count) 'Texture-coordinate count does not match vertex count.'
    Assert-True ($contractA.NormalCount -eq $contractA.Vertices.Count) 'Normal count does not match vertex count.'
    Assert-True (($contractA.Materials -join ',') -eq 'DeadlimitTest,__deadlimit_outline') 'Expected exactly the hero and reserved outline materials.'
    Assert-True ($contractA.HeroFaces.Count -gt 0) 'Hero face set is empty.'
    Assert-True ($contractA.HeroFaces.Count -eq $contractA.ShellFaces.Count) 'Hero and shell face counts differ.'

    for ($index = 0; $index -lt $sourceVertexCount; $index++) {
        $heroA = $contractA.Vertices[$index]
        $heroB = $contractB.Vertices[$index]
        $shellA = $contractA.Vertices[$sourceVertexCount + $index]
        $shellB = $contractB.Vertices[$sourceVertexCount + $index]

        for ($axis = 0; $axis -lt 3; $axis++) {
            Assert-True ([Math]::Abs($heroA[$axis] - $heroB[$axis]) -lt 1e-8) 'Changing outline width modified hero geometry.'
            Assert-True ([Math]::Abs($shellA[$axis] - ($heroA[$axis] * (1.0 + $widthA))) -lt 1e-7) 'Width A shell displacement is incorrect.'
            Assert-True ([Math]::Abs($shellB[$axis] - ($heroB[$axis] * (1.0 + $widthB))) -lt 1e-7) 'Width B shell displacement is incorrect.'
        }
    }

    for ($index = 0; $index -lt $contractA.HeroFaces.Count; $index++) {
        $hero = $contractA.HeroFaces[$index]
        $shell = $contractA.ShellFaces[$index]
        Assert-True ($shell[0] -eq ($sourceVertexCount + $hero[0])) 'Shell face first index does not map to the hero face.'
        Assert-True ($shell[1] -eq ($sourceVertexCount + $hero[2])) 'Shell face winding was not reversed.'
        Assert-True ($shell[2] -eq ($sourceVertexCount + $hero[1])) 'Shell face winding was not reversed.'
    }

    Write-Output "Deadlimit Shade outline mesh contract smoke passed for widths $widthA and $widthB."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
