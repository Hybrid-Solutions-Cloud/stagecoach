#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-StagecoachInventory {
    <#
    .SYNOPSIS
        Discovers Azure VMs and Arc-enabled servers, including how each one is reachable.
    .DESCRIPTION
        Queries Azure Resource Graph across every subscription the signed-in
        account can see (or one subscription with -SubscriptionId) for:
          - Azure VMs, joined to their NIC (private/public IP, VNet)
          - Azure Bastion hosts, mapped to VMs by VNet (falling back to any
            Bastion in the same subscription for peered-VNet reachability)
          - Azure Arc-enabled servers
        Results are cached to ~/.stagecoach/inventory.json; use -Cached to read
        the cache without hitting Azure.
    .PARAMETER SubscriptionId
        Limit discovery to one or more subscription IDs.
    .PARAMETER Cached
        Return the locally cached inventory (no Azure calls).
    .OUTPUTS
        StagecoachTarget[]
    .EXAMPLE
        Get-StagecoachInventory | Format-Table Name, Kind, OsType, PrivateIpAddress, BastionName
    #>
    [CmdletBinding()]
    [OutputType('StagecoachTarget[]')]
    param(
        [Parameter(Mandatory = $false)]
        [string[]]$SubscriptionId,

        [switch]$Cached
    )

    if ($Cached) {
        return Get-StagecoachCachedInventory
    }

    $machineQuery = @"
Resources
| where type =~ 'microsoft.compute/virtualmachines' or type =~ 'microsoft.hybridcompute/machines'
| extend kind = iff(type =~ 'microsoft.compute/virtualmachines', 'AzureVM', 'ArcServer'),
         osType = coalesce(tostring(properties.storageProfile.osDisk.osType), tostring(properties.osType), ''),
         osName = coalesce(tostring(properties.osName), tostring(properties.osProfile.computerName), name),
         powerState = coalesce(tostring(properties.extended.instanceView.powerState.displayStatus), tostring(properties.status), ''),
         agentStatus = tostring(properties.status),
         domainName = coalesce(tostring(properties.domainName), ''),
         adminUsername = coalesce(tostring(properties.osProfile.adminUsername), ''),
         nicId = tolower(coalesce(tostring(properties.networkProfile.networkInterfaces[0].id), ''))
| project id, name, resourceGroup, subscriptionId, tenantId, location, kind, osType, osName, powerState, agentStatus, domainName, adminUsername, nicId, tags
"@

    $nicQuery = @"
Resources
| where type =~ 'microsoft.network/networkinterfaces'
| mv-expand ipconfig = properties.ipConfigurations
| extend subnetId = tolower(tostring(ipconfig.properties.subnet.id))
| project nicId = tolower(id),
          privateIp = tostring(ipconfig.properties.privateIPAddress),
          publicIpId = tolower(tostring(ipconfig.properties.publicIPAddress.id)),
          vnetId = tolower(substring(subnetId, 0, indexof(subnetId, '/subnets/')))
"@

    $publicIpQuery = @"
Resources
| where type =~ 'microsoft.network/publicipaddresses'
| project publicIpId = tolower(id), ipAddress = tostring(properties.ipAddress)
"@

    $bastionQuery = @"
Resources
| where type =~ 'microsoft.network/bastionhosts'
| extend subnetId = tolower(tostring(properties.ipConfigurations[0].properties.subnet.id))
| project id, name, resourceGroup, subscriptionId,
          sku = tostring(sku.name),
          vnetId = iff(indexof(subnetId, '/subnets/') > 0, substring(subnetId, 0, indexof(subnetId, '/subnets/')), '')
"@

    Write-Verbose 'Querying Azure Resource Graph for machines, NICs, public IPs, and Bastion hosts...'
    $machines = Invoke-ArgQuery -Query $machineQuery -SubscriptionId $SubscriptionId
    $nics = Invoke-ArgQuery -Query $nicQuery -SubscriptionId $SubscriptionId
    $publicIps = Invoke-ArgQuery -Query $publicIpQuery -SubscriptionId $SubscriptionId
    $bastions = Invoke-ArgQuery -Query $bastionQuery -SubscriptionId $SubscriptionId

    $nicById = @{}
    foreach ($nic in @($nics)) {
        $key = Get-StagecoachProp -InputObject $nic -Name 'nicId' -Default ''
        if ($key -and -not $nicById.ContainsKey($key)) { $nicById[$key] = $nic }
    }

    $ipById = @{}
    foreach ($pip in @($publicIps)) {
        $key = Get-StagecoachProp -InputObject $pip -Name 'publicIpId' -Default ''
        if ($key) { $ipById[$key] = Get-StagecoachProp -InputObject $pip -Name 'ipAddress' -Default '' }
    }

    $bastionByVNet = @{}
    $bastionBySubscription = @{}
    foreach ($bastion in @($bastions)) {
        $vnet = Get-StagecoachProp -InputObject $bastion -Name 'vnetId' -Default ''
        $sub = Get-StagecoachProp -InputObject $bastion -Name 'subscriptionId' -Default ''
        if ($vnet -and -not $bastionByVNet.ContainsKey($vnet)) { $bastionByVNet[$vnet] = $bastion }
        if ($sub -and -not $bastionBySubscription.ContainsKey($sub)) { $bastionBySubscription[$sub] = $bastion }
    }

    $targets = [System.Collections.Generic.List[StagecoachTarget]]::new()

    foreach ($item in @($machines)) {
        $target = [StagecoachTarget]::new()
        $target.Id = Get-StagecoachProp -InputObject $item -Name 'id' -Default ''
        $target.Name = Get-StagecoachProp -InputObject $item -Name 'name' -Default ''
        $target.ResourceGroup = Get-StagecoachProp -InputObject $item -Name 'resourceGroup' -Default ''
        $target.SubscriptionId = Get-StagecoachProp -InputObject $item -Name 'subscriptionId' -Default ''
        $target.TenantId = Get-StagecoachProp -InputObject $item -Name 'tenantId' -Default ''
        $target.Location = Get-StagecoachProp -InputObject $item -Name 'location' -Default ''
        $target.Kind = [StagecoachTargetKind](Get-StagecoachProp -InputObject $item -Name 'kind' -Default 'AzureVM')
        $target.OsType = Get-StagecoachProp -InputObject $item -Name 'osType' -Default ''
        $target.OsName = Get-StagecoachProp -InputObject $item -Name 'osName' -Default ''
        $target.PowerState = Get-StagecoachProp -InputObject $item -Name 'powerState' -Default ''
        $target.AgentStatus = Get-StagecoachProp -InputObject $item -Name 'agentStatus' -Default ''
        $target.DomainName = Get-StagecoachProp -InputObject $item -Name 'domainName' -Default ''
        $target.AdminUsername = Get-StagecoachProp -InputObject $item -Name 'adminUsername' -Default ''
        $target.NicId = Get-StagecoachProp -InputObject $item -Name 'nicId' -Default ''

        if ([string]::IsNullOrWhiteSpace($target.DomainName) -or
            $target.DomainName -eq 'WORKGROUP' -or
            $target.DomainName -eq $target.Name) {
            $target.DomainType = [StagecoachDomainType]::Workgroup
        }
        else {
            $target.DomainType = [StagecoachDomainType]::ActiveDirectory
        }

        $tags = Get-StagecoachProp -InputObject $item -Name 'tags'
        if ($tags -is [pscustomobject]) {
            foreach ($prop in $tags.PSObject.Properties) {
                $target.Tags[$prop.Name] = [string]$prop.Value
            }
        }

        # Join NIC → IPs and VNet (Azure VMs only; Arc machines have no Azure NIC).
        if ($target.NicId -and $nicById.ContainsKey($target.NicId)) {
            $nic = $nicById[$target.NicId]
            $target.PrivateIpAddress = Get-StagecoachProp -InputObject $nic -Name 'privateIp' -Default ''
            $target.VNetId = Get-StagecoachProp -InputObject $nic -Name 'vnetId' -Default ''
            $publicIpId = Get-StagecoachProp -InputObject $nic -Name 'publicIpId' -Default ''
            if ($publicIpId -and $ipById.ContainsKey($publicIpId)) {
                $target.PublicIpAddress = $ipById[$publicIpId]
            }
        }

        # Map a Bastion host: same VNet first, then any Bastion in the same
        # subscription (Standard+ SKU can reach peered VNets).
        if ($target.Kind -eq [StagecoachTargetKind]::AzureVM) {
            $bastion = $null
            $sameVNet = $false
            if ($target.VNetId -and $bastionByVNet.ContainsKey($target.VNetId)) {
                $bastion = $bastionByVNet[$target.VNetId]
                $sameVNet = $true
            }
            elseif ($target.SubscriptionId -and $bastionBySubscription.ContainsKey($target.SubscriptionId)) {
                $bastion = $bastionBySubscription[$target.SubscriptionId]
            }

            if ($bastion) {
                $target.BastionId = Get-StagecoachProp -InputObject $bastion -Name 'id' -Default ''
                $target.BastionName = Get-StagecoachProp -InputObject $bastion -Name 'name' -Default ''
                $target.BastionResourceGroup = Get-StagecoachProp -InputObject $bastion -Name 'resourceGroup' -Default ''
                $target.BastionSku = Get-StagecoachProp -InputObject $bastion -Name 'sku' -Default ''
                $target.BastionSameVNet = $sameVNet
            }
        }

        $targets.Add($target)
    }

    $sorted = @($targets | Sort-Object -Property Name)
    Save-StagecoachInventory -Targets $sorted
    return $sorted
}
