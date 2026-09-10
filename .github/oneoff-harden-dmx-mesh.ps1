$ErrorActionPreference = 'Stop'

$scriptPath = '.deadlimit/scripts/DeadlimitPipelineScripts.ms'
$source = Get-Content -LiteralPath $scriptPath -Raw

$replacements = @(
    @('FEATURE: EXACT VERTEX-COLOR UNDO FOR DIRECT EDITABLE POLY WRITES', 'FEATURE: EXACT VERTEX-COLOR UNDO FOR DIRECT EDITABLE POLY/MESH WRITES'),
    @('before/after channel-0 arrays and display flags for Editable Poly targets.', 'before/after channel-0 arrays and display flags for Editable Poly/Editable Mesh targets.'),
    @('meshop.getNumMapVerts editableObject 0', 'meshop.getNumCPVVerts editableObject'),
    @('meshop.setNumMapVerts editableObject 0 snapshot[3].count keep:false', 'meshop.setNumCPVVerts editableObject snapshot[3].count'),
    @('local meshFace = getFace editableObject targetFaceIndex', 'local meshFace = meshop.getFace editableObject targetFaceIndex'),
    @('meshop.setNumMapVerts editableObject 0 totalCorners keep:false', 'meshop.setNumCPVVerts editableObject totalCorners')
)
foreach ($pair in $replacements) {
    $source = $source.Replace($pair[0], $pair[1])
}

# Map channel 0 already has one map face per geometry face when support is enabled.
# Autodesk explicitly warns against resizing channel-0 faces with meshop.setNumMapFaces.
$source = [regex]::Replace(
    $source,
    '(?m)^[ \t]*meshop\.setNumMapFaces editableObject 0 (?:snapshot\[4\]\.count|faceCount) keep:false\r?\n?',
    '')

if ($source.Contains('meshop.setNumMapFaces editableObject 0')) {
    throw 'Unsafe Editable Mesh channel-0 setNumMapFaces call remains.'
}
if (-not $source.Contains('meshop.setNumCPVVerts editableObject totalCorners')) {
    throw 'Editable Mesh CPV allocation path is missing.'
}
if (-not $source.Contains('meshop.getFace editableObject targetFaceIndex')) {
    throw 'Editable Mesh face lookup path is missing.'
}

[IO.File]::WriteAllText((Resolve-Path $scriptPath).Path, $source, [Text.UTF8Encoding]::new($false))

$testPath = 'internal/tests/experimental-dmx-vertex-color-smoke.ps1'
$test = Get-Content -LiteralPath $testPath -Raw
$test = $test.Replace('meshop.setNumMapVerts editableObject 0 totalCorners keep:false', 'meshop.setNumCPVVerts editableObject totalCorners')
$test = $test.Replace('meshop.setNumMapFaces editableObject 0 faceCount keep:false', 'meshop.getNumMapFaces editableObject 0')
$test = $test.Replace('local meshFace = getFace editableObject targetFaceIndex', 'local meshFace = meshop.getFace editableObject targetFaceIndex')
$extraGuard = @'

if ($source.Contains('meshop.setNumMapFaces editableObject 0')) {
    throw 'Editable Mesh channel 0 must not be resized with meshop.setNumMapFaces.'
}
'@
if (-not $test.Contains('Editable Mesh channel 0 must not be resized')) {
    $test = $test.Replace("if (`$source.Contains('Experimental DMX Vertex Color transfer currently requires an Editable Poly base object.')) {", $extraGuard + "`r`nif (`$source.Contains('Experimental DMX Vertex Color transfer currently requires an Editable Poly base object.')) {")
}
[IO.File]::WriteAllText((Resolve-Path $testPath).Path, $test, [Text.UTF8Encoding]::new($false))

& $testPath

git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add --all
git commit -s -m 'Harden Editable Mesh vertex color channel writes'
git push origin HEAD:fix/experimental-dmx-editable-mesh
