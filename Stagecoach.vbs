Set WshShell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
strScriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
strPsScript = strScriptDir & "\scripts\Start-StagecoachApp.ps1"

WshShell.Run "pwsh.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & strPsScript & """", 0, False
