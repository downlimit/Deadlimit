Option Explicit

' Legacy compatibility entry point. The active desktop application is Deadlimit Manager.
Dim shell, fso, internalDir, rootDir, commandPath
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
internalDir = fso.GetParentFolderName(WScript.ScriptFullName)
rootDir = fso.GetParentFolderName(internalDir)
commandPath = rootDir & "\DeadlimitManager.cmd"
shell.CurrentDirectory = rootDir
shell.Run "cmd.exe /c " & Chr(34) & Chr(34) & commandPath & Chr(34) & Chr(34), 1, False
