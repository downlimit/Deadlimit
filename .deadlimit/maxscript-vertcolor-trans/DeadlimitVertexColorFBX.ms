/*
    Deadlimit Vertex Color FBX sidecar exporter.

    Run this script after exporting the regular DMX with Wall Worm. It reads the
    last Wall Worm export folder from 3dsMax.ini, finds the newest DMX there, and
    exports selected geometry beside that DMX as <name>_vertexcolor.fbx.
    Deadlimit validates/transfers it and deletes the FBX during PREPARE.
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

global deadlimitVertexColorFbxRollout
global deadlimitVertexColorLastDmx
global deadlimitVertexColorIsPrimaryDmx
global deadlimitVertexColorFindLatestDmx
global deadlimitVertexColorSetFbxParam
global deadlimitVertexColorRestoreFbxParams
global deadlimitVertexColorExportSidecar
global deadlimitVertexColorSelectedGeometry
global deadlimitVertexColorSetDisplay
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

fn deadlimitVertexColorExportSidecar =
(
    local primaryDmx = deadlimitVertexColorFindLatestDmx()

    local originalSelection = selection as array
    local selectedGeometry = for node in originalSelection where (superClassOf node) == GeometryClass collect node
    if selectedGeometry.count == 0 then
        throw "Select at least one geometry object."

    local folder = getFilenamePath primaryDmx
    local baseName = getFilenameFile primaryDmx
    local sidecarPath = folder + baseName + "_vertexcolor.fbx"
    local guidValue = ((dotNetClass "System.Guid").NewGuid()).ToString "N"
    local temporaryPath = folder + baseName + ".vertexcolor-" + guidValue + ".tmp.fbx"
    local fileClass = dotNetClass "System.IO.File"

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

        if doesFileExist temporaryPath do deleteFile temporaryPath
        select selectedGeometry
        local exported = exportFile temporaryPath #noPrompt selectedOnly:true using:FBXEXP
        if exported != true or not (doesFileExist temporaryPath) then
            throw "3ds Max FBX exporter did not create the sidecar."

        fileClass.Copy temporaryPath sidecarPath true
        deleteFile temporaryPath
        deadlimitVertexColorRestoreFbxParams savedParams
        select originalSelection
        #(primaryDmx, sidecarPath)
    )
    catch
    (
        local errorText = getCurrentException()
        if doesFileExist temporaryPath do deleteFile temporaryPath
        deadlimitVertexColorRestoreFbxParams savedParams
        select originalSelection
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

rollout deadlimitVertexColorFbxRollout "Vertex Color FBX Export" width:310
(
    label targetLabel "" width:286 height:16 align:#left
    button exportButton "EXPORT SELECTED VERTEX COLOR FBX" width:286 height:26
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

    on deadlimitVertexColorFbxRollout open do refreshTarget()

    on exportButton pressed do
    (
        try
        (
            local result = deadlimitVertexColorExportSidecar()
            statusLabel.text = "SUCCESS: sidecar ready for PREPARE"
            refreshTarget()
            format "Vertex Color FBX: %\nDMX: %\n" result[2] result[1]
            local successMessage = "SUCCESS\n\nVertex Color FBX exported to:\n" + result[2] + "\n\nRun PREPARE in Deadlimit. It will transfer Vertex Color and delete the FBX after success."
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
