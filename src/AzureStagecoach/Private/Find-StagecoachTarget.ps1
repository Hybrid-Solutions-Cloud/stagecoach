#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-StagecoachTarget {
    <#
    .SYNOPSIS
        Resolves a machine name (wildcards allowed) to exactly one inventory target.
    #>
    [CmdletBinding()]
    [OutputType([StagecoachTarget])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $inventory = @(Get-StagecoachInventory -Cached)
    if ($inventory.Count -eq 0) {
        Write-Information '[stagecoach] No cached inventory — discovering machines first...' -InformationAction Continue
        $inventory = @(Get-StagecoachInventory)
    }

    $found = @($inventory | Where-Object { $_.Name -like $Name })
    if ($found.Count -eq 0) {
        throw "No machine named '$Name' in the inventory. Run Get-StagecoachInventory to refresh, or check the name."
    }
    if ($found.Count -gt 1) {
        $list = ($found | ForEach-Object { "$($_.Name) [$($_.Kind), $($_.ResourceGroup)]" }) -join '; '
        throw "'$Name' matches more than one machine: $list. Pipe the exact target from Get-StagecoachInventory instead."
    }
    return $found[0]
}
