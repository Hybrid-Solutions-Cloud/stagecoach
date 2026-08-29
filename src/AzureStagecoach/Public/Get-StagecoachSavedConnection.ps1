#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-StagecoachSavedConnection {
    <#
    .SYNOPSIS
        Lists saved logins (previous connections), most recently used first.
    .DESCRIPTION
        Saved logins remember the target, connection method, and username of
        previous sessions so reconnecting is one step. Passwords are never
        stored — authentication always happens live via Entra ID / az.
    .EXAMPLE
        Get-StagecoachSavedConnection
    #>
    [CmdletBinding()]
    param()

    $parsed = Read-StagecoachJsonFile -FileName 'connections.json'
    if ($null -eq $parsed) { return @() }

    $entries = @($parsed) | Where-Object { $null -ne $_ } | Sort-Object -Property @{
        Expression = { [datetime](Get-StagecoachProp -InputObject $_ -Name 'LastUsed' -Default '2000-01-01') }
    } -Descending

    return @($entries)
}

function Remove-StagecoachSavedConnection {
    <#
    .SYNOPSIS
        Removes a saved login by target name or resource ID.
    .EXAMPLE
        Remove-StagecoachSavedConnection -Name vm-web-01
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Name
    )

    $existing = @(Get-StagecoachSavedConnection)
    $keep = @($existing | Where-Object {
            (Get-StagecoachProp -InputObject $_ -Name 'TargetName') -ne $Name -and
            (Get-StagecoachProp -InputObject $_ -Name 'TargetId') -ne $Name
        })

    if ($keep.Count -eq $existing.Count) {
        Write-Warning "No saved connection matched '$Name'."
        return
    }

    if ($PSCmdlet.ShouldProcess($Name, 'Remove saved connection')) {
        Write-StagecoachJsonFile -FileName 'connections.json' -Value $keep
    }
}
