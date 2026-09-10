$ErrorActionPreference = 'Stop'

$scriptPath = '.deadlimit/scripts/DeadlimitPipelineScripts.ms'
$source = Get-Content -LiteralPath $scriptPath -Raw

$required = @(
    'Experimental DMX Vertex Color transfer currently requires an Editable Poly or Editable Mesh base object.',
    '(classOf editableObject) == Editable_Mesh',
    'meshop.getMapSupport editableObject 0',
    'meshop.setMapSupport editableObject 0 true',
    'meshop.setNumMapVerts editableObject 0 totalCorners keep:false',
    'meshop.setNumMapFaces editableObject 0 faceCount keep:false',
    'meshop.setMapVert editableObject 0 mapVertexIndex colorValue',
    'meshop.setMapFace editableObject 0 faceIndex',
    'local meshFace = getFace editableObject targetFaceIndex',
    'targetKind'
)
foreach ($pattern in $required) {
    if (-not $source.Contains($pattern)) {
        throw "Experimental DMX Editable Mesh contract is missing: $pattern"
    }
}

if ($source.Contains('Experimental DMX Vertex Color transfer currently requires an Editable Poly base object.')) {
    throw 'The old Editable-Poly-only guard is still present.'
}

$readme = Get-Content -LiteralPath '.deadlimit/scripts/README.md' -Raw
if (-not $readme.Contains('Editable Poly or Editable Mesh base object')) {
    throw 'Deadlimit Scripts README does not document Editable Mesh support.'
}

Write-Host 'Experimental DMX Vertex Color Editable Mesh smoke passed.'
