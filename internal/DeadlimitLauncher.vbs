Option Explicit

Dim shell, fso, internalDir, rootDir, commandPath
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

internalDir = fso.GetParentFolderName(WScript.ScriptFullName)
rootDir = fso.GetParentFolderName(internalDir)
commandPath = rootDir & "\Deadlimit.cmd"

On Error Resume Next
HideInfrastructurePath rootDir & "\.github"
HideInfrastructurePath commandPath
On Error GoTo 0

shell.CurrentDirectory = rootDir
shell.Run "cmd.exe /c " & Chr(34) & Chr(34) & commandPath & Chr(34) & Chr(34), 1, False

Sub HideInfrastructurePath(path)
    Dim item
    If fso.FolderExists(path) Then
        Set item = fso.GetFolder(path)
        item.Attributes = item.Attributes Or 2 Or 4
    ElseIf fso.FileExists(path) Then
        Set item = fso.GetFile(path)
        item.Attributes = item.Attributes Or 2 Or 4
    End If
End Sub
