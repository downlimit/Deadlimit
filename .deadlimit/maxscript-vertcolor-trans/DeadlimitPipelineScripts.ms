/*
    PORTING / MAINTENANCE CONTRACT
    ==============================

    This file is an artist-facing Deadlock/Source 2 pipeline tool. Preserve the
    observable contracts below when moving it to another DCC (for example Blender).
    Max-specific modifier-stack workarounds may be replaced by native DCC APIs.

    Feature map:
    1. Vertex Color tools convert and spread a single representative color between
       geometry color attribute channel 0, object palette/wire color, and material.
    2. Viewport ON/OFF makes the chosen DCC display the stored vertex colors without
       changing their numeric values.
    3. Bone tools change only the helper's visual shape/length. Joint transforms,
       pivots, hierarchy, animation, naming, and exported orientation stay intact.
    4. Inner Lineart turns selected inner edges into coincident zero-area seam faces.
       The extra normals create Deadlock shader linework without a visible geometric gap.
       Its optional marker writes black Vertex Alpha while preserving Vertex Color RGB.
    5. Skin-aware Inner Lineart assigns every generated coincident vertex the exact
       source vertex weights and keeps those pairs synchronized after later skin edits.
    6. Vertex Color FBX is a temporary sidecar for Deadlimit PREPARE. Optional fixed
       gamma affects only that export and never rewrites the artist's source colors.

    Cross-feature invariants:
    - Vertex Color means Max map channel 0. A Blender port should use one explicitly
      named color attribute and preserve its corner/face-corner indexing.
    - Selection order is data: SPREAD uses the first selected geometry as reference.
    - All destructive-looking operations are one Undo transaction and fail before the
      source stack is touched whenever the preflight can detect an unsafe topology.
    - Temporary modifiers, snapshots, selections, exporter settings, and scene dirty
      state are restored on success and failure. A real Skin repair intentionally keeps
      the scene dirty so the artist can save it.
    - Geometry indices in this MaxScript are 1-based. Translate them explicitly for a
      0-based API; silent off-by-one errors corrupt Skin and map-corner ownership.
    - Existing smoothing groups/hard edges, material IDs, edge visibility, every
      supported map channel, and modifier order are part of the asset contract.

    Suggested Blender architecture:
    - Keep DCC-neutral algorithms (sector partitioning, seam-group metadata, color
      conversion, validation) separate from Blender adapters (BMesh, color attributes,
      vertex groups/Armature, modifier visibility, FBX operator, UI panels).
    - Store seam groups as persistent object custom data with a schema/version key.
    - Run topology work on a temporary mesh datablock, validate it, then commit it in
      one operator. Do not depend on evaluated-mesh vertex indices for editable data.
    - Add regression scenes for triangulated junctions, map seams, hard edges, Skin
      weights/DQ equivalents, modifier sandwiches, save/reload, and repeated sync.
*/

/*
    Deadlimit Max Script for 3ds Max.

    Run this script after exporting the regular DMX with Wall Worm. It reads the
    last Wall Worm export folder from 3dsMax.ini, finds the newest DMX there, and
    exports selected geometry and renderable Shape/Spline objects beside that DMX as <name>_vertexcolor.fbx.
    Deadlimit Aggregator consumes it during PREPARE and keeps failed/cancelled handoffs for retry.
*/

/*
    FEATURE: PALETTE -> VERTEX COLOR FILL MODIFIER

    Max's object wireColor is the compact per-object palette color used by the artist.
    This modifier creates a complete channel-0 topology and writes one uniform color to
    every geometric vertex/map corner. deadlimitVertexColorBakeNode later collapses it
    below the user's existing modifiers so Skin and procedural work remain above it.

    Porting invariant: this is a deliberate solid-color fill. Spatial resampling and
    nearest-vertex transfer are outside its contract. Create valid data for every face corner, even
    when the destination mesh previously had no color attribute.
*/
plugin simpleMeshMod DeadlimitPaletteVertexFillModifier
name:"Deadlimit Palette Vertex Fill"
classID:#(0x5a4b1031, 0x279d5612)
(
    parameters main
    (
        fillColor type:#color default:(color 255 255 255) animatable:false
    )

    on modifyMesh do
    (
        local mapColor = [fillColor.r / 255.0, fillColor.g / 255.0, fillColor.b / 255.0]
        meshop.setMapSupport mesh 0 true
        meshop.setNumMapVerts mesh 0 mesh.numVerts keep:false
        for vertexIndex = 1 to mesh.numVerts do meshop.setMapVert mesh 0 vertexIndex mapColor
        meshop.setNumMapFaces mesh 0 mesh.numFaces keep:false
        for faceIndex = 1 to mesh.numFaces do meshop.setMapFace mesh 0 faceIndex (getFace mesh faceIndex)
    )
)

/*
    FEATURE: SOURCE 2 FIXED-GAMMA EXPORT VIEW

    Source 2 consumes this vertex-color FBX path with a different gamma expectation.
    Export RGB is clamped to [0,1] and transformed as value^(GAMMA/2.2). The artist-facing
    default is GAMMA 1.0, which applies the established 1/2.2 correction. GAMMA 2.2
    produces exponent 1.0 and therefore preserves the stored RGB values.
    Alpha/topology are untouched. The modifier exists only during FBX export and is removed
    or restored in cleanup.

    Porting invariant: stored colors, viewport colors, and Marmoset-oriented exports stay
    unchanged. Apply the transform to an evaluated/export copy when FIXED GAMMA is on.
    Never bake this correction into the working asset as a side effect of export.
*/
plugin simpleMeshMod DeadlimitVertexColorFixedGammaModifier
name:"Deadlimit Vertex Color Fixed Gamma"
classID:#(0x5a4b1032, 0x279d5612)
(
    parameters main
    (
        gammaExponent type:#float default:(1.0 / 2.2) animatable:false
    )

    on modifyMesh do
    (
        if meshop.getMapSupport mesh 0 do
        (
            local appliedExponent = amax 0.000001 gammaExponent
            local mapVertexCount = meshop.getNumMapVerts mesh 0
            for mapVertexIndex = 1 to mapVertexCount do
            (
                local mapColor = meshop.getMapVert mesh 0 mapVertexIndex
                if mapColor.x < 0.0 then mapColor.x = 0.0 else if mapColor.x > 1.0 then mapColor.x = 1.0
                if mapColor.y < 0.0 then mapColor.y = 0.0 else if mapColor.y > 1.0 then mapColor.y = 1.0
                if mapColor.z < 0.0 then mapColor.z = 0.0 else if mapColor.z > 1.0 then mapColor.z = 1.0
                meshop.setMapVert mesh 0 mapVertexIndex [mapColor.x ^ appliedExponent, mapColor.y ^ appliedExponent, mapColor.z ^ appliedExponent]
            )
        )
    )
)

/*
    PUBLIC SCRIPT STATE
    Globals keep rollout callbacks, tests, and repeated fileIn/reload calls pointing at
    the newest function definitions. Treat these names as the script's internal API.
*/
global deadlimitVertexColorFbxRollout
global deadlimitPipelineFloater
global deadlimitVertexColorToolsRollout
global deadlimitVertexColorBoneToolsRollout
global deadlimitVertexColorInnerLineartRollout
global deadlimitVertexColorLastDmx
global deadlimitVertexColorIsPrimaryDmx
global deadlimitVertexColorFindLatestDmx
global deadlimitVertexColorSetFbxParam
global deadlimitVertexColorRestoreFbxParams
global deadlimitVertexColorAttachShapeMeshModifiers
global deadlimitVertexColorAttachFixedGammaModifiers
global deadlimitVertexColorRemoveTemporaryModifiers
global deadlimitVertexColorExportSidecar
global deadlimitVertexColorSelectedGeometry
global deadlimitVertexColorSelectedBones
global deadlimitVertexColorSetDisplay
global deadlimitVertexColorRememberDisplayNodes
global deadlimitVertexColorRefreshDisplay
global deadlimitVertexColorRefreshTrackedDisplay
global deadlimitVertexColorDisplayRefreshNodes
global deadlimitVertexColorCaptureUndoSnapshots
global deadlimitVertexColorRestoreUndoSnapshots
global deadlimitVertexColorRecordUndoTransaction
global deadlimitVertexColorHandleSceneUndoRedo
global deadlimitVertexColorUndoTransactions
global deadlimitVertexColorRedoTransactions
global deadlimitVertexColorToggleDisplay
global deadlimitVertexColorToggleLineartDisplay
global deadlimitVertexColorFitBoneLengths
global deadlimitVertexColorSetSelectedBoneLength
global deadlimitVertexColorFlipSelectedBoneX
global deadlimitVertexColorCreateInnerLineart
global deadlimitVertexColorBuildInnerLineart
global deadlimitVertexColorCreateDetachedInnerLineart
global deadlimitDetachedInnerLineartBuild
global deadlimitDetachedInnerLineartApplySkins
global deadlimitDetachedInnerLineartApplyExplicitNormals
global deadlimitDetachedInnerLineartUniqueName
global deadlimitDetachedInnerLineartAddOutputVertex
global deadlimitDetachedInnerLineartAddBoundaryVertex
global deadlimitDetachedInnerLineartAddFaceRecord
global deadlimitDetachedInnerLineartReverseArray
global deadlimitInnerLineartFindFreeSmoothingGroup
global deadlimitVertexColorSyncInnerLineartSkin
global deadlimitVertexColorCaptureInnerLineartSkins
global deadlimitVertexColorRestoreInnerLineartSkins
global deadlimitVertexColorReadFaceSmoothingGroups
global deadlimitVertexColorApplyInnerLineartModifierResult
global deadlimitVertexColorFillPaletteToVertex
global deadlimitVertexColorFillVertexToPalette
global deadlimitVertexColorRedistribute
global deadlimitVertexColorClipboard
global deadlimitVertexColorReadFirst
global deadlimitVertexColorBakeNode
global deadlimitVertexColorBakeEditablePoly
global deadlimitVertexColorCopyVertex
global deadlimitVertexColorCopyPalette
global deadlimitVertexColorPasteVertex
global deadlimitVertexColorPastePalette
global deadlimitVertexColorResultText
global deadlimitInnerLineartSkinProperty = "DeadlimitInnerLineartSkinGroupsV1"
deadlimitVertexColorDisplayRefreshNodes = #()
deadlimitVertexColorUndoTransactions = #()
deadlimitVertexColorRedoTransactions = #()

/*
    FEATURE: SKIN-AWARE INNER LINEART METADATA AND SYNCHRONIZATION

    Metadata schema: each group is #(sourceVertex, cloneVertex, cloneVertex, ...).
    The first member remains the authoritative artist-painted Skin vertex. Remaining
    members are coincident topology clones made by Inner Lineart. Overlapping groups are
    merged while retaining the oldest source, then serialized into an object user prop
    so save/reload does not lose the relationship.

    Create Lineart snapshots Skin modifiers above the edited stack level before topology
    changes. It preserves regular bone influences/weights, DQ blend weight, normalization,
    and supported rigid state. Existing vertices are validated and only repaired when Max
    changed them; every new clone receives the exact source data.

    SYNC LINEART SKIN re-copies the current source data after later manual weight painting.
    It first evaluates the mesh immediately below each Skin and requires every stored group
    to remain coincident. This rejects stale indices instead of painting weights onto an
    unrelated vertex. FBX export runs the same sync automatically.

    Porting invariant: use persistent editable-mesh IDs or a validated source/clone mapping.
    Position matching alone is insufficient as identity; coincidence is only a safety check.
    Exclusion lists and Skin gizmo internals are outside this per-vertex contract.
*/
fn deadlimitInnerLineartGroupsOverlap groupA groupB =
(
    for vertexIndex in groupA where (findItem groupB vertexIndex) > 0 do return true
    false
)

fn deadlimitInnerLineartAppendUnique values value =
(
    if (findItem values value) == 0 do append values value
    values
)

fn deadlimitInnerLineartMergeGroups existingGroups incomingGroups =
(
    local result = deepCopy existingGroups
    for incomingGroup in incomingGroups where incomingGroup != undefined and incomingGroup.count > 1 do
    (
        local merged = #()
        for vertexIndex in incomingGroup do deadlimitInnerLineartAppendUnique merged vertexIndex
        local masterVertex = incomingGroup[1]
        for existingGroup in result while masterVertex == incomingGroup[1] where deadlimitInnerLineartGroupsOverlap existingGroup merged do
            masterVertex = existingGroup[1]

        for groupIndex = result.count to 1 by -1 where deadlimitInnerLineartGroupsOverlap result[groupIndex] merged do
        (
            for vertexIndex in result[groupIndex] do deadlimitInnerLineartAppendUnique merged vertexIndex
            deleteItem result groupIndex
        )

        local ordered = #(masterVertex)
        for vertexIndex in merged where vertexIndex != masterVertex do append ordered vertexIndex
        append result ordered
    )
    result
)

fn deadlimitInnerLineartSerializeGroups groups =
(
    local result = ""
    for groupIndex = 1 to groups.count do
    (
        if groupIndex > 1 do result += ";"
        local group = groups[groupIndex]
        for itemIndex = 1 to group.count do
        (
            if itemIndex > 1 do result += ","
            result += group[itemIndex] as string
        )
    )
    result
)

fn deadlimitInnerLineartDeserializeGroups text =
(
    local result = #()
    if text == undefined or text.count == 0 then return result
    for groupText in (filterString text ";") do
    (
        local group = #()
        for itemText in (filterString groupText ",") do
        (
            local vertexIndex = try(itemText as integer)catch(0)
            if vertexIndex <= 0 then throw "Stored INNER LINEART Skin groups are malformed. Recreate the lineart data."
            deadlimitInnerLineartAppendUnique group vertexIndex
        )
        if group.count > 1 do append result group
    )
    result
)

fn deadlimitInnerLineartReadGroups node =
(
    deadlimitInnerLineartDeserializeGroups (getUserProp node deadlimitInnerLineartSkinProperty)
)

fn deadlimitInnerLineartWriteGroups node groups =
(
    setUserProp node deadlimitInnerLineartSkinProperty (deadlimitInnerLineartSerializeGroups groups)
    groups
)

fn deadlimitInnerLineartSkinWeightData skinModifier vertexIndex node:undefined =
(
    local boneIds = #()
    local weights = #()
    local influenceCount = skinOps.GetVertexWeightCount skinModifier vertexIndex
    for influenceIndex = 1 to influenceCount do
    (
        append boneIds (skinOps.GetVertexWeightBoneID skinModifier vertexIndex influenceIndex)
        append weights (skinOps.GetVertexWeight skinModifier vertexIndex influenceIndex)
    )
    local dqWeight = try(skinOps.getVertexDQWeight skinModifier vertexIndex)catch(undefined)
    local rigid = try(skinOps.isRigidVertex skinModifier vertexIndex node:node)catch(false)
    local unnormalized = try(skinOps.isUnNormalizeVertex skinModifier vertexIndex node:node)catch(false)
    #(boneIds, weights, dqWeight, rigid, unnormalized)
)

fn deadlimitInnerLineartSkinWeightsEqual dataA dataB tolerance:1e-5 =
(
    if dataA[1].count != dataB[1].count then return false
    for influenceIndex = 1 to dataA[1].count do
    (
        local matchingIndex = findItem dataB[1] dataA[1][influenceIndex]
        if matchingIndex == 0 or abs(dataA[2][influenceIndex] - dataB[2][matchingIndex]) > tolerance then return false
    )
    if dataA[3] != undefined and dataB[3] != undefined and abs(dataA[3] - dataB[3]) > tolerance then return false
    if dataA[4] != dataB[4] or dataA[5] != dataB[5] then return false
    true
)

fn deadlimitInnerLineartApplySkinWeightData skinModifier node targetVertex data =
(
    if data[1].count == 0 then throw ("Skin vertex " + (targetVertex as string) + " has no bone influences and cannot be copied safely.")
    skinOps.ReplaceVertexWeights skinModifier targetVertex data[1] data[2] node:node
    if data[3] != undefined do skinOps.setVertexDQWeight skinModifier targetVertex data[3]
    try(skinOps.unNormalizeVertex skinModifier targetVertex data[5] node:node)catch()
    if data[4] do try(skinOps.rigidVertex skinModifier targetVertex node:node)catch()
)

fn deadlimitVertexColorCaptureInnerLineartSkins node sourceModifier oldVertCount =
(
    local sourceModifierIndex = if sourceModifier == undefined then (node.modifiers.count + 1) else modPanel.getModifierIndex node sourceModifier
    if sourceModifierIndex == undefined then throw "The active Edit Poly modifier is no longer in the selected object's stack."
    local result = #()
    select node
    max modify mode
    for modifierIndex = 1 to (sourceModifierIndex - 1) do
    (
        local currentModifier = node.modifiers[modifierIndex]
        if (classOf currentModifier) == Skin do
        (
            modPanel.setCurrentObject currentModifier node:node ui:true
            local skinVertexCount = skinOps.GetNumberVertices currentModifier
            if skinVertexCount != oldVertCount then
                throw ("Skin '" + currentModifier.name + "' sees " + (skinVertexCount as string) + " vertices while INNER LINEART sees " + (oldVertCount as string) + ". Disable or move topology-changing modifiers between Edit Poly and Skin.")
            local vertexData = for vertexIndex = 1 to oldVertCount collect (deadlimitInnerLineartSkinWeightData currentModifier vertexIndex node:node)
            append result #(currentModifier, vertexData)
        )
    )
    result
)

fn deadlimitVertexColorRestoreInnerLineartSkins node skinSnapshots seamGroups expectedVertCount =
(
    local restoredVertices = 0
    local repairedExistingVertices = 0
    select node
    max modify mode
    for snapshot in skinSnapshots do
    (
        local skinModifier = snapshot[1]
        local oldVertexData = snapshot[2]
        if (modPanel.getModifierIndex node skinModifier) == undefined then throw "A Skin modifier disappeared while INNER LINEART was running."
        modPanel.setCurrentObject skinModifier node:node ui:true
        local currentVertCount = skinOps.GetNumberVertices skinModifier
        if currentVertCount != expectedVertCount then
            throw ("Skin '" + skinModifier.name + "' produced " + (currentVertCount as string) + " vertices after INNER LINEART; expected " + (expectedVertCount as string) + ".")

        for vertexIndex = 1 to oldVertexData.count do
        (
            local currentData = deadlimitInnerLineartSkinWeightData skinModifier vertexIndex node:node
            if not (deadlimitInnerLineartSkinWeightsEqual oldVertexData[vertexIndex] currentData) do
            (
                deadlimitInnerLineartApplySkinWeightData skinModifier node vertexIndex oldVertexData[vertexIndex]
                repairedExistingVertices += 1
            )
        )

        for group in seamGroups do
        (
            local sourceVertex = group[1]
            if sourceVertex > oldVertexData.count then throw "INNER LINEART generated a Skin group with an invalid source vertex."
            for itemIndex = 2 to group.count do
            (
                if group[itemIndex] > currentVertCount then throw "INNER LINEART generated a Skin group outside the resulting topology."
                deadlimitInnerLineartApplySkinWeightData skinModifier node group[itemIndex] oldVertexData[sourceVertex]
                restoredVertices += 1
            )
        )
        skinOps.Invalidate skinModifier 0
        update node

        for vertexIndex = 1 to oldVertexData.count where not (deadlimitInnerLineartSkinWeightsEqual oldVertexData[vertexIndex] (deadlimitInnerLineartSkinWeightData skinModifier vertexIndex node:node)) do
            throw ("Skin '" + skinModifier.name + "' changed existing vertex " + (vertexIndex as string) + " during INNER LINEART.")
        for group in seamGroups do
        (
            local sourceData = deadlimitInnerLineartSkinWeightData skinModifier group[1] node:node
            for itemIndex = 2 to group.count where not (deadlimitInnerLineartSkinWeightsEqual sourceData (deadlimitInnerLineartSkinWeightData skinModifier group[itemIndex] node:node)) do
                throw ("Skin '" + skinModifier.name + "' could not synchronize INNER LINEART vertex " + (group[itemIndex] as string) + ".")
        )
    )
    #(skinSnapshots.count, restoredVertices, repairedExistingVertices)
)

fn deadlimitInnerLineartSnapshotBelowModifier node modifier =
(
    local modifierIndex = modPanel.getModifierIndex node modifier
    if modifierIndex == undefined then throw "Skin modifier is no longer in the object's stack."
    local workingNode = undefined
    local resultMesh = undefined
    try
    (
        with undo off with redraw off
        (
            workingNode = copy node
            for removeIndex = 1 to modifierIndex do deleteModifier workingNode 1
            resultMesh = snapshotAsMesh workingNode
            delete workingNode
            workingNode = undefined
        )
        resultMesh
    )
    catch
    (
        with undo off try(if isValidNode workingNode do delete workingNode)catch()
        if resultMesh != undefined do try(delete resultMesh)catch()
        throw()
    )
)

fn deadlimitVertexColorSyncInnerLineartSkin nodes:undefined =
(
    local targetNodes = if nodes == undefined then deadlimitVertexColorSelectedGeometry() else nodes
    local synchronizedNodes = 0
    local synchronizedSkins = 0
    local synchronizedGroups = 0
    local changedClones = 0

    for node in targetNodes where isValidNode node and (superClassOf node) == GeometryClass do
    (
        local groups = deadlimitInnerLineartReadGroups node
        if groups.count > 0 do
        (
            local highestVertex = 0
            for group in groups do for vertexIndex in group where vertexIndex > highestVertex do highestVertex = vertexIndex
            local nodeSkinCount = 0
            local totalSkinCount = 0
            for currentModifier in node.modifiers where (classOf currentModifier) == Skin do
            (
                totalSkinCount += 1
                local inputMesh = deadlimitInnerLineartSnapshotBelowModifier node currentModifier
                local inputVertCount = inputMesh.numVerts
                local skinVertCount = skinOps.GetNumberVertices currentModifier
                if inputVertCount >= highestVertex and skinVertCount == inputVertCount do
                (
                    local positionTolerance = 1e-4
                    local groupsCoincident = true
                    for group in groups do
                    (
                        local sourcePosition = getVert inputMesh group[1]
                        for itemIndex = 2 to group.count do
                            if distance sourcePosition (getVert inputMesh group[itemIndex]) > positionTolerance then
                                groupsCoincident = false
                    )

                    if groupsCoincident do
                    (
                        select node
                        max modify mode
                        modPanel.setCurrentObject currentModifier node:node ui:true
                        for group in groups do
                        (
                            local sourceData = deadlimitInnerLineartSkinWeightData currentModifier group[1] node:node
                            for itemIndex = 2 to group.count do
                            (
                                local cloneData = deadlimitInnerLineartSkinWeightData currentModifier group[itemIndex] node:node
                                if not (deadlimitInnerLineartSkinWeightsEqual sourceData cloneData) do
                                (
                                    deadlimitInnerLineartApplySkinWeightData currentModifier node group[itemIndex] sourceData
                                    changedClones += 1
                                )
                            )
                            synchronizedGroups += 1
                        )
                        skinOps.Invalidate currentModifier 0
                        update node
                        for group in groups do
                        (
                            local sourceData = deadlimitInnerLineartSkinWeightData currentModifier group[1] node:node
                            for itemIndex = 2 to group.count where not (deadlimitInnerLineartSkinWeightsEqual sourceData (deadlimitInnerLineartSkinWeightData currentModifier group[itemIndex] node:node)) do
                                throw ("Skin '" + currentModifier.name + "' failed to synchronize INNER LINEART vertex " + (group[itemIndex] as string) + ".")
                        )
                        nodeSkinCount += 1
                        synchronizedSkins += 1
                    )
                )
                delete inputMesh
            )
            if nodeSkinCount == 0 and totalSkinCount > 0 then
                throw ("No Skin above INNER LINEART on '" + node.name + "' sees the stored seam topology. Move Skin above the lineart Edit Poly and remove topology-changing modifiers between them.")
            if nodeSkinCount > 0 do synchronizedNodes += 1
        )
    )
    redrawViews()
    #(synchronizedNodes, synchronizedSkins, synchronizedGroups, changedClones)
)

/*
    FEATURE: WALL WORM DMX + VERTEX-COLOR FBX SIDECAR

    Wall Worm writes the primary DMX and remembers its last export folder in 3dsMax.ini.
    This script finds the newest primary DMX, then exports selected geometry/renderable
    shapes beside it as <dmx-name>_vertexcolor.fbx for Deadlimit PREPARE to consume.

    Export contract:
    - selection only; ASCII FBX; animation/cameras/lights off; triangulation on;
    - renderable shapes receive a temporary mesh conversion;
    - FIXED GAMMA receives the temporary gamma modifier described above;
    - Inner Lineart Skin groups are synchronized before exporter evaluation;
    - write a unique temporary FBX first, then replace the sidecar only after success;
    - always restore selection, FBX settings, and temporary modifiers.

    A Blender port should keep this handoff protocol even if its exporter options differ.
    PREPARE owns validation and deletion of the accepted sidecar; failed/cancelled runs keep
    the file for retry and diagnosis.
*/
fn deadlimitVertexColorIsPrimaryDmx path =
(
    local fileName = toLower (filenameFromPath path)
    (matchPattern fileName pattern:"*_vertexcolor.dmx") == false
)

fn deadlimitVertexColorFindLatestDmx =
(
    local exportFolder = getINISetting (getMAXIniFile()) "Wall Worm SMD Exporter" "History_00"
    if exportFolder == undefined or exportFolder.count == 0 or not (doesDirectoryExist exportFolder) then
        throw "Wall Worm has no valid last export folder. Export the regular DMX first."

    local directoryClass = dotNetClass "System.IO.Directory"
    local fileClass = dotNetClass "System.IO.File"
    local latestPath = undefined
    local latestTicks = 0
    for path in (directoryClass.GetFiles exportFolder "*.dmx") do
    (
        if deadlimitVertexColorIsPrimaryDmx path then
        (
            local ticks = (fileClass.GetLastWriteTimeUtc path).Ticks
            if latestPath == undefined or ticks > latestTicks then
            (
                latestPath = path
                latestTicks = ticks
            )
        )
    )

    if latestPath == undefined then
        throw ("No DMX exists in the last Wall Worm export folder: " + exportFolder)

    deadlimitVertexColorLastDmx = latestPath
    latestPath
)

fn deadlimitVertexColorSetFbxParam name value =
(
    local result = FBXExporterSetParam name value
    if result == unsupplied then throw ("FBX exporter does not support option: " + name)
)

fn deadlimitVertexColorRestoreFbxParams params =
(
    for pair in params do
    (
        try (FBXExporterSetParam pair[1] pair[2]) catch()
    )
)

fn deadlimitVertexColorAttachFixedGammaModifiers nodes gammaExponent:(1.0 / 2.2) =
(
    if gammaExponent <= 0.0 then throw "Fixed Gamma EXP must be greater than zero."
    local temporaryModifiers = #()
    try
    (
        with undo off
        (
            for node in nodes do
            (
                local existingGammaModifier = undefined
                for existingModifier in node.modifiers while existingGammaModifier == undefined do
                    if (classOf existingModifier) == DeadlimitVertexColorFixedGammaModifier do existingGammaModifier = existingModifier

                if existingGammaModifier == undefined then
                (
                    local gammaModifier = DeadlimitVertexColorFixedGammaModifier gammaExponent:gammaExponent
                    addModifier node gammaModifier before:1
                    append temporaryModifiers #(node, gammaModifier, #delete)
                )
                else
                (
                    local previousExponent = existingGammaModifier.gammaExponent
                    existingGammaModifier.gammaExponent = gammaExponent
                    append temporaryModifiers #(node, existingGammaModifier, #restoreGamma, previousExponent)
                )
            )
        )
        temporaryModifiers
    )
    catch
    (
        deadlimitVertexColorRemoveTemporaryModifiers temporaryModifiers
        throw()
    )
)

fn deadlimitVertexColorRemoveTemporaryModifiers temporaryModifiers =
(
    with undo off
    (
        for item in temporaryModifiers do
            if isValidNode item[1] do
            (
                if item.count >= 4 then
                (
                    if item[3] == #restoreGamma then
                        try(item[2].gammaExponent = item[4])catch()
                    else
                        try(deleteModifier item[1] item[2])catch()
                )
                else
                    try(deleteModifier item[1] item[2])catch()
            )
    )
)

fn deadlimitVertexColorAttachShapeMeshModifiers nodes =
(
    local temporaryModifiers = #()
    try
    (
        with undo off
        (
            for node in nodes where (superClassOf node) == shape do
            (
                local alreadyApplied = false
                for existingModifier in node.modifiers while not alreadyApplied do
                    alreadyApplied = (classOf existingModifier) == Turn_to_Mesh

                if not alreadyApplied then
                (
                    local meshModifier = Turn_to_Mesh()
                    addModifier node meshModifier before:1
                    append temporaryModifiers #(node, meshModifier)
                )
            )
        )
        temporaryModifiers
    )
    catch
    (
        deadlimitVertexColorRemoveTemporaryModifiers temporaryModifiers
        throw()
    )
)

fn deadlimitVertexColorExportSidecar fixedGamma:false gammaValue:1.0 =
(
    local primaryDmx = deadlimitVertexColorFindLatestDmx()
    if gammaValue <= 0.0 then throw "Fixed Gamma value must be greater than zero."
    local gammaExponent = gammaValue / 2.2

    local originalSelection = selection as array
    local selectedExportNodes = for node in originalSelection where
    (
        isValidNode node and
        (
            (superClassOf node) == GeometryClass or
            ((superClassOf node) == shape and (try (node.renderable == true) catch (false)))
        )
    ) collect node
    if selectedExportNodes.count == 0 then
        throw "Select at least one geometry object or renderable shape."

    local folder = getFilenamePath primaryDmx
    local baseName = getFilenameFile primaryDmx
    local sidecarPath = folder + baseName + "_vertexcolor.fbx"
    local guidValue = ((dotNetClass "System.Guid").NewGuid()).ToString "N"
    local temporaryPath = folder + baseName + ".vertexcolor-" + guidValue + ".tmp.fbx"
    local fileClass = dotNetClass "System.IO.File"
    local temporaryShapeModifiers = #()
    local temporaryGammaModifiers = #()
    local sceneWasDirty = getSaveRequired()
    local skinSyncResult = #(0, 0, 0, 0)

    local savedParams = #(
        #("ASCII", FBXExporterGetParam "ASCII"),
        #("Animation", FBXExporterGetParam "Animation"),
        #("Cameras", FBXExporterGetParam "Cameras"),
        #("Lights", FBXExporterGetParam "Lights"),
        #("Triangulate", FBXExporterGetParam "Triangulate")
    )

    try
    (
        skinSyncResult = deadlimitVertexColorSyncInnerLineartSkin nodes:selectedExportNodes

        deadlimitVertexColorSetFbxParam "ASCII" true
        deadlimitVertexColorSetFbxParam "Animation" false
        deadlimitVertexColorSetFbxParam "Cameras" false
        deadlimitVertexColorSetFbxParam "Lights" false
        deadlimitVertexColorSetFbxParam "Triangulate" true

        temporaryShapeModifiers = deadlimitVertexColorAttachShapeMeshModifiers selectedExportNodes

        if fixedGamma do
        (
            temporaryGammaModifiers = deadlimitVertexColorAttachFixedGammaModifiers selectedExportNodes gammaExponent:gammaExponent
        )

        if doesFileExist temporaryPath do deleteFile temporaryPath
        select selectedExportNodes
        local exported = exportFile temporaryPath #noPrompt selectedOnly:true using:FBXEXP
        if exported != true or not (doesFileExist temporaryPath) then
            throw "3ds Max FBX exporter did not create the sidecar."

        fileClass.Copy temporaryPath sidecarPath true
        deleteFile temporaryPath
        deadlimitVertexColorRemoveTemporaryModifiers temporaryGammaModifiers
        deadlimitVertexColorRemoveTemporaryModifiers temporaryShapeModifiers
        deadlimitVertexColorRestoreFbxParams savedParams
        select originalSelection
        if not sceneWasDirty and skinSyncResult[4] == 0 do setSaveRequired false autobackupRequired:false
        #(primaryDmx, sidecarPath, fixedGamma, skinSyncResult, gammaValue, gammaExponent)
    )
    catch
    (
        local errorText = getCurrentException()
        if doesFileExist temporaryPath do deleteFile temporaryPath
        deadlimitVertexColorRemoveTemporaryModifiers temporaryGammaModifiers
        deadlimitVertexColorRemoveTemporaryModifiers temporaryShapeModifiers
        deadlimitVertexColorRestoreFbxParams savedParams
        select originalSelection
        if not sceneWasDirty and skinSyncResult[4] == 0 do setSaveRequired false autobackupRequired:false
        format "Vertex Color FBX failed: %\n" errorText
        throw()
    )
)

/*
    FEATURE: VIEWPORT VERTEX-COLOR DISPLAY

    These functions only change display flags. Numeric channel-0 data and materials are
    untouched. ON/OFF uses a majority rule across the selected meshes: if most are on,
    force all off; otherwise force all on. This makes mixed selections deterministic.
    The Inner Lineart display toggle keeps shaded vertex display enabled and switches the
    source between channel 0 RGB (#color) and special channel -2 Vertex Alpha (#alpha).
    Nitrous caches selected-object display data beyond a regular redraw. Cycling the display
    flags, updating the node in both states, invalidating its screen rectangle, and forcing
    a complete redraw makes the selected mesh switch immediately without selection changes.

    Porting invariant: configure the viewport/material preview to show the same stored
    color/alpha attribute the workflow reads. Display switches never rewrite either channel.

    Nitrous can retain stale channel-0 display data while an object remains selected. A normal
    redraw is insufficient in that state, which makes a successful scene Undo look as if the
    colors were not restored. RefreshDisplay preserves the requested display mode, briefly
    disables shaded vertex display without recording Undo data, updates the node, restores the
    flags, invalidates its viewport rectangle, and completes a redraw. No VertexPaint modifier
    is added, so the user's modifier stack remains byte-for-byte the same.

    The last meshes touched by a color command are retained as session-only node references.
    Non-persistent #sceneUndo/#sceneRedo callbacks refresh those meshes after Max has restored
    their map channels. Invalid/deleted nodes are ignored. Never use notifyDependents here: it
    proved unsafe in interactive Nitrous sessions and can crash Max.
*/
fn deadlimitVertexColorRememberDisplayNodes nodes =
(
    deadlimitVertexColorDisplayRefreshNodes = for node in nodes where isValidNode node collect node
    deadlimitVertexColorDisplayRefreshNodes.count
)

fn deadlimitVertexColorRefreshDisplay nodes =
(
    local refreshed = 0
    with undo off
    (
        with redraw off
        (
            for node in nodes where isValidNode node do
            (
                local displayType = node.vertexColorType
                local displayEnabled = node.showVertexColors
                local shadedEnabled = node.vertexColorsShaded

                node.showVertexColors = false
                node.vertexColorsShaded = false
                update node
                nodeInvalRect node

                node.vertexColorType = displayType
                node.showVertexColors = displayEnabled
                node.vertexColorsShaded = shadedEnabled
                update node
                nodeInvalRect node
                refreshed += 1
            )
        )
    )
    forceCompleteRedraw()
    refreshed
)

fn deadlimitVertexColorRefreshTrackedDisplay =
(
    if deadlimitVertexColorDisplayRefreshNodes == undefined then return 0
    deadlimitVertexColorRefreshDisplay deadlimitVertexColorDisplayRefreshNodes
)

/*
    FEATURE: EXACT VERTEX-COLOR UNDO FOR DIRECT EDITABLE POLY WRITES

    polyop map-channel writes do not create a channel RestoreObj, even inside an Undo context
    and even when polyop receives the scene node. Max still creates the named Undo entry for
    the accompanying node display changes, so a plain Ctrl+Z can misleadingly report success
    while channel 0 remains filled. Keep an in-session transaction stack containing exact
    before/after channel-0 arrays and display flags for Editable Poly targets.

    #sceneUndo/#sceneRedo supplies the named Undo entry after Max finishes its own restore.
    When that name matches the top Deadlimit transaction, restore the corresponding snapshot
    with Undo disabled, then refresh Nitrous. This preserves the original base-object instance,
    modifier instances/order/settings, topology, and sub-object selections. Transactions are
    session-only because Max does not preserve its Undo queue across restart or scene reload.
 */
fn deadlimitVertexColorCaptureUndoSnapshots nodes =
(
    local snapshots = #()
    for node in nodes where isValidNode node and (classOf node.baseObject) == Editable_Poly do
    (
        local editablePoly = node.baseObject
        local mapSupported = polyop.getMapSupport editablePoly 0
        local mapVertices = #()
        local mapFaces = #()
        if mapSupported do
        (
            local mapVertexCount = polyop.getNumMapVerts editablePoly 0
            local mapFaceCount = polyop.getNumMapFaces editablePoly 0
            for mapVertexIndex = 1 to mapVertexCount do append mapVertices (copy (polyop.getMapVert editablePoly 0 mapVertexIndex))
            for mapFaceIndex = 1 to mapFaceCount do append mapFaces (deepCopy (polyop.getMapFace editablePoly 0 mapFaceIndex))
        )
        append snapshots #(
            node,
            mapSupported,
            mapVertices,
            mapFaces,
            node.vertexColorType,
            node.showVertexColors,
            node.vertexColorsShaded
        )
    )
    snapshots
)

fn deadlimitVertexColorRestoreUndoSnapshots snapshots =
(
    local restoredNodes = #()
    with undo off
    (
        for snapshot in snapshots do
        (
            local node = snapshot[1]
            if isValidNode node and (classOf node.baseObject) == Editable_Poly do
            (
                local editablePoly = node.baseObject
                if snapshot[2] then
                (
                    polyop.setMapSupport editablePoly 0 true
                    polyop.setNumMapVerts editablePoly 0 snapshot[3].count keep:false
                    for mapVertexIndex = 1 to snapshot[3].count do polyop.setMapVert editablePoly 0 mapVertexIndex snapshot[3][mapVertexIndex]
                    polyop.setNumMapFaces editablePoly 0 snapshot[4].count keep:false
                    for mapFaceIndex = 1 to snapshot[4].count do polyop.setMapFace editablePoly 0 mapFaceIndex snapshot[4][mapFaceIndex]
                )
                else
                    polyop.setMapSupport editablePoly 0 false

                node.vertexColorType = snapshot[5]
                node.showVertexColors = snapshot[6]
                node.vertexColorsShaded = snapshot[7]
                update node
                append restoredNodes node
            )
        )
    )
    deadlimitVertexColorRememberDisplayNodes restoredNodes
    deadlimitVertexColorRefreshDisplay restoredNodes
    restoredNodes.count
)

fn deadlimitVertexColorRecordUndoTransaction undoLabel beforeSnapshots afterSnapshots =
(
    if beforeSnapshots.count == 0 then return false
    append deadlimitVertexColorUndoTransactions #(undoLabel, beforeSnapshots, afterSnapshots)
    deadlimitVertexColorRedoTransactions = #()
    true
)

fn deadlimitVertexColorHandleSceneUndoRedo isRedo =
(
    local sourceTransactions = if isRedo then deadlimitVertexColorRedoTransactions else deadlimitVertexColorUndoTransactions
    if sourceTransactions.count == 0 then return false

    local transaction = sourceTransactions[sourceTransactions.count]
    local callbackLabel = callbacks.notificationParam()
    if callbackLabel == undefined or (callbackLabel as string) != transaction[1] then return false

    local snapshots = if isRedo then transaction[3] else transaction[2]
    deadlimitVertexColorRestoreUndoSnapshots snapshots
    deleteItem sourceTransactions sourceTransactions.count
    if isRedo then append deadlimitVertexColorUndoTransactions transaction else append deadlimitVertexColorRedoTransactions transaction
    true
)

fn deadlimitVertexColorSelectedGeometry =
(
    local nodes = for node in selection where isValidNode node and (superClassOf node) == GeometryClass collect node
    if nodes.count == 0 then throw "Select at least one geometry object."
    nodes
)

fn deadlimitVertexColorSetDisplay enabled =
(
    local nodes = deadlimitVertexColorSelectedGeometry()
    for node in nodes do
    (
        node.vertexColorType = #color
        node.showVertexColors = enabled
        node.vertexColorsShaded = enabled
    )
    deadlimitVertexColorRememberDisplayNodes nodes
    deadlimitVertexColorRefreshDisplay nodes
    nodes.count
)

fn deadlimitVertexColorToggleDisplay =
(
    local nodes = deadlimitVertexColorSelectedGeometry()
    local enabledCount = 0
    for node in nodes where node.showVertexColors and node.vertexColorsShaded do enabledCount += 1
    local enableDisplay = not (enabledCount > (nodes.count / 2.0))

    for node in nodes do
    (
        node.vertexColorType = #color
        node.showVertexColors = enableDisplay
        node.vertexColorsShaded = enableDisplay
    )
    deadlimitVertexColorRememberDisplayNodes nodes
    deadlimitVertexColorRefreshDisplay nodes
    #(nodes.count, enableDisplay, enabledCount)
)

fn deadlimitVertexColorToggleLineartDisplay =
(
    local nodes = deadlimitVertexColorSelectedGeometry()
    local alphaDisplayCount = 0
    for node in nodes where node.vertexColorType == #alpha and node.showVertexColors and node.vertexColorsShaded do alphaDisplayCount += 1
    local showLineart = not (alphaDisplayCount > (nodes.count / 2.0))
    local targetType = if showLineart then #alpha else #color

    for node in nodes do
    (
        node.vertexColorType = targetType
        node.showVertexColors = true
        node.vertexColorsShaded = true
    )
    deadlimitVertexColorRememberDisplayNodes nodes
    deadlimitVertexColorRefreshDisplay nodes
    #(nodes.count, showLineart, alphaDisplayCount)
)

/*
    FEATURE: BONE DISPLAY TOOLS

    These commands edit BoneGeometry presentation only:
    - FIT X uses average pivot-to-child-pivot distance; a leaf uses half its distance to
      the parent. Length planning happens before mutation so a multi-selection is stable.
    - SET applies a world-space centimeter length while compensating local X scale.
    - FLIP X normalizes negative length and rotates objectOffsetRot 180 degrees, producing
      a convex tail while the node transform/pivot remains unchanged.

    Joint transforms, hierarchy, names, Skin bindings, animation controllers, and export
    orientation must remain unchanged in their respective representations. A Blender port may use
    custom bone-shape/display properties; never rotate edit bones or pose bones to imitate
    this visual correction.
*/
fn deadlimitVertexColorSelectedBones =
(
    local bones = for node in selection where isValidNode node and (classOf node) == BoneGeometry collect node
    if bones.count == 0 then throw "Select at least one BoneGeometry bone."
    bones
)

fn deadlimitVertexColorFitBoneLengths =
(
    local bones = deadlimitVertexColorSelectedBones()

    -- Build the complete plan first so every target uses the original hierarchy.
    local lengthPlan = #()
    local skipped = 0
    for bone in bones do
    (
        local boneChildren = for child in bone.children where isValidNode child and (classOf child) == BoneGeometry collect child
        local targetWorldLength = undefined

        if boneChildren.count > 0 then
        (
            local totalDistance = 0.0
            for child in boneChildren do totalDistance += distance bone.pos child.pos
            targetWorldLength = totalDistance / boneChildren.count
        )
        else if isValidNode bone.parent then
        (
            targetWorldLength = (distance bone.pos bone.parent.pos) * 0.5
        )

        local localXAxisScale = length bone.transform.row1
        if targetWorldLength != undefined and targetWorldLength >= 0.0 and localXAxisScale > 0.000000001 then
        (
            local direction = if bone.length < 0.0 then -1.0 else 1.0
            append lengthPlan #(bone, direction * targetWorldLength / localXAxisScale)
        )
        else
            skipped += 1
    )

    undo "Fit Selected Bone Display Lengths" on
    (
        for item in lengthPlan do item[1].length = item[2]
    )

    redrawViews()
    #(lengthPlan.count, skipped)
)

fn deadlimitVertexColorSetSelectedBoneLength centimeters =
(
    if centimeters < 0.0 then throw "Bone display length cannot be negative. Use Flip Selected X for direction."
    local targetWorldLength = units.decodeValue ((centimeters as string) + "cm")
    local bones = deadlimitVertexColorSelectedBones()
    local lengthPlan = #()
    local skipped = 0

    for bone in bones do
    (
        local localXAxisScale = length bone.transform.row1
        if localXAxisScale > 0.000000001 then
        (
            local direction = if bone.length < 0.0 then -1.0 else 1.0
            append lengthPlan #(bone, direction * targetWorldLength / localXAxisScale)
        )
        else
            skipped += 1
    )

    undo "Set Selected Bone Display Length" on
    (
        for item in lengthPlan do item[1].length = item[2]
    )

    redrawViews()
    #(lengthPlan.count, skipped)
)

fn deadlimitVertexColorFlipSelectedBoneX =
(
    local bones = deadlimitVertexColorSelectedBones()
    local displayFlip = quat 180 [0,0,1]
    undo "Flip Selected Bone Display X" on
    (
        for bone in bones do
        (
            -- Convert the old negative-length flip to a regular convex bone mesh.
            if bone.length < 0.0 then bone.length = -bone.length
            -- Rotate only the display object around the node pivot. The node TM stays intact.
            bone.objectOffsetRot = bone.objectOffsetRot * displayFlip
        )
    )
    redrawViews()
    bones.count
)

/*
    FEATURE: VERTEX COLOR CONVERT / SPREAD / CLIPBOARD

    Terminology:
    - "Palette" is node.wireColor, one RGB value per object.
    - "Vertex" is Max map channel 0. readFirst samples the first map vertex actually used
      by a map face, which is the representative color for this solid-color workflow.
    - "Material" is the material reference itself; SPREAD assigns the same reference.

    Palette to Vertex writes a valid uniform channel 0 into an Editable Poly base object
    without collapsing or rebuilding its modifier stack. Vertex, edge, and face selections
    are restored explicitly after the map-channel write. Non-Editable-Poly targets keep the
    legacy temporary-modifier path for compatibility.
    Vertex to Palette reads the representative channel-0 color. SPREAD uses the first
    selected geometry as reference and applies enabled VERT/PALETTE/MAT fields to the rest.
    Per-object failures are collected so one unsupported target does not cancel all targets.

    Porting invariant: preserve selection order, editable base-object type, component
    selections, modifier instances/order/settings, and place the color attribute below
    deformation. This feature intentionally does not transfer a spatially varying paint
    pattern between different meshes.
*/
fn deadlimitVertexColorBakeEditablePoly node fillColor =
(
    local editablePoly = node.baseObject
    if (classOf editablePoly) != Editable_Poly then
        throw "Direct Vertex Color write requires an Editable Poly base object."

    local vertexSelection = copy (polyop.getVertSelection editablePoly)
    local edgeSelection = copy (polyop.getEdgeSelection editablePoly)
    local faceSelection = copy (polyop.getFaceSelection editablePoly)
    local mapColor = [fillColor.r / 255.0, fillColor.g / 255.0, fillColor.b / 255.0]
    local vertexCount = polyop.getNumVerts editablePoly
    local faceCount = polyop.getNumFaces editablePoly

    try
    (
        with redraw off
        (
            polyop.setMapSupport editablePoly 0 true
            polyop.setNumMapVerts editablePoly 0 vertexCount keep:false
            for vertexIndex = 1 to vertexCount do
                polyop.setMapVert editablePoly 0 vertexIndex mapColor

            polyop.setNumMapFaces editablePoly 0 faceCount keep:false
            for faceIndex = 1 to faceCount do
                polyop.setMapFace editablePoly 0 faceIndex (polyop.getFaceVerts editablePoly faceIndex)

            polyop.setVertSelection editablePoly vertexSelection
            polyop.setEdgeSelection editablePoly edgeSelection
            polyop.setFaceSelection editablePoly faceSelection
            update node
            node.vertexColorType = #color
            node.showVertexColors = true
            node.vertexColorsShaded = true
        )
        true
    )
    catch
    (
        try(polyop.setVertSelection editablePoly vertexSelection)catch()
        try(polyop.setEdgeSelection editablePoly edgeSelection)catch()
        try(polyop.setFaceSelection editablePoly faceSelection)catch()
        throw()
    )
)

fn deadlimitVertexColorBakeNode node fillColor =
(
    if (classOf node.baseObject) == Editable_Poly then
        return deadlimitVertexColorBakeEditablePoly node fillColor

    local paintModifier = undefined
    local fillModifier = undefined
    try
    (
        -- Put both temporary modifiers at the bottom of the stack. This keeps
        -- Skin and every existing user modifier above the baked color data.
        paintModifier = VertexPaint()
        addModifier node paintModifier before:(node.modifiers.count + 1)
        fillModifier = DeadlimitPaletteVertexFillModifier fillColor:fillColor
        addModifier node fillModifier before:(node.modifiers.count + 1)

        local paintIndex = findItem node.modifiers paintModifier
        if paintIndex == 0 then throw "Could not place VertexPaint in the modifier stack."
        if not (maxOps.CollapseNodeTo node paintIndex true) then throw "Collapse To failed."

        node.vertexColorType = #color
        node.showVertexColors = true
        node.vertexColorsShaded = true
        true
    )
    catch
    (
        try (if fillModifier != undefined do deleteModifier node fillModifier) catch()
        try (if paintModifier != undefined do deleteModifier node paintModifier) catch()
        throw()
    )
)

fn deadlimitVertexColorReadFirst node =
(
    local snapshotMesh = undefined
    try
    (
        snapshotMesh = snapshotAsMesh node
        if not (meshop.getMapSupport snapshotMesh 0) then throw "Vertex Color channel 0 is missing."
        local mapVertCount = meshop.getNumMapVerts snapshotMesh 0
        if mapVertCount < 1 then throw "Vertex Color channel 0 is empty."

        local mapVertIndex = 0
        local mapFaceCount = meshop.getNumMapFaces snapshotMesh 0
        for faceIndex = 1 to mapFaceCount while mapVertIndex == 0 do
        (
            local mapFace = meshop.getMapFace snapshotMesh 0 faceIndex
            if mapFace.x > 0 then mapVertIndex = mapFace.x as integer
            else if mapFace.y > 0 then mapVertIndex = mapFace.y as integer
            else if mapFace.z > 0 then mapVertIndex = mapFace.z as integer
        )
        if mapVertIndex == 0 then mapVertIndex = 1

        local mapColor = meshop.getMapVert snapshotMesh 0 mapVertIndex
        local red = amax 0.0 (amin 255.0 (mapColor.x * 255.0))
        local green = amax 0.0 (amin 255.0 (mapColor.y * 255.0))
        local blue = amax 0.0 (amin 255.0 (mapColor.z * 255.0))
        local result = color red green blue
        delete snapshotMesh
        snapshotMesh = undefined
        result
    )
    catch
    (
        try (if snapshotMesh != undefined do delete snapshotMesh) catch()
        throw()
    )
)

fn deadlimitVertexColorFillPaletteToVertex =
(
    local nodes = deadlimitVertexColorSelectedGeometry()
    local changed = 0
    local errors = #()
    local undoLabel = "FillPaletteToVertex"
    local beforeSnapshots = deadlimitVertexColorCaptureUndoSnapshots nodes

    undo undoLabel on
    (
        for node in nodes do
        (
            try
            (
                deadlimitVertexColorBakeNode node node.wireColor
                changed += 1
            )
            catch
            (
                local errorText = substituteString (getCurrentException()) "-- Runtime error: " ""
                append errors (node.name + ": " + errorText)
            )
        )
    )

    deadlimitVertexColorRememberDisplayNodes nodes
    deadlimitVertexColorRefreshDisplay nodes
    local afterSnapshots = deadlimitVertexColorCaptureUndoSnapshots nodes
    deadlimitVertexColorRecordUndoTransaction undoLabel beforeSnapshots afterSnapshots
    #(changed, errors)
)

fn deadlimitVertexColorFillVertexToPalette =
(
    local nodes = deadlimitVertexColorSelectedGeometry()
    local changed = 0
    local errors = #()

    undo "FillVertexToPalette" on
    (
        for node in nodes do
        (
            try
            (
                node.wireColor = deadlimitVertexColorReadFirst node
                changed += 1
            )
            catch
            (
                local errorText = substituteString (getCurrentException()) "-- Runtime error: " ""
                append errors (node.name + ": " + errorText)
            )
        )
    )

    redrawViews()
    #(changed, errors)
)

fn deadlimitVertexColorRedistribute applyVertex applyPalette applyMaterial =
(
    if not applyVertex and not applyPalette and not applyMaterial then
        throw "Enable VERT, PALETTE, or MAT before redistributing."

    local nodes = deadlimitVertexColorSelectedGeometry()
    if nodes.count < 2 then throw "Select the reference mesh first, then at least one target mesh."

    local referenceNode = nodes[1]
    local referenceVertexColor = if applyVertex then deadlimitVertexColorReadFirst referenceNode else undefined
    local referencePaletteColor = if applyPalette then copy referenceNode.wireColor else undefined
    local referenceMaterial = if applyMaterial then referenceNode.material else undefined
    local changed = 0
    local errors = #()
    local undoLabel = "Redistribute Colors"
    local targetNodes = for nodeIndex = 2 to nodes.count collect nodes[nodeIndex]
    local beforeSnapshots = if applyVertex then deadlimitVertexColorCaptureUndoSnapshots targetNodes else #()

    undo undoLabel on
    (
        for nodeIndex = 2 to nodes.count do
        (
            local node = nodes[nodeIndex]
            try
            (
                if applyVertex do deadlimitVertexColorBakeNode node referenceVertexColor
                if applyPalette do node.wireColor = referencePaletteColor
                if applyMaterial do node.material = referenceMaterial
                changed += 1
            )
            catch
            (
                local errorText = substituteString (getCurrentException()) "-- Runtime error: " ""
                append errors (node.name + ": " + errorText)
            )
        )
    )

    if applyVertex then
    (
        deadlimitVertexColorRememberDisplayNodes nodes
        deadlimitVertexColorRefreshDisplay nodes
        local afterSnapshots = deadlimitVertexColorCaptureUndoSnapshots targetNodes
        deadlimitVertexColorRecordUndoTransaction undoLabel beforeSnapshots afterSnapshots
    )
    else
        redrawViews()
    #(referenceNode.name, changed, errors)
)

fn deadlimitVertexColorCopyVertex =
(
    local nodes = deadlimitVertexColorSelectedGeometry()
    deadlimitVertexColorClipboard = deadlimitVertexColorReadFirst nodes[1]
    #(nodes[1].name, deadlimitVertexColorClipboard)
)

fn deadlimitVertexColorCopyPalette =
(
    local nodes = deadlimitVertexColorSelectedGeometry()
    deadlimitVertexColorClipboard = copy nodes[1].wireColor
    #(nodes[1].name, deadlimitVertexColorClipboard)
)

fn deadlimitVertexColorPasteVertex =
(
    if deadlimitVertexColorClipboard == undefined then throw "Color buffer is empty. Use Copy Vertex or Copy Palette first."
    local nodes = deadlimitVertexColorSelectedGeometry()
    local changed = 0
    local errors = #()
    local undoLabel = "Paste Vertex"
    local beforeSnapshots = deadlimitVertexColorCaptureUndoSnapshots nodes

    undo undoLabel on
    (
        for node in nodes do
        (
            try
            (
                deadlimitVertexColorBakeNode node deadlimitVertexColorClipboard
                changed += 1
            )
            catch
            (
                local errorText = substituteString (getCurrentException()) "-- Runtime error: " ""
                append errors (node.name + ": " + errorText)
            )
        )
    )

    deadlimitVertexColorRememberDisplayNodes nodes
    deadlimitVertexColorRefreshDisplay nodes
    local afterSnapshots = deadlimitVertexColorCaptureUndoSnapshots nodes
    deadlimitVertexColorRecordUndoTransaction undoLabel beforeSnapshots afterSnapshots
    #(changed, errors)
)

fn deadlimitVertexColorPastePalette =
(
    if deadlimitVertexColorClipboard == undefined then throw "Color buffer is empty. Use Copy Vertex or Copy Palette first."
    local nodes = deadlimitVertexColorSelectedGeometry()
    undo "Paste Palette" on
    (
        for node in nodes do node.wireColor = deadlimitVertexColorClipboard
    )
    redrawViews()
    nodes.count
)

/*
    FEATURE: INNER LINEART TOPOLOGY KERNEL

    User contract: selected manifold inner edges become an invisible, zero-width strip of
    real faces. Coincident face normals give the Deadlock shader an inner outline. There is
    no bevel width and no stack of chamfer rings. A chain endpoint collapses to one vertex;
    a three-way junction gets a triangle, a four-way junction gets a quad, and higher-valence
    junctions are capped without creating an arbitrary fan of visible internal rings.

    Algorithm outline:
    1. Validate non-zero selected edges, exactly two adjacent faces, and no pre-existing
       zero-area lineart face along the selection.
    2. At every selected vertex, partition its one-ring faces into sectors separated by the
       selected edges. Order closed junction sectors by adjacency.
    3. Create one coincident clone per sector and remap original face corners to their sector
       clone. Keep #(source, clones...) for Skin synchronization.
    4. Procedurally add the seam strip/junction cap and snap every clone to the original XYZ.
       Exact coincidence is essential; tiny offsets cause shading leaks.
    5. Rebuild all supported map channels by face-corner ownership. New seam map points are
       collapsed at the matching source corner, so UVs and vertex colors remain valid.
    6. Restore original smoothing groups, material IDs, edge visibility, and clear random
       sub-object selections. When both sides of a selected seam have the same smoothing
       mask, new lineart faces inherit it. A seam between different masks receives one
       globally unused smoothing-group bit so it stays distinct from both surfaces. Junction
       caps use the same all-equal versus mixed-neighborhood rule.
    7. Optional Alpha Marker writes black to special map channel -2 for every participating
       face corner. Existing non-marker alpha remains intact; a missing alpha channel is
       initialized white. Channel 0 RGB vertex paint is never modified by the marker.

    deadlimitVertexColorBuildInnerLineart is DCC-neutral in intent. Its return contract is
    #(newSeamFaces, newSeamEdges, resultingVertexCount, sourceCloneGroups). A Blender port
    should implement this kernel with BMesh/custom data layers and unit-test triangulated
    chains, T/X junctions, UV seams, color seams, hard edges, and repeated operations.
*/
fn deadlimitVertexColorPolyFaceArea poly faceIndex =
(
    local faceVerts = polyop.getFaceVerts poly faceIndex
    if faceVerts == undefined then return undefined
    if faceVerts.count < 3 then return 0.0

    local origin = polyop.getVert poly faceVerts[1]
    local doubleArea = 0.0
    for index = 2 to (faceVerts.count - 1) do
    (
        local edgeA = (polyop.getVert poly faceVerts[index]) - origin
        local edgeB = (polyop.getVert poly faceVerts[index + 1]) - origin
        doubleArea += length (cross edgeA edgeB)
    )
    doubleArea * 0.5
)

global deadlimitVertexColorBuildInnerLineart
global deadlimitInnerLineartEdgeHasVert
global deadlimitInnerLineartSectorIndex
global deadlimitInnerLineartOrderedSectors
global deadlimitInnerLineartBuildVertexSectors
global deadlimitInnerLineartFindVertexRecord
global deadlimitInnerLineartCloneForFace
global deadlimitInnerLineartDirectedEdge
global deadlimitInnerLineartThreeUnique
global deadlimitInnerLineartFindEdge
global deadlimitInnerLineartMapPoint
global deadlimitInnerLineartFaceArea

fn deadlimitInnerLineartEdgeHasVert edgeVerts vertIndex =
(
    edgeVerts != undefined and edgeVerts.count == 2 and (edgeVerts[1] == vertIndex or edgeVerts[2] == vertIndex)
)

fn deadlimitInnerLineartSectorIndex sectors faceIndex =
(
    for sectorIndex = 1 to sectors.count where (findItem sectors[sectorIndex] faceIndex) > 0 do return sectorIndex
    0
)

fn deadlimitInnerLineartOrderedSectors node selectedEdges sourceVert sectors =
(
    if sectors.count < 3 then return sectors

    local neighbors = for sectorIndex = 1 to sectors.count collect #()
    local incidentEdges = polyop.getEdgesUsingVert node #{sourceVert}
    for edgeIndex in incidentEdges where selectedEdges[edgeIndex] do
    (
        local edgeFaces = polyop.getEdgeFaces node edgeIndex
        if edgeFaces != undefined and edgeFaces.count == 2 do
        (
            local sectorA = deadlimitInnerLineartSectorIndex sectors edgeFaces[1]
            local sectorB = deadlimitInnerLineartSectorIndex sectors edgeFaces[2]
            if sectorA > 0 and sectorB > 0 and sectorA != sectorB do
            (
                if (findItem neighbors[sectorA] sectorB) == 0 do append neighbors[sectorA] sectorB
                if (findItem neighbors[sectorB] sectorA) == 0 do append neighbors[sectorB] sectorA
            )
        )
    )

    for sectorIndex = 1 to sectors.count where neighbors[sectorIndex].count != 2 do
        throw ("Selected edges form a non-manifold junction at vertex " + (sourceVert as string) + ".")

    local order = #(1)
    local previous = 0
    local current = 1
    while order.count < sectors.count do
    (
        local nextSector = if neighbors[current][1] != previous then neighbors[current][1] else neighbors[current][2]
        if (findItem order nextSector) > 0 do throw ("Selected junction cannot be ordered at vertex " + (sourceVert as string) + ".")
        append order nextSector
        previous = current
        current = nextSector
    )

    local ordered = #()
    for sectorIndex in order do append ordered sectors[sectorIndex]
    ordered
)

fn deadlimitInnerLineartBuildVertexSectors node selectedEdges selectedVerts =
(
    local records = #()
    for sourceVert in selectedVerts do
    (
        local incidentFaceBits = polyop.getFacesUsingVert node #{sourceVert}
        local incidentFaces = for faceIndex in incidentFaceBits collect faceIndex
        local remaining = deepCopy incidentFaces
        local sectors = #()

        while remaining.count > 0 do
        (
            local queue = #(remaining[1])
            local sector = #()
            deleteItem remaining 1
            while queue.count > 0 do
            (
                local faceIndex = queue[1]
                deleteItem queue 1
                append sector faceIndex
                local faceEdges = polyop.getFaceEdges node faceIndex
                for edgeIndex in faceEdges do
                (
                    local edgeVerts = polyop.getEdgeVerts node edgeIndex
                    if deadlimitInnerLineartEdgeHasVert edgeVerts sourceVert and not selectedEdges[edgeIndex] do
                    (
                        local edgeFaces = polyop.getEdgeFaces node edgeIndex
                        if edgeFaces != undefined do for neighborFace in edgeFaces where neighborFace != faceIndex do
                        (
                            local remainingIndex = findItem remaining neighborFace
                            if remainingIndex > 0 do
                            (
                                append queue neighborFace
                                deleteItem remaining remainingIndex
                            )
                        )
                    )
                )
            )
            append sectors sector
        )

        sectors = deadlimitInnerLineartOrderedSectors node selectedEdges sourceVert sectors
        local normal = [0,0,0]
        for faceIndex in incidentFaces do normal += polyop.getFaceNormal node faceIndex
        if length normal <= 1e-8 then normal = [0,0,1] else normal = normalize normal
        local referenceAxis = if abs normal.z < 0.9 then [0,0,1] else [0,1,0]
        local basisX = cross normal referenceAxis
        if length basisX <= 1e-8 then basisX = [1,0,0] else basisX = normalize basisX
        local basisY = normalize (cross normal basisX)
        append records #(sourceVert, sectors, #(), basisX, basisY)
    )
    records
)

fn deadlimitInnerLineartFindVertexRecord records sourceVert =
(
    for record in records where record[1] == sourceVert do return record
    undefined
)

fn deadlimitInnerLineartCloneForFace records sourceVert faceIndex =
(
    local record = deadlimitInnerLineartFindVertexRecord records sourceVert
    if record == undefined then return sourceVert
    local sectorIndex = deadlimitInnerLineartSectorIndex record[2] faceIndex
    if sectorIndex == 0 then throw ("Could not resolve vertex sector for vertex " + (sourceVert as string) + ", face " + (faceIndex as string) + ".")
    record[3][sectorIndex]
)

fn deadlimitInnerLineartDirectedEdge faceVerts vertA vertB =
(
    for cornerIndex = 1 to faceVerts.count do
    (
        local nextIndex = if cornerIndex == faceVerts.count then 1 else cornerIndex + 1
        if faceVerts[cornerIndex] == vertA and faceVerts[nextIndex] == vertB then return #(vertA, vertB)
        if faceVerts[cornerIndex] == vertB and faceVerts[nextIndex] == vertA then return #(vertB, vertA)
    )
    undefined
)

fn deadlimitInnerLineartFindFreeSmoothingGroup smoothingGroups =
(
    local usedMask = 0
    for smoothingGroup in smoothingGroups do usedMask = bit.or usedMask smoothingGroup

    for bitIndex = 0 to 31 do
    (
        local candidate = bit.shift 1 bitIndex
        if (bit.and usedMask candidate) == 0 do return candidate
    )
    0
)

fn deadlimitInnerLineartThreeUnique verts =
(
    verts.count == 3 and verts[1] != verts[2] and verts[1] != verts[3] and verts[2] != verts[3]
)

fn deadlimitInnerLineartFindEdge node vertA vertB =
(
    local commonEdges = (polyop.getEdgesUsingVert node #{vertA}) * (polyop.getEdgesUsingVert node #{vertB})
    for edgeIndex in commonEdges do return edgeIndex
    0
)

fn deadlimitInnerLineartMapPoint mapRecord sourceFaceVerts faceIndex sourceVert =
(
    local cornerIndex = findItem sourceFaceVerts[faceIndex] sourceVert
    if cornerIndex == 0 then throw ("Map source vertex is not used by face " + (faceIndex as string) + ".")
    local sourceMapFace = mapRecord[3][faceIndex]
    if cornerIndex > sourceMapFace.count then throw ("Map face degree mismatch on face " + (faceIndex as string) + ".")
    local mapVertIndex = sourceMapFace[cornerIndex]
    mapRecord[2][mapVertIndex]
)

fn deadlimitInnerLineartFaceArea node faceIndex =
(
    local faceVerts = polyop.getFaceVerts node faceIndex
    if faceVerts == undefined or faceVerts.count < 3 then return 0.0
    local origin = polyop.getVert node faceVerts[1]
    local doubleArea = 0.0
    for cornerIndex = 2 to (faceVerts.count - 1) do
    (
        local edgeA = polyop.getVert node faceVerts[cornerIndex] - origin
        local edgeB = polyop.getVert node faceVerts[cornerIndex + 1] - origin
        doubleArea += length (cross edgeA edgeB)
    )
    doubleArea * 0.5
)

fn deadlimitVertexColorBuildInnerLineart node selectedEdges sourceSegments minimumLength oldVertCount oldFaceCount vertexMarker:false =
(
    local sourceVertPositions = for vertIndex = 1 to oldVertCount collect (polyop.getVert node vertIndex)
    local sourceFaceVerts = #()
    local sourceFaceSmoothing = #()
    local sourceFaceMaterials = #()
    local sourceFaceEdgeVisibility = #()
    for faceIndex = 1 to oldFaceCount do
    (
        local faceVerts = polyop.getFaceVerts node faceIndex
        local faceEdges = polyop.getFaceEdges node faceIndex
        append sourceFaceVerts faceVerts
        append sourceFaceSmoothing (polyop.getFaceSmoothGroup node faceIndex)
        append sourceFaceMaterials (polyop.getFaceMatID node faceIndex)
        append sourceFaceEdgeVisibility (for edgeIndex in faceEdges collect (polyop.getEdgeVis node edgeIndex))
    )
    local separateSeamSmoothing = deadlimitInnerLineartFindFreeSmoothingGroup sourceFaceSmoothing

    local sourceNumMaps = polyop.getNumMaps node
    local sourceMapChannels = #(-2, -1)
    if sourceNumMaps > 0 do for mapChannel = 0 to (sourceNumMaps - 1) do append sourceMapChannels mapChannel
    local sourceMapRecords = #()
    for mapChannel in sourceMapChannels where polyop.getMapSupport node mapChannel do
    (
        local mapVertCount = polyop.getNumMapVerts node mapChannel
        local mapFaceCount = polyop.getNumMapFaces node mapChannel
        if mapVertCount <= 0 or mapFaceCount < oldFaceCount then
            throw ("Map channel " + (mapChannel as string) + " is incomplete and cannot be preserved safely.")
        local mapVerts = for mapVertIndex = 1 to mapVertCount collect (polyop.getMapVert node mapChannel mapVertIndex)
        local mapFaces = #()
        for faceIndex = 1 to oldFaceCount do
        (
            local mapFace = polyop.getMapFace node mapChannel faceIndex
            if mapFace.count != sourceFaceVerts[faceIndex].count then
                throw ("Map channel " + (mapChannel as string) + " has a face-degree mismatch on face " + (faceIndex as string) + ".")
            append mapFaces mapFace
        )
        append sourceMapRecords #(mapChannel, mapVerts, mapFaces)
    )

    local selectedVerts = polyop.getVertsUsingEdge node selectedEdges
    local selectedEdgeCounts = for vertIndex = 1 to oldVertCount collect 0
    for edgeIndex in selectedEdges do
    (
        local edgeVerts = polyop.getEdgeVerts node edgeIndex
        selectedEdgeCounts[edgeVerts[1]] += 1
        selectedEdgeCounts[edgeVerts[2]] += 1
    )
    for edgeIndex in selectedEdges do
    (
        local edgeVerts = polyop.getEdgeVerts node edgeIndex
        if selectedEdgeCounts[edgeVerts[1]] == 1 and selectedEdgeCounts[edgeVerts[2]] == 1 do
            throw ("Isolated selected edge " + (edgeIndex as string) + " cannot terminate a connected zero-width face at both ends. Select a connected chain of at least two edges.")
    )
    local vertexRecords = deadlimitInnerLineartBuildVertexSectors node selectedEdges selectedVerts
    local constructionOffset = minimumLength * 0.001
    if constructionOffset < 1e-6 do constructionOffset = 1e-6
    local outputPositions = deepCopy sourceVertPositions
    local snapPairs = #()
    local markerVerts = #{}
    local seamGroups = #()

    for record in vertexRecords do
    (
        local sourceVert = record[1]
        local sourcePosition = sourceVertPositions[sourceVert]
        local sectorCount = record[2].count
        local cloneIds = #()
        for sectorIndex = 1 to sectorCount do
        (
            local cloneId = sourceVert
            if sectorIndex > 1 do
            (
                append outputPositions sourcePosition
                cloneId = outputPositions.count
            )
            local temporaryPosition = sourcePosition
            if sectorCount > 1 do
            (
                local angle = 360.0 * (sectorIndex - 1) / sectorCount
                temporaryPosition += ((record[4] * cos angle) + (record[5] * sin angle)) * constructionOffset
            )
            outputPositions[cloneId] = temporaryPosition
            append cloneIds cloneId
            append snapPairs #(cloneId, sourcePosition)
            markerVerts[cloneId] = true
        )
        if cloneIds.count > 1 do append seamGroups (deepCopy cloneIds)
        record[3] = cloneIds
    )

    -- Face record: geometry verts, smoothing group, material ID, edge visibility, source map refs, seam flag.
    local faceRecords = #()
    for faceIndex = 1 to oldFaceCount do
    (
        local outputFaceVerts = deepCopy sourceFaceVerts[faceIndex]
        for cornerIndex = 1 to outputFaceVerts.count do
        (
            local sourceVert = sourceFaceVerts[faceIndex][cornerIndex]
            if selectedVerts[sourceVert] do outputFaceVerts[cornerIndex] = deadlimitInnerLineartCloneForFace vertexRecords sourceVert faceIndex
        )
        append faceRecords #(outputFaceVerts, sourceFaceSmoothing[faceIndex], sourceFaceMaterials[faceIndex], sourceFaceEdgeVisibility[faceIndex], undefined, false)
    )

    local hiddenEdgePairs = #()
    for edgeIndex in selectedEdges do
    (
        local edgeVerts = polyop.getEdgeVerts node edgeIndex
        local edgeFaces = polyop.getEdgeFaces node edgeIndex
        local directed = deadlimitInnerLineartDirectedEdge sourceFaceVerts[edgeFaces[1]] edgeVerts[1] edgeVerts[2]
        if directed == undefined then throw ("Could not resolve winding for selected edge " + (edgeIndex as string) + ".")
        local sourceA = directed[1]
        local sourceB = directed[2]
        local faceA = edgeFaces[1]
        local faceB = edgeFaces[2]
        local smoothingA = sourceFaceSmoothing[faceA]
        local smoothingB = sourceFaceSmoothing[faceB]
        local seamSmoothing = smoothingA
        if smoothingA != smoothingB do
        (
            if separateSeamSmoothing == 0 then throw "INNER LINEART needs one free smoothing group for a mixed-smoothing seam."
            seamSmoothing = separateSeamSmoothing
        )
        local aA = deadlimitInnerLineartCloneForFace vertexRecords sourceA faceA
        local bA = deadlimitInnerLineartCloneForFace vertexRecords sourceB faceA
        local aB = deadlimitInnerLineartCloneForFace vertexRecords sourceA faceB
        local bB = deadlimitInnerLineartCloneForFace vertexRecords sourceB faceB
        local triA = #(bA, aA, aB)
        local triB = #(bA, aB, bB)
        local madeA = false
        local madeB = false
        if deadlimitInnerLineartThreeUnique triA do
        (
            append faceRecords #(triA, seamSmoothing, sourceFaceMaterials[faceA], #(true,true,true), #(#(faceA,sourceB), #(faceA,sourceA), #(faceA,sourceA)), true)
            madeA = true
        )
        if deadlimitInnerLineartThreeUnique triB do
        (
            append faceRecords #(triB, seamSmoothing, sourceFaceMaterials[faceB], #(true,true,true), #(#(faceB,sourceB), #(faceB,sourceA), #(faceB,sourceB)), true)
            madeB = true
        )
        if madeA and madeB do append hiddenEdgePairs #(bA, aB)

        if not madeA and not madeB do
        (
            local sourcePosition = sourceVertPositions[sourceB]
            local vertexRecord = deadlimitInnerLineartFindVertexRecord vertexRecords sourceB
            append outputPositions (sourcePosition + vertexRecord[5] * constructionOffset)
            local extraClone = outputPositions.count
            append snapPairs #(extraClone, sourcePosition)
            markerVerts[extraClone] = true
            local foundSourceGroup = false
            for groupIndex = 1 to seamGroups.count where seamGroups[groupIndex][1] == sourceB do (append seamGroups[groupIndex] extraClone; foundSourceGroup = true)
            if not foundSourceGroup do append seamGroups #(sourceB, extraClone)
            local fallbackTri = #(bA, aA, extraClone)
            append faceRecords #(fallbackTri, seamSmoothing, sourceFaceMaterials[faceA], #(true,true,true), #(#(faceA,sourceB), #(faceA,sourceA), #(faceA,sourceB)), true)
        )
    )

    for record in vertexRecords where record[3].count >= 3 do
    (
        local clones = record[3]
        local sourceVert = record[1]
        local sourceFace = record[2][1][1]
        local sourceRef = #(sourceFace, sourceVert)
        local capSmoothing = sourceFaceSmoothing[sourceFace]
        local mixedCapSmoothing = false
        for sector in record[2] while not mixedCapSmoothing do
            for incidentFace in sector where sourceFaceSmoothing[incidentFace] != capSmoothing do mixedCapSmoothing = true
        if mixedCapSmoothing do
        (
            if separateSeamSmoothing == 0 then throw "INNER LINEART needs one free smoothing group for a mixed-smoothing junction."
            capSmoothing = separateSeamSmoothing
        )
        local capMaterial = sourceFaceMaterials[sourceFace]

        if clones.count <= 4 then
        (
            local capFace = deepCopy clones
            local capVisibility = for cornerIndex = 1 to capFace.count collect true
            local capRefs = for cornerIndex = 1 to capFace.count collect sourceRef
            append faceRecords #(capFace, capSmoothing, capMaterial, capVisibility, capRefs, true)
        )
        else
        (
            -- Keep every junction face at quad size or smaller while retaining
            -- real, visible edges through the center cap.
            local sectorIndex = 2
            while sectorIndex < clones.count do
            (
                local remaining = clones.count - sectorIndex + 1
                local capFace = if remaining >= 3 then
                    #(clones[1], clones[sectorIndex], clones[sectorIndex + 1], clones[sectorIndex + 2])
                else
                    #(clones[1], clones[sectorIndex], clones[sectorIndex + 1])
                local capVisibility = for cornerIndex = 1 to capFace.count collect true
                local capRefs = for cornerIndex = 1 to capFace.count collect sourceRef
                append faceRecords #(capFace, capSmoothing, capMaterial, capVisibility, capRefs, true)
                sectorIndex += if remaining >= 3 then 2 else 1
            )
        )
    )

    polyop.deleteFaces node #{1..oldFaceCount} delIsoVerts:true
    polyop.deleteIsoVerts node
    if polyop.getNumVerts node != 0 or polyop.getNumFaces node != 0 then throw "INNER LINEART could not clear its working copy."

    for vertexIndex = 1 to outputPositions.count do
    (
        local createdVert = polyop.createVert node outputPositions[vertexIndex]
        if createdVert != vertexIndex then throw "INNER LINEART vertex reconstruction changed vertex ordering."
    )

    local seamFaces = #{}
    for faceIndex = 1 to faceRecords.count do
    (
        local record = faceRecords[faceIndex]
        local createdFace = polyop.createPolygon node record[1]
        if createdFace == undefined or createdFace != faceIndex then throw ("INNER LINEART could not reconstruct face " + (faceIndex as string) + ".")
        polyop.setFaceSmoothGroup node #{faceIndex} record[2]
        polyop.setFaceMatID node #{faceIndex} record[3]
        if record[6] do seamFaces[faceIndex] = true
    )
    polyop.fillInMesh node

    for faceIndex = 1 to oldFaceCount do
    (
        local faceEdges = polyop.getFaceEdges node faceIndex
        local visibility = faceRecords[faceIndex][4]
        for cornerIndex = 1 to faceEdges.count do polyop.setEdgeVis node #{faceEdges[cornerIndex]} visibility[cornerIndex]
    )
    for edgePair in hiddenEdgePairs do
    (
        local edgeIndex = deadlimitInnerLineartFindEdge node edgePair[1] edgePair[2]
        if edgeIndex > 0 do polyop.setEdgeVis node #{edgeIndex} false
    )

    for snapPair in snapPairs do polyop.setVert node snapPair[1] snapPair[2]

    polyop.setNumMaps node sourceNumMaps keep:false
    for mapRecord in sourceMapRecords do
    (
        local mapChannel = mapRecord[1]
        local outputMapVerts = deepCopy mapRecord[2]
        local outputMapFaces = #()
        for faceIndex = 1 to oldFaceCount do append outputMapFaces (deepCopy mapRecord[3][faceIndex])
        for faceIndex = (oldFaceCount + 1) to faceRecords.count do
        (
            local refs = faceRecords[faceIndex][5]
            local outputMapFace = #()
            for sourceRef in refs do
            (
                append outputMapVerts (deadlimitInnerLineartMapPoint mapRecord sourceFaceVerts sourceRef[1] sourceRef[2])
                append outputMapFace outputMapVerts.count
            )
            append outputMapFaces outputMapFace
        )

        if vertexMarker and mapChannel == -2 do
        (
            for faceIndex = 1 to faceRecords.count do
            (
                local geomVerts = faceRecords[faceIndex][1]
                local mapFace = outputMapFaces[faceIndex]
                for cornerIndex = 1 to geomVerts.count where markerVerts[geomVerts[cornerIndex]] do
                (
                    append outputMapVerts [0,0,0]
                    mapFace[cornerIndex] = outputMapVerts.count
                )
            )
        )

        polyop.setMapSupport node mapChannel true
        polyop.setNumMapVerts node mapChannel outputMapVerts.count keep:false
        for mapVertIndex = 1 to outputMapVerts.count do polyop.setMapVert node mapChannel mapVertIndex outputMapVerts[mapVertIndex]
        polyop.setNumMapFaces node mapChannel outputMapFaces.count keep:false
        for mapFaceIndex = 1 to outputMapFaces.count do polyop.setMapFace node mapChannel mapFaceIndex outputMapFaces[mapFaceIndex]
    )

    local hasSourceVertexAlpha = false
    for mapRecord in sourceMapRecords where mapRecord[1] == -2 do hasSourceVertexAlpha = true
    if vertexMarker and not hasSourceVertexAlpha do
    (
        polyop.setMapSupport node -2 true
        polyop.setNumMapVerts node -2 2 keep:false
        polyop.setMapVert node -2 1 [1,1,1]
        polyop.setMapVert node -2 2 [0,0,0]
        polyop.setNumMapFaces node -2 faceRecords.count keep:false
        for faceIndex = 1 to faceRecords.count do
        (
            local geomVerts = faceRecords[faceIndex][1]
            local mapFace = for geomVert in geomVerts collect (if markerVerts[geomVert] then 2 else 1)
            polyop.setMapFace node -2 faceIndex mapFace
        )
    )

    local areaTolerance = minimumLength * minimumLength * 1e-8
    if areaTolerance < 1e-12 do areaTolerance = 1e-12
    for faceIndex in seamFaces where (deadlimitInnerLineartFaceArea node faceIndex) > areaTolerance do
        throw ("INNER LINEART generated a non-zero seam face: " + (faceIndex as string) + ".")

    local seamEdges = polyop.getEdgesUsingFace node seamFaces
    polyop.setVertSelection node #{}
    polyop.setEdgeSelection node #{}
    polyop.setFaceSelection node #{}
    #(seamFaces, seamEdges, polyop.getNumVerts node, seamGroups)
)


/*
    FEATURE: COMMIT INNER LINEART AT THE ACTIVE EDITABLE LEVEL

    The artist chooses the destination by making either the base Editable Poly or one Edit
    Poly modifier active. The operation must stay inside that exact level, including when
    it is sandwiched between Skin and other modifiers. It must not add a surprise permanent
    Edit Poly modifier at the top of the stack.

    Max adapter strategy:
    - Base Editable Poly: build on a copy, replace baseObject inside one Undo transaction,
      then restore Skin and persistent seam metadata.
    - Active Edit Poly modifier: isolate/evaluate only through that modifier on a temporary
      node, run the kernel, attach the complete result into the same modifier, remove the
      old faces there, restore smoothing/selections, and delete the temporary node.
    - Bare Editable Mesh: convert it to Editable Poly in the same Undo transaction. A mesh
      with modifiers requires the user to choose an explicit Edit Poly level first.

    Preflight compares vertex/face/edge counts at the chosen level. Any topology-changing
    modifier between that level and Skin is rejected before commit because Skin's index-based
    weights would address the wrong vertices. Runtime failure rolls back the whole transaction.

    Porting invariant: expose an explicit editable target and preserve modifier order. Build
    and validate temporary topology first, then atomically replace/commit only that target.
*/
fn deadlimitVertexColorCreateInnerLineartFromBase useUndo:true vertexMarker:false =
(
    if selection.count != 1 then throw "Select one Editable Poly object and its inner edges."

    local node = selection[1]
    if not isValidNode node or (classOf node.baseObject) != Editable_Poly then
        throw "INNER LINEART requires one Editable Poly base object."
    if subObjectLevel != 2 then throw "Enter Edge sub-object mode and select the inner edges."

    local sourcePoly = node.baseObject
    local selectedEdges = polyop.getEdgeSelection sourcePoly
    local sourceEdgeCount = selectedEdges.numberSet
    if sourceEdgeCount == 0 then throw "Select at least one inner edge."

    local sourceSegments = #()
    local invalidEdges = #()
    local existingLineartEdges = #()
    local minimumLength = undefined
    for edgeIndex in selectedEdges do
    (
        local edgeFaces = polyop.getEdgeFaces sourcePoly edgeIndex
        if edgeFaces == undefined or edgeFaces.count != 2 then
            append invalidEdges edgeIndex
        else
        (
            local edgeVerts = polyop.getEdgeVerts sourcePoly edgeIndex
            if edgeVerts == undefined then
                append invalidEdges edgeIndex
            else
            (
                local a = polyop.getVert sourcePoly edgeVerts[1]
                local b = polyop.getVert sourcePoly edgeVerts[2]
                local edgeLength = distance a b
                if edgeLength <= 1e-8 then
                    append invalidEdges edgeIndex
                else
                (
                    local lineartTolerance = edgeLength * edgeLength * 1e-8
                    if lineartTolerance < 1e-12 do lineartTolerance = 1e-12
                    for faceIndex in edgeFaces while (findItem existingLineartEdges edgeIndex) == 0 do
                    (
                        local adjacentArea = deadlimitVertexColorPolyFaceArea sourcePoly faceIndex
                        if adjacentArea != undefined and adjacentArea <= lineartTolerance do append existingLineartEdges edgeIndex
                    )
                    append sourceSegments #(a, b)
                    if minimumLength == undefined or edgeLength < minimumLength do minimumLength = edgeLength
                )
            )
        )
    )

    if invalidEdges.count > 0 then
        throw ("Selected edges must be non-zero inner edges shared by exactly two faces. Invalid edge: " + (invalidEdges[1] as string) + ".")
    if existingLineartEdges.count > 0 then
        throw ("Selected edge already borders a zero-width INNER LINEART face: " + (existingLineartEdges[1] as string) + ".")

    local oldVertCount = polyop.getNumVerts sourcePoly
    local oldFaceCount = polyop.getNumFaces sourcePoly
    local sourceSmoothingGroups = for faceIndex = 1 to oldFaceCount collect (polyop.getFaceSmoothGroup sourcePoly faceIndex)
    local existingSeamGroups = deadlimitInnerLineartReadGroups node
    local skinSnapshots = deadlimitVertexColorCaptureInnerLineartSkins node undefined oldVertCount
    local workingPoly = copy sourcePoly
    local operationResult = deadlimitVertexColorBuildInnerLineart workingPoly selectedEdges sourceSegments minimumLength oldVertCount oldFaceCount vertexMarker:vertexMarker
    for faceIndex = 1 to oldFaceCount do polyop.setFaceSmoothGroup workingPoly faceIndex sourceSmoothingGroups[faceIndex]
    local applyError = undefined
    local originalMaterial = node.material

    if useUndo then
    (
        undo "Create Inner Lineart" on
        (
            try
            (
                node.baseObject = workingPoly
                node.material = originalMaterial
                polyop.setVertSelection node.baseObject #{}
                polyop.setEdgeSelection node.baseObject #{}
                polyop.setFaceSelection node.baseObject #{}
                update node
                select node
                deadlimitVertexColorRestoreInnerLineartSkins node skinSnapshots operationResult[4] operationResult[3]
                deadlimitInnerLineartWriteGroups node (deadlimitInnerLineartMergeGroups existingSeamGroups operationResult[4])
                max modify mode
                modPanel.setCurrentObject node.baseObject node:node ui:true
                subObjectLevel = 2
            )
            catch (applyError = getCurrentException())
        )
    )
    else
    (
        with undo off
        (
            try
            (
                node.baseObject = workingPoly
                node.material = originalMaterial
                polyop.setVertSelection node.baseObject #{}
                polyop.setEdgeSelection node.baseObject #{}
                polyop.setFaceSelection node.baseObject #{}
                update node
                select node
                deadlimitVertexColorRestoreInnerLineartSkins node skinSnapshots operationResult[4] operationResult[3]
                deadlimitInnerLineartWriteGroups node (deadlimitInnerLineartMergeGroups existingSeamGroups operationResult[4])
                max modify mode
                modPanel.setCurrentObject node.baseObject node:node ui:true
                subObjectLevel = 2
            )
            catch (applyError = getCurrentException())
        )
    )

    if applyError != undefined then
    (
        if useUndo do try(max undo)catch()
        with undo off try(node.baseObject = sourcePoly)catch()
        throw applyError
    )

    redrawViews()
    #(sourceEdgeCount, operationResult[1].numberSet, operationResult[2].numberSet, operationResult[3] - oldVertCount)
)
fn deadlimitVertexColorReadFaceSmoothingGroups poly =
(
    local smoothingGroups = #()
    for faceIndex = 1 to (polyop.getNumFaces poly) do append smoothingGroups (polyop.getFaceSmoothGroup poly faceIndex)
    smoothingGroups
)
fn deadlimitVertexColorRestoreModifierSmoothingGroups modifier node smoothingGroups faceOffset:0 =
(
    local allFaces = #{(faceOffset + 1)..(faceOffset + smoothingGroups.count)}
    modifier.SetEPolySelLevel #Face
    subObjectLevel = 4
    modifier.SetSelection #Face allFaces node:node
    modifier.Select #Face allFaces node:node
    modifier.smoothingGroupsToSet = 0
    modifier.smoothingGroupsToClear = -1
    modifier.ButtonOp #SetSmooth

    local uniqueGroups = #()
    for smoothingGroup in smoothingGroups where (findItem uniqueGroups smoothingGroup) == 0 do append uniqueGroups smoothingGroup
    for smoothingGroup in uniqueGroups where smoothingGroup != 0 do
    (
        local groupFaces = #{}
        for faceIndex = 1 to smoothingGroups.count where smoothingGroups[faceIndex] == smoothingGroup do groupFaces[faceOffset + faceIndex] = true
        modifier.SetSelection #Face groupFaces node:node
        modifier.Select #Face groupFaces node:node
        modifier.smoothingGroupsToSet = smoothingGroup
        modifier.smoothingGroupsToClear = 0
        modifier.ButtonOp #SetSmooth
    )
)
fn deadlimitVertexColorSetModifierSelections modifier node selections =
(
    modifier.SetSelection #Vertex (copy selections[1]) node:node
    modifier.SetSelection #Edge (copy selections[2]) node:node
    modifier.SetSelection #Face (copy selections[3]) node:node
)
fn deadlimitVertexColorApplyInnerLineartModifierResult node sourceModifier workingNode oldFaceCount originalMaterial preparedSmoothingGroups =
(
    if sourceModifier == undefined or (modPanel.getModifierIndex node sourceModifier) == undefined then
        throw "The active Edit Poly modifier is no longer in the selected object's stack."
    try
    (
        select node
        max modify mode
        subObjectLevel = 0

        modPanel.setCurrentObject sourceModifier node:node ui:true

        sourceModifier.Attach workingNode editPolyNode:node
        node.material = originalMaterial
        update node
        deadlimitVertexColorRestoreModifierSmoothingGroups sourceModifier node preparedSmoothingGroups faceOffset:oldFaceCount

        local originalFaces = #{1..oldFaceCount}
        sourceModifier.SetEPolySelLevel #Face
        subObjectLevel = 4
        sourceModifier.SetSelection #Face originalFaces node:node
        sourceModifier.Select #Face originalFaces node:node
        sourceModifier.ButtonOp #DeleteFace
        sourceModifier.ButtonOp #RemoveIsoVerts
        update node
        deadlimitVertexColorRestoreModifierSmoothingGroups sourceModifier node preparedSmoothingGroups
        deadlimitVertexColorSetModifierSelections sourceModifier node #(#{}, #{}, #{})
        update node

        modPanel.setCurrentObject sourceModifier node:node ui:true
        sourceModifier.SetEPolySelLevel #Edge
        subObjectLevel = 2
        sourceModifier
    )
    catch
    (
        throw()
    )
)

fn deadlimitVertexColorCreateInnerLineartFromModifier node sourceModifier useUndo:true vertexMarker:false =
(
    local sourceModifierIndex = modPanel.getModifierIndex node sourceModifier
    if sourceModifierIndex == undefined then throw "The active Edit Poly modifier is no longer in the selected object's stack."
    if subObjectLevel != 2 then throw "Enter Edge sub-object mode in the active Edit Poly modifier and select the inner edges."

    local selectedEdges = copy (sourceModifier.GetSelection #Edge node:node)
    local sourceEdgeCount = selectedEdges.numberSet
    if sourceEdgeCount == 0 then throw "Select at least one inner edge in the active Edit Poly modifier."

    local oldVertCount = sourceModifier.GetNumVertices node:node
    local oldFaceCount = sourceModifier.GetNumFaces node:node
    local oldEdgeCount = sourceModifier.GetNumEdges node:node
    local sourceSmoothingGroups = for faceIndex = 1 to oldFaceCount collect (sourceModifier.GetFaceSmoothingGroups faceIndex node:node)
    local existingSeamGroups = deadlimitInnerLineartReadGroups node
    local skinSnapshots = deadlimitVertexColorCaptureInnerLineartSkins node sourceModifier oldVertCount
    if oldVertCount <= 0 or oldFaceCount <= 0 or oldEdgeCount <= 0 then
        throw "The active Edit Poly modifier does not expose a valid polygon mesh at this stack level."

    for edgeIndex in selectedEdges do
        if edgeIndex < 1 or edgeIndex > oldEdgeCount do throw ("Selected edge is outside the active Edit Poly topology: " + (edgeIndex as string) + ".")

    local workingNode = undefined
    local workingPoly = undefined
    local operationResult = undefined
    local preparedSmoothingGroups = undefined
    local sourceSegments = #()
    local invalidEdges = #()
    local existingLineartEdges = #()
    local minimumLength = undefined

    try
    (
        with undo off with redraw off
        (
            workingNode = copy node
            workingNode.name = uniqueName "__DEADLIMIT_INNER_LINEART_WORK__"

            for modifierIndex = 1 to (sourceModifierIndex - 1) do deleteModifier workingNode 1
            collapseStack workingNode
            if (classOf workingNode.baseObject) != Editable_Poly do convertToPoly workingNode
            workingPoly = workingNode.baseObject

            if polyop.getNumVerts workingPoly != oldVertCount or polyop.getNumFaces workingPoly != oldFaceCount or polyop.getNumEdges workingPoly != oldEdgeCount then
                throw "The isolated active Edit Poly result changed topology while it was prepared. INNER LINEART stopped before touching the original stack."

            for edgeIndex in selectedEdges do
            (
                local edgeFaces = polyop.getEdgeFaces workingPoly edgeIndex
                if edgeFaces == undefined or edgeFaces.count != 2 then
                    append invalidEdges edgeIndex
                else
                (
                    local edgeVerts = polyop.getEdgeVerts workingPoly edgeIndex
                    if edgeVerts == undefined then
                        append invalidEdges edgeIndex
                    else
                    (
                        local a = polyop.getVert workingPoly edgeVerts[1]
                        local b = polyop.getVert workingPoly edgeVerts[2]
                        local edgeLength = distance a b
                        if edgeLength <= 1e-8 then
                            append invalidEdges edgeIndex
                        else
                        (
                            local lineartTolerance = edgeLength * edgeLength * 1e-8
                            if lineartTolerance < 1e-12 do lineartTolerance = 1e-12
                            for faceIndex in edgeFaces while (findItem existingLineartEdges edgeIndex) == 0 do
                            (
                                local adjacentArea = deadlimitVertexColorPolyFaceArea workingPoly faceIndex
                                if adjacentArea != undefined and adjacentArea <= lineartTolerance do append existingLineartEdges edgeIndex
                            )
                            append sourceSegments #(a, b)
                            if minimumLength == undefined or edgeLength < minimumLength do minimumLength = edgeLength
                        )
                    )
                )
            )

            if invalidEdges.count > 0 then
                throw ("Selected edges must be non-zero inner edges shared by exactly two faces. Invalid edge: " + (invalidEdges[1] as string) + ".")
            if existingLineartEdges.count > 0 then
                throw ("Selected edge already borders a zero-width INNER LINEART face: " + (existingLineartEdges[1] as string) + ".")

            operationResult = deadlimitVertexColorBuildInnerLineart workingPoly selectedEdges sourceSegments minimumLength oldVertCount oldFaceCount vertexMarker:vertexMarker
            for faceIndex = 1 to oldFaceCount do polyop.setFaceSmoothGroup workingPoly faceIndex sourceSmoothingGroups[faceIndex]
            preparedSmoothingGroups = deadlimitVertexColorReadFaceSmoothingGroups workingPoly
            update workingNode
        )
    )
    catch
    (
        with undo off try(if isValidNode workingNode do delete workingNode)catch()
        throw()
    )

    local appliedModifier = undefined
    local applyError = undefined
    local originalMaterial = node.material


    if useUndo then
    (
        undo "Create Inner Lineart" on
        (
            try
            (
                appliedModifier = deadlimitVertexColorApplyInnerLineartModifierResult node sourceModifier workingNode oldFaceCount originalMaterial preparedSmoothingGroups
                deadlimitVertexColorRestoreInnerLineartSkins node skinSnapshots operationResult[4] operationResult[3]
                deadlimitInnerLineartWriteGroups node (deadlimitInnerLineartMergeGroups existingSeamGroups operationResult[4])
                modPanel.setCurrentObject sourceModifier node:node ui:true
            )
            catch
            (
                applyError = getCurrentException()
            )
        )
    )
    else
    (
        with undo off
        (
            try
            (
                appliedModifier = deadlimitVertexColorApplyInnerLineartModifierResult node sourceModifier workingNode oldFaceCount originalMaterial preparedSmoothingGroups
                deadlimitVertexColorRestoreInnerLineartSkins node skinSnapshots operationResult[4] operationResult[3]
                deadlimitInnerLineartWriteGroups node (deadlimitInnerLineartMergeGroups existingSeamGroups operationResult[4])
                modPanel.setCurrentObject sourceModifier node:node ui:true
            )
            catch
            (
                applyError = getCurrentException()
            )
        )
    )

    if applyError != undefined then
    (
        if useUndo do try(max undo)catch()
        with undo off try(if isValidNode workingNode do delete workingNode)catch()
        throw applyError
    )

    redrawViews()
    #(sourceEdgeCount, operationResult[1].numberSet, operationResult[2].numberSet, operationResult[3] - oldVertCount)
)

fn deadlimitVertexColorCreateInnerLineartFromMesh useUndo:true vertexMarker:false =
(
    if selection.count != 1 then throw "Select one Editable Mesh object and its inner edges."

    local node = selection[1]
    if not isValidNode node or (classOf node.baseObject) != Editable_Mesh then
        throw "INNER LINEART Editable Mesh conversion requires one Editable Mesh base object."
    if node.modifiers.count > 0 then
        throw "Set an Edit Poly modifier active, or collapse/convert the stack manually before using INNER LINEART on an Editable Mesh base."
    if subObjectLevel != 2 then throw "Enter Edge sub-object mode and select the inner edges."

    local selectedEdges = getEdgeSelection node
    if selectedEdges.numberSet == 0 then throw "Select at least one inner edge."
    local operationResult = undefined

    if useUndo then
    (
        undo "Convert Mesh and Create Inner Lineart" on
        (
            convertToPoly node
            update node
            subObjectLevel = 2
            operationResult = deadlimitVertexColorCreateInnerLineartFromBase useUndo:false vertexMarker:vertexMarker
        )
    )
    else
    (
        with undo off
        (
            convertToPoly node
            update node
            subObjectLevel = 2
            operationResult = deadlimitVertexColorCreateInnerLineartFromBase useUndo:false vertexMarker:vertexMarker
        )
    )
    operationResult
)

fn deadlimitVertexColorCreateInnerLineart useUndo:true vertexMarker:false =
(
    if selection.count != 1 then throw "Select one object and its inner edges."

    local node = selection[1]
    if not isValidNode node then throw "Select one valid geometry object."
    local currentStackObject = try(modPanel.getCurrentObject())catch(undefined)

    if currentStackObject != undefined and (classOf currentStackObject) == Edit_Poly and (modPanel.getModifierIndex node currentStackObject) != undefined then
        deadlimitVertexColorCreateInnerLineartFromModifier node currentStackObject useUndo:useUndo vertexMarker:vertexMarker
    else if (classOf node.baseObject) == Editable_Poly and (node.modifiers.count == 0 or currentStackObject == node.baseObject or currentStackObject == node) then
        deadlimitVertexColorCreateInnerLineartFromBase useUndo:useUndo vertexMarker:vertexMarker
    else if (classOf node.baseObject) == Editable_Poly then
        throw "Make the base Editable Poly or the intended Edit Poly modifier active before creating INNER LINEART."
    else if (classOf node.baseObject) == Editable_Mesh then
        deadlimitVertexColorCreateInnerLineartFromMesh useUndo:useUndo vertexMarker:vertexMarker
    else
        throw "INNER LINEART requires an Editable Poly/Editable Mesh base object or an active Edit Poly modifier."
)

/*
    FEATURE: DETACHED INNER LINEART STRIP

    This is the current artist-facing Inner Lineart path. The selected source edges are read
    from a base Editable Poly or the active Edit Poly modifier, then a separate strip object is
    generated. Source topology, source Skin vertex IDs, modifier instances, sub-object data,
    UVs, and vertex paint remain untouched.

    Geometry contract:
    - Width is a full world-space millimeter width centered on every selected edge.
    - Every selected edge contributes a strip face using the two adjacent source face planes.
    - Degree-1 endpoints optionally collapse to one geometry vertex.
    - Weighted graph distance to the nearest endpoint controls taper. The normalized factor is
      1-(1-t)^EXP: EXP 1 is linear/roof-shaped, EXP 2 is rounded/bridge-shaped. Closed networks
      have no endpoints and remain at full width.
    - Incident faces are partitioned into sectors between selected edges. Each sector owns one
      stitched miter vertex. Degree 2 shares one cross-section edge with no cap face; degree 3
      adds one triangle; degree 4 adds one quad; degree 5 adds one quad plus one triangle. Higher
      valences continue with quads and at most one triangle, without center fans or n-gons.
    - All generated faces use smoothing group 1. Face-corner map data is copied from the source
      face/vertex reference, preserving UV seams, Vertex Color, Alpha, and Illumination.

    Rigging contract:
    - Each generated vertex records one authoritative source vertex ID.
    - Copy Skin is artist-controlled and OFF by default. When enabled, every Skin above the
      active authoring level is copied to the new object. Its complete bind state and bone order
      come from the copied modifier; every generated weight/DQ/rigid state is then replaced
      from the recorded source vertex.
    - Copy Skin only reads the source. Source weights and topology remain unchanged.

    Rendering contract:
    - Geometry, junctions, and every map face are completed in the source orientation first.
    - Optional polygon flip then reverses the complete generated face set in one polyop pass.
    - Optional normal inversion runs after the flip: it adds Edit Normals, makes every normal
      explicit, negates it, then collapses that modifier into the Editable Poly base. Copy Skin,
      when requested, is added only after this bake and therefore remains live.
    - The detached node copies both node.transform and objectOffsetPos/Rot/Scale. Generated local
      positions are authored against source.objectTransform, so arbitrary position, rotation,
      non-uniform scale, parent transform, and object-offset transform stay spatially aligned.
      With both defaults enabled, face winding points inward while explicit normals point back
      toward the original surface, matching the Deadlock expanded-backface outline.

    Blender port: build an independent BMesh strip from the selected-edge graph, copy corner
    custom-data layers, create vertex groups from source IDs, reverse polygons, and write custom
    split normals. Keep source mesh datablocks and Armature weights immutable.
 */
fn deadlimitDetachedInnerLineartUniqueName sourceName =
(
    local prefix = "lineart_" + sourceName + "_"
    local suffixIndex = 1
    while suffixIndex < 10000 do
    (
        local suffix = if suffixIndex < 10 then "0" + (suffixIndex as string) else suffixIndex as string
        local candidate = prefix + suffix
        if getNodeByName candidate exact:true == undefined do return candidate
        suffixIndex += 1
    )
    uniqueName prefix
)

fn deadlimitDetachedInnerLineartReverseArray values =
(
    for valueIndex = values.count to 1 by -1 collect values[valueIndex]
)

fn deadlimitDetachedInnerLineartAddOutputVertex outputPositions outputSourceVertices position sourceVertex =
(
    append outputPositions position
    append outputSourceVertices sourceVertex
    outputPositions.count
)

fn deadlimitDetachedInnerLineartAddBoundaryVertex outputPositions outputSourceVertices sourcePositions sourceNode adjacency taperEnds endpointVertexIds junctionItems halfWidth sourceVertex sourceFace direction factor =
(
    local vertexId = 0
    local desiredPosition = sourcePositions[sourceVertex]
    if taperEnds and adjacency[sourceVertex].count == 1 and factor <= 1e-8 then
    (
        vertexId = endpointVertexIds[sourceVertex]
    )
    else
    (
        local localScale = distance (sourcePositions[sourceVertex] * sourceNode.objectTransform) ((sourcePositions[sourceVertex] + direction) * sourceNode.objectTransform)
        if localScale <= 1e-8 do localScale = 1.0
        desiredPosition = sourcePositions[sourceVertex] + direction * (halfWidth * factor / localScale)
    )

    -- Consecutive collinear edges resolve to the same boundary point. Reuse that geometry
    -- vertex so straight strips are manifold without a zero-area junction cap.
    local mergeTolerance = amax 1e-7 (halfWidth * 1e-5)
    if vertexId == 0 do for item in junctionItems[sourceVertex] while vertexId == 0 do
        if distance outputPositions[item[1]] desiredPosition <= mergeTolerance do vertexId = item[1]
    if vertexId == 0 do vertexId = deadlimitDetachedInnerLineartAddOutputVertex outputPositions outputSourceVertices desiredPosition sourceVertex
    if taperEnds and adjacency[sourceVertex].count == 1 and factor <= 1e-8 do endpointVertexIds[sourceVertex] = vertexId

    local alreadyRecorded = false
    for item in junctionItems[sourceVertex] while not alreadyRecorded do alreadyRecorded = item[1] == vertexId
    if not alreadyRecorded do append junctionItems[sourceVertex] #(vertexId, sourceFace)
    vertexId
)

fn deadlimitDetachedInnerLineartAddFaceRecord outputPositions faceRecords rawVertexIds rawSourceRefs desiredNormal materialId flipGeometry =
(
    local vertexIds = #()
    local sourceRefs = #()
    for cornerIndex = 1 to rawVertexIds.count do
        if vertexIds.count == 0 or rawVertexIds[cornerIndex] != vertexIds[vertexIds.count] do
        (
            append vertexIds rawVertexIds[cornerIndex]
            append sourceRefs rawSourceRefs[cornerIndex]
        )
    if vertexIds.count > 2 and vertexIds[1] == vertexIds[vertexIds.count] do
    (
        deleteItem vertexIds vertexIds.count
        deleteItem sourceRefs sourceRefs.count
    )
    if vertexIds.count < 3 then return false
    local center = [0,0,0]
    for vertexId in vertexIds do center += outputPositions[vertexId]
    center /= vertexIds.count
    local polygonNormal = [0,0,0]
    for cornerIndex = 1 to vertexIds.count do
    (
        local nextIndex = if cornerIndex == vertexIds.count then 1 else cornerIndex + 1
        polygonNormal += cross (outputPositions[vertexIds[cornerIndex]] - center) (outputPositions[vertexIds[nextIndex]] - center)
    )
    if length polygonNormal <= 1e-10 then return false
    if dot polygonNormal desiredNormal < 0.0 do
    (
        vertexIds = deadlimitDetachedInnerLineartReverseArray vertexIds
        sourceRefs = deadlimitDetachedInnerLineartReverseArray sourceRefs
    )
    if flipGeometry do
    (
        vertexIds = deadlimitDetachedInnerLineartReverseArray vertexIds
        sourceRefs = deadlimitDetachedInnerLineartReverseArray sourceRefs
    )
    append faceRecords #(vertexIds, sourceRefs, materialId)
    true
)

fn deadlimitDetachedInnerLineartBuild sourceNode sourcePoly selectedEdges widthWorld flipGeometry:true taperEnds:true taperExponent:2.0 =
(
    if selectedEdges.numberSet < 2 then throw "Select at least two connected inner edges."
    if widthWorld <= 0.0 then throw "Lineart width must be greater than zero."
    if taperExponent <= 0.0 then throw "Taper EXP must be greater than zero."

    local sourceVertCount = polyop.getNumVerts sourcePoly
    local sourceFaceCount = polyop.getNumFaces sourcePoly
    local sourceEdgeCount = polyop.getNumEdges sourcePoly
    local sourcePositions = for vertexIndex = 1 to sourceVertCount collect (polyop.getVert sourcePoly vertexIndex)
    local sourceFaceVerts = #()
    local sourceFaceNormals = #()
    local sourceFaceMaterials = #()
    for faceIndex = 1 to sourceFaceCount do
    (
        append sourceFaceVerts (polyop.getFaceVerts sourcePoly faceIndex)
        append sourceFaceNormals (polyop.getFaceNormal sourcePoly faceIndex)
        append sourceFaceMaterials (polyop.getFaceMatID sourcePoly faceIndex)
    )

    local sourceMapChannels = #(-2, -1)
    local sourceNumMaps = polyop.getNumMaps sourcePoly
    if sourceNumMaps > 0 do for mapChannel = 0 to (sourceNumMaps - 1) do append sourceMapChannels mapChannel
    local sourceMapRecords = #()
    for mapChannel in sourceMapChannels where polyop.getMapSupport sourcePoly mapChannel do
    (
        local mapVertCount = polyop.getNumMapVerts sourcePoly mapChannel
        local mapFaceCount = polyop.getNumMapFaces sourcePoly mapChannel
        if mapVertCount <= 0 or mapFaceCount < sourceFaceCount then throw ("Map channel " + (mapChannel as string) + " is incomplete.")
        local mapVerts = for mapVertIndex = 1 to mapVertCount collect (polyop.getMapVert sourcePoly mapChannel mapVertIndex)
        local mapFaces = #()
        for faceIndex = 1 to sourceFaceCount do
        (
            local mapFace = polyop.getMapFace sourcePoly mapChannel faceIndex
            if mapFace.count != sourceFaceVerts[faceIndex].count then throw ("Map channel " + (mapChannel as string) + " has a face-degree mismatch.")
            append mapFaces mapFace
        )
        append sourceMapRecords #(mapChannel, mapVerts, mapFaces)
    )

    local adjacency = for vertexIndex = 1 to sourceVertCount collect #()
    local selectedVerts = #{}
    local edgeRecords = #()
    for edgeIndex in selectedEdges do
    (
        if edgeIndex < 1 or edgeIndex > sourceEdgeCount then throw ("Selected edge is outside the active topology: " + (edgeIndex as string) + ".")
        local edgeVerts = polyop.getEdgeVerts sourcePoly edgeIndex
        local edgeFaces = polyop.getEdgeFaces sourcePoly edgeIndex
        if edgeVerts == undefined or edgeFaces == undefined or edgeFaces.count != 2 then throw ("Selected edge must be shared by exactly two faces: " + (edgeIndex as string) + ".")
        local edgeLength = distance sourcePositions[edgeVerts[1]] sourcePositions[edgeVerts[2]]
        if edgeLength <= 1e-8 then throw ("Selected edge has zero length: " + (edgeIndex as string) + ".")
        append adjacency[edgeVerts[1]] #(edgeVerts[2], edgeLength)
        append adjacency[edgeVerts[2]] #(edgeVerts[1], edgeLength)
        selectedVerts[edgeVerts[1]] = true
        selectedVerts[edgeVerts[2]] = true
        append edgeRecords #(edgeIndex, edgeVerts[1], edgeVerts[2], edgeFaces[1], edgeFaces[2])
    )

    local componentIds = for vertexIndex = 1 to sourceVertCount collect 0
    local componentCount = 0
    for startVertex in selectedVerts where componentIds[startVertex] == 0 do
    (
        componentCount += 1
        local queue = #(startVertex)
        componentIds[startVertex] = componentCount
        local componentDegreeSum = 0
        while queue.count > 0 do
        (
            local currentVertex = queue[1]
            deleteItem queue 1
            componentDegreeSum += adjacency[currentVertex].count
            for neighborRecord in adjacency[currentVertex] do
            (
                local neighborVertex = neighborRecord[1]
                if componentIds[neighborVertex] == 0 do
                (
                    componentIds[neighborVertex] = componentCount
                    append queue neighborVertex
                )
            )
        )
        if componentDegreeSum < 4 then throw "Every selected edge component must contain at least two connected edges."
    )

    local infiniteDistance = 1e30
    local endpointDistances = for vertexIndex = 1 to sourceVertCount collect infiniteDistance
    local visited = #{}
    for vertexIndex in selectedVerts where adjacency[vertexIndex].count == 1 do endpointDistances[vertexIndex] = 0.0
    for iteration = 1 to selectedVerts.numberSet do
    (
        local currentVertex = 0
        local currentDistance = infiniteDistance
        for vertexIndex in selectedVerts where not visited[vertexIndex] and endpointDistances[vertexIndex] < currentDistance do
        (
            currentVertex = vertexIndex
            currentDistance = endpointDistances[vertexIndex]
        )
        if currentVertex > 0 do
        (
            visited[currentVertex] = true
            for neighborRecord in adjacency[currentVertex] do
            (
                local candidateDistance = currentDistance + neighborRecord[2]
                if candidateDistance < endpointDistances[neighborRecord[1]] do endpointDistances[neighborRecord[1]] = candidateDistance
            )
        )
    )
    local componentMaxDistances = for componentIndex = 1 to componentCount collect 0.0
    for vertexIndex in selectedVerts where endpointDistances[vertexIndex] < infiniteDistance do
        componentMaxDistances[componentIds[vertexIndex]] = amax componentMaxDistances[componentIds[vertexIndex]] endpointDistances[vertexIndex]
    local taperFactors = for vertexIndex = 1 to sourceVertCount collect 1.0
    if taperEnds do for vertexIndex in selectedVerts do
    (
        local maxDistance = componentMaxDistances[componentIds[vertexIndex]]
        if endpointDistances[vertexIndex] < infiniteDistance and maxDistance > 1e-8 then
        (
            local normalizedDistance = amax 0.0 (amin 1.0 (endpointDistances[vertexIndex] / maxDistance))
            taperFactors[vertexIndex] = 1.0 - (pow (1.0 - normalizedDistance) taperExponent)
        )
    )

    local outputPositions = #()
    local outputSourceVertices = #()
    local faceRecords = #()
    local junctionItems = for vertexIndex = 1 to sourceVertCount collect #()
    local vertexFaceOutputs = for vertexIndex = 1 to sourceVertCount collect #()
    local boundaryOccurrences = for vertexIndex = 1 to sourceVertCount collect #()
    local halfWidth = widthWorld * 0.5

    -- First resolve the two inset directions contributed by every selected edge. Junction
    -- topology is built later from face sectors, so a valence-N source vertex produces exactly
    -- N center vertices regardless of triangulation around it.
    for edgeRecord in edgeRecords do
    (
        local sourceA = edgeRecord[2]
        local sourceB = edgeRecord[3]
        local faceA = edgeRecord[4]
        local faceB = edgeRecord[5]
        local positionA = sourcePositions[sourceA]
        local positionB = sourcePositions[sourceB]
        local tangent = normalize (positionB - positionA)
        local midpoint = (positionA + positionB) * 0.5

        local faceDirections = #()
        for faceIndex in #(faceA, faceB) do
        (
            local faceCenter = [0,0,0]
            for faceVertex in sourceFaceVerts[faceIndex] do faceCenter += sourcePositions[faceVertex]
            faceCenter /= sourceFaceVerts[faceIndex].count
            local sideDirection = cross sourceFaceNormals[faceIndex] tangent
            if length sideDirection <= 1e-8 then throw ("Cannot resolve strip direction on edge " + (edgeRecord[1] as string) + ".")
            sideDirection = normalize sideDirection
            if dot sideDirection (faceCenter - midpoint) < 0.0 do sideDirection = -sideDirection
            append faceDirections sideDirection
        )

        local desiredNormal = sourceFaceNormals[faceA] + sourceFaceNormals[faceB]
        if length desiredNormal <= 1e-8 then desiredNormal = sourceFaceNormals[faceA] else desiredNormal = normalize desiredNormal
        append edgeRecord faceDirections[1]
        append edgeRecord faceDirections[2]
        append edgeRecord desiredNormal
        append boundaryOccurrences[sourceA] #(faceA, faceDirections[1])
        append boundaryOccurrences[sourceA] #(faceB, faceDirections[2])
        append boundaryOccurrences[sourceB] #(faceA, faceDirections[1])
        append boundaryOccurrences[sourceB] #(faceB, faceDirections[2])
    )

    local sourceObjectTM = sourceNode.objectTransform
    local inverseSourceObjectTM = inverse sourceObjectTM
    for sourceVertex in selectedVerts do
    (
        local occurrences = boundaryOccurrences[sourceVertex]
        local selectedDegree = adjacency[sourceVertex].count
        local factor = taperFactors[sourceVertex]
        local centerLocal = sourcePositions[sourceVertex]
        local centerWorld = centerLocal * sourceObjectTM

        if selectedDegree == 1 then
        (
            if taperEnds and factor <= 1e-8 then
            (
                local endpointId = deadlimitDetachedInnerLineartAddOutputVertex outputPositions outputSourceVertices centerLocal sourceVertex
                for occurrence in occurrences do append vertexFaceOutputs[sourceVertex] #(occurrence[1], endpointId)
                append junctionItems[sourceVertex] #(endpointId, occurrences[1][1])
            )
            else
            (
                for occurrence in occurrences do
                (
                    local worldDirection = ((centerLocal + occurrence[2]) * sourceObjectTM) - centerWorld
                    if length worldDirection <= 1e-8 then throw "Cannot transform a lineart boundary direction."
                    worldDirection = normalize worldDirection
                    local desiredLocal = (centerWorld + worldDirection * (halfWidth * factor)) * inverseSourceObjectTM
                    local outputId = deadlimitDetachedInnerLineartAddOutputVertex outputPositions outputSourceVertices desiredLocal sourceVertex
                    append vertexFaceOutputs[sourceVertex] #(occurrence[1], outputId)
                    append junctionItems[sourceVertex] #(outputId, occurrence[1])
                )
            )
        )
        else
        (
            -- Removing selected edges from the incident face fan yields one sector between each
            -- neighboring selected edge. Each sector becomes one miter vertex. This is the key
            -- invariant: degree 2 -> 2 vertices/one shared edge, degree 3 -> triangle, degree 4
            -- -> quad, degree 5 -> quad plus triangle, with no center fan or loose duplicates.
            local incidentFaces = polyop.getFacesUsingVert sourcePoly #{sourceVertex}
            local faceSectorIds = for faceIndex = 1 to sourceFaceCount collect 0
            local sectorFaces = #()
            for startFace in incidentFaces where faceSectorIds[startFace] == 0 do
            (
                append sectorFaces #()
                local sectorId = sectorFaces.count
                local faceQueue = #(startFace)
                faceSectorIds[startFace] = sectorId
                while faceQueue.count > 0 do
                (
                    local currentFace = faceQueue[1]
                    deleteItem faceQueue 1
                    append sectorFaces[sectorId] currentFace
                    local currentFaceEdges = polyop.getFaceEdges sourcePoly currentFace
                    for connectingEdge in currentFaceEdges where not selectedEdges[connectingEdge] do
                    (
                        local connectingVerts = polyop.getEdgeVerts sourcePoly connectingEdge
                        if connectingVerts[1] == sourceVertex or connectingVerts[2] == sourceVertex do
                        (
                            local neighborFaces = polyop.getEdgeFaces sourcePoly connectingEdge
                            for neighborFace in neighborFaces where incidentFaces[neighborFace] and faceSectorIds[neighborFace] == 0 do
                            (
                                faceSectorIds[neighborFace] = sectorId
                                append faceQueue neighborFace
                            )
                        )
                    )
                )
            )
            if sectorFaces.count != selectedDegree then
                throw ("Selected-edge junction at source vertex " + (sourceVertex as string) + " has " + (selectedDegree as string) + " edges but " + (sectorFaces.count as string) + " face sectors. Check for non-manifold topology.")

            for sectorId = 1 to sectorFaces.count do
            (
                local sectorOccurrences = #()
                for occurrence in occurrences where faceSectorIds[occurrence[1]] == sectorId do append sectorOccurrences occurrence
                if sectorOccurrences.count != 2 then
                    throw ("Lineart sector at source vertex " + (sourceVertex as string) + " is bounded by " + (sectorOccurrences.count as string) + " selected-edge sides; expected 2.")

                local worldDirectionA = ((centerLocal + sectorOccurrences[1][2]) * sourceObjectTM) - centerWorld
                local worldDirectionB = ((centerLocal + sectorOccurrences[2][2]) * sourceObjectTM) - centerWorld
                if length worldDirectionA <= 1e-8 or length worldDirectionB <= 1e-8 then throw "Cannot transform a lineart sector direction."
                worldDirectionA = normalize worldDirectionA
                worldDirectionB = normalize worldDirectionB
                local directionDot = dot worldDirectionA worldDirectionB
                local denominator = 1.0 + directionDot
                local worldOffset = [0,0,0]
                if denominator > 1e-5 then
                    worldOffset = (worldDirectionA + worldDirectionB) * (halfWidth * factor / denominator)
                else
                (
                    local fallbackDirection = worldDirectionA + worldDirectionB
                    if length fallbackDirection <= 1e-8 then fallbackDirection = worldDirectionA else fallbackDirection = normalize fallbackDirection
                    worldOffset = fallbackDirection * (halfWidth * factor)
                )
                local desiredLocal = (centerWorld + worldOffset) * inverseSourceObjectTM
                local outputId = deadlimitDetachedInnerLineartAddOutputVertex outputPositions outputSourceVertices desiredLocal sourceVertex
                local representativeFace = sectorOccurrences[1][1]
                append junctionItems[sourceVertex] #(outputId, representativeFace)
                for occurrence in sectorOccurrences do append vertexFaceOutputs[sourceVertex] #(occurrence[1], outputId)
            )
        )
    )

    -- Build the strips after all sector vertices exist. Consecutive edges therefore reuse the
    -- exact same cross-section edge instead of leaving near-coincident, unstitched geometry.
    for edgeRecord in edgeRecords do
    (
        local sourceA = edgeRecord[2]
        local sourceB = edgeRecord[3]
        local faceA = edgeRecord[4]
        local faceB = edgeRecord[5]
        local aLeft = 0
        local bLeft = 0
        local aRight = 0
        local bRight = 0
        for faceOutput in vertexFaceOutputs[sourceA] do
        (
            if faceOutput[1] == faceA do aLeft = faceOutput[2]
            if faceOutput[1] == faceB do aRight = faceOutput[2]
        )
        for faceOutput in vertexFaceOutputs[sourceB] do
        (
            if faceOutput[1] == faceA do bLeft = faceOutput[2]
            if faceOutput[1] == faceB do bRight = faceOutput[2]
        )
        if amin #(aLeft,bLeft,aRight,bRight) <= 0 then throw "Could not resolve a stitched lineart sector vertex."
        deadlimitDetachedInnerLineartAddFaceRecord outputPositions faceRecords #(aLeft,bLeft,bRight,aRight) #(#(faceA,sourceA),#(faceA,sourceB),#(faceB,sourceB),#(faceB,sourceA)) edgeRecord[8] sourceFaceMaterials[faceA] flipGeometry
    )

    for sourceVertex in selectedVerts where junctionItems[sourceVertex].count >= 3 do
    (
        local items = deepCopy junctionItems[sourceVertex]
        local center = sourcePositions[sourceVertex]
        local junctionNormal = [0,0,0]
        for item in items do junctionNormal += sourceFaceNormals[item[2]]
        if length junctionNormal <= 1e-8 then junctionNormal = [0,0,1] else junctionNormal = normalize junctionNormal
        local basisX = outputPositions[items[1][1]] - center
        basisX -= junctionNormal * (dot basisX junctionNormal)
        if length basisX <= 1e-8 then basisX = if abs junctionNormal.z < 0.9 then cross junctionNormal [0,0,1] else cross junctionNormal [0,1,0]
        basisX = normalize basisX
        local basisY = normalize (cross junctionNormal basisX)
        local angledItems = for item in items collect #(item, atan2 (dot (outputPositions[item[1]] - center) basisY) (dot (outputPositions[item[1]] - center) basisX))
        for outerIndex = 1 to (angledItems.count - 1) do for innerIndex = outerIndex + 1 to angledItems.count do
            if angledItems[innerIndex][2] < angledItems[outerIndex][2] do
            (
                local swapItem = angledItems[outerIndex]
                angledItems[outerIndex] = angledItems[innerIndex]
                angledItems[innerIndex] = swapItem
            )
        items = for angledItem in angledItems collect angledItem[1]

        local ringVertexIds = for item in items collect item[1]
        local ringSourceRefs = for item in items collect #(item[2], sourceVertex)
        local cursor = 2
        while (ringVertexIds.count - cursor + 2) > 4 do
        (
            deadlimitDetachedInnerLineartAddFaceRecord outputPositions faceRecords #(ringVertexIds[1],ringVertexIds[cursor],ringVertexIds[cursor+1],ringVertexIds[cursor+2]) #(ringSourceRefs[1],ringSourceRefs[cursor],ringSourceRefs[cursor+1],ringSourceRefs[cursor+2]) junctionNormal sourceFaceMaterials[ringSourceRefs[1][1]] flipGeometry
            cursor += 2
        )
        local remainingCount = ringVertexIds.count - cursor + 2
        if remainingCount == 3 then
            deadlimitDetachedInnerLineartAddFaceRecord outputPositions faceRecords #(ringVertexIds[1],ringVertexIds[cursor],ringVertexIds[cursor+1]) #(ringSourceRefs[1],ringSourceRefs[cursor],ringSourceRefs[cursor+1]) junctionNormal sourceFaceMaterials[ringSourceRefs[1][1]] flipGeometry
        else if remainingCount == 4 then
            deadlimitDetachedInnerLineartAddFaceRecord outputPositions faceRecords #(ringVertexIds[1],ringVertexIds[cursor],ringVertexIds[cursor+1],ringVertexIds[cursor+2]) #(ringSourceRefs[1],ringSourceRefs[cursor],ringSourceRefs[cursor+1],ringSourceRefs[cursor+2]) junctionNormal sourceFaceMaterials[ringSourceRefs[1][1]] flipGeometry
    )

    if faceRecords.count == 0 then throw "The selected network did not produce any valid lineart faces."
    #(outputPositions, outputSourceVertices, faceRecords, sourceMapRecords, sourceFaceVerts, selectedEdges.numberSet)
)

fn deadlimitDetachedInnerLineartApplySkins lineartNode skinSnapshots sourceVertexIds =
(
    local copiedSkins = #()
    for snapshotIndex = skinSnapshots.count to 1 by -1 do
    (
        local sourceSkin = skinSnapshots[snapshotIndex][1]
        local sourceVertexData = skinSnapshots[snapshotIndex][2]
        local copiedSkin = copy sourceSkin
        addModifier lineartNode copiedSkin
        select lineartNode
        max modify mode
        modPanel.setCurrentObject copiedSkin node:lineartNode ui:true
        update lineartNode
        local targetVertexCount = skinOps.GetNumberVertices copiedSkin
        if targetVertexCount != sourceVertexIds.count then throw ("Copied Skin '" + copiedSkin.name + "' sees " + (targetVertexCount as string) + " vertices; expected " + (sourceVertexIds.count as string) + ".")
        for targetVertex = 1 to sourceVertexIds.count do
        (
            local sourceVertex = sourceVertexIds[targetVertex]
            if sourceVertex < 1 or sourceVertex > sourceVertexData.count then throw "Generated lineart contains an invalid Skin source vertex."
            deadlimitInnerLineartApplySkinWeightData copiedSkin lineartNode targetVertex sourceVertexData[sourceVertex]
        )
        skinOps.Invalidate copiedSkin 0
        update lineartNode
        append copiedSkins copiedSkin
    )
    copiedSkins
)

fn deadlimitDetachedInnerLineartApplyExplicitNormals lineartNode =
(
    -- Edit Normals is a temporary authoring tool. It is collapsed before optional Skin is added,
    -- leaving a clean Editable Poly base with baked explicit normals and no Edit Normals entry in
    -- the artist-facing stack.
    local normalModifier = Edit_Normals()
    addModifier lineartNode normalModifier
    select lineartNode
    max modify mode
    modPanel.setCurrentObject normalModifier node:lineartNode ui:true
    update lineartNode
    local normalCount = normalModifier.GetNumNormals node:lineartNode
    if normalCount <= 0 then throw "Edit Normals did not expose generated lineart normals."
    local normalSelection = #{1..normalCount}
    normalModifier.MakeExplicit selection:normalSelection node:lineartNode
    for normalIndex = 1 to normalCount do normalModifier.SetNormal normalIndex (-(normalModifier.GetNormal normalIndex node:lineartNode)) node:lineartNode
    update lineartNode
    collapseStack lineartNode
    if (classOf lineartNode.baseObject) != Editable_Poly do convertToPoly lineartNode
    if lineartNode.modifiers.count != 0 then throw "Edit Normals could not be collapsed into the generated Editable Poly."
    update lineartNode
    lineartNode.baseObject
)

fn deadlimitVertexColorCreateDetachedInnerLineart widthMm:1.0 customLayer:false layerName:"lineart" flipGeometry:true invertNormals:true taperEnds:true taperExponent:2.0 copySkin:false =
(
    if selection.count != 1 then throw "Select one source object and at least two inner edges."
    if widthMm <= 0.0 then throw "Lineart width in millimeters must be greater than zero."
    if taperExponent <= 0.0 then throw "Taper EXP must be greater than zero."
    if customLayer and (trimRight (trimLeft layerName)).count == 0 then throw "Custom layer name cannot be empty."

    local sourceNode = selection[1]
    if not isValidNode sourceNode or (superClassOf sourceNode) != GeometryClass then throw "Select one valid geometry object."
    if subObjectLevel != 2 then throw "Enter Edge sub-object mode and select at least two inner edges."
    local currentStackObject = try(modPanel.getCurrentObject())catch(undefined)
    local sourceModifier = undefined
    local selectedEdges = #{}
    local sourceVertCount = 0
    local workingNode = undefined
    local workingPoly = undefined

    try
    (
        with undo off with redraw off
        (
            if currentStackObject != undefined and (classOf currentStackObject) == Edit_Poly and (modPanel.getModifierIndex sourceNode currentStackObject) != undefined then
            (
                sourceModifier = currentStackObject
                selectedEdges = copy (sourceModifier.GetSelection #Edge node:sourceNode)
                sourceVertCount = sourceModifier.GetNumVertices node:sourceNode
                local sourceFaceCount = sourceModifier.GetNumFaces node:sourceNode
                local sourceEdgeCount = sourceModifier.GetNumEdges node:sourceNode
                local sourceModifierIndex = modPanel.getModifierIndex sourceNode sourceModifier
                workingNode = copy sourceNode
                for modifierIndex = 1 to (sourceModifierIndex - 1) do deleteModifier workingNode 1
                collapseStack workingNode
                if (classOf workingNode.baseObject) != Editable_Poly do convertToPoly workingNode
                workingPoly = workingNode.baseObject
                if polyop.getNumVerts workingPoly != sourceVertCount or polyop.getNumFaces workingPoly != sourceFaceCount or polyop.getNumEdges workingPoly != sourceEdgeCount then throw "The active Edit Poly result changed topology while it was isolated."
            )
            else if (classOf sourceNode.baseObject) == Editable_Poly and (sourceNode.modifiers.count == 0 or currentStackObject == sourceNode.baseObject or currentStackObject == sourceNode) then
            (
                selectedEdges = copy (polyop.getEdgeSelection sourceNode.baseObject)
                sourceVertCount = polyop.getNumVerts sourceNode.baseObject
                workingNode = copy sourceNode
                workingNode.name = uniqueName "__DEADLIMIT_DETACHED_LINEART_WORK__"
                while workingNode.modifiers.count > 0 do deleteModifier workingNode 1
                if (classOf workingNode.baseObject) != Editable_Poly do convertToPoly workingNode
                workingPoly = workingNode.baseObject
            )
            else
                throw "Make the base Editable Poly or intended Edit Poly modifier active before creating detached lineart."
        )

        if selectedEdges.numberSet < 2 then throw "Select at least two connected inner edges."
        local skinSnapshots = #()
        if copySkin do skinSnapshots = deadlimitVertexColorCaptureInnerLineartSkins sourceNode sourceModifier sourceVertCount
        local widthWorld = (units.decodeValue "1mm") * widthMm
        local buildResult = deadlimitDetachedInnerLineartBuild sourceNode workingPoly selectedEdges widthWorld flipGeometry:false taperEnds:taperEnds taperExponent:taperExponent
        with undo off try(if isValidNode workingNode do delete workingNode)catch()
        workingNode = undefined

        local lineartNode = undefined
        local copiedSkins = #()
        undo "Create Detached Inner Lineart" on
        (
            lineartNode = mesh name:(deadlimitDetachedInnerLineartUniqueName sourceNode.name) vertices:#([0,0,0],[1,0,0],[0,1,0]) faces:#([1,2,3])
            convertToPoly lineartNode
            local lineartPoly = lineartNode.baseObject
            polyop.deleteFaces lineartPoly #{1}
            local remainingDummyVertices = polyop.getNumVerts lineartPoly
            if remainingDummyVertices > 0 do polyop.deleteVerts lineartPoly #{1..remainingDummyVertices}

            -- Local strip positions were solved through source.objectTransform. Mirror every
            -- transform component on the detached node so object offsets are not lost when the
            -- source uses Reset XForm-style offsets, rotated pivots, or non-uniform object scale.
            lineartNode.transform = sourceNode.transform
            lineartNode.objectOffsetPos = sourceNode.objectOffsetPos
            lineartNode.objectOffsetRot = sourceNode.objectOffsetRot
            lineartNode.objectOffsetScale = sourceNode.objectOffsetScale
            lineartNode.material = sourceNode.material
            lineartNode.wireColor = sourceNode.wireColor
            for position in buildResult[1] do polyop.createVert lineartPoly position
            for faceRecord in buildResult[3] do
            (
                local faceIndex = polyop.createPolygon lineartPoly faceRecord[1]
                if faceIndex == undefined or faceIndex == 0 then throw "Could not create a detached lineart face."
                polyop.setFaceSmoothGroup lineartPoly faceIndex 1
                polyop.setFaceMatID lineartPoly faceIndex faceRecord[3]
            )

            local sourceFaceVerts = buildResult[5]
            if buildResult[4].count > 0 do
            (
                local positiveMapCount = 0
                for mapRecord in buildResult[4] where mapRecord[1] >= 0 do positiveMapCount = amax positiveMapCount (mapRecord[1] + 1)
                if positiveMapCount > 0 do polyop.setNumMaps lineartPoly positiveMapCount keep:false
                for mapRecord in buildResult[4] do
                (
                    local mapChannel = mapRecord[1]
                    local totalMapVertices = 0
                    for faceRecord in buildResult[3] do totalMapVertices += faceRecord[2].count
                    polyop.setMapSupport lineartPoly mapChannel true
                    polyop.setNumMapVerts lineartPoly mapChannel totalMapVertices keep:false
                    polyop.setNumMapFaces lineartPoly mapChannel buildResult[3].count keep:false
                    local outputMapVertex = 0
                    for faceIndex = 1 to buildResult[3].count do
                    (
                        local mapFace = #()
                        for sourceRef in buildResult[3][faceIndex][2] do
                        (
                            local sourceFace = sourceRef[1]
                            local sourceVertex = sourceRef[2]
                            local sourceCorner = findItem sourceFaceVerts[sourceFace] sourceVertex
                            if sourceCorner == 0 then throw "Detached lineart map source corner is invalid."
                            local sourceMapVertex = mapRecord[3][sourceFace][sourceCorner]
                            outputMapVertex += 1
                            polyop.setMapVert lineartPoly mapChannel outputMapVertex mapRecord[2][sourceMapVertex]
                            append mapFace outputMapVertex
                        )
                        polyop.setMapFace lineartPoly mapChannel faceIndex mapFace
                    )
                )
            )

            -- Post-process contract: all topology and map faces exist before winding changes.
            -- Editable Poly flips complete elements, so select the complete generated face set
            -- and call its native FlipNormals button operation once. This reverses geometry and
            -- every map face together; polyop.flipNormals is intentionally avoided because it is
            -- a no-op on this manifold Editable Poly result in 3ds Max 2025.
            if flipGeometry do
            (
                local generatedFaceCount = polyop.getNumFaces lineartPoly
                if generatedFaceCount > 0 do
                (
                    select lineartNode
                    max modify mode
                    modPanel.setCurrentObject lineartPoly node:lineartNode ui:true
                    subObjectLevel = 5
                    lineartPoly.SetSelection #Face #{1..generatedFaceCount}
                    lineartPoly.ButtonOp #FlipNormals
                    lineartPoly.SetSelection #Face #{}
                    subObjectLevel = 0
                )
            )
            update lineartNode

            local targetLayer = undefined
            if customLayer then
            (
                local cleanLayerName = trimRight (trimLeft layerName)
                targetLayer = LayerManager.getLayerFromName cleanLayerName
                if targetLayer == undefined do targetLayer = LayerManager.newLayerFromName cleanLayerName
            )
            else
                targetLayer = LayerManager.getLayerFromName (sourceNode.layer as string)
            if targetLayer != undefined do targetLayer.addNode lineartNode

            -- Bake custom normals while Edit Normals is the only modifier. Optional Skin is added
            -- afterwards and remains editable instead of being consumed by collapseStack.
            if invertNormals do deadlimitDetachedInnerLineartApplyExplicitNormals lineartNode
            if copySkin do copiedSkins = deadlimitDetachedInnerLineartApplySkins lineartNode skinSnapshots buildResult[2]
            update lineartNode
            select lineartNode
        )
        redrawViews()
        #(buildResult[6], buildResult[3].count, buildResult[1].count, lineartNode.name, copiedSkins.count)
    )
    catch
    (
        with undo off try(if isValidNode workingNode do delete workingNode)catch()
        throw()
    )
)

fn deadlimitVertexColorResultText actionName result =
(
    local text = actionName + ": " + (result[1] as string) + " object(s)."
    if result[2].count > 0 then
    (
        text += "  Skipped: " + (result[2].count as string) + "."
        for errorText in result[2] do format "%\n" errorText
    )
    text
)

/*
    UI / WORKFLOW CONTRACT

    Keep the interface compact and ordered as follows: Bone Tools, Vertex Color, Inner
    Lineart, Export Vertex Color. All rollouts open by default. The export controls stay at
    the bottom so topology/color preparation reads top-to-bottom.

    Status and modal messages are part of the artist workflow: batch operations report
    changed/skipped targets; Skin sync reports changed clones; export reports both the FBX
    sidecar and gamma state. A Blender port should preserve these confirmations and surface
    preflight errors without partially editing the asset.
*/
try (destroyDialog deadlimitVertexColorFbxRollout) catch()
try (closeRolloutFloater deadlimitPipelineFloater) catch()

rollout deadlimitVertexColorToolsRollout "VERTEX COLOR"
(
    label convertLabel "Convert:" width:44 height:18 across:4 align:#left
    button toVertexButton "Palette to Vertex" width:82 height:21 tooltip:"Fill channel 0 from each selected object's wire color."
    button toPaletteButton "Vertex to Palette" width:82 height:21 tooltip:"Copy the first used channel 0 color to each selected object's wire color."
    button toggleVertexColorButton "ON/OFF Vertex" width:82 height:21 tooltip:"Toggle shaded Vertex Color display for all selected meshes. A majority currently ON forces all OFF; otherwise all are forced ON."
    checkbox redistributeVertexCheckbox "VERT" checked:true width:55 across:4 tooltip:"Copy the reference mesh's first used Vertex Color to target meshes."
    checkbox redistributePaletteCheckbox "PALETTE" checked:true width:75 tooltip:"Copy the reference mesh's wire color to target meshes."
    checkbox redistributeMaterialCheckbox "MAT" checked:true width:55 tooltip:"Assign the reference mesh's material to target meshes."
    button redistributeColorsButton "SPREAD" width:90 height:21 tooltip:"Use the first selected mesh as reference and apply the enabled data to every other selected mesh."

    on redistributeColorsButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorRedistribute redistributeVertexCheckbox.checked redistributePaletteCheckbox.checked redistributeMaterialCheckbox.checked
            local statusText = "Redistribute from " + result[1] + ": " + (result[2] as string) + " target(s)."
            if result[3].count > 0 do statusText += " Skipped: " + (result[3].count as string) + "."
            for errorText in result[3] do format "%\n" errorText
            deadlimitVertexColorFbxRollout.statusLabel.text = statusText
        )
        catch (deadlimitVertexColorFbxRollout.statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on toVertexButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorFillPaletteToVertex()
            deadlimitVertexColorFbxRollout.statusLabel.text = deadlimitVertexColorResultText "PAL > VERT" result
        )
        catch (deadlimitVertexColorFbxRollout.statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on toPaletteButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorFillVertexToPalette()
            deadlimitVertexColorFbxRollout.statusLabel.text = deadlimitVertexColorResultText "VERT > PAL" result
        )
        catch (deadlimitVertexColorFbxRollout.statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on toggleVertexColorButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorToggleDisplay()
            local stateText = if result[2] then "ON" else "OFF"
            deadlimitVertexColorFbxRollout.statusLabel.text = "Vertex Color " + stateText + ": " + (result[1] as string) + " object(s)."
        )
        catch (deadlimitVertexColorFbxRollout.statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )
)

rollout deadlimitVertexColorBoneToolsRollout "BONE TOOLS"
(
    button fitBoneLengthsButton "Fit to Hierarchy" width:150 height:22 across:2 tooltip:"Fit selected BoneGeometry lengths to the average bone-child pivot distance. Selected leaf bones use half the parent-pivot distance."
    button flipBoneXButton "Flip X" width:94 height:22 tooltip:"Flip selected BoneGeometry display meshes along local X while preserving node transforms, pivots, hierarchy, and names."
    spinner boneLengthCmSpinner "Length (cm):" range:[0.0,100000.0,10.0] type:#float scale:0.1 fieldWidth:62 width:150 align:#left across:2 tooltip:"Visual length in centimeters."
    button setBoneLengthButton "SET" width:94 height:22 tooltip:"Set selected bones to this visual length while preserving their current X direction."

    on fitBoneLengthsButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorFitBoneLengths()
            deadlimitVertexColorFbxRollout.statusLabel.text = "Fit Selected: " + (result[1] as string) + " resized, " + (result[2] as string) + " skipped."
        )
        catch (deadlimitVertexColorFbxRollout.statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on setBoneLengthButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorSetSelectedBoneLength boneLengthCmSpinner.value
            deadlimitVertexColorFbxRollout.statusLabel.text = "Set Length: " + (result[1] as string) + " resized, " + (result[2] as string) + " skipped."
        )
        catch (deadlimitVertexColorFbxRollout.statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on flipBoneXButton pressed do
    (
        try (deadlimitVertexColorFbxRollout.statusLabel.text = "Flip Selected X: " + ((deadlimitVertexColorFlipSelectedBoneX()) as string) + " bone(s).")
        catch (deadlimitVertexColorFbxRollout.statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

)

rollout deadlimitVertexColorInnerLineartRollout "INNER LINEART"
(
    spinner lineartWidthMmSpinner "Width (mm):" range:[0.01,1000.0,1.0] type:#float scale:0.1 fieldWidth:58 width:160 across:2 tooltip:"Full world-space width of the generated strip in millimeters."
    checkbox copyLineartSkinCheckbox "Copy Skin" checked:false width:105 tooltip:"Copy Skin modifiers and source-vertex weights to the separate lineart object."
    checkbox flipLineartCheckbox "Flip Polygons" checked:true width:105 across:2 tooltip:"Reverse generated face winding."
    checkbox invertLineartNormalsCheckbox "Invert Normals" checked:true width:115 tooltip:"Reverse generated vertex normals and bake them into the Editable Poly base."
    checkbox taperLineartCheckbox "Taper Ends" checked:true width:105 across:2 tooltip:"Collapse degree-1 lineart endpoints to a single vertex."
    spinner taperExponentSpinner "EXP:" range:[0.1,10.0,2.0] type:#float scale:0.1 fieldWidth:50 width:115 enabled:true tooltip:"1 = linear roof; 2 = rounded bridge-like falloff."
    checkbox customLineartLayerCheckbox "Custom Layer" checked:false width:105 across:2 tooltip:"Place generated strips into the named layer."
    editText lineartLayerNameEdit text:"lineart" width:185 height:18 enabled:false tooltip:"Existing or new layer name for generated lineart."
    button createInnerLineartButton "CREATE LINEART" width:220 height:24 align:#center tooltip:"Build a separate lineart strip from the selected edge network."

    on createInnerLineartButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorCreateDetachedInnerLineart widthMm:lineartWidthMmSpinner.value customLayer:customLineartLayerCheckbox.checked layerName:lineartLayerNameEdit.text flipGeometry:flipLineartCheckbox.checked invertNormals:invertLineartNormalsCheckbox.checked taperEnds:taperLineartCheckbox.checked taperExponent:taperExponentSpinner.value copySkin:copyLineartSkinCheckbox.checked
            deadlimitVertexColorFbxRollout.statusLabel.text = "Lineart '" + result[4] + "': " + (result[1] as string) + " edge(s), " + (result[2] as string) + " face(s), " + (result[5] as string) + " Skin."
        )
        catch (deadlimitVertexColorFbxRollout.statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on customLineartLayerCheckbox changed state do
    (
        lineartLayerNameEdit.enabled = state
    )

    on taperLineartCheckbox changed state do
    (
        taperExponentSpinner.enabled = state
    )
)

rollout deadlimitVertexColorFbxRollout "EXPORT VERTEX COLOR"
(
    label targetLabel "" width:270 height:28 align:#left
    label statusLabel "Export the regular DMX with Wall Worm first." width:270 height:28 align:#left
    label exportTopSpacer "" width:270 height:10
    button exportButton "EXPORT VERTEX COLOR FBX" width:190 height:26 align:#center tooltip:"Export selected geometry and renderable Shape/Spline objects beside the latest Wall Worm DMX."
    checkbox fixedGammaCheckbox "FIXED GAMMA" checked:true width:120 height:22 across:2 tooltip:"Enable the temporary RGB power transform for Source 2. Disable for unchanged/Marmoset export."
    spinner gammaValueSpinner "GAMMA" range:[0.01,4.0,1.0] type:#float scale:0.01 fieldwidth:55 width:115 height:22 enabled:true tooltip:"Export power is GAMMA / 2.2. Default 1.0 applies the Source 2 correction; 2.2 preserves the stored RGB."
    label exportBottomSpacer "" width:270 height:12

    fn showResultMessage text title isError =
    (
        local messageBoxClass = dotNetClass "System.Windows.Forms.MessageBox"
        local messageBoxButtons = dotNetClass "System.Windows.Forms.MessageBoxButtons"
        local messageBoxIcon = dotNetClass "System.Windows.Forms.MessageBoxIcon"
        local icon = if isError then messageBoxIcon.Error else messageBoxIcon.Information
        messageBoxClass.Show text title messageBoxButtons.OK icon
    )

    fn refreshTarget =
    (
        try
        (
            local latest = deadlimitVertexColorFindLatestDmx()
            targetLabel.text = "Latest DMX: " + latest
        )
        catch
        (
            targetLabel.text = "Latest DMX: <none>"
        )
    )

    on deadlimitVertexColorFbxRollout open do
    (
        refreshTarget()
    )

    on fixedGammaCheckbox changed state do
    (
        gammaValueSpinner.enabled = state
    )

    on exportButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorExportSidecar fixedGamma:fixedGammaCheckbox.checked gammaValue:gammaValueSpinner.value
            local gammaStatus = if result[3] then (" | GAMMA " + (result[5] as string)) else ""
            statusLabel.text = "SUCCESS: sidecar ready for PREPARE" + gammaStatus
            refreshTarget()
            format "Vertex Color FBX: %\nDMX: %\n" result[2] result[1]
            format "Inner Lineart Skin: % Skin(s), % clone(s) updated.\n" result[4][2] result[4][4]
            local gammaMessage = if result[3] then ("ON: " + (result[5] as string) + " / 2.2 = RGB exponent " + (result[6] as string)) else "OFF: stored RGB unchanged"
            local successMessage = "SUCCESS\n\nVertex Color FBX exported to:\n" + result[2] + "\n\nFixed Gamma: " + gammaMessage + "\nLineart Skin: " + (result[4][4] as string) + " clone(s) synchronized\n\nRun PREPARE in Deadlimit Aggregator. A failed or cancelled PREPARE keeps the FBX for retry."
            showResultMessage successMessage "VERTEX COLOR FBX READY" false
        )
        catch
        (
            local errorText = getCurrentException()
            local cleanError = substituteString errorText "-- Runtime error: " ""
            statusLabel.text = "FAILED: Vertex Color FBX was not exported"
            showResultMessage ("FAILED\n\n" + cleanError) "VERTEX COLOR FBX FAILED" true
        )
    )

)

deadlimitPipelineFloater = newRolloutFloater "Deadlimit Max Script" 380 410
addRollout deadlimitVertexColorBoneToolsRollout deadlimitPipelineFloater rolledUp:false
addRollout deadlimitVertexColorToolsRollout deadlimitPipelineFloater rolledUp:false
addRollout deadlimitVertexColorInnerLineartRollout deadlimitPipelineFloater rolledUp:false
addRollout deadlimitVertexColorFbxRollout deadlimitPipelineFloater rolledUp:false

-- Refresh only the session-local meshes last touched by a Vertex Color command. These
-- callbacks are deliberately non-persistent so loading the .max never embeds script code.
callbacks.removeScripts id:#DeadlimitVertexColorViewportRefresh
callbacks.addScript #sceneUndo "try(deadlimitVertexColorHandleSceneUndoRedo false)catch()" id:#DeadlimitVertexColorViewportRefresh persistent:false
callbacks.addScript #sceneRedo "try(deadlimitVertexColorHandleSceneUndoRedo true)catch()" id:#DeadlimitVertexColorViewportRefresh persistent:false
