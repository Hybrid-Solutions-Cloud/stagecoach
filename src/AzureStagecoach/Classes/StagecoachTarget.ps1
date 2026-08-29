#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

enum StagecoachTargetKind {
    AzureVM
    ArcServer
}

enum StagecoachDomainType {
    Workgroup
    ActiveDirectory
    EntraID
}

class StagecoachTarget {
    [string]$Id
    [string]$Name
    [string]$ResourceGroup
    [string]$SubscriptionId
    [string]$TenantId
    [string]$Location
    [StagecoachTargetKind]$Kind
    [string]$OsType
    [string]$OsName
    [string]$PowerState
    [string]$AgentStatus
    [string]$DomainName
    [StagecoachDomainType]$DomainType
    [string]$AdminUsername
    [string]$NicId
    [string]$VNetId
    [string]$PrivateIpAddress
    [string]$PublicIpAddress
    [string]$BastionId
    [string]$BastionName
    [string]$BastionResourceGroup
    [string]$BastionSku
    [bool]$BastionSameVNet
    [hashtable]$Tags

    StagecoachTarget() {
        $this.Tags = @{}
    }

    [bool] IsWindows() {
        return $this.OsType -match 'Windows'
    }

    [bool] HasBastion() {
        return -not [string]::IsNullOrWhiteSpace($this.BastionName)
    }
}
