/*
    Deadlimit Scripts — experimental additions.

    This entrypoint loads the regular Deadlimit Pipeline Scripts first, then adds
    experiments that intentionally stay outside the stable rollout contract.
*/

global deadlimitExperimentalScriptPath
global deadlimitExperimentalDmxVertexColorRollout
global deadlimitDmxVertexColorTransferPayload
global deadlimitExperimentalPullDmxVertexColor
global deadlimitExperimentalFindManagerExe
global deadlimitExperimentalFaceKey

deadlimitExperimentalScriptPath = getSourceFileName()

local deadlimitExperimentalBaseScript = getFilenamePath deadlimitExperimentalScriptPath + "DeadlimitPipelineScripts.ms"
if not (doesFileExist deadlimitExperimentalBaseScript) then
    throw ("Deadlimit base script was not found beside the experimental entrypoint: " + deadlimitExperimentalBaseScript)
fileIn deadlimitExperimentalBaseScript

fn deadlimitExperimentalFindManagerExe =
(
    local pathClass = dotNetClass "System.IO.Path"
    local scriptsFolder = getFilenamePath deadlimitExperimentalScriptPath
    local bundleRoot = pathClass.GetFullPath (scriptsFolder + "..\\..\\")
    local candidates = #(
        bundleRoot + "DeadlimitManager.exe",
        bundleRoot + "internal\\src\\Deadlimit\\bin\\Release\\net10.0-windows\\DeadlimitManager.exe",
        bundleRoot + "internal\\src\\Deadlimit\\bin\\Debug\\net10.0-windows\\DeadlimitManager.exe"
    )

    for candidate in candidates where doesFileExist candidate do return candidate
    undefined
)

fn deadlimitExperimentalFaceKey vertexIds =
(
    local ordered = for vertexId in vertexIds collect (vertexId as integer)
    sort ordered
    local key = ""
    for vertexId in ordered do key += (vertexId as string) + ","
    key
)

fn deadlimitExperimentalQuoteProcessArg value =
(
    local text = value as string
    "\"" + (substituteString text "\"" "\\\"") + "\""
)

fn deadlimitExperimentalRunDmxPayloadHelper managerExe dmxPath meshName vertexCount faceCount payloadPath =
(
    local processStartInfo = dotNetObject "System.Diagnostics.ProcessStartInfo"
    processStartInfo.FileName = managerExe
    processStartInfo.UseShellExecute = false
    processStartInfo.CreateNoWindow = true
    processStartInfo.RedirectStandardOutput = true
    processStartInfo.RedirectStandardError = true
    processStartInfo.Arguments =
        "--extract-dmx-vertex-color-transfer " +
        (deadlimitExperimentalQuoteProcessArg dmxPath) + " " +
        (deadlimitExperimentalQuoteProcessArg meshName) + " " +
        (vertexCount as string) + " " +
        (faceCount as string) + " " +
        (deadlimitExperimentalQuoteProcessArg payloadPath)

    local processClass = dotNetClass "System.Diagnostics.Process"
    local process = processClass.Start processStartInfo
    local standardOutput = process.StandardOutput.ReadToEnd()
    local standardError = process.StandardError.ReadToEnd()
    process.WaitForExit()

    if process.ExitCode != 0 then
    (
        local reason = trimRight (trimLeft standardError)
        if reason.count == 0 do reason = trimRight (trimLeft standardOutput)
        if reason.count == 0 do reason = "Deadlimit Manager helper failed with exit code " + (process.ExitCode as string) + "."
        throw reason
    )

    trimRight (trimLeft standardOutput)
)

fn deadlimitExperimentalValidateAndMapPayload targetNode payload =
(
    if payload == undefined or (classOf payload) != Array or payload.count != 5 then
        throw "DMX helper returned an invalid Vertex Color payload."

    if (classOf targetNode.baseObject) != Editable_Poly then
        throw "Experimental DMX Vertex Color transfer currently requires an Editable Poly base object."

    local editablePoly = targetNode.baseObject
    local targetVertexCount = polyop.getNumVerts editablePoly
    local targetFaceCount = polyop.getNumFaces editablePoly
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
        local targetVerts = for vertexId in (polyop.getFaceVerts editablePoly targetFaceIndex) collect (vertexId as integer)
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

fn deadlimitExperimentalApplyMappedColors targetNode mappedColors =
(
    local editablePoly = targetNode.baseObject
    local faceCount = polyop.getNumFaces editablePoly
    local totalCorners = 0
    for faceIndex = 1 to faceCount do totalCorners += mappedColors[faceIndex].count
    if totalCorners <= 0 then throw "No DMX Vertex Color corners were available to transfer."

    local beforeSnapshots = deadlimitVertexColorCaptureUndoSnapshots #(targetNode)
    if beforeSnapshots.count != 1 then
        throw "Could not capture the selected Editable Poly before Vertex Color transfer."

    try
    (
        undo "Pull Vertex Color from DMX" on
        (
            polyop.setMapSupport editablePoly 0 true
            polyop.setNumMapVerts editablePoly 0 totalCorners keep:false
            polyop.setNumMapFaces editablePoly 0 faceCount keep:false

            local mapVertexIndex = 0
            for faceIndex = 1 to faceCount do
            (
                local mapFace = #()
                for colorValue in mappedColors[faceIndex] do
                (
                    mapVertexIndex += 1
                    polyop.setMapVert editablePoly 0 mapVertexIndex colorValue
                    append mapFace mapVertexIndex
                )
                polyop.setMapFace editablePoly 0 faceIndex mapFace
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
        local errorText = getCurrentException()
        deadlimitVertexColorRestoreUndoSnapshots beforeSnapshots
        throw errorText
    )
)

fn deadlimitExperimentalPullDmxVertexColor =
(
    if selection.count != 1 then throw "Select exactly one mesh."
    local targetNode = selection[1]
    if not isValidNode targetNode or (superClassOf targetNode) != GeometryClass then
        throw "Select exactly one valid geometry mesh."
    if (classOf targetNode.baseObject) != Editable_Poly then
        throw "Experimental DMX Vertex Color transfer currently requires an Editable Poly base object."

    local editablePoly = targetNode.baseObject
    local vertexCount = polyop.getNumVerts editablePoly
    local faceCount = polyop.getNumFaces editablePoly
    if vertexCount <= 0 or faceCount <= 0 then throw "Selected mesh has no usable polygon topology."

    local dmxPath = getOpenFileName caption:"Select source DMX with original Vertex Color" types:"DMX (*.dmx)|*.dmx|"
    if dmxPath == undefined then return undefined

    local managerExe = deadlimitExperimentalFindManagerExe()
    if managerExe == undefined then
        throw "DeadlimitManager.exe was not found beside this Deadlimit Scripts installation. Update/reinstall Deadlimit and copy the current script command again."

    local pathClass = dotNetClass "System.IO.Path"
    local guidValue = ((dotNetClass "System.Guid").NewGuid()).ToString "N"
    local payloadPath = pathClass.Combine (pathClass.GetTempPath()) ("deadlimit-dmx-vertexcolor-" + guidValue + ".ms")
    local result = undefined
    deadlimitDmxVertexColorTransferPayload = undefined

    try
    (
        local helperResult = deadlimitExperimentalRunDmxPayloadHelper managerExe dmxPath targetNode.name vertexCount faceCount payloadPath
        if not (doesFileExist payloadPath) then throw "Deadlimit Manager did not create the DMX Vertex Color payload."
        fileIn payloadPath
        if deadlimitDmxVertexColorTransferPayload == undefined then throw "DMX Vertex Color payload did not load."

        local mappedColors = deadlimitExperimentalValidateAndMapPayload targetNode deadlimitDmxVertexColorTransferPayload
        local cornerCount = deadlimitExperimentalApplyMappedColors targetNode mappedColors
        local sourceMeshName = deadlimitDmxVertexColorTransferPayload[1]
        format "Deadlimit experimental Vertex Color: % <- % (% corners)\n" targetNode.name sourceMeshName cornerCount
        result = #(targetNode, sourceMeshName, cornerCount, helperResult)
    )
    catch
    (
        local errorText = getCurrentException()
        format "Deadlimit experimental Vertex Color failed: %\n" errorText
        messageBox (errorText as string) title:"Deadlimit Scripts — Experimental"
    )

    deadlimitDmxVertexColorTransferPayload = undefined
    try(if doesFileExist payloadPath do deleteFile payloadPath)catch()
    result
)

rollout deadlimitExperimentalDmxVertexColorRollout "EXPERIMENTAL"
(
    label lblDmxPull "Copies original DMX channel 0 to one identical selected mesh." width:340 align:#left
    button btnDmxPull "PULL VERTEX COLOR FOR SELECTED MESH..." width:340 height:30

    on btnDmxPull pressed do deadlimitExperimentalPullDmxVertexColor()
)

if deadlimitPipelineFloater != undefined do
    addRollout deadlimitExperimentalDmxVertexColorRollout deadlimitPipelineFloater rolledUp:false
