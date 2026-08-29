#Requires -Version 7.0
<#
.SYNOPSIS
    Puts a Stagecoach shortcut on the desktop (Windows) pointing at the one-click launcher.
.EXAMPLE
    pwsh ./scripts/Install-Stagecoach.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    Write-Host "On macOS/Linux just run: pwsh -c `"Import-Module ./src/AzureStagecoach/AzureStagecoach.psd1; Start-Stagecoach`""
    return
}

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$launcher = Join-Path -Path $repoRoot -ChildPath 'Stagecoach.cmd'
$desktop = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$shortcutPath = Join-Path -Path $desktop -ChildPath 'Stagecoach.lnk'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $launcher
$shortcut.WorkingDirectory = $repoRoot
$shortcut.Description = 'Stagecoach — one login, every VM, one click'
$shortcut.Save()

Write-Host "Shortcut created: $shortcutPath"
