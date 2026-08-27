#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$port = 8085
$url = "http://127.0.0.1:$port/"

# Check if backend listener is already running on port 8085
$isRunning = $false
try {
    $tcp = [System.Net.Sockets.TcpClient]::new()
    $connect = $tcp.BeginConnect('127.0.0.1', $port, $null, $null)
    $success = $connect.AsyncWaitHandle.WaitOne(500, $false)
    if ($success -and $tcp.Connected) {
        $isRunning = $true
        $tcp.EndConnect($connect)
    }
    $tcp.Close()
}
catch {
    $isRunning = $false
}

# If not running, spawn backend in hidden background process
if (-not $isRunning) {
    $repoRoot = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
    $modulePath = Join-Path -Path $repoRoot -ChildPath 'src\AzureStagecoach\AzureStagecoach.psd1'
    $psCommand = "Import-Module '$modulePath' -Force; Start-Stagecoach -Port $port -NoBrowser"
    Start-Process -FilePath "pwsh.exe" -ArgumentList "-NoProfile -WindowStyle Hidden -Command `"$psCommand`"" -WindowStyle Hidden
    Start-Sleep -Milliseconds 1000
}

# Launch UI in clean standalone App Window mode (Edge / Chrome --app)
$edgePath = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edgePath)) {
    $edgePath = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
}

if (Test-Path $edgePath) {
    Start-Process -FilePath $edgePath -ArgumentList "--app=$url --window-size=1360,900"
}
else {
    Start-Process $url
}
