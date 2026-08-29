#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-StagecoachCredential {
    <#
    .SYNOPSIS
        Resolves credentials for a given target VM or Arc server across LAPS, Domain, and Key Vault.
    .DESCRIPTION
        Follows the Stagecoach multi-tier credential resolution hierarchy:
        1. Entra Windows LAPS via Microsoft Graph (if device ID provided).
        2. Domain User mappings / Domain Key Vault secrets (if Domain-joined).
        3. Key Vault per-VM convention (vm-<name>-localadmin or stagecoach-secret tag).
    .PARAMETER Target
        The StagecoachTarget object or VM name.
    .PARAMETER VaultName
        The Key Vault name to search (default: kv-hcs-vault-01).
    .OUTPUTS
        PSCustomObject with Source, Username, Password
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        $Target,

        [Parameter(Mandatory = $false)]
        [string]$VaultName = 'kv-hcs-vault-01'
    )

    process {
        Write-Verbose "Resolving credentials for target '$($Target.Name)' (DomainType: $($Target.DomainType))"

        # Tier 1: Check Resource Tag for explicit Key Vault Secret ID
        if ($Target.Tags -and $Target.Tags.ContainsKey('stagecoach-secret')) {
            $secretId = $Target.Tags['stagecoach-secret']
            Write-Verbose "Attempting secret resolution from tag: $secretId"
            $resolved = Resolve-KeyVaultSecret -SecretId $secretId
            if ($resolved) {
                $user = if ($Target.Tags.ContainsKey('stagecoach-user')) { $Target.Tags['stagecoach-user'] } else { '.\Administrator' }
                return [pscustomobject]@{
                    Source   = 'KeyVaultTag'
                    Username = $user
                    Password = $resolved.Password
                }
            }
        }

        # Tier 2: Check Entra LAPS if Target has Device ID or Entra tag
        if ($Target.Tags -and $Target.Tags.ContainsKey('deviceId')) {
            $laps = Resolve-LapsPassword -DeviceId $Target.Tags['deviceId']
            if ($laps) {
                return $laps
            }
        }

        # Tier 3: Active Directory Domain Machine
        if ($Target.DomainType -eq [StagecoachDomainType]::ActiveDirectory) {
            $domainSecretName = "domain-$($Target.DomainName.Replace('.', '-'))-admin"
            Write-Verbose "Checking for domain-wide secret '$domainSecretName' in vault '$VaultName'"
            $domainResolved = Resolve-KeyVaultSecret -VaultName $VaultName -SecretName $domainSecretName
            if ($domainResolved) {
                return [pscustomobject]@{
                    Source   = 'DomainKeyVault'
                    Username = "$($Target.DomainName)\Administrator"
                    Password = $domainResolved.Password
                }
            }
        }

        # Tier 4: Standard Per-VM Key Vault Convention
        $vmSecretName = "vm-$($Target.Name.ToLowerInvariant())-localadmin"
        Write-Verbose "Checking per-VM secret '$vmSecretName' in vault '$VaultName'"
        $vmResolved = Resolve-KeyVaultSecret -VaultName $VaultName -SecretName $vmSecretName
        if ($vmResolved) {
            return [pscustomobject]@{
                Source   = 'KeyVaultConvention'
                Username = '.\Administrator'
                Password = $vmResolved.Password
            }
        }

        Write-Verbose "No automatic credentials found for target '$($Target.Name)'."
        return $null
    }
}

