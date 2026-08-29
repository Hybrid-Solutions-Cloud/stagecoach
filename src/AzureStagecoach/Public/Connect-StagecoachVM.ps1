#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Connect-StagecoachVM {
    <#
    .SYNOPSIS
        Opens an RDP or SSH session to an Azure VM (via Bastion or directly) or an Arc-enabled server.
    .DESCRIPTION
        Picks the right route for the target and launches it with your current
        Entra ID authentication:
          - Azure VM with a Bastion host → az network bastion rdp / ssh / tunnel
          - Azure Arc-enabled server     → az ssh arc [--rdp]
          - Azure VM without Bastion     → mstsc (direct RDP) / az ssh vm
        SSH sessions run in the current console; RDP and tunnels launch in their
        own window. Successful launches are remembered as saved logins (target,
        method, username — never passwords) for one-step reconnects.
    .PARAMETER Name
        Target machine name, looked up in the discovered inventory.
    .PARAMETER Target
        A StagecoachTarget from Get-StagecoachInventory (pipeline friendly).
    .PARAMETER Method
        Auto (default: RDP for Windows targets, SSH otherwise), Rdp, Ssh, or Tunnel.
    .PARAMETER LocalUser
        Local or domain account to use instead of your Entra ID identity.
    .PARAMETER TunnelPort
        Local port for -Method Tunnel (default: random 50000-50999).
    .PARAMETER NoSave
        Do not record this connection as a saved login.
    .EXAMPLE
        Connect-StagecoachVM vm-web-01
    .EXAMPLE
        Get-StagecoachInventory | Where-Object Name -eq 'arc-dc-02' | Connect-StagecoachVM -Method Rdp -LocalUser 'CORP\kturner'
    #>
    [CmdletBinding(DefaultParameterSetName = 'ByName')]
    [OutputType('StagecoachSession')]
    param(
        [Parameter(ParameterSetName = 'ByName', Mandatory = $true, Position = 0)]
        [string]$Name,

        [Parameter(ParameterSetName = 'ById', Mandatory = $true)]
        [string]$Id,

        [Parameter(ParameterSetName = 'ByTarget', Mandatory = $true, ValueFromPipeline = $true)]
        $Target,

        [ValidateSet('Auto', 'Rdp', 'Ssh', 'Tunnel')]
        [string]$Method = 'Auto',

        [Parameter(Mandatory = $false)]
        [string]$LocalUser,

        [int]$TunnelPort = 0,

        [switch]$NoSave
    )

    process {
        if ($PSCmdlet.ParameterSetName -eq 'ByName') {
            $Target = Find-StagecoachTarget -Name $Name
        }
        elseif ($PSCmdlet.ParameterSetName -eq 'ById') {
            $Target = Find-StagecoachTarget -Id $Id
        }

        $route = Resolve-StagecoachRoute -Target $Target -Method $Method -LocalUser $LocalUser -TunnelPort $TunnelPort

        $session = [StagecoachSession]::new()
        $session.TargetId = $Target.Id
        $session.TargetName = $Target.Name
        $session.Method = $route.Method

        foreach ($note in $route.Notes) {
            Write-Information "[stagecoach] $note" -InformationAction Continue
        }
        Write-Information "[stagecoach] $($route.Method) → $($Target.Name): $($route.Tool) $($route.Arguments -join ' ')" -InformationAction Continue

        if ($route.Interactive) {
            # SSH-style session: hand the current console to the client.
            & $route.Tool @($route.Arguments)
            $exitCode = $LASTEXITCODE
            if ($exitCode -ne 0) {
                $session.State = [StagecoachSessionState]::Failed
                $session.ErrorMessage = "$($route.Tool) exited with code $exitCode."
                if ($Target.Kind -eq [StagecoachTargetKind]::ArcServer) {
                    Write-Warning "Arc SSH failed. If this server has never accepted SSH: run 'Enable-StagecoachArcSsh -Target <target>' to configure the connection endpoint, and 'Install-StagecoachOpenSsh -Target <target>' if Windows OpenSSH is not installed on it."
                }
                return $session
            }
            $session.State = [StagecoachSessionState]::Disconnected
        }
        else {
            # Resolve 'az' to its real path (az.cmd on Windows) so Start-Process finds it.
            $toolPath = $route.Tool
            $resolvedCmd = Get-Command -Name $route.Tool -ErrorAction SilentlyContinue
            if ($resolvedCmd -and $resolvedCmd.Source) { $toolPath = $resolvedCmd.Source }

            $proc = Start-Process -FilePath $toolPath -ArgumentList $route.Arguments -PassThru
            if ($proc) {
                $session.HelperProcessId = $proc.Id
                $session.LocalPort = $route.LocalPort
                $session.State = [StagecoachSessionState]::Active
                Save-StagecoachSessionRecord -TargetId $Target.Id -TargetName $Target.Name `
                    -Method $route.Method -ProcessId $proc.Id -LocalPort $route.LocalPort
            }
        }

        if (-not $NoSave) {
            Save-StagecoachConnectionProfile -Target $Target -Method $route.Method -Username $LocalUser
        }

        return $session
    }
}
