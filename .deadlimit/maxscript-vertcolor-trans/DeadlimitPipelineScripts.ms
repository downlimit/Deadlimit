/*
    Deadlimit Pipeline Scripts for 3ds Max.

    Run this script after exporting the regular DMX with Wall Worm. It reads the
    last Wall Worm export folder from 3dsMax.ini, finds the newest DMX there, and
    exports selected geometry and renderable Shape/Spline objects beside that DMX as <name>_vertexcolor.fbx.
    Deadlimit consumes it during PREPARE and keeps failed/cancelled handoffs for retry.
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

plugin simpleMeshMod DeadlimitVertexColorFixedGammaModifier
name:"Deadlimit Vertex Color Fixed Gamma"
classID:#(0x5a4b1032, 0x279d5612)
(
    on modifyMesh do
    (
        if meshop.getMapSupport mesh 0 do
        (
            local inverseGamma = 1.0 / 2.2
            local mapVertexCount = meshop.getNumMapVerts mesh 0
            for mapVertexIndex = 1 to mapVertexCount do
            (
                local mapColor = meshop.getMapVert mesh 0 mapVertexIndex
                if mapColor.x < 0.0 then mapColor.x = 0.0 else if mapColor.x > 1.0 then mapColor.x = 1.0
                if mapColor.y < 0.0 then mapColor.y = 0.0 else if mapColor.y > 1.0 then mapColor.y = 1.0
                if mapColor.z < 0.0 then mapColor.z = 0.0 else if mapColor.z > 1.0 then mapColor.z = 1.0
                meshop.setMapVert mesh 0 mapVertexIndex [mapColor.x ^ inverseGamma, mapColor.y ^ inverseGamma, mapColor.z ^ inverseGamma]
            )
        )
    )
)

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
global deadlimitVertexColorToggleDisplay
global deadlimitVertexColorFitBoneLengths
global deadlimitVertexColorSetSelectedBoneLength
global deadlimitVertexColorFlipSelectedBoneX
global deadlimitVertexColorCreateInnerLineart
global deadlimitVertexColorBuildInnerLineart
global deadlimitVertexColorReadFaceSmoothingGroups
global deadlimitVertexColorApplyInnerLineartModifierResult
global deadlimitVertexColorFillPaletteToVertex
global deadlimitVertexColorFillVertexToPalette
global deadlimitVertexColorRedistribute
global deadlimitVertexColorClipboard
global deadlimitVertexColorReadFirst
global deadlimitVertexColorBakeNode
global deadlimitVertexColorCopyVertex
global deadlimitVertexColorCopyPalette
global deadlimitVertexColorPasteVertex
global deadlimitVertexColorPastePalette
global deadlimitVertexColorResultText

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

fn deadlimitVertexColorAttachFixedGammaModifiers nodes =
(
    local temporaryModifiers = #()
    try
    (
        with undo off
        (
            for node in nodes do
            (
                local alreadyApplied = false
                for existingModifier in node.modifiers while not alreadyApplied do
                    alreadyApplied = (classOf existingModifier) == DeadlimitVertexColorFixedGammaModifier

                if not alreadyApplied then
                (
                    local gammaModifier = DeadlimitVertexColorFixedGammaModifier()
                    addModifier node gammaModifier before:1
                    append temporaryModifiers #(node, gammaModifier)
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
            if isValidNode item[1] do try (deleteModifier item[1] item[2]) catch()
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

fn deadlimitVertexColorExportSidecar fixedGamma:false =
(
    local primaryDmx = deadlimitVertexColorFindLatestDmx()

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

    local savedParams = #(
        #("ASCII", FBXExporterGetParam "ASCII"),
        #("Animation", FBXExporterGetParam "Animation"),
        #("Cameras", FBXExporterGetParam "Cameras"),
        #("Lights", FBXExporterGetParam "Lights"),
        #("Triangulate", FBXExporterGetParam "Triangulate")
    )

    try
    (
        deadlimitVertexColorSetFbxParam "ASCII" true
        deadlimitVertexColorSetFbxParam "Animation" false
        deadlimitVertexColorSetFbxParam "Cameras" false
        deadlimitVertexColorSetFbxParam "Lights" false
        deadlimitVertexColorSetFbxParam "Triangulate" true

        temporaryShapeModifiers = deadlimitVertexColorAttachShapeMeshModifiers selectedExportNodes

        if fixedGamma do
            temporaryGammaModifiers = deadlimitVertexColorAttachFixedGammaModifiers selectedExportNodes

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
        if not sceneWasDirty do setSaveRequired false autobackupRequired:false
        #(primaryDmx, sidecarPath, fixedGamma)
    )
    catch
    (
        local errorText = getCurrentException()
        if doesFileExist temporaryPath do deleteFile temporaryPath
        deadlimitVertexColorRemoveTemporaryModifiers temporaryGammaModifiers
        deadlimitVertexColorRemoveTemporaryModifiers temporaryShapeModifiers
        deadlimitVertexColorRestoreFbxParams savedParams
        select originalSelection
        if not sceneWasDirty do setSaveRequired false autobackupRequired:false
        format "Vertex Color FBX failed: %\n" errorText
        throw()
    )
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
    redrawViews()
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
    redrawViews()
    #(nodes.count, enableDisplay, enabledCount)
)

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
fn deadlimitVertexColorBakeNode node fillColor =
(
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

    undo "FillPaletteToVertex" on
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

    redrawViews()
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

    undo "Redistribute Colors" on
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

    undo "Paste Vertex" on
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

    redrawViews()
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

    local sourceNumMaps = polyop.getNumMaps node
    local sourceMapRecords = #()
    if sourceNumMaps > 0 do for mapChannel = 0 to (sourceNumMaps - 1) where polyop.getMapSupport node mapChannel do
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
            append faceRecords #(triA, sourceFaceSmoothing[faceA], sourceFaceMaterials[faceA], #(true,true,true), #(#(faceA,sourceB), #(faceA,sourceA), #(faceA,sourceA)), true)
            madeA = true
        )
        if deadlimitInnerLineartThreeUnique triB do
        (
            append faceRecords #(triB, sourceFaceSmoothing[faceB], sourceFaceMaterials[faceB], #(true,true,true), #(#(faceB,sourceB), #(faceB,sourceA), #(faceB,sourceB)), true)
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
            local fallbackTri = #(bA, aA, extraClone)
            append faceRecords #(fallbackTri, sourceFaceSmoothing[faceA], sourceFaceMaterials[faceA], #(true,true,true), #(#(faceA,sourceB), #(faceA,sourceA), #(faceA,sourceB)), true)
        )
    )

    for record in vertexRecords where record[3].count >= 3 do
    (
        local clones = record[3]
        local sourceVert = record[1]
        local sourceFace = record[2][1][1]
        local sourceRef = #(sourceFace, sourceVert)
        local capSmoothing = sourceFaceSmoothing[sourceFace]
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

    local outputNumMaps = sourceNumMaps
    if vertexMarker and outputNumMaps < 1 do outputNumMaps = 1
    polyop.setNumMaps node outputNumMaps keep:false
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

        if vertexMarker and mapChannel == 0 do
        (
            for faceIndex = 1 to faceRecords.count do
            (
                local geomVerts = faceRecords[faceIndex][1]
                local mapFace = outputMapFaces[faceIndex]
                for cornerIndex = 1 to geomVerts.count where markerVerts[geomVerts[cornerIndex]] do
                (
                    local currentColor = outputMapVerts[mapFace[cornerIndex]]
                    append outputMapVerts (currentColor * 0.5)
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

    local hasSourceVertexColor = false
    for mapRecord in sourceMapRecords where mapRecord[1] == 0 do hasSourceVertexColor = true
    if vertexMarker and not hasSourceVertexColor do
    (
        polyop.setMapSupport node 0 true
        polyop.setNumMapVerts node 0 2 keep:false
        polyop.setMapVert node 0 1 [1,1,1]
        polyop.setMapVert node 0 2 [0.5,0.5,0.5]
        polyop.setNumMapFaces node 0 faceRecords.count keep:false
        for faceIndex = 1 to faceRecords.count do
        (
            local geomVerts = faceRecords[faceIndex][1]
            local mapFace = for geomVert in geomVerts collect (if markerVerts[geomVert] then 2 else 1)
            polyop.setMapFace node 0 faceIndex mapFace
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
    #(seamFaces, seamEdges, polyop.getNumVerts node)
)


fn deadlimitVertexColorCreateInnerLineartFromBase useUndo:true vertexMarker:false =
(
    if selection.count != 1 then throw "Select one Editable Poly object and its inner edges."

    local node = selection[1]
    if not isValidNode node or (classOf node.baseObject) != Editable_Poly then
        throw "INNER LINEART requires one Editable Poly base object."
    if node.modifiers.count > 0 then
        throw "Make the intended Edit Poly modifier active before creating INNER LINEART inside an existing modifier stack."
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
    local workingPoly = copy sourcePoly
    local operationResult = deadlimitVertexColorBuildInnerLineart workingPoly selectedEdges sourceSegments minimumLength oldVertCount oldFaceCount vertexMarker:vertexMarker
    for faceIndex = 1 to oldFaceCount do polyop.setFaceSmoothGroup workingPoly faceIndex sourceSmoothingGroups[faceIndex]
    local preparedSmoothingGroups = deadlimitVertexColorReadFaceSmoothingGroups workingPoly
    local workingNode = undefined
    local helperModifier = undefined
    local applyError = undefined
    local originalMaterial = node.material

    try
    (
        with undo off with redraw off
        (
            workingNode = copy node
            workingNode.name = uniqueName "__DEADLIMIT_INNER_LINEART_WORK__"
            workingNode.baseObject = workingPoly
            update workingNode
        )
    )
    catch
    (
        with undo off try(if isValidNode workingNode do delete workingNode)catch()
        throw()
    )

    if useUndo then
    (
        undo "Create Inner Lineart Pass" on
        (
            try (helperModifier = deadlimitVertexColorApplyInnerLineartModifierResult node undefined workingNode oldFaceCount originalMaterial preparedSmoothingGroups undefined)
            catch (applyError = getCurrentException())
        )
    )
    else
    (
        with undo off
        (
            try (helperModifier = deadlimitVertexColorApplyInnerLineartModifierResult node undefined workingNode oldFaceCount originalMaterial preparedSmoothingGroups undefined)
            catch (applyError = getCurrentException())
        )
    )

    if applyError != undefined then
    (
        with undo off try(if isValidNode workingNode do delete workingNode)catch()
        throw applyError
    )

    redrawViews()
    #(sourceEdgeCount, operationResult[1].numberSet, operationResult[2].numberSet, operationResult[3] - oldVertCount)
)
fn deadlimitVertexColorInsertModifierAbove node sourceModifier newModifier =
(
    local sourceIndex = modPanel.getModifierIndex node sourceModifier
    if sourceIndex == undefined then throw "The active Edit Poly modifier is no longer in the selected object's stack."

    if sourceIndex <= 1 then
        addModifier node newModifier
    else
        addModifier node newModifier before:(sourceIndex - 1)

    local insertedIndex = modPanel.getModifierIndex node newModifier
    local movedSourceIndex = modPanel.getModifierIndex node sourceModifier
    if insertedIndex == undefined or movedSourceIndex == undefined or insertedIndex != (movedSourceIndex - 1) then
        throw "INNER LINEART could not insert its helper directly above the active Edit Poly modifier."
    insertedIndex
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
fn deadlimitVertexColorApplyInnerLineartModifierResult node sourceModifier workingNode oldFaceCount originalMaterial preparedSmoothingGroups sourceSelections =
(
    local helperModifier = undefined
    try
    (
        select node
        max modify mode
        subObjectLevel = 0

        helperModifier = Edit_Poly()
        helperModifier.name = uniqueName "DEADLIMIT INNER LINEART"
        if sourceModifier == undefined then
            addModifier node helperModifier
        else
            deadlimitVertexColorInsertModifierAbove node sourceModifier helperModifier
        modPanel.setCurrentObject helperModifier node:node ui:true

        helperModifier.Attach workingNode editPolyNode:node
        node.material = originalMaterial
        update node
        deadlimitVertexColorRestoreModifierSmoothingGroups helperModifier node preparedSmoothingGroups faceOffset:oldFaceCount

        local originalFaces = #{1..oldFaceCount}
        helperModifier.SetEPolySelLevel #Face
        subObjectLevel = 4
        helperModifier.SetSelection #Face originalFaces node:node
        helperModifier.Select #Face originalFaces node:node
        helperModifier.ButtonOp #DeleteFace
        helperModifier.ButtonOp #RemoveIsoVerts
        update node
        deadlimitVertexColorRestoreModifierSmoothingGroups helperModifier node preparedSmoothingGroups
        deadlimitVertexColorSetModifierSelections helperModifier node #(#{}, #{}, #{})
        if sourceModifier != undefined do deadlimitVertexColorSetModifierSelections sourceModifier node sourceSelections
        update node

        modPanel.setCurrentObject helperModifier node:node ui:true
        helperModifier.SetEPolySelLevel #Object
        subObjectLevel = 0
        helperModifier
    )
    catch
    (
        try(if helperModifier != undefined and (modPanel.getModifierIndex node helperModifier) != undefined do deleteModifier node helperModifier)catch()
        throw()
    )
)

fn deadlimitVertexColorCreateInnerLineartFromModifier node sourceModifier useUndo:true vertexMarker:false =
(
    local sourceModifierIndex = modPanel.getModifierIndex node sourceModifier
    if sourceModifierIndex == undefined then throw "The active Edit Poly modifier is no longer in the selected object's stack."
    if subObjectLevel != 2 then throw "Enter Edge sub-object mode in the active Edit Poly modifier and select the inner edges."

    local selectedEdges = copy (sourceModifier.GetSelection #Edge node:node)
    local sourceSelections = #(
        copy (sourceModifier.GetSelection #Vertex node:node),
        copy selectedEdges,
        copy (sourceModifier.GetSelection #Face node:node))
    local sourceEdgeCount = selectedEdges.numberSet
    if sourceEdgeCount == 0 then throw "Select at least one inner edge in the active Edit Poly modifier."

    for modifierIndex = 1 to (sourceModifierIndex - 1) do
    (
        if matchPattern node.modifiers[modifierIndex].name pattern:"INNER LINEART*" ignoreCase:true then
            throw "An INNER LINEART result already exists above this Edit Poly. Add/select a new Edit Poly above that result for another lineart pass."
    )

    local oldVertCount = sourceModifier.GetNumVertices node:node
    local oldFaceCount = sourceModifier.GetNumFaces node:node
    local oldEdgeCount = sourceModifier.GetNumEdges node:node
    local sourceSmoothingGroups = for faceIndex = 1 to oldFaceCount collect (sourceModifier.GetFaceSmoothingGroups faceIndex node:node)
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

    local helperModifier = undefined
    local applyError = undefined
    local originalMaterial = node.material


    if useUndo then
    (
        undo "Create Inner Lineart" on
        (
            try (helperModifier = deadlimitVertexColorApplyInnerLineartModifierResult node sourceModifier workingNode oldFaceCount originalMaterial preparedSmoothingGroups sourceSelections)
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
            try (helperModifier = deadlimitVertexColorApplyInnerLineartModifierResult node sourceModifier workingNode oldFaceCount originalMaterial preparedSmoothingGroups sourceSelections)
            catch
            (
                applyError = getCurrentException()
            )
        )
    )

    if applyError != undefined then
    (
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
    else if (classOf node.baseObject) == Editable_Poly then
        deadlimitVertexColorCreateInnerLineartFromBase useUndo:useUndo vertexMarker:vertexMarker
    else if (classOf node.baseObject) == Editable_Mesh then
        deadlimitVertexColorCreateInnerLineartFromMesh useUndo:useUndo vertexMarker:vertexMarker
    else
        throw "INNER LINEART requires an Editable Poly/Editable Mesh base object or an active Edit Poly modifier."
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
    checkbox vertexMarkerCheckbox "VertexColor Marker" checked:false width:120 across:2 tooltip:"Darken the participating Vertex Color corners while creating lineart topology."
    button createInnerLineartButton "Create Lineart" width:137 height:22 tooltip:"Create a switchable DEADLIMIT INNER LINEART Edit Poly pass from the selected inner edges."

    on createInnerLineartButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorCreateInnerLineart vertexMarker:vertexMarkerCheckbox.checked
            deadlimitVertexColorFbxRollout.statusLabel.text = "Inner Lineart pass: " + (result[1] as string) + " edge(s), " + (result[2] as string) + " face(s)."
        )
        catch (deadlimitVertexColorFbxRollout.statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )
)

rollout deadlimitVertexColorFbxRollout "EXPORT VERTEX COLOR"
(
    label targetLabel "" width:270 height:28 align:#left
    label statusLabel "Export the regular DMX with Wall Worm first." width:270 height:28 align:#left
    label exportTopSpacer "" width:270 height:10
    button exportButton "EXPORT VERTEX COLOR FBX" width:190 height:26 align:#center tooltip:"Export selected geometry and renderable Shape/Spline objects beside the latest Wall Worm DMX."
    checkbox fixedGammaCheckbox "FIXED GAMMA" checked:true width:110 height:22 align:#center tooltip:"Export RGB as value^(1/2.2) for Source 2. The scene colors stay unchanged. Disable for unmodified/Marmoset export."
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

    on exportButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorExportSidecar fixedGamma:fixedGammaCheckbox.checked
            local gammaStatus = if result[3] then " | FIXED GAMMA" else ""
            statusLabel.text = "SUCCESS: sidecar ready for PREPARE" + gammaStatus
            refreshTarget()
            format "Vertex Color FBX: %\nDMX: %\n" result[2] result[1]
            local gammaMessage = if result[3] then "ON: RGB ^ (1/2.2)" else "OFF: stored RGB unchanged"
            local successMessage = "SUCCESS\n\nVertex Color FBX exported to:\n" + result[2] + "\n\nFixed Gamma: " + gammaMessage + "\n\nRun PREPARE in Deadlimit. A failed or cancelled PREPARE keeps the FBX for retry."
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

deadlimitPipelineFloater = newRolloutFloater "Deadlimit Pipeline Scripts" 380 410
addRollout deadlimitVertexColorBoneToolsRollout deadlimitPipelineFloater rolledUp:false
addRollout deadlimitVertexColorToolsRollout deadlimitPipelineFloater rolledUp:false
addRollout deadlimitVertexColorInnerLineartRollout deadlimitPipelineFloater rolledUp:false
addRollout deadlimitVertexColorFbxRollout deadlimitPipelineFloater rolledUp:false
