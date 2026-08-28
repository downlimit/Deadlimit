/*
    Deadlimit Vertex Color FBX sidecar exporter.

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
global deadlimitVertexColorBoneToolsRollout
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
global deadlimitVertexColorFitBoneLengths
global deadlimitVertexColorSetSelectedBoneLength
global deadlimitVertexColorFlipSelectedBoneX
global deadlimitVertexColorFillPaletteToVertex
global deadlimitVertexColorFillVertexToPalette
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

rollout deadlimitVertexColorBoneToolsRollout "BONE TOOLS"
(
    button fitBoneLengthsButton "Fit Selected to Hierarchy" width:260 height:24 tooltip:"Fit selected BoneGeometry lengths to the average bone-child pivot distance. Selected leaf bones use half the parent-pivot distance."
    spinner boneLengthCmSpinner "Length (cm):" range:[0.0,100000.0,10.0] type:#float scale:0.1 fieldWidth:64 width:133 align:#left across:2 tooltip:"Visual length in centimeters."
    button setBoneLengthButton "SET" width:55 height:24 offset:[40,0] tooltip:"Set selected bones to this visual length while preserving their current X direction."
    button flipBoneXButton "Flip Selected X" width:260 height:24 tooltip:"Flip selected BoneGeometry display meshes along local X while preserving node transforms, pivots, hierarchy, and names."

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

    on deadlimitVertexColorBoneToolsRollout rolledUp isOpen do
    (
        try
        (
            local oldHeight = deadlimitVertexColorFbxRollout.boneToolsSubRollout.height
            local newHeight = if isOpen then 121 else 24
            deadlimitVertexColorFbxRollout.boneToolsSubRollout.height = newHeight
            local dialogSize = getDialogSize deadlimitVertexColorFbxRollout
            setDialogSize deadlimitVertexColorFbxRollout [dialogSize.x, dialogSize.y + (newHeight - oldHeight)]
        )
        catch()
    )
)

rollout deadlimitVertexColorFbxRollout "Vertex Color FBX Export" width:310
(
    label targetLabel "" width:286 height:16 align:#left
    button exportButton "EXPORT VERTEX COLOR FBX" width:184 height:26 across:2 tooltip:"Export selected geometry and renderable Shape/Spline objects beside the latest Wall Worm DMX."
    checkbox fixedGammaCheckbox "FIXED GAMMA" checked:false width:94 height:26 offset:[50,0] tooltip:"Export RGB as value^(1/2.2) for Source 2. The scene colors stay unchanged. Leave off for unmodified/Marmoset export."
    label statusLabel "Export the regular DMX with Wall Worm first." width:286 height:16 align:#left
    label toolsLabel "VERTEX COLOR TOOLS" width:286 height:14 align:#left
    button toVertexButton "To Vertex" width:139 height:24 across:2 tooltip:"Fill channel 0 from each selected object's wire color. Bakes below Skin and enables shaded Vertex Color display."
    button toPaletteButton "To Palette" width:139 height:24 tooltip:"Copy the first available channel 0 color to each selected object's wire color."
    button copyVertexButton "Copy Vertex" width:139 height:24 across:2 tooltip:"Copy the first available channel 0 color from the first selected mesh to the shared color buffer."
    button copyPaletteButton "Copy Palette" width:139 height:24 tooltip:"Copy the wire color from the first selected mesh to the shared color buffer."
    button pasteVertexButton "Paste Vertex" width:139 height:24 across:2 tooltip:"Fill channel 0 on all selected meshes from the shared color buffer."
    button pastePaletteButton "Paste Palette" width:139 height:24 tooltip:"Set the wire color on all selected meshes from the shared color buffer."
    button enableVertexColorButton "Enable VertColor" width:139 height:24 across:2 tooltip:"Show selected objects with shaded Vertex Color channel 0."
    button disableVertexColorButton "Disable VertColor" width:139 height:24 tooltip:"Return selected objects to normal viewport shading."
    subRollout boneToolsSubRollout "" width:286 height:24 scrollBar:#off

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
        addSubRollout boneToolsSubRollout deadlimitVertexColorBoneToolsRollout rolledUp:true
        deadlimitVertexColorBoneToolsRollout.open = false
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

    on toVertexButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorFillPaletteToVertex()
            statusLabel.text = deadlimitVertexColorResultText "To Vertex" result
        )
        catch (statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on toPaletteButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorFillVertexToPalette()
            statusLabel.text = deadlimitVertexColorResultText "To Palette" result
        )
        catch (statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on copyVertexButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorCopyVertex()
            statusLabel.text = "Copy Vertex: " + result[1] + "  " + (result[2] as string)
        )
        catch (statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on copyPaletteButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorCopyPalette()
            statusLabel.text = "Copy Palette: " + result[1] + "  " + (result[2] as string)
        )
        catch (statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on pasteVertexButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorPasteVertex()
            statusLabel.text = deadlimitVertexColorResultText "Paste Vertex" result
        )
        catch (statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on pastePaletteButton pressed do
    (
        try (statusLabel.text = "Paste Palette: " + ((deadlimitVertexColorPastePalette()) as string) + " object(s).")
        catch (statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on enableVertexColorButton pressed do
    (
        try (statusLabel.text = "Enable VertColor: " + ((deadlimitVertexColorSetDisplay true) as string) + " object(s).")
        catch (statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

    on disableVertexColorButton pressed do
    (
        try (statusLabel.text = "Disable VertColor: " + ((deadlimitVertexColorSetDisplay false) as string) + " object(s).")
        catch (statusLabel.text = substituteString (getCurrentException()) "-- Runtime error: " "")
    )

)

createDialog deadlimitVertexColorFbxRollout
