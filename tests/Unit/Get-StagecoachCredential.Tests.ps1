#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

BeforeAll {
    $modulePath = Join-Path -Path $PSScriptRoot -ChildPath '../../src/AzureStagecoach/AzureStagecoach.psd1'
    Import-Module $modulePath -Force
}

Describe 'Get-StagecoachCredential' {
    Context 'Key Vault resolution' {
        It 'resolves a secret from the stagecoach-secret tag' {
            Mock -ModuleName 'AzureStagecoach' -CommandName 'Resolve-KeyVaultSecret' -MockWith {
                [pscustomobject]@{
                    Source   = 'KeyVault'
                    SecretId = 'https://vault.azure.net/secrets/mysecret'
                    Password = 'SamplePassword123!'
                }
            }

            $cred = InModuleScope AzureStagecoach {
                $target = [StagecoachTarget]::new()
                $target.Name = 'srv-tagged'
                $target.Tags['stagecoach-secret'] = 'https://vault.azure.net/secrets/mysecret'
                $target.Tags['stagecoach-user'] = 'customAdmin'
                Get-StagecoachCredential -Target $target
            }

            $cred.Source | Should -Be 'KeyVaultTag'
            $cred.Username | Should -Be 'customAdmin'
            $cred.Password | Should -Be 'SamplePassword123!'
        }

        It 'resolves a domain-wide secret for Active Directory joined servers' {
            Mock -ModuleName 'AzureStagecoach' -CommandName 'Resolve-KeyVaultSecret' -MockWith {
                [pscustomobject]@{
                    Source     = 'KeyVault'
                    VaultName  = 'kv-hcs-vault-01'
                    SecretName = 'domain-corp-contoso-com-admin'
                    Password   = 'DomainSecretPass!'
                }
            }

            $cred = InModuleScope AzureStagecoach {
                $target = [StagecoachTarget]::new()
                $target.Name = 'srv-ad-01'
                $target.DomainName = 'corp.contoso.com'
                $target.DomainType = [StagecoachDomainType]::ActiveDirectory
                Get-StagecoachCredential -Target $target -VaultName 'kv-hcs-vault-01'
            }

            $cred.Source | Should -Be 'DomainKeyVault'
            $cred.Username | Should -Be 'corp.contoso.com\Administrator'
            $cred.Password | Should -Be 'DomainSecretPass!'
        }
    }
}
