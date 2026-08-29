#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-ArgQuery {
    <#
    .SYNOPSIS
        Runs an Azure Resource Graph query via az, following skip tokens for full results.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query,

        [Parameter(Mandatory = $false)]
        [string[]]$SubscriptionId
    )

    $allRows = [System.Collections.Generic.List[object]]::new()
    $skipToken = $null

    do {
        $arguments = @('graph', 'query', '-q', $Query, '--first', '1000')
        if ($SubscriptionId) {
            $arguments += @('--subscriptions') + $SubscriptionId
        }
        if ($skipToken) {
            $arguments += @('--skip-token', $skipToken)
        }

        $page = Invoke-StagecoachAz -Arguments $arguments -AsJson
        if ($null -eq $page) { break }

        $data = Get-StagecoachProp -InputObject $page -Name 'data' -Default @()
        foreach ($row in @($data)) {
            $allRows.Add($row)
        }

        $skipToken = Get-StagecoachProp -InputObject $page -Name 'skip_token'
    } while ($skipToken)

    return $allRows.ToArray()
}
