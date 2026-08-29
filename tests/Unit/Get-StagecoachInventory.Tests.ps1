#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

BeforeAll {
    $modulePath = Join-Path -Path $PSScriptRoot -ChildPath '../../src/AzureStagecoach/AzureStagecoach.psd1'
    Import-Module $modulePath -Force
}

Describe 'Get-StagecoachInventory' {
    BeforeAll {
        Mock -ModuleName 'AzureStagecoach' -CommandName 'Save-StagecoachInventory' -MockWith { }

        Mock -ModuleName 'AzureStagecoach' -CommandName 'Invoke-ArgQuery' -MockWith {
            param($Query, $SubscriptionId)

            if ($Query -match 'virtualmachines') {
                return @(
                    [pscustomobject]@{
                        id = '/subscriptions/sub1/resourcegroups/rg1/providers/microsoft.compute/virtualmachines/vm-hub-01'
                        name = 'vm-hub-01'; resourceGroup = 'rg1'; subscriptionId = 'sub1'; tenantId = 'ten1'; location = 'eastus'
                        kind = 'AzureVM'; osType = 'Windows'; osName = 'vm-hub-01'; powerState = 'VM running'; agentStatus = ''
                        domainName = ''; adminUsername = 'azadmin'; nicId = '/subscriptions/sub1/resourcegroups/rg1/providers/microsoft.network/networkinterfaces/nic-hub-01'
                        tags = $null
                    },
                    [pscustomobject]@{
                        id = '/subscriptions/sub1/resourcegroups/rg2/providers/microsoft.compute/virtualmachines/vm-spoke-01'
                        name = 'vm-spoke-01'; resourceGroup = 'rg2'; subscriptionId = 'sub1'; tenantId = 'ten1'; location = 'eastus'
                        kind = 'AzureVM'; osType = 'Linux'; osName = 'vm-spoke-01'; powerState = 'VM running'; agentStatus = ''
                        domainName = ''; adminUsername = ''; nicId = '/subscriptions/sub1/resourcegroups/rg2/providers/microsoft.network/networkinterfaces/nic-spoke-01'
                        tags = $null
                    },
                    [pscustomobject]@{
                        id = '/subscriptions/sub1/resourcegroups/rg3/providers/microsoft.hybridcompute/machines/arc-dc-01'
                        name = 'arc-dc-01'; resourceGroup = 'rg3'; subscriptionId = 'sub1'; tenantId = 'ten1'; location = 'eastus'
                        kind = 'ArcServer'; osType = 'Windows'; osName = 'Windows Server 2022'; powerState = 'Connected'; agentStatus = 'Connected'
                        domainName = 'corp.contoso.com'; adminUsername = ''; nicId = ''
                        tags = [pscustomobject]@{ env = 'prod' }
                    }
                )
            }
            if ($Query -match 'networkinterfaces') {
                return @(
                    [pscustomobject]@{
                        nicId = '/subscriptions/sub1/resourcegroups/rg1/providers/microsoft.network/networkinterfaces/nic-hub-01'
                        privateIp = '10.0.1.4'
                        publicIpId = '/subscriptions/sub1/resourcegroups/rg1/providers/microsoft.network/publicipaddresses/pip-hub-01'
                        vnetId = '/subscriptions/sub1/resourcegroups/rg-net/providers/microsoft.network/virtualnetworks/vnet-hub'
                    },
                    [pscustomobject]@{
                        nicId = '/subscriptions/sub1/resourcegroups/rg2/providers/microsoft.network/networkinterfaces/nic-spoke-01'
                        privateIp = '10.1.1.4'
                        publicIpId = ''
                        vnetId = '/subscriptions/sub1/resourcegroups/rg-net/providers/microsoft.network/virtualnetworks/vnet-spoke'
                    }
                )
            }
            if ($Query -match 'publicipaddresses') {
                return @(
                    [pscustomobject]@{
                        publicIpId = '/subscriptions/sub1/resourcegroups/rg1/providers/microsoft.network/publicipaddresses/pip-hub-01'
                        ipAddress = '52.10.20.30'
                    }
                )
            }
            if ($Query -match 'bastionhosts') {
                return @(
                    [pscustomobject]@{
                        id = '/subscriptions/sub1/resourcegroups/rg-net/providers/microsoft.network/bastionhosts/bas-hub'
                        name = 'bas-hub'; resourceGroup = 'rg-net'; subscriptionId = 'sub1'; sku = 'Standard'
                        vnetId = '/subscriptions/sub1/resourcegroups/rg-net/providers/microsoft.network/virtualnetworks/vnet-hub'
                    }
                )
            }
            return @()
        }
    }

    It 'joins NIC private/public IPs onto Azure VMs' {
        $inventory = @(Get-StagecoachInventory)
        $hub = $inventory | Where-Object Name -eq 'vm-hub-01'
        $hub.PrivateIpAddress | Should -Be '10.0.1.4'
        $hub.PublicIpAddress | Should -Be '52.10.20.30'
    }

    It 'maps a same-VNet Bastion host onto the VM' {
        $inventory = @(Get-StagecoachInventory)
        $hub = $inventory | Where-Object Name -eq 'vm-hub-01'
        $hub.BastionName | Should -Be 'bas-hub'
        $hub.BastionResourceGroup | Should -Be 'rg-net'
        $hub.BastionSameVNet | Should -BeTrue
    }

    It 'falls back to a same-subscription Bastion for VMs in other VNets' {
        $inventory = @(Get-StagecoachInventory)
        $spoke = $inventory | Where-Object Name -eq 'vm-spoke-01'
        $spoke.BastionName | Should -Be 'bas-hub'
        $spoke.BastionSameVNet | Should -BeFalse
    }

    It 'classifies Arc servers with a real domain as ActiveDirectory and maps no Bastion' {
        $inventory = @(Get-StagecoachInventory)
        $arc = $inventory | Where-Object Name -eq 'arc-dc-01'
        "$($arc.Kind)" | Should -Be 'ArcServer'
        "$($arc.DomainType)" | Should -Be 'ActiveDirectory'
        $arc.BastionName | Should -BeNullOrEmpty
        $arc.Tags['env'] | Should -Be 'prod'
    }

    It 'classifies empty-domain machines as Workgroup and keeps the VM admin username' {
        $inventory = @(Get-StagecoachInventory)
        $hub = $inventory | Where-Object Name -eq 'vm-hub-01'
        "$($hub.DomainType)" | Should -Be 'Workgroup'
        $hub.AdminUsername | Should -Be 'azadmin'
    }

    It 'caches the discovered inventory' {
        Get-StagecoachInventory | Out-Null
        Should -Invoke -ModuleName 'AzureStagecoach' -CommandName 'Save-StagecoachInventory' -Times 1
    }
}
