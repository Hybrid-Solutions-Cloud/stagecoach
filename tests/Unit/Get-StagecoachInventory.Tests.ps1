#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

BeforeAll {
    $classesPath = Join-Path -Path $PSScriptRoot -ChildPath '..\..\src\AzureStagecoach\Classes\StagecoachTarget.ps1'
    . $classesPath
    $sessionPath = Join-Path -Path $PSScriptRoot -ChildPath '..\..\src\AzureStagecoach\Classes\StagecoachSession.ps1'
    . $sessionPath
    $modulePath = Join-Path -Path $PSScriptRoot -ChildPath '..\..\src\AzureStagecoach\AzureStagecoach.psd1'
    Import-Module $modulePath -Force
}

Describe 'Get-StagecoachInventory' {
    Context 'Domain Classification' {
        It 'Correctly identifies Workgroup machines when domain is empty or WORKGROUP' {
            Mock -ModuleName 'AzureStagecoach' -CommandName 'Invoke-ArgQuery' -MockWith {
                return @(
                    [pscustomobject]@{
                        id = '/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.HybridCompute/machines/srv-wg'
                        name = 'srv-wg'
                        resourceGroup = 'rg1'
                        subscriptionId = 'sub1'
                        tenantId = 'ten1'
                        location = 'eastus'
                        kind = 'ArcServer'
                        osType = 'Windows'
                        osName = 'Windows Server 2022'
                        powerState = 'Connected'
                        agentStatus = 'Connected'
                        domainName = 'WORKGROUP'
                        tags = $null
                    }
                )
            }

            $inventory = @(Get-StagecoachInventory)
            $inventory.Count | Should -Be 1
            $inventory[0].DomainType | Should -Be ([StagecoachDomainType]::Workgroup)
            $inventory[0].Name | Should -Be 'srv-wg'
        }

        It 'Correctly identifies Active Directory domain-joined machines' {
            Mock -ModuleName 'AzureStagecoach' -CommandName 'Invoke-ArgQuery' -MockWith {
                return @(
                    [pscustomobject]@{
                        id = '/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.HybridCompute/machines/srv-ad'
                        name = 'srv-ad'
                        resourceGroup = 'rg1'
                        subscriptionId = 'sub1'
                        tenantId = 'ten1'
                        location = 'eastus'
                        kind = 'ArcServer'
                        osType = 'Windows'
                        osName = 'Windows Server 2025'
                        powerState = 'Connected'
                        agentStatus = 'Connected'
                        domainName = 'CORP.CONTOSO.COM'
                        tags = $null
                    }
                )
            }

            $inventory = @(Get-StagecoachInventory)
            $inventory.Count | Should -Be 1
            $inventory[0].DomainType | Should -Be ([StagecoachDomainType]::ActiveDirectory)
            $inventory[0].DomainName | Should -Be 'CORP.CONTOSO.COM'
        }
    }
}

