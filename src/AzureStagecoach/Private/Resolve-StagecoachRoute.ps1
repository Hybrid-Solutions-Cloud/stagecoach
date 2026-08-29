#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-StagecoachRoute {
    <#
    .SYNOPSIS
        Decides how to reach a target: which tool, which arguments, which launch style.
    .DESCRIPTION
        Pure decision logic (no side effects) so it can be unit tested:
          Arc server        → az ssh arc [--rdp]
          Azure VM +Bastion → az network bastion rdp | ssh | tunnel
          Azure VM direct   → mstsc /v:<ip> | az ssh vm
        Auto method: Windows targets get RDP, everything else SSH. RDP routes
        that need a Windows client fall back to a Bastion tunnel elsewhere.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [StagecoachTarget]$Target,

        [ValidateSet('Auto', 'Rdp', 'Ssh', 'Tunnel')]
        [string]$Method = 'Auto',

        [Parameter(Mandatory = $false)]
        [string]$LocalUser,

        [int]$TunnelPort = 0,

        # Overridable for tests; defaults to the real client OS.
        [bool]$WindowsClient = $IsWindows
    )

    $resolved = $Method
    if ($resolved -eq 'Auto') {
        $resolved = if ($Target.IsWindows()) { 'Rdp' } else { 'Ssh' }
    }

    $route = [pscustomobject]@{
        Method      = $resolved
        Tool        = 'az'
        Arguments   = @()
        Interactive = $false   # true → run in this console (SSH shells); false → detached window
        LocalPort   = 0
        Notes       = @()
    }

    if ($Target.Kind -eq [StagecoachTargetKind]::ArcServer) {
        if ($resolved -eq 'Tunnel') {
            throw "Tunnel connections require an Azure Bastion host; '$($Target.Name)' is an Arc-enabled server. Use -Method Ssh or Rdp."
        }

        $route.Arguments = @('ssh', 'arc', '--resource-group', $Target.ResourceGroup, '--name', $Target.Name, '--subscription', $Target.SubscriptionId)
        if ($LocalUser) {
            $route.Arguments += @('--local-user', $LocalUser)
        }
        if ($resolved -eq 'Rdp') {
            if (-not $WindowsClient) {
                throw "Arc RDP ('az ssh arc --rdp') needs a Windows client. Use -Method Ssh from this machine."
            }
            $route.Arguments += @('--rdp')
            $route.Notes += 'RDP over the Arc SSH relay — a local RDP client window will open once the tunnel is up.'
        }
        else {
            $route.Interactive = $true
            if (-not $LocalUser) {
                $route.Notes += 'Signing in with your Entra ID identity. Pass -LocalUser for a local/domain account.'
            }
        }
        return $route
    }

    # --- Azure VM ---
    if ($Target.HasBastion()) {
        $bastionArgs = @('network', 'bastion')
        $commonArgs = @('--name', $Target.BastionName, '--resource-group', $Target.BastionResourceGroup,
            '--target-resource-id', $Target.Id, '--subscription', $Target.SubscriptionId)

        if (-not $Target.BastionSameVNet) {
            $route.Notes += "Bastion '$($Target.BastionName)' is in a different VNet — this works only if the VNets are peered and the Bastion SKU supports it."
        }

        if ($resolved -eq 'Rdp' -and -not $WindowsClient) {
            $route.Notes += 'Bastion native RDP needs a Windows client; switching to a tunnel — point your RDP client at the local port.'
            $resolved = 'Tunnel'
            $route.Method = 'Tunnel'
        }

        switch ($resolved) {
            'Rdp' {
                $route.Arguments = $bastionArgs + @('rdp') + $commonArgs
                $route.Notes += 'An mstsc window opens once Bastion accepts the connection.'
            }
            'Ssh' {
                $sshArgs = $bastionArgs + @('ssh') + $commonArgs
                if ($LocalUser) {
                    $sshArgs += @('--auth-type', 'password', '--username', $LocalUser)
                }
                else {
                    $sshArgs += @('--auth-type', 'AAD')
                    $route.Notes += 'Entra ID SSH works on Linux targets with the AADSSHLoginForLinux extension; pass -LocalUser otherwise.'
                }
                $route.Arguments = $sshArgs
                $route.Interactive = $true
            }
            'Tunnel' {
                $resourcePort = if ($Target.IsWindows()) { 3389 } else { 22 }
                $localPort = if ($TunnelPort -gt 0) { $TunnelPort } else { Get-Random -Minimum 50000 -Maximum 50999 }
                $route.LocalPort = $localPort
                $route.Arguments = $bastionArgs + @('tunnel') + $commonArgs + @('--resource-port', "$resourcePort", '--port', "$localPort")
                $route.Notes += "Tunnel opens on localhost:$localPort → $($Target.Name):$resourcePort. Connect your RDP/SSH client to 127.0.0.1:$localPort; closing the tunnel window ends the session."
            }
        }
        return $route
    }

    # --- Azure VM, no Bastion: direct reachability ---
    switch ($resolved) {
        'Rdp' {
            $ip = if ($Target.PublicIpAddress) { $Target.PublicIpAddress } else { $Target.PrivateIpAddress }
            if (-not $ip) {
                throw "No Bastion host or IP address found for '$($Target.Name)'. Deploy an Azure Bastion in its VNet (or subscription) and refresh the inventory."
            }
            if (-not $WindowsClient) {
                throw "Direct RDP uses mstsc, which needs a Windows client. Connect your own RDP client to $ip instead."
            }
            $route.Tool = 'mstsc.exe'
            $route.Arguments = @("/v:$ip")
            $route.Notes += "Direct RDP to $ip (no Bastion found for this VM)."
        }
        'Ssh' {
            $route.Arguments = @('ssh', 'vm', '--resource-group', $Target.ResourceGroup, '--name', $Target.Name, '--subscription', $Target.SubscriptionId)
            if ($LocalUser) {
                $route.Arguments += @('--local-user', $LocalUser)
            }
            else {
                $route.Notes += 'Signing in with your Entra ID identity (AADSSHLoginForLinux). Pass -LocalUser for a local account.'
            }
            $route.Interactive = $true
        }
        'Tunnel' {
            throw "Tunnel connections require an Azure Bastion host and none was found for '$($Target.Name)'."
        }
    }
    return $route
}
