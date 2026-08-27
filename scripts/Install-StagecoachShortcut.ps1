#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

<#
.SYNOPSIS
    Creates a desktop shortcut and Start Menu shortcut for Stagecoach.
#>

$repoRoot = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
$vbsPath = Join-Path -Path $repoRoot -ChildPath 'Stagecoach.vbs'
$desktopPath = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)

$wsh = New-Object -ComObject WScript.Shell

# Desktop Shortcut
$desktopShortcutPath = Join-Path -Path $desktopPath -ChildPath 'Stagecoach.lnk'
$shortcut = $wsh.CreateShortcut($desktopShortcutPath)
$shortcut.TargetPath = "wscript.exe"
$shortcut.Arguments = "`"$vbsPath`""
$shortcut.WorkingDirectory = $repoRoot
$shortcut.Description = "Stagecoach — Desktop Command Center for Azure & Arc VMs"
$shortcut.Save()

Write-Host "[Stagecoach] Shortcut created successfully on Desktop: $desktopShortcutPath" -ForegroundColor Green

