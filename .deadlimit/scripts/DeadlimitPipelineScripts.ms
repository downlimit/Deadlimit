/*
    Deadlimit Scripts — single artist-facing entrypoint.

    The implementation is split internally only to keep experimental code isolated in
    source control. Artists load this file only; it opens the normal Deadlimit Scripts
    floater and appends the EXPERIMENTAL rollout to that same window.
*/

global deadlimitPipelineEntryScriptPath
global deadlimitPipelineCoreScriptPath
global deadlimitPipelineExperimentalScriptPath

deadlimitPipelineEntryScriptPath = getSourceFileName()
deadlimitPipelineCoreScriptPath = getFilenamePath deadlimitPipelineEntryScriptPath + "internal\\DeadlimitPipelineScripts.ms"
deadlimitPipelineExperimentalScriptPath = getFilenamePath deadlimitPipelineEntryScriptPath + "internal\\DeadlimitPipelineScripts.Experimental.ms"

if not (doesFileExist deadlimitPipelineCoreScriptPath) then
    throw ("Deadlimit Scripts core was not found: " + deadlimitPipelineCoreScriptPath)
if not (doesFileExist deadlimitPipelineExperimentalScriptPath) then
    throw ("Deadlimit Scripts experimental rollout was not found: " + deadlimitPipelineExperimentalScriptPath)

fileIn deadlimitPipelineExperimentalScriptPath

-- The experimental implementation lives one folder deeper than the public entrypoint.
-- Override only its Manager locator so it resolves from the normal scripts folder.
fn deadlimitExperimentalFindManagerExe =
(
    local pathClass = dotNetClass "System.IO.Path"
    local scriptsFolder = getFilenamePath deadlimitPipelineEntryScriptPath
    local bundleRoot = pathClass.GetFullPath (scriptsFolder + "..\\..\\")
    local candidates = #(
        bundleRoot + "DeadlimitManager.exe",
        bundleRoot + "internal\\src\\Deadlimit\\bin\\Release\\net10.0-windows\\DeadlimitManager.exe",
        bundleRoot + "internal\\src\\Deadlimit\\bin\\Debug\\net10.0-windows\\DeadlimitManager.exe"
    )

    for candidate in candidates where doesFileExist candidate do return candidate
    undefined
)
