#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-KeyVaultSecret {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$SecretId,

        [Parameter(Mandatory = $false)]
        [string]$VaultName = 'kv-hcs-vault-01',

        [Parameter(Mandatory = $false)]
        [string]$SecretName
    )

    try {
        if ($SecretId) {
            $secretValue = az keyvault secret show --id $SecretId --query value -o tsv 2>$null
            if ($LASTEXITCODE -eq 0 -and $secretValue) {
                return [pscustomobject]@{
                    Source   = 'KeyVault'
                    SecretId = $SecretId
                    Password = $secretValue.Trim()
                }
            }
        }
        elseif ($VaultName -and $SecretName) {
            $secretValue = az keyvault secret show --vault-name $VaultName --name $SecretName --query value -o tsv 2>$null
            if ($LASTEXITCODE -eq 0 -and $secretValue) {
                return [pscustomobject]@{
                    Source     = 'KeyVault'
                    VaultName  = $VaultName
                    SecretName = $SecretName
                    Password   = $secretValue.Trim()
                }
            }
        }
    }
    catch {
        Write-Verbose "Key Vault secret retrieval failed: $_"
    }

    return $null
}

