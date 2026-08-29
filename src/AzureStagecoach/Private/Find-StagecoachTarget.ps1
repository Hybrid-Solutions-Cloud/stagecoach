#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-StagecoachTarget {
    <#
    .SYNOPSIS
        Resolves a machine name (wildcards allowed) to exactly one inventory target.
    #>
    [CmdletBinding(DefaultParameterSetName = 'ByName')]
    [OutputType([StagecoachTarget])]
    param(
        [Parameter(ParameterSetName = 'ByName', Mandatory = $true)]
        [string]$Name,

        [Parameter(ParameterSetName = 'ById', Mandatory = $true)]
        [string]$Id
    )

    $inventory = @(Get-StagecoachInventory -Cached)
    if ($inventory.Count -eq 0) {
        Write-Information '[stagecoach] No cached inventory — discovering machines first...' -InformationAction Continue
        $inventory = @(Get-StagecoachInventory)
    }

    if ($PSCmdlet.ParameterSetName -eq 'ById') {
        $Name = $Id  # for error messages
        $found = @($inventory | Where-Object { $_.Id -eq $Id })
    }
    else {
        $found = @($inventory | Where-Object { $_.Name -like $Name })
    }
    if ($found.Count -eq 0) {
        throw "No machine named '$Name' in the inventory. Run Get-StagecoachInventory to refresh, or check the name."
    }
    if ($found.Count -gt 1) {
        $list = ($found | ForEach-Object { "$($_.Name) [$($_.Kind), $($_.ResourceGroup)]" }) -join '; '
        throw "'$Name' matches more than one machine: $list. Pipe the exact target from Get-StagecoachInventory instead."
    }
    return $found[0]
}
