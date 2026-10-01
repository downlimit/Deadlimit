$ErrorActionPreference = 'Stop'

$scriptPath = '.deadlimit/scripts/DeadlimitPipelineScripts.ms'
$source = Get-Content -LiteralPath $scriptPath -Raw

$required = @(
    'button createInnerLineartButton "CREATE LINEART"',
    'button repairLineartNormalsButton "REPAIR NORMALS"',
    'deadlimitVertexColorCreateDetachedInnerLineartFast widthMm:',
    'local capturedNormals = #()',
    'collect (getNormal lineartNode vertexIndex)',
    'meshop.flipNormals lineartNode #{1..faceCount}',
    'normalModifier.GetVertexID faceIndex cornerIndex node:lineartNode',
    'targetNormals[normalId] = vertexNormals[vertexId]',
    'deadlimitDetachedInnerLineartRepairNormals()',
    'normalModifier.GetNormalExplicit normalIndex node:node',
    'matchPattern node.name pattern:"lineart_*" ignoreCase:true'
)
foreach ($pattern in $required) {
    if (-not $source.Contains($pattern)) {
        throw "Inner Lineart normal contract is missing: $pattern"
    }
}

if ($source.Contains('button createInnerLineartFastButton "ALTERNATIVE FAST"')) {
    throw 'The legacy second lineart creation button is still present.'
}
if ($source.Contains('for vertexIndex = 1 to vertexCount do setNormal lineartNode')) {
    throw 'The lossy Editable Mesh setNormal path is still present.'
}

$captureIndex = $source.IndexOf('collect (getNormal lineartNode vertexIndex)')
$flipIndex = $source.IndexOf('meshop.flipNormals lineartNode #{1..faceCount}')
if ($captureIndex -lt 0 -or $flipIndex -lt 0 -or $captureIndex -ge $flipIndex) {
    throw 'Lineart normals must be captured before mesh winding is flipped.'
}

$readme = Get-Content -LiteralPath '.deadlimit/scripts/README.md' -Raw
if (-not $readme.Contains('REPAIR NORMALS sits beside CREATE LINEART')) {
    throw 'Deadlimit Scripts README does not document lineart normal repair.'
}
if ($readme.Contains('ALTERNATIVE FAST')) {
    throw 'Deadlimit Scripts README still documents the removed second creation path.'
}

Write-Host 'Inner Lineart normal contract smoke passed.'
