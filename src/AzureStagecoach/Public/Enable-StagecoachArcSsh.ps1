#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Enable-StagecoachArcSsh {
    <#
    .SYNOPSIS
        Enables SSH connectivity on an Azure Arc-enabled server (connection endpoint + SSH service config).
    .DESCRIPTION
        Creates the Microsoft.HybridConnectivity 'default' endpoint and its SSH
        service configuration (port 22) on the Arc machine — the two Azure-side
        pieces 'az ssh arc' needs before it can connect. This modifies Azure
        state, so it always asks for confirmation first.
        If the server itself lacks an SSH service (Windows without OpenSSH),
        also run Install-StagecoachOpenSsh.
    .PARAMETER Name
        Arc server name, looked up in the discovered inventory.
    .PARAMETER Target
        A StagecoachTarget from Get-StagecoachInventory.
    .PARAMETER Port
        SSH port on the machine (default 22).
    .EXAMPLE
        Enable-StagecoachArcSsh arc-app-03
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High', DefaultParameterSetName = 'ByName')]
    param(
        [Parameter(ParameterSetName = 'ByName', Mandatory = $true, Position = 0)]
        [string]$Name,

        [Parameter(ParameterSetName = 'ByTarget', Mandatory = $true, ValueFromPipeline = $true)]
        $Target,

        [ValidateRange(1, 65535)]
        [int]$Port = 22
    )

    process {
        if ($PSCmdlet.ParameterSetName -eq 'ByName') {
            $Target = Find-StagecoachTarget -Name $Name
        }

        if ($Target.Kind -ne [StagecoachTargetKind]::ArcServer) {
            throw "'$($Target.Name)' is an Azure VM, not an Arc-enabled server — Bastion or direct SSH applies instead."
        }

        $apiVersion = '2023-03-15'
        $endpointUri = "https://management.azure.com$($Target.Id)/providers/Microsoft.HybridConnectivity/endpoints/default?api-version=$apiVersion"
        $serviceUri = "https://management.azure.com$($Target.Id)/providers/Microsoft.HybridConnectivity/endpoints/default/serviceConfigurations/SSH?api-version=$apiVersion"

        if (-not $PSCmdlet.ShouldProcess($Target.Name, "Enable Arc SSH connectivity (create default endpoint + SSH service configuration on port $Port)")) {
            return
        }

        Write-Information "[stagecoach] Creating default connectivity endpoint on '$($Target.Name)'..." -InformationAction Continue
        Invoke-StagecoachAz -Arguments @('rest', '--method', 'put', '--url', $endpointUri, '--body', '{"properties": {"type": "default"}}') -AsJson | Out-Null

        Write-Information "[stagecoach] Creating SSH service configuration (port $Port)..." -InformationAction Continue
        Invoke-StagecoachAz -Arguments @('rest', '--method', 'put', '--url', $serviceUri, '--body', "{`"properties`": {`"serviceName`": `"SSH`", `"port`": $Port}}") -AsJson | Out-Null

        Write-Information "[stagecoach] Arc SSH connectivity enabled on '$($Target.Name)'. Connect with: Connect-StagecoachVM $($Target.Name) -Method Ssh" -InformationAction Continue
    }
}
