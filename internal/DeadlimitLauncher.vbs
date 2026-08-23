Option Explicit

Dim shell, fso, internalDir, rootDir
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

internalDir = fso.GetParentFolderName(WScript.ScriptFullName)
rootDir = fso.GetParentFolderName(internalDir)

shell.CurrentDirectory = rootDir
shell.Run "dotnet run --project ""internal\src\Deadlimit""", 0, False
