#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Connect-StagecoachVM {
    <#
    .SYNOPSIS
        Launches an RDP or SSH connection to an Azure VM or Arc-enabled server.
    .DESCRIPTION
        Selects the optimal connection route (Bastion, Arc SSH Relay, or Direct),
        stages any resolved credentials, and spawns the background helper or native MSTSC client.
    .PARAMETER Target
        The StagecoachTarget object representing the VM.
    .PARAMETER LocalUser
        The local or domain username to use for authentication.
    .PARAMETER Rdp
        Boolean indicating whether to launch an RDP session (default: $true for Windows).
    .OUTPUTS
        StagecoachSession
    #>
    [CmdletBinding()]
    [OutputType([StagecoachSession])]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [StagecoachTarget]$Target,

        [Parameter(Mandatory = $false)]
        [string]$LocalUser,

        [Parameter(Mandatory = $false)]
        [bool]$Rdp = $true
    )

    process {
        $session = [StagecoachSession]::new()
        $session.TargetId = $Target.Id
        $session.TargetName = $Target.Name

        # Determine connection path based on target kind
        if ($Target.Kind -eq [StagecoachTargetKind]::ArcServer) {
            $session.Method = 'ArcSshRelay'
            $arguments = @('ssh', 'arc', '--resource-group', $Target.ResourceGroup, '--name', $Target.Name)

            # Inject username
            $userToUse = $LocalUser
            if (-not $userToUse) {
                if ($Target.DomainType -eq [StagecoachDomainType]::ActiveDirectory) {
                    $userToUse = "$($Target.DomainName)\Administrator"
                }
                else {
                    $userToUse = '.\Administrator'
                }
            }
            $arguments += @('--local-user', $userToUse)

            if ($Rdp) {
                $arguments += @('--rdp')
            }

            Write-Verbose "Launching Arc connection: az $($arguments -join ' ')"

            $procInfo = New-Object System.Diagnostics.ProcessStartInfo
            $procInfo.FileName = 'az'
            $procInfo.Arguments = $arguments -join ' '
            $procInfo.UseShellExecute = $true
            $procInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal

            $proc = [System.Diagnostics.Process]::Start($procInfo)
            if ($proc) {
                $session.HelperProcessId = $proc.Id
                $session.State = [StagecoachSessionState]::Active
            }
        }
        elseif ($Target.Kind -eq [StagecoachTargetKind]::AzureVM) {
            if ($Target.BastionHostId) {
                $session.Method = 'BastionNative'
                $bastionParts = $Target.BastionHostId.Split('/')
                $bastionName = $bastionParts[-1]
                $bastionRg = $bastionParts[4]

                $arguments = @('network', 'bastion', 'rdp', '--name', $bastionName, '--resource-group', $bastionRg, '--target-resource-id', $Target.Id)

                Write-Verbose "Launching Bastion connection: az $($arguments -join ' ')"

                $procInfo = New-Object System.Diagnostics.ProcessStartInfo
                $procInfo.FileName = 'az'
                $procInfo.Arguments = $arguments -join ' '
                $procInfo.UseShellExecute = $true

                $proc = [System.Diagnostics.Process]::Start($procInfo)
                if ($proc) {
                    $session.HelperProcessId = $proc.Id
                    $session.State = [StagecoachSessionState]::Active
                }
            }
            else {
                $session.Method = 'DirectMstsc'
                $ip = if ($Target.PublicIpAddress) { $Target.PublicIpAddress } else { $Target.PrivateIpAddress }
                if (-not $ip) {
                    throw "No IP address found for direct connection to '$($Target.Name)'."
                }

                Write-Verbose "Launching Direct MSTSC: mstsc /v:$ip"
                $proc = Start-Process -FilePath 'mstsc.exe' -ArgumentList "/v:$ip" -PassThru
                if ($proc) {
                    $session.ClientProcessId = $proc.Id
                    $session.State = [StagecoachSessionState]::Active
                }
            }
        }

        return $session
    }
}

