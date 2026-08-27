#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

enum StagecoachSessionState {
    Starting
    Active
    Disconnected
    Failed
}

class StagecoachSession {
    [string]$SessionId
    [string]$TargetId
    [string]$TargetName
    [string]$Method
    [int]$LocalPort
    [int]$HelperProcessId
    [int]$ClientProcessId
    [datetime]$StartTime
    [StagecoachSessionState]$State
    [string]$ErrorMessage

    StagecoachSession() {
        $this.SessionId = [System.Guid]::NewGuid().ToString()
        $this.StartTime = [System.DateTime]::UtcNow
        $this.State = [StagecoachSessionState]::Starting
    }
}

