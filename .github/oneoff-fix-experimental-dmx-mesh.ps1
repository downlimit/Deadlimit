$ErrorActionPreference = 'Stop'

$scriptPath = '.deadlimit/scripts/DeadlimitPipelineScripts.ms'
$script = Get-Content -LiteralPath $scriptPath -Raw

$captureReplacement = @'
fn deadlimitVertexColorCaptureUndoSnapshots nodes =
(
    local snapshots = #()
    for node in nodes where isValidNode node and ((classOf node.baseObject) == Editable_Poly or (classOf node.baseObject) == Editable_Mesh) do
    (
        local editableObject = node.baseObject
        local targetKind = if (classOf editableObject) == Editable_Poly then #poly else #mesh
        local mapSupported = if targetKind == #poly then (polyop.getMapSupport editableObject 0) else (meshop.getMapSupport editableObject 0)
        local mapVertices = #()
        local mapFaces = #()
        if mapSupported do
        (
            local mapVertexCount = if targetKind == #poly then (polyop.getNumMapVerts editableObject 0) else (meshop.getNumMapVerts editableObject 0)
            local mapFaceCount = if targetKind == #poly then (polyop.getNumMapFaces editableObject 0) else (meshop.getNumMapFaces editableObject 0)
            for mapVertexIndex = 1 to mapVertexCount do
                append mapVertices (copy (if targetKind == #poly then (polyop.getMapVert editableObject 0 mapVertexIndex) else (meshop.getMapVert editableObject 0 mapVertexIndex)))
            for mapFaceIndex = 1 to mapFaceCount do
            (
                local mapFace = if targetKind == #poly then (deepCopy (polyop.getMapFace editableObject 0 mapFaceIndex)) else (copy (meshop.getMapFace editableObject 0 mapFaceIndex))
                append mapFaces mapFace
            )
        )
        append snapshots #(
            node,
            mapSupported,
            mapVertices,
            mapFaces,
            node.vertexColorType,
            node.showVertexColors,
            node.vertexColorsShaded,
            targetKind
        )
    )
    snapshots
)

'@
$script = [regex]::Replace(
    $script,
    '(?s)fn deadlimitVertexColorCaptureUndoSnapshots nodes =\s*\(.*?\)\s*(?=fn deadlimitVertexColorRestoreUndoSnapshots snapshots =)',
    $captureReplacement,
    1)

$restoreReplacement = @'
fn deadlimitVertexColorRestoreUndoSnapshots snapshots =
(
    local restoredNodes = #()
    with undo off
    (
        for snapshot in snapshots do
        (
            local node = snapshot[1]
            if isValidNode node do
            (
                local editableObject = node.baseObject
                local currentKind = if (classOf editableObject) == Editable_Poly then #poly else if (classOf editableObject) == Editable_Mesh then #mesh else #unsupported
                local targetKind = if snapshot.count >= 8 then snapshot[8] else currentKind
                if currentKind == targetKind and targetKind != #unsupported do
                (
                    if snapshot[2] then
                    (
                        if targetKind == #poly then
                        (
                            polyop.setMapSupport editableObject 0 true
                            polyop.setNumMapVerts editableObject 0 snapshot[3].count keep:false
                            for mapVertexIndex = 1 to snapshot[3].count do polyop.setMapVert editableObject 0 mapVertexIndex snapshot[3][mapVertexIndex]
                            polyop.setNumMapFaces editableObject 0 snapshot[4].count keep:false
                            for mapFaceIndex = 1 to snapshot[4].count do polyop.setMapFace editableObject 0 mapFaceIndex snapshot[4][mapFaceIndex]
                        )
                        else
                        (
                            meshop.setMapSupport editableObject 0 true
                            meshop.setNumMapVerts editableObject 0 snapshot[3].count keep:false
                            for mapVertexIndex = 1 to snapshot[3].count do meshop.setMapVert editableObject 0 mapVertexIndex snapshot[3][mapVertexIndex]
                            meshop.setNumMapFaces editableObject 0 snapshot[4].count keep:false
                            for mapFaceIndex = 1 to snapshot[4].count do meshop.setMapFace editableObject 0 mapFaceIndex snapshot[4][mapFaceIndex]
                        )
                    )
                    else
                    (
                        if targetKind == #poly then polyop.setMapSupport editableObject 0 false else meshop.setMapSupport editableObject 0 false
                    )

                    node.vertexColorType = snapshot[5]
                    node.showVertexColors = snapshot[6]
                    node.vertexColorsShaded = snapshot[7]
                    update node
                    append restoredNodes node
                )
            )
        )
    )
    deadlimitVertexColorRememberDisplayNodes restoredNodes
    deadlimitVertexColorRefreshDisplay restoredNodes
    restoredNodes.count
)

'@
$script = [regex]::Replace(
    $script,
    '(?s)fn deadlimitVertexColorRestoreUndoSnapshots snapshots =\s*\(.*?\)\s*(?=fn deadlimitVertexColorRecordUndoTransaction undoLabel)',
    $restoreReplacement,
    1)

$validateReplacement = @'
fn deadlimitExperimentalValidateAndMapPayload targetNode payload =
(
    if payload == undefined or (classOf payload) != Array or payload.count != 5 then
        throw "DMX helper returned an invalid Vertex Color payload."

    local editableObject = targetNode.baseObject
    local targetKind = if (classOf editableObject) == Editable_Poly then #poly else if (classOf editableObject) == Editable_Mesh then #mesh else #unsupported
    if targetKind == #unsupported then
        throw "Experimental DMX Vertex Color transfer currently requires an Editable Poly or Editable Mesh base object."

    local targetVertexCount = if targetKind == #poly then (polyop.getNumVerts editableObject) else editableObject.numVerts
    local targetFaceCount = if targetKind == #poly then (polyop.getNumFaces editableObject) else editableObject.numFaces
    if payload[2] != targetVertexCount or payload[3] != targetFaceCount then
        throw "Selected mesh topology changed while the DMX payload was being prepared."

    local sourceFaces = payload[4]
    local sourceColors = payload[5]
    if sourceFaces.count != targetFaceCount or sourceColors.count != targetFaceCount then
        throw "DMX payload face count is inconsistent."

    local sourceByKey = dotNetObject "System.Collections.Hashtable"
    for sourceFaceIndex = 1 to sourceFaces.count do
    (
        local sourceVerts = sourceFaces[sourceFaceIndex]
        local sourceFaceColors = sourceColors[sourceFaceIndex]
        if sourceVerts.count < 3 or sourceFaceColors.count != sourceVerts.count then
            throw ("DMX payload contains an invalid polygon at face " + (sourceFaceIndex as string) + ".")

        local key = deadlimitExperimentalFaceKey sourceVerts
        if sourceByKey.ContainsKey key then
            throw ("DMX contains duplicate polygon vertex sets; exact face ownership is ambiguous at " + key)
        sourceByKey.Add key sourceFaceIndex
    )

    local mappedColors = for faceIndex = 1 to targetFaceCount collect undefined
    local usedSourceFaces = dotNetObject "System.Collections.Hashtable"

    for targetFaceIndex = 1 to targetFaceCount do
    (
        local targetVerts = if targetKind == #poly then
            (for vertexId in (polyop.getFaceVerts editableObject targetFaceIndex) collect (vertexId as integer))
        else
        (
            local meshFace = getFace editableObject targetFaceIndex
            #(meshFace.x as integer, meshFace.y as integer, meshFace.z as integer)
        )
        local key = deadlimitExperimentalFaceKey targetVerts
        if not (sourceByKey.ContainsKey key) then
            throw ("Selected mesh topology differs from the DMX at face " + (targetFaceIndex as string) + ". Nothing was changed.")

        local sourceFaceIndex = sourceByKey.Item[key]
        if usedSourceFaces.ContainsKey sourceFaceIndex then
            throw "Selected mesh maps more than one face to the same DMX polygon. Nothing was changed."
        usedSourceFaces.Add sourceFaceIndex true

        local sourceVerts = sourceFaces[sourceFaceIndex]
        local sourceFaceColors = sourceColors[sourceFaceIndex]
        if sourceVerts.count != targetVerts.count then
            throw ("Polygon degree differs at selected face " + (targetFaceIndex as string) + ". Nothing was changed.")

        local targetColors = #()
        for targetVertex in targetVerts do
        (
            local sourceCorner = findItem sourceVerts targetVertex
            if sourceCorner == 0 then
                throw ("Vertex numbering differs at selected face " + (targetFaceIndex as string) + ". Nothing was changed.")
            append targetColors sourceFaceColors[sourceCorner]
        )
        mappedColors[targetFaceIndex] = targetColors
    )

    if usedSourceFaces.Count != sourceFaces.count then
        throw "Not every DMX polygon matched the selected mesh. Nothing was changed."

    mappedColors
)

'@
$script = [regex]::Replace(
    $script,
    '(?s)fn deadlimitExperimentalValidateAndMapPayload targetNode payload =\s*\(.*?\)\s*(?=fn deadlimitExperimentalApplyMappedColors targetNode mappedColors =)',
    $validateReplacement,
    1)

$applyReplacement = @'
fn deadlimitExperimentalApplyMappedColors targetNode mappedColors =
(
    local editableObject = targetNode.baseObject
    local targetKind = if (classOf editableObject) == Editable_Poly then #poly else if (classOf editableObject) == Editable_Mesh then #mesh else #unsupported
    if targetKind == #unsupported then
        throw "Experimental DMX Vertex Color transfer currently requires an Editable Poly or Editable Mesh base object."

    local faceCount = if targetKind == #poly then (polyop.getNumFaces editableObject) else editableObject.numFaces
    local totalCorners = 0
    for faceIndex = 1 to faceCount do totalCorners += mappedColors[faceIndex].count
    if totalCorners <= 0 then throw "No DMX Vertex Color corners were available to transfer."

    local beforeSnapshots = deadlimitVertexColorCaptureUndoSnapshots #(targetNode)
    if beforeSnapshots.count != 1 then
        throw "Could not capture the selected editable mesh before Vertex Color transfer."

    try
    (
        undo "Pull Vertex Color from DMX" on
        (
            if targetKind == #poly then
            (
                polyop.setMapSupport editableObject 0 true
                polyop.setNumMapVerts editableObject 0 totalCorners keep:false
                polyop.setNumMapFaces editableObject 0 faceCount keep:false

                local mapVertexIndex = 0
                for faceIndex = 1 to faceCount do
                (
                    local mapFace = #()
                    for colorValue in mappedColors[faceIndex] do
                    (
                        mapVertexIndex += 1
                        polyop.setMapVert editableObject 0 mapVertexIndex colorValue
                        append mapFace mapVertexIndex
                    )
                    polyop.setMapFace editableObject 0 faceIndex mapFace
                )
            )
            else
            (
                meshop.setMapSupport editableObject 0 true
                meshop.setNumMapVerts editableObject 0 totalCorners keep:false
                meshop.setNumMapFaces editableObject 0 faceCount keep:false

                local mapVertexIndex = 0
                for faceIndex = 1 to faceCount do
                (
                    local faceColors = mappedColors[faceIndex]
                    if faceColors.count != 3 then
                        throw ("Editable Mesh face " + (faceIndex as string) + " is not triangular. Nothing was changed.")
                    local firstMapVertex = mapVertexIndex + 1
                    for colorValue in faceColors do
                    (
                        mapVertexIndex += 1
                        meshop.setMapVert editableObject 0 mapVertexIndex colorValue
                    )
                    meshop.setMapFace editableObject 0 faceIndex [firstMapVertex, firstMapVertex + 1, firstMapVertex + 2]
                )
            )

            targetNode.vertexColorType = #color
            targetNode.showVertexColors = true
            targetNode.vertexColorsShaded = true
            update targetNode
        )

        local afterSnapshots = deadlimitVertexColorCaptureUndoSnapshots #(targetNode)
        deadlimitVertexColorRecordUndoTransaction "Pull Vertex Color from DMX" beforeSnapshots afterSnapshots
        deadlimitVertexColorRememberDisplayNodes #(targetNode)
        deadlimitVertexColorRefreshDisplay #(targetNode)
        totalCorners
    )
    catch
    (
        deadlimitVertexColorRestoreUndoSnapshots beforeSnapshots
        throw()
    )
)

'@
$script = [regex]::Replace(
    $script,
    '(?s)fn deadlimitExperimentalApplyMappedColors targetNode mappedColors =\s*\(.*?\)\s*(?=fn deadlimitExperimentalPullDmxVertexColor =)',
    $applyReplacement,
    1)

$oldPull = @'
    if (classOf targetNode.baseObject) != Editable_Poly then
        throw "Experimental DMX Vertex Color transfer currently requires an Editable Poly base object."

    local editablePoly = targetNode.baseObject
    local vertexCount = polyop.getNumVerts editablePoly
    local faceCount = polyop.getNumFaces editablePoly
'@
$newPull = @'
    local editableObject = targetNode.baseObject
    local targetKind = if (classOf editableObject) == Editable_Poly then #poly else if (classOf editableObject) == Editable_Mesh then #mesh else #unsupported
    if targetKind == #unsupported then
        throw "Experimental DMX Vertex Color transfer currently requires an Editable Poly or Editable Mesh base object."

    local vertexCount = if targetKind == #poly then (polyop.getNumVerts editableObject) else editableObject.numVerts
    local faceCount = if targetKind == #poly then (polyop.getNumFaces editableObject) else editableObject.numFaces
'@
if (-not $script.Contains($oldPull)) {
    throw 'Experimental pull preflight block was not found.'
}
$script = $script.Replace($oldPull, $newPull)

if ($script -match 'Experimental DMX Vertex Color transfer currently requires an Editable Poly base object\.') {
    throw 'Old Editable-Poly-only experimental guard remains.'
}
if ($script -notmatch 'meshop\.setMapFace editableObject 0 faceIndex') {
    throw 'Editable Mesh channel-0 write path was not installed.'
}
[IO.File]::WriteAllText((Resolve-Path $scriptPath).Path, $script, [Text.UTF8Encoding]::new($false))

$readmePath = '.deadlimit/scripts/README.md'
$readme = Get-Content -LiteralPath $readmePath -Raw
$oldReadme = 'EXPERIMENTAL is a rollout inside the same `DeadlimitPipelineScripts.ms` window, not a separate artist-facing script. `PULL VERTEX COLOR FOR SELECTED MESH...` lets the artist select one Editable Poly, choose a source DMX, and restore DMX channel 0 only when Deadlimit can prove an identical mesh by name/topology. The command fails without changing the mesh if matching is ambiguous or topology differs.'
$newReadme = 'EXPERIMENTAL is a rollout inside the same `DeadlimitPipelineScripts.ms` window, not a separate artist-facing script. `PULL VERTEX COLOR FOR SELECTED MESH...` lets the artist select one Editable Poly or Editable Mesh base object, choose a source DMX, and restore DMX channel 0 only when Deadlimit can prove an identical mesh by name/topology. Both base-object paths write channel 0 directly without collapsing the modifier stack. The command fails without changing the mesh if matching is ambiguous or topology differs.'
if (-not $readme.Contains($oldReadme)) {
    throw 'Experimental README paragraph was not found.'
}
$readme = $readme.Replace($oldReadme, $newReadme)
[IO.File]::WriteAllText((Resolve-Path $readmePath).Path, $readme, [Text.UTF8Encoding]::new($false))

$testPath = 'internal/tests/experimental-dmx-vertex-color-smoke.ps1'
$test = @'
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
'@
[IO.File]::WriteAllText((Join-Path (Resolve-Path '.').Path $testPath), $test + "`r`n", [Text.UTF8Encoding]::new($false))

$buildPath = '.github/workflows/build.yml'
$build = Get-Content -LiteralPath $buildPath -Raw
$anchor = @'
      - name: Texture naming alias smoke
        shell: pwsh
        run: internal/tests/texture-naming-alias-smoke.ps1
'@
$addition = @'
      - name: Experimental DMX vertex color smoke
        shell: pwsh
        run: internal/tests/experimental-dmx-vertex-color-smoke.ps1
'@
if (-not $build.Contains($anchor)) {
    throw 'Build workflow insertion point was not found.'
}
$build = $build.Replace($anchor, $anchor + $addition)
[IO.File]::WriteAllText((Resolve-Path $buildPath).Path, $build, [Text.UTF8Encoding]::new($false))

& $testPath

git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add --all
git commit -s -m 'Support Editable Mesh DMX vertex color pull'
git push origin HEAD:fix/experimental-dmx-editable-mesh
