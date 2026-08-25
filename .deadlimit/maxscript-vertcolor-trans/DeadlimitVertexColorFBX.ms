/*
    Deadlimit Vertex Color FBX sidecar exporter.

    Run this script after exporting the regular DMX with Wall Worm. It reads the
    last Wall Worm export folder from 3dsMax.ini, finds the newest DMX there, and
    exports selected geometry beside that DMX as <name>_vertexcolor.fbx.
    Deadlimit validates/transfers it and deletes the FBX during PREPARE.
*/

global deadlimitVertexColorFbxRollout
global deadlimitVertexColorLastDmx
global deadlimitVertexColorIsPrimaryDmx
global deadlimitVertexColorFindLatestDmx
global deadlimitVertexColorSetFbxParam
global deadlimitVertexColorRestoreFbxParams
global deadlimitVertexColorExportSidecar

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

try (destroyDialog deadlimitVertexColorFbxRollout) catch()

rollout deadlimitVertexColorFbxRollout "Vertex Color FBX Export" width:430
(
    label targetLabel "" width:400 height:34 align:#left
    button exportButton "EXPORT SELECTED VERTEX COLOR FBX" width:400 height:36
    label statusLabel "Export the regular DMX with Wall Worm first." width:400 height:34 align:#left

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
)

createDialog deadlimitVertexColorFbxRollout
