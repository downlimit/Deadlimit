Option Explicit

Dim shell, fso, internalDir, rootDir, appPath, commandPath
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

internalDir = fso.GetParentFolderName(WScript.ScriptFullName)
rootDir = fso.GetParentFolderName(internalDir)
appPath = rootDir & "\internal\src\Deadlimit\bin\Release\net10.0-windows\Deadlimit.exe"
commandPath = rootDir & "\Deadlimit.cmd"

On Error Resume Next
HideInfrastructurePath rootDir & "\.github"
HideInfrastructurePath commandPath
On Error GoTo 0

shell.CurrentDirectory = rootDir
If fso.FileExists(appPath) Then
    shell.Run Chr(34) & appPath & Chr(34), 0, False
Else
    shell.Run "cmd.exe /c " & Chr(34) & Chr(34) & commandPath & Chr(34) & Chr(34), 0, False
End If

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
