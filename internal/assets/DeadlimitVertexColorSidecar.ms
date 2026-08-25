/*
    Deadlimit Vertex Color sidecar exporter.

    The normal artist DMX remains owned by the Wall Worm exporter UI. This helper
    exports only selected geometry to <latest DMX basename>_vertexcolor.dmx. It
    adds a temporary ChannelMod to the selected nodes and removes it after export.
*/

global deadlimitProjectRoot = @"__DEADLIMIT_PROJECT_ROOT__"
global deadlimitBridgeName = "__Deadlimit_VertexColor_Bridge__"
global deadlimitVertexColorRollout
global deadlimitIsPrimaryDmx
global deadlimitFindLatestPrimaryDmx
global deadlimitMapChannelSupported
global deadlimitFindSharedFreeMapChannel
global deadlimitRemoveBridges
global deadlimitRemoveStaleBridges
global deadlimitValidateColorDmx
global deadlimitExportVertexColorSidecar

fn deadlimitIsPrimaryDmx path =
(
    local fileName = toLower (filenameFromPath path)
    (matchPattern fileName pattern:"*_vertexcolor.dmx") == false
)

fn deadlimitFindLatestPrimaryDmx =
(
    local directoryClass = dotNetClass "System.IO.Directory"
    local fileClass = dotNetClass "System.IO.File"
    local latestPath = undefined
    local latestTicks = 0

    for path in (directoryClass.GetFiles deadlimitProjectRoot "*.dmx") do
    (
        if deadlimitIsPrimaryDmx path then
        (
            local ticks = (fileClass.GetLastWriteTimeUtc path).Ticks
            if latestPath == undefined or ticks > latestTicks then
            (
                latestPath = path
                latestTicks = ticks
            )
        )
    )

    latestPath
)

fn deadlimitMapChannelSupported node channel =
(
    local probe = snapshotAsMesh node
    meshop.getMapSupport probe channel
)

fn deadlimitFindSharedFreeMapChannel nodes =
(
    for channel = 2 to 99 do
    (
        local used = false
        for node in nodes while not used do
        (
            used = deadlimitMapChannelSupported node channel
        )
        if not used do return channel
    )

    throw "Deadlimit Vertex Color: no shared free map channel was found in range 2..99."
)

fn deadlimitRemoveBridges bridges =
(
    for pair in bridges do
    (
        try
        (
            if pair[1] != undefined and isValidNode pair[1] do deleteModifier pair[1] pair[2]
        )
        catch()
    )
)

fn deadlimitRemoveStaleBridges nodes =
(
    for node in nodes do
    (
        local stale = for modifier in node.modifiers where modifier.name == deadlimitBridgeName collect modifier
        for modifier in stale do
        (
            try (deleteModifier node modifier) catch()
        )
    )
)

fn deadlimitValidateColorDmx path =
(
    local fileClass = dotNetClass "System.IO.File"
    local text = fileClass.ReadAllText path
    if (findString text "\"color$0\"") == undefined then
        throw "Deadlimit Vertex Color: Wall Worm output is missing color$0."
    if (findString text "\"color$0Indices\"") == undefined then
        throw "Deadlimit Vertex Color: Wall Worm output is missing color$0Indices."
)

fn deadlimitExportVertexColorSidecar =
(
    if ::WallWormS2DMXExport == undefined then
        throw "Deadlimit Vertex Color requires Wall Worm 7.x with WallWormS2DMXExport."

    if ChannelMod == undefined then
        throw "Deadlimit Vertex Color requires the Wall Worm ChannelMod modifier."

    if not (doesDirectoryExist deadlimitProjectRoot) then
        throw ("Deadlimit project folder does not exist: " + deadlimitProjectRoot)

    local primaryDmx = deadlimitFindLatestPrimaryDmx()
    if primaryDmx == undefined then
        throw "Deadlimit Vertex Color: no primary DMX exists in the project root. Export it with Wall Worm first."

    local selectedNodes = for node in selection where (superClassOf node) == GeometryClass collect node
    if selectedNodes.count == 0 then
        throw "Deadlimit Vertex Color: select at least one geometry object."

    deadlimitRemoveStaleBridges selectedNodes

    local hasAnyColor = false
    for node in selectedNodes while not hasAnyColor do
    (
        hasAnyColor = deadlimitMapChannelSupported node 0
    )
    if not hasAnyColor then
        throw "Deadlimit Vertex Color: selected objects have no Max map channel 0."

    local baseName = getFilenameFile primaryDmx
    local sidecarPath = pathConfig.appendPath deadlimitProjectRoot (baseName + "_vertexcolor.dmx")
    local metadataFolder = pathConfig.appendPath deadlimitProjectRoot ".deadlimit\\wallworm"
    makeDir metadataFolder all:true
    local guidClass = dotNetClass "System.Guid"
    local guidValue = guidClass.NewGuid()
    local temporaryPath = pathConfig.appendPath metadataFolder (baseName + ".vertexcolor-" + (guidValue.ToString "N") + ".dmx")
    local bridges = #()
    local colorChannel = deadlimitFindSharedFreeMapChannel selectedNodes

    try
    (
        for sourceNode in selectedNodes do
        (
            if deadlimitMapChannelSupported sourceNode 0 then
            (
                local bridge = ChannelMod()
                bridge.name = deadlimitBridgeName
                bridge.fromChannel = 0
                bridge.toChannel = colorChannel
                bridge.mode = 0
                bridge.multiplier = 1.0
                bridge.normalize = false
                bridge.clearSource = false
                addModifier sourceNode bridge
                append bridges #(sourceNode, bridge)
            )
        )

        WallWormS2DMXExport \
            filename:temporaryPath \
            meshlist:selectedNodes \
            dmxVersion:22 \
            binary:false \
            encodingVersion:4 \
            color:colorChannel

        if not (doesFileExist temporaryPath) then
            throw ("Deadlimit Vertex Color: Wall Worm did not create " + temporaryPath)

        deadlimitValidateColorDmx temporaryPath

        local fileClass = dotNetClass "System.IO.File"
        fileClass.Copy temporaryPath sidecarPath true
        deleteFile temporaryPath
        deadlimitRemoveBridges bridges
        select selectedNodes
        sidecarPath
    )
    catch
    (
        local errorText = getCurrentException()
        deadlimitRemoveBridges bridges
        if doesFileExist temporaryPath do deleteFile temporaryPath
        select selectedNodes
        format "Deadlimit Vertex Color failed: %\n" errorText
        throw()
    )
)

try (destroyDialog deadlimitVertexColorRollout) catch()

rollout deadlimitVertexColorRollout "Deadlimit Vertex Color" width:430
(
    label targetLabel "" width:400 align:#left
    button exportButton "EXPORT SELECTED VERTEX COLOR" width:400 height:36
    label statusLabel "Export the normal DMX with Wall Worm first." width:400 height:34 align:#left

    fn refreshTarget =
    (
        local latest = deadlimitFindLatestPrimaryDmx()
        targetLabel.text = if latest == undefined then "Latest DMX: <none>" else ("Latest DMX: " + filenameFromPath latest)
    )

    on deadlimitVertexColorRollout open do refreshTarget()

    on exportButton pressed do
    (
        try
        (
            local output = deadlimitExportVertexColorSidecar()
            statusLabel.text = "Created: " + filenameFromPath output
            refreshTarget()
            format "Deadlimit Vertex Color sidecar: %\n" output
        )
        catch
        (
            local errorText = getCurrentException()
            statusLabel.text = errorText
            messageBox errorText title:"Deadlimit Vertex Color"
        )
    )
)

createDialog deadlimitVertexColorRollout
