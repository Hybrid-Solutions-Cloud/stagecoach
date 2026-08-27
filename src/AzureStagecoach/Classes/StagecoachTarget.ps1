#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

enum StagecoachTargetKind {
    AzureVM
    ArcServer
    AzureLocalVM
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
    [string]$BastionHostId
    [string]$PublicIpAddress
    [string]$PrivateIpAddress
    [hashtable]$Tags

    StagecoachTarget() {
        $this.Tags = @{}
    }
}

