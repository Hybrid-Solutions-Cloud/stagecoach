#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-ArgQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query,

        [Parameter(Mandatory = $false)]
        [string]$SubscriptionId,

        [Parameter(Mandatory = $false)]
        [string]$TenantId
    )

    $azCmd = Get-Command -Name 'az' -ErrorAction SilentlyContinue
    if (-not $azCmd) {
        throw "Azure CLI ('az') is not installed or not in PATH."
    }

    $arguments = @('graph', 'query', '-q', $Query, '-o', 'json')
    if ($SubscriptionId) {
        $arguments += @('--subscriptions', $SubscriptionId)
    }
    if ($TenantId) {
        $arguments += @('--tenant', $TenantId)
    }

    $rawResult = & az @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Resource Graph query failed: $rawResult"
    }

    $parsed = $rawResult | ConvertFrom-Json
    if ($parsed -and $parsed.data) {
        return $parsed.data
    }

    return $parsed
}

