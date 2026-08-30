#Requires -Version 7.0

[CmdletBinding()]
param([ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot "src/Stagecoach.App/bin/$Configuration/net10.0-windows10.0.19041.0/Stagecoach.App.exe"
if (-not (Test-Path -LiteralPath $target)) { throw "Build Stagecoach first: scripts/Build.ps1 -Configuration $Configuration" }
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$shortcutPath = Join-Path $desktop 'Stagecoach.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = Split-Path -Parent $target
$shortcut.Description = 'Stagecoach — one-click Azure, Bastion, Arc, and Azure Local connections'
$shortcut.Save()
Write-Output $shortcutPath
