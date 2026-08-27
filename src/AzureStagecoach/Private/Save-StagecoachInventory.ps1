#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Save-StagecoachInventory {
    <#
    .SYNOPSIS
        Saves discovered inventory items to the local persistent cache file (~/.stagecoach/inventory.json).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [StagecoachTarget[]]$Targets
    )

    $configDir = Join-Path -Path ([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile)) -ChildPath '.stagecoach'
    if (-not (Test-Path $configDir)) {
        New-Item -Path $configDir -ItemType Directory -Force | Out-Null
    }

    $cacheFile = Join-Path -Path $configDir -ChildPath 'inventory.json'
    $json = $Targets | ConvertTo-Json -Depth 5
    Set-Content -Path $cacheFile -Value $json -Encoding utf8
}

function Get-StagecoachCachedInventory {
    <#
    .SYNOPSIS
        Reads discovered inventory items from the local persistent cache file (~/.stagecoach/inventory.json).
    #>
    [CmdletBinding()]
    [OutputType([StagecoachTarget[]])]
    param()

    $cacheFile = Join-Path -Path ([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile)) -ChildPath '.stagecoach\inventory.json'
    if (-not (Test-Path $cacheFile)) {
        return @()
    }

    try {
        $raw = Get-Content -Path $cacheFile -Raw -Encoding utf8
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return @()
        }
        $parsed = $raw | ConvertFrom-Json
        $targets = [System.Collections.Generic.List[StagecoachTarget]]::new()
        foreach ($item in $parsed) {
            $target = [StagecoachTarget]::new()
            $target.Id = $item.Id
            $target.Name = $item.Name
            $target.ResourceGroup = $item.ResourceGroup
            $target.SubscriptionId = $item.SubscriptionId
            $target.TenantId = $item.TenantId
            $target.Location = $item.Location
            $target.Kind = [StagecoachTargetKind]::$($item.Kind)
            $target.OsType = $item.OsType
            $target.OsName = $item.OsName
            $target.PowerState = $item.PowerState
            $target.AgentStatus = $item.AgentStatus
            $target.DomainName = $item.DomainName
            $target.DomainType = [StagecoachDomainType]::$($item.DomainType)
            if ($item.Tags) {
                foreach ($prop in $item.Tags.PSObject.Properties) {
                    $target.Tags[$prop.Name] = [string]$prop.Value
                }
            }
            $targets.Add($target)
        }
        return , $targets.ToArray()
    }
    catch {
        Write-Verbose "Failed to load local inventory cache: $_"
        return @()
    }
}

