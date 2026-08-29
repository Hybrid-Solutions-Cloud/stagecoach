#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Connect-StagecoachAccount {
    <#
    .SYNOPSIS
        Signs in to Azure with an Entra ID account (wraps 'az login').
    .DESCRIPTION
        Runs an interactive az login. Sign in once — Stagecoach then reuses the
        Azure CLI token cache for discovery and every connection it launches.
    .PARAMETER TenantId
        Sign in to a specific tenant instead of the account's home tenant.
    .PARAMETER UseDeviceCode
        Use device-code sign-in (for hosts where a browser cannot be opened).
    .EXAMPLE
        Connect-StagecoachAccount
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$TenantId,

        [switch]$UseDeviceCode
    )

    $arguments = @('login')
    if ($TenantId) { $arguments += @('--tenant', $TenantId) }
    if ($UseDeviceCode) { $arguments += @('--use-device-code') }

    $accounts = Invoke-StagecoachAz -Arguments $arguments -AsJson
    if (-not $accounts) {
        throw 'Sign-in did not return any subscriptions. Check the account has Azure access.'
    }

    $summary = @($accounts) | ForEach-Object {
        [pscustomobject]@{
            Subscription = Get-StagecoachProp -InputObject $_ -Name 'name'
            TenantId     = Get-StagecoachProp -InputObject $_ -Name 'tenantId'
            User         = Get-StagecoachProp -InputObject (Get-StagecoachProp -InputObject $_ -Name 'user') -Name 'name'
            IsDefault    = Get-StagecoachProp -InputObject $_ -Name 'isDefault' -Default $false
        }
    }

    Write-Information "[stagecoach] Signed in — $(@($summary).Count) subscription(s) visible." -InformationAction Continue
    return $summary
}
