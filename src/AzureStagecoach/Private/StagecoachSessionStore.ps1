#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Session registry (~/.stagecoach/sessions.json): PID, target, method, local
# port, start time. No secrets, ever — see the plan §4.3.1.

function Save-StagecoachSessionRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetId,

        [Parameter(Mandatory = $true)]
        [string]$TargetName,

        [Parameter(Mandatory = $true)]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [int]$ProcessId,

        [int]$LocalPort = 0
    )

    $records = @(Read-StagecoachJsonFile -FileName 'sessions.json')
    $records = @($records | Where-Object { $null -ne $_ })

    $records += [pscustomobject]@{
        SessionId  = [guid]::NewGuid().ToString()
        TargetId   = $TargetId
        TargetName = $TargetName
        Method     = $Method
        ProcessId  = $ProcessId
        LocalPort  = $LocalPort
        StartTime  = [System.DateTime]::UtcNow.ToString('o')
    }

    Write-StagecoachJsonFile -FileName 'sessions.json' -Value $records
}

function Get-StagecoachSession {
    <#
    .SYNOPSIS
        Lists live Stagecoach sessions (tunnels, RDP helpers, SSH windows).
    .DESCRIPTION
        Reads the session registry, prunes entries whose process has exited,
        and returns what is still running.
    #>
    [CmdletBinding()]
    param()

    $records = @(Read-StagecoachJsonFile -FileName 'sessions.json')
    $records = @($records | Where-Object { $null -ne $_ })
    if ($records.Count -eq 0) { return @() }

    $alive = @($records | Where-Object {
            $procId = [int](Get-StagecoachProp -InputObject $_ -Name 'ProcessId' -Default 0)
            $procId -gt 0 -and $null -ne (Get-Process -Id $procId -ErrorAction SilentlyContinue)
        })

    if ($alive.Count -ne $records.Count) {
        Write-StagecoachJsonFile -FileName 'sessions.json' -Value $alive
    }
    return @($alive)
}

function Stop-StagecoachSession {
    <#
    .SYNOPSIS
        Stops a live Stagecoach session by session ID or process ID.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$SessionId
    )

    $records = @(Get-StagecoachSession)
    $record = $records | Where-Object {
        (Get-StagecoachProp -InputObject $_ -Name 'SessionId') -eq $SessionId -or
        "$(Get-StagecoachProp -InputObject $_ -Name 'ProcessId')" -eq $SessionId
    } | Select-Object -First 1

    if (-not $record) {
        Write-Warning "No live session matched '$SessionId'."
        return
    }

    $procId = [int](Get-StagecoachProp -InputObject $record -Name 'ProcessId')
    $name = Get-StagecoachProp -InputObject $record -Name 'TargetName'
    if ($PSCmdlet.ShouldProcess("$name (pid $procId)", 'Stop session')) {
        if ($IsWindows) {
            # /T takes the whole helper tree (pwsh wrapper + az + tunnel child).
            & taskkill /PID $procId /T /F 2>&1 | Out-Null
        }
        else {
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
        Get-StagecoachSession | Out-Null
    }
}
