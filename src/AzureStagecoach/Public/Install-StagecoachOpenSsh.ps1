#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Install-StagecoachOpenSsh {
    <#
    .SYNOPSIS
        Installs the Windows OpenSSH extension on an Azure VM or Arc-enabled server that needs it.
    .DESCRIPTION
        Windows machines need an SSH server before 'az ssh arc' / 'az ssh vm'
        (and Arc RDP-over-SSH) can reach them. This installs Microsoft's
        WindowsOpenSSH extension (publisher Microsoft.Azure.OpenSSH):
          - Arc server → az connectedmachine extension create
          - Azure VM   → az vm extension set
        This modifies Azure state, so it always asks for confirmation first.
        Linux machines normally ship with OpenSSH — nothing is installed there.
    .PARAMETER Name
        Machine name, looked up in the discovered inventory.
    .PARAMETER Target
        A StagecoachTarget from Get-StagecoachInventory.
    .EXAMPLE
        Install-StagecoachOpenSsh arc-fs-01
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High', DefaultParameterSetName = 'ByName')]
    param(
        [Parameter(ParameterSetName = 'ByName', Mandatory = $true, Position = 0)]
        [string]$Name,

        [Parameter(ParameterSetName = 'ByTarget', Mandatory = $true, ValueFromPipeline = $true)]
        $Target
    )

    process {
        if ($PSCmdlet.ParameterSetName -eq 'ByName') {
            $Target = Find-StagecoachTarget -Name $Name
        }

        if (-not $Target.IsWindows()) {
            Write-Information "[stagecoach] '$($Target.Name)' is not Windows — Linux machines ship with OpenSSH already; nothing to install." -InformationAction Continue
            return
        }

        if (-not $PSCmdlet.ShouldProcess($Target.Name, 'Install the WindowsOpenSSH extension (Microsoft.Azure.OpenSSH)')) {
            return
        }

        if ($Target.Kind -eq [StagecoachTargetKind]::ArcServer) {
            # 'az connectedmachine' is itself an az CLI extension; make sure it is present.
            $extensionList = Invoke-StagecoachAz -Arguments @('extension', 'list') -AsJson -AllowFailure
            $installed = @()
            if ($extensionList) {
                $installed = @($extensionList | ForEach-Object { Get-StagecoachProp -InputObject $_ -Name 'name' })
            }
            if ('connectedmachine' -notin $installed) {
                Write-Information "[stagecoach] Installing az CLI extension 'connectedmachine'..." -InformationAction Continue
                Invoke-StagecoachAz -Arguments @('extension', 'add', '--name', 'connectedmachine', '--only-show-errors') | Out-Null
            }

            Write-Information "[stagecoach] Installing WindowsOpenSSH on Arc server '$($Target.Name)' (this can take a few minutes)..." -InformationAction Continue
            Invoke-StagecoachAz -Arguments @(
                'connectedmachine', 'extension', 'create',
                '--machine-name', $Target.Name,
                '--resource-group', $Target.ResourceGroup,
                '--subscription', $Target.SubscriptionId,
                '--location', $Target.Location,
                '--name', 'WindowsOpenSSH',
                '--publisher', 'Microsoft.Azure.OpenSSH',
                '--type', 'WindowsOpenSSH'
            ) -AsJson | Out-Null
        }
        else {
            Write-Information "[stagecoach] Installing WindowsOpenSSH on Azure VM '$($Target.Name)' (this can take a few minutes)..." -InformationAction Continue
            Invoke-StagecoachAz -Arguments @(
                'vm', 'extension', 'set',
                '--resource-group', $Target.ResourceGroup,
                '--vm-name', $Target.Name,
                '--subscription', $Target.SubscriptionId,
                '--name', 'WindowsOpenSSH',
                '--publisher', 'Microsoft.Azure.OpenSSH'
            ) -AsJson | Out-Null
        }

        Write-Information "[stagecoach] OpenSSH extension installed on '$($Target.Name)'." -InformationAction Continue
    }
}
