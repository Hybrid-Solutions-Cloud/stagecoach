@echo off
rem Stagecoach one-click launcher — double-click me.
where pwsh >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required. Install it from https://aka.ms/powershell then run this again.
    pause
    exit /b 1
)
pwsh -NoLogo -NoExit -ExecutionPolicy Bypass -Command "Import-Module '%~dp0src\AzureStagecoach\AzureStagecoach.psd1' -Force; Start-Stagecoach"
