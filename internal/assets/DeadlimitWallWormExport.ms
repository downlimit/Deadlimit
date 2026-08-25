/*
    Deadlimit Wall Worm DMX22 export helper.

    Project root is injected by Deadlimit at runtime. The helper never mutates the
    artist node: it exports from a temporary copy, bridges Max map channel 0 to a
    free positive channel with ChannelMod, and passes that channel to
    WallWormS2DMXExport color:<channel>.
*/

local deadlimitProjectRoot = @"__DEADLIMIT_PROJECT_ROOT__"

global deadlimitExportSelected

fn deadlimitSanitizeFileName value =
(
    local result = value as string
    for token in #("\\", "/", ":", "*", "?", "\"", "<", ">", "|") do
    (
        result = substituteString result token "_"
    )
    result
)

fn deadlimitMapChannelSupported node channel =
(
    local probe = snapshotAsMesh node
    meshop.getMapSupport probe channel
)

fn deadlimitFindFreePositiveMapChannel node =
(
    for channel = 2 to 99 do
    (
        if not (deadlimitMapChannelSupported node channel) do return channel
    )

    throw "Deadlimit export: no free positive Max map channel was found in range 2..99."
)

fn deadlimitValidateColorStream outputPath =
(
    local fileClass = dotNetClass "System.IO.File"
    local text = fileClass.ReadAllText outputPath

    if (findString text "\"color$0\"") == undefined then
        throw "Deadlimit export: Wall Worm output is missing color$0."

    if (findString text "\"color$0Indices\"") == undefined then
        throw "Deadlimit export: Wall Worm output is missing color$0Indices."
)

fn deadlimitExportNode sourceNode =
(
    local outputName = (deadlimitSanitizeFileName sourceNode.name) + ".dmx"
    local outputPath = pathConfig.appendPath deadlimitProjectRoot outputName
    local tempSuffix = (dotNetClass "System.Guid").NewGuid().ToString "N"
    local tempPath = pathConfig.appendPath deadlimitProjectRoot ("." + outputName + ".deadlimit-" + tempSuffix + ".dmx")
    local exportNode = undefined
    local sourceHasColor = deadlimitMapChannelSupported sourceNode 0

    try
    (
        exportNode = copy sourceNode
        exportNode.name = uniqueName (sourceNode.name + "__deadlimit_export")

        local colorChannel = 0
        if sourceHasColor then
        (
            colorChannel = deadlimitFindFreePositiveMapChannel exportNode

            local bridge = ChannelMod()
            bridge.fromChannel = 0
            bridge.toChannel = colorChannel
            bridge.mode = 0
            bridge.multiplier = 1.0
            bridge.normalize = false
            bridge.clearSource = false
            addModifier exportNode bridge
        )

        WallWormS2DMXExport \
            filename:tempPath \
            meshlist:#(exportNode) \
            dmxVersion:22 \
            binary:false \
            encodingVersion:4 \
            color:colorChannel

        if not (doesFileExist tempPath) then
            throw ("Deadlimit export: Wall Worm did not create " + tempPath)

        if sourceHasColor then
            deadlimitValidateColorStream tempPath

        local fileClass = dotNetClass "System.IO.File"
        fileClass.Copy tempPath outputPath true
        deleteFile tempPath

        if exportNode != undefined and isValidNode exportNode do delete exportNode
        exportNode = undefined
        outputPath
    )
    catch
    (
        local errorText = getCurrentException()
        if exportNode != undefined and isValidNode exportNode do delete exportNode
        exportNode = undefined
        if doesFileExist tempPath do deleteFile tempPath
        throw errorText
    )
)

fn deadlimitExportSelected =
(
    if ::WallWormS2DMXExport == undefined then
        throw "Deadlimit export requires Wall Worm 7.x with WallWormS2DMXExport."

    if ChannelMod == undefined then
        throw "Deadlimit export requires the Wall Worm ChannelMod modifier."

    if not (doesDirectoryExist deadlimitProjectRoot) then
        throw ("Deadlimit project folder does not exist: " + deadlimitProjectRoot)

    local selectedNodes = for node in selection collect node
    if selectedNodes.count == 0 then
        throw "Deadlimit export: select at least one geometry node."

    local plannedNames = #()
    for node in selectedNodes do
    (
        if node == undefined or not (isValidNode node) then
            throw "Deadlimit export received an invalid scene node."

        if (superClassOf node) != GeometryClass then
            throw ("Deadlimit export supports geometry nodes only: " + node.name)

        local outputName = toLower ((deadlimitSanitizeFileName node.name) + ".dmx")
        if (findItem plannedNames outputName) != 0 then
            throw ("Deadlimit export: selected nodes resolve to the same DMX filename: " + outputName)
        append plannedNames outputName
    )

    local outputs = #()
    for node in selectedNodes do
    (
        append outputs (deadlimitExportNode node)
    )

    format "Deadlimit: exported % DMX file(s) to %\n" outputs.count deadlimitProjectRoot
    for output in outputs do format "  %\n" output
    outputs
)

deadlimitExportSelected()
