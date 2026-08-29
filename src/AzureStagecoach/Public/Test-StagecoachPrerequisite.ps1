#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-StagecoachPrerequisite {
    <#
    .SYNOPSIS
        Checks (and optionally installs) everything Stagecoach needs on this workstation.
    .DESCRIPTION
        Verifies the Azure CLI is installed, whether an account is signed in, and
        whether the required az CLI extensions are present:
          - resource-graph  (inventory discovery)
          - ssh             (az ssh vm / az ssh arc)
          - bastion         (az network bastion rdp / ssh / tunnel)
        With -InstallMissing, absent extensions are installed via 'az extension add'.
    .PARAMETER InstallMissing
        Install any missing az CLI extensions automatically.
    .OUTPUTS
        PSCustomObject with AzCliPresent, LoggedIn, Account, MissingExtensions, Ready.
    .EXAMPLE
        Test-StagecoachPrerequisite -InstallMissing
    #>
    [CmdletBinding()]
    param(
        [switch]$InstallMissing
    )

    $requiredExtensions = @('resource-graph', 'ssh', 'bastion')

    $result = [pscustomobject]@{
        AzCliPresent      = $false
        LoggedIn          = $false
        Account           = $null
        MissingExtensions = @()
        Ready             = $false
    }

    if (-not (Get-Command -Name 'az' -ErrorAction SilentlyContinue)) {
        Write-Warning "Azure CLI ('az') is not installed or not on PATH. Install it from https://aka.ms/azure-cli"
        return $result
    }
    $result.AzCliPresent = $true

    $account = Invoke-StagecoachAz -Arguments @('account', 'show') -AsJson -AllowFailure
    if ($account) {
        $result.LoggedIn = $true
        $user = Get-StagecoachProp -InputObject $account -Name 'user'
        $result.Account = [pscustomobject]@{
            User         = Get-StagecoachProp -InputObject $user -Name 'name'
            TenantId     = Get-StagecoachProp -InputObject $account -Name 'tenantId'
            Subscription = Get-StagecoachProp -InputObject $account -Name 'name'
        }
    }

    $installed = @()
    $extensionList = Invoke-StagecoachAz -Arguments @('extension', 'list') -AsJson -AllowFailure
    if ($extensionList) {
        $installed = @($extensionList | ForEach-Object { Get-StagecoachProp -InputObject $_ -Name 'name' })
    }

    $missing = @($requiredExtensions | Where-Object { $_ -notin $installed })

    if ($missing.Count -gt 0 -and $InstallMissing) {
        foreach ($ext in $missing) {
            Write-Information "[stagecoach] Installing az CLI extension '$ext'..." -InformationAction Continue
            Invoke-StagecoachAz -Arguments @('extension', 'add', '--name', $ext, '--only-show-errors') | Out-Null
        }
        $missing = @()
    }

    $result.MissingExtensions = $missing
    $result.Ready = $result.AzCliPresent -and $result.LoggedIn -and ($missing.Count -eq 0)
    return $result
}
