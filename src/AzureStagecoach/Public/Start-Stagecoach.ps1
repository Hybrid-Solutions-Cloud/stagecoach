#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Start-Stagecoach {
    <#
    .SYNOPSIS
        The Stagecoach front door: sign in once, pick a machine, get a session.
    .DESCRIPTION
        Interactive console launcher. On start it checks prerequisites (Azure
        CLI + extensions, offering to install anything missing), signs you in
        with Entra ID if needed, and loads your machines. Saved logins from
        previous sessions are listed first for one-keystroke reconnects.
    .PARAMETER Refresh
        Re-discover the inventory from Azure before showing the menu.
    .EXAMPLE
        Start-Stagecoach
    #>
    [CmdletBinding()]
    [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
        Justification = 'Interactive console UI — host output is the product.')]
    param(
        [switch]$Refresh
    )

    Write-Host ''
    Write-Host '  ═══════════════════════════════════════════' -ForegroundColor DarkYellow
    Write-Host '   STAGECOACH — one login, every VM, one pick ' -ForegroundColor Yellow
    Write-Host '  ═══════════════════════════════════════════' -ForegroundColor DarkYellow
    Write-Host ''

    # --- 1. Prerequisites -------------------------------------------------
    $prereq = Test-StagecoachPrerequisite
    if (-not $prereq.AzCliPresent) {
        Write-Host '  Azure CLI is not installed. Get it from https://aka.ms/azure-cli then run Start-Stagecoach again.' -ForegroundColor Red
        return
    }
    if (@($prereq.MissingExtensions).Count -gt 0) {
        Write-Host "  Missing az CLI extensions: $($prereq.MissingExtensions -join ', ')" -ForegroundColor Yellow
        $answer = Read-Host '  Install them now? [Y/n]'
        if ($answer -notmatch '^[nN]') {
            $prereq = Test-StagecoachPrerequisite -InstallMissing
        }
        else {
            Write-Host '  Stagecoach needs those extensions to discover and connect. Exiting.' -ForegroundColor Red
            return
        }
    }

    # --- 2. Sign-in -------------------------------------------------------
    if (-not $prereq.LoggedIn) {
        Write-Host '  No Azure session found — opening Entra ID sign-in...' -ForegroundColor Cyan
        Connect-StagecoachAccount | Out-Null
    }
    else {
        Write-Host "  Signed in as $($prereq.Account.User)" -ForegroundColor Green
    }

    # --- 3. Inventory -----------------------------------------------------
    $inventory = @()
    if (-not $Refresh) {
        $inventory = @(Get-StagecoachInventory -Cached)
    }
    if ($inventory.Count -eq 0 -or $Refresh) {
        Write-Host '  Discovering your machines (Azure VMs, Bastion hosts, Arc servers)...' -ForegroundColor Cyan
        $inventory = @(Get-StagecoachInventory)
    }
    Write-Host "  $($inventory.Count) machine(s) available." -ForegroundColor Green
    Write-Host ''

    # --- 4. Menu loop -----------------------------------------------------
    while ($true) {
        $saved = @(Get-StagecoachSavedConnection | Select-Object -First 9)

        if ($saved.Count -gt 0) {
            Write-Host '  Recent connections:' -ForegroundColor Yellow
            for ($i = 0; $i -lt $saved.Count; $i++) {
                $entry = $saved[$i]
                $user = Get-StagecoachProp -InputObject $entry -Name 'Username' -Default ''
                $userLabel = if ($user) { " as $user" } else { ' as Entra ID' }
                Write-Host ("   [{0}] {1}  ({2}, {3}{4})" -f ($i + 1),
                    (Get-StagecoachProp -InputObject $entry -Name 'TargetName'),
                    (Get-StagecoachProp -InputObject $entry -Name 'Kind'),
                    (Get-StagecoachProp -InputObject $entry -Name 'Method'), $userLabel)
            }
            Write-Host ''
        }

        Write-Host '  [1-9] reconnect   [L] list machines   [R] refresh from Azure   [A] add account   [Q] quit' -ForegroundColor DarkGray
        Write-Host '  ...or type part of a machine name to search.' -ForegroundColor DarkGray
        $choice = Read-Host '  stagecoach'
        if ([string]::IsNullOrWhiteSpace($choice)) { continue }

        switch -Regex ($choice.Trim()) {
            '^[Qq]$' { Write-Host '  Happy trails.' -ForegroundColor DarkYellow; return }
            '^[Rr]$' {
                Write-Host '  Refreshing inventory...' -ForegroundColor Cyan
                $inventory = @(Get-StagecoachInventory)
                Write-Host "  $($inventory.Count) machine(s)." -ForegroundColor Green
                continue
            }
            '^[Aa]$' {
                Connect-StagecoachAccount | Format-Table -AutoSize | Out-Host
                Write-Host '  Refreshing inventory with the new account...' -ForegroundColor Cyan
                $inventory = @(Get-StagecoachInventory)
                continue
            }
            '^[Ll]$' {
                Show-StagecoachPicker -Inventory $inventory
                continue
            }
            '^[1-9]$' {
                $index = [int]$choice - 1
                if ($index -ge $saved.Count) { Write-Host '  No such entry.' -ForegroundColor Red; continue }
                $entry = $saved[$index]
                $targetId = Get-StagecoachProp -InputObject $entry -Name 'TargetId'
                $target = $inventory | Where-Object { $_.Id -eq $targetId } | Select-Object -First 1
                if (-not $target) {
                    Write-Host '  That machine is no longer in the inventory — refresh with [R].' -ForegroundColor Red
                    continue
                }
                $connectArgs = @{
                    Target = $target
                    Method = (Get-StagecoachProp -InputObject $entry -Name 'Method' -Default 'Auto')
                }
                $user = Get-StagecoachProp -InputObject $entry -Name 'Username' -Default ''
                if ($user) { $connectArgs.LocalUser = $user }
                Connect-StagecoachVM @connectArgs | Out-Null
                continue
            }
            default {
                Show-StagecoachPicker -Inventory $inventory -Filter $choice.Trim()
                continue
            }
        }
    }
}

function Show-StagecoachPicker {
    <#
    .SYNOPSIS
        Interactive machine picker: filter, choose, set method/user, connect.
    #>
    [CmdletBinding()]
    [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
        Justification = 'Interactive console UI — host output is the product.')]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [StagecoachTarget[]]$Inventory,

        [Parameter(Mandatory = $false)]
        [string]$Filter
    )

    $list = @($Inventory)
    if ($Filter) {
        $list = @($Inventory | Where-Object { $_.Name -like "*$Filter*" })
    }
    if ($list.Count -eq 0) {
        Write-Host "  No machines match '$Filter'." -ForegroundColor Red
        return
    }

    Write-Host ''
    for ($i = 0; $i -lt $list.Count; $i++) {
        $t = $list[$i]
        $route = if ($t.Kind -eq [StagecoachTargetKind]::ArcServer) { 'Arc relay' }
        elseif ($t.HasBastion()) { "Bastion: $($t.BastionName)" }
        elseif ($t.PublicIpAddress -or $t.PrivateIpAddress) { 'Direct' }
        else { 'No route!' }
        $os = if ($t.OsType) { $t.OsType } else { '?' }
        Write-Host ("   [{0,3}] {1,-30} {2,-10} {3,-9} {4}" -f ($i + 1), $t.Name, $t.Kind, $os, $route)
    }
    Write-Host ''

    $pick = Read-Host "  Machine number (1-$($list.Count)), or Enter to go back"
    if ([string]::IsNullOrWhiteSpace($pick) -or $pick -notmatch '^\d+$') { return }
    $index = [int]$pick - 1
    if ($index -lt 0 -or $index -ge $list.Count) { Write-Host '  No such entry.' -ForegroundColor Red; return }
    $target = $list[$index]

    $defaultMethod = if ($target.IsWindows()) { 'Rdp' } else { 'Ssh' }
    $methodChoice = Read-Host "  Method: [Enter]=$defaultMethod, or rdp / ssh / tunnel"
    $method = switch -Regex ($methodChoice.Trim()) {
        '^[Rr]' { 'Rdp'; break }
        '^[Ss]' { 'Ssh'; break }
        '^[Tt]' { 'Tunnel'; break }
        default { $defaultMethod }
    }

    $userPrompt = if ($target.AdminUsername) {
        "  Username: [Enter]=Entra ID, or a local/domain user (VM admin is '$($target.AdminUsername)')"
    }
    else {
        '  Username: [Enter]=Entra ID, or a local/domain user'
    }
    $user = Read-Host $userPrompt

    $connectArgs = @{ Target = $target; Method = $method }
    if (-not [string]::IsNullOrWhiteSpace($user)) { $connectArgs.LocalUser = $user.Trim() }

    try {
        Connect-StagecoachVM @connectArgs | Out-Null
    }
    catch {
        Write-Host "  Connection failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}
