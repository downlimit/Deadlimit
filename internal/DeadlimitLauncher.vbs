Option Explicit

Dim shell, fso, internalDir, rootDir
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

internalDir = fso.GetParentFolderName(WScript.ScriptFullName)
rootDir = fso.GetParentFolderName(internalDir)

On Error Resume Next
HideInfrastructurePath rootDir & "\.github"
HideInfrastructurePath rootDir & "\Deadlimit.cmd"
On Error GoTo 0

shell.CurrentDirectory = rootDir
shell.Run "dotnet run --project ""internal\src\Deadlimit""", 0, False

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
