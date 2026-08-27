#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

<#
.SYNOPSIS
    Creates a desktop shortcut and Start Menu shortcut for Stagecoach Desktop Application.
#>

$repoRoot = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
$exePath = Join-Path -Path $repoRoot -ChildPath 'src\Stagecoach.App\bin\Release\net9.0-windows\Stagecoach.App.exe'

# Fallback to Debug if Release not built yet
if (-not (Test-Path $exePath)) {
    $exePath = Join-Path -Path $repoRoot -ChildPath 'src\Stagecoach.App\bin\Debug\net9.0-windows\Stagecoach.App.exe'
}

# Fallback to silent VBS launcher if dotnet binary is not present
if (-not (Test-Path $exePath)) {
    $exePath = "wscript.exe"
    $vbsPath = Join-Path -Path $repoRoot -ChildPath 'Stagecoach.vbs'
    $arguments = "`"$vbsPath`""
}
else {
    $arguments = ""
}

$desktopPath = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$wsh = New-Object -ComObject WScript.Shell

$desktopShortcutPath = Join-Path -Path $desktopPath -ChildPath 'Stagecoach.lnk'
$shortcut = $wsh.CreateShortcut($desktopShortcutPath)
$shortcut.TargetPath = if ($arguments) { "wscript.exe" } else { $exePath }
if ($arguments) { $shortcut.Arguments = $arguments }
$shortcut.WorkingDirectory = $repoRoot
$shortcut.Description = "Stagecoach — Desktop Command Center for Azure & Arc VMs"
$shortcut.Save()

Write-Host "[Stagecoach] Shortcut updated on Desktop pointing to: $exePath" -ForegroundColor Green
