#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-StagecoachInventory {
    <#
    .SYNOPSIS
        Discovers Azure VMs and Arc-enabled servers across subscriptions via Azure Resource Graph.
    .DESCRIPTION
        Queries Azure Resource Graph for all virtual machines and hybrid compute instances,
        parsing their OS, power state, and domain/workgroup status.
    .PARAMETER SubscriptionId
        Optional Subscription ID to filter results.
    .OUTPUTS
        StagecoachTarget[]
    #>
    [CmdletBinding()]
    [OutputType([StagecoachTarget[]])]
    param(
        [Parameter(Mandatory = $false, ValueFromPipelineByPropertyName = $true)]
        [string]$SubscriptionId
    )

    process {
        $kqlQuery = @"
Resources
| where type =~ 'microsoft.compute/virtualmachines'
    or type =~ 'microsoft.hybridcompute/machines'
| extend kind = iff(type =~ 'microsoft.compute/virtualmachines', 'AzureVM', 'ArcServer'),
         osType = coalesce(tostring(properties.storageProfile.osDisk.osType), tostring(properties.osType), tostring(properties.osName)),
         osName = coalesce(tostring(properties.osName), tostring(properties.osProfile.computerName), name),
         powerState = coalesce(tostring(properties.extended.instanceView.powerState.displayStatus), tostring(properties.status)),
         agentStatus = tostring(properties.status),
         domainName = coalesce(tostring(properties.domainName), '')
| project id, name, resourceGroup, subscriptionId, tenantId, location, kind, osType, osName, powerState, agentStatus, domainName, tags
"@

        $results = Invoke-ArgQuery -Query $kqlQuery -SubscriptionId $SubscriptionId

        $targets = [System.Collections.Generic.List[StagecoachTarget]]::new()

        foreach ($item in $results) {
            $target = [StagecoachTarget]::new()
            $target.Id = $item.id
            $target.Name = $item.name
            $target.ResourceGroup = $item.resourceGroup
            $target.SubscriptionId = $item.subscriptionId
            $target.TenantId = $item.tenantId
            $target.Location = $item.location
            $target.Kind = [StagecoachTargetKind]::$($item.kind)
            $target.OsType = $item.osType
            $target.OsName = $item.osName
            $target.PowerState = $item.powerState
            $target.AgentStatus = $item.agentStatus
            $target.DomainName = $item.domainName

            # Domain classification logic
            if ([string]::IsNullOrWhiteSpace($item.domainName) -or $item.domainName -eq 'WORKGROUP' -or $item.domainName -eq $item.name) {
                $target.DomainType = [StagecoachDomainType]::Workgroup
            }
            else {
                $target.DomainType = [StagecoachDomainType]::ActiveDirectory
            }

            if ($item.tags) {
                $tagsObj = $item.tags
                if ($tagsObj -is [PSCustomObject]) {
                    foreach ($prop in $tagsObj.PSObject.Properties) {
                        $target.Tags[$prop.Name] = [string]$prop.Value
                    }
                }
            }

            $targets.Add($target)
        }

        return , $targets.ToArray()
    }
}

