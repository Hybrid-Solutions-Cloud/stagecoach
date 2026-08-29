#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

BeforeAll {
    $modulePath = Join-Path -Path $PSScriptRoot -ChildPath '../../src/AzureStagecoach/AzureStagecoach.psd1'
    Import-Module $modulePath -Force
}

Describe 'Resolve-StagecoachRoute' {

    Context 'Arc-enabled servers' {
        It 'routes a Windows Arc server to az ssh arc --rdp by default (Windows client)' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'ArcServer'; $t.Name = 'arc-dc-01'; $t.ResourceGroup = 'rg-hyb'; $t.SubscriptionId = 'sub1'; $t.OsType = 'Windows'
                $r = Resolve-StagecoachRoute -Target $t -Method Auto -WindowsClient $true
                $r.Method | Should -Be 'Rdp'
                $r.Tool | Should -Be 'az'
                ($r.Arguments -join ' ') | Should -Match '^ssh arc '
                $r.Arguments | Should -Contain '--rdp'
                $r.Interactive | Should -BeFalse
            }
        }

        It 'routes a Linux Arc server to an interactive az ssh arc session' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'ArcServer'; $t.Name = 'arc-web-01'; $t.ResourceGroup = 'rg-hyb'; $t.SubscriptionId = 'sub1'; $t.OsType = 'Linux'
                $r = Resolve-StagecoachRoute -Target $t -Method Auto -WindowsClient $true
                $r.Method | Should -Be 'Ssh'
                $r.Arguments | Should -Not -Contain '--rdp'
                $r.Interactive | Should -BeTrue
            }
        }

        It 'passes -LocalUser through as --local-user' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'ArcServer'; $t.Name = 'arc-fs-01'; $t.ResourceGroup = 'rg-hyb'; $t.SubscriptionId = 'sub1'; $t.OsType = 'Windows'
                $r = Resolve-StagecoachRoute -Target $t -Method Rdp -LocalUser 'CORP\admin' -WindowsClient $true
                $localUserIndex = [array]::IndexOf($r.Arguments, '--local-user')
                $localUserIndex | Should -BeGreaterThan -1
                $r.Arguments[$localUserIndex + 1] | Should -Be 'CORP\admin'
            }
        }

        It 'refuses Arc RDP from a non-Windows client' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'ArcServer'; $t.Name = 'arc-dc-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'; $t.OsType = 'Windows'
                { Resolve-StagecoachRoute -Target $t -Method Rdp -WindowsClient $false } | Should -Throw '*Windows client*'
            }
        }

        It 'refuses Tunnel for Arc servers' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'ArcServer'; $t.Name = 'arc-dc-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'; $t.OsType = 'Windows'
                { Resolve-StagecoachRoute -Target $t -Method Tunnel -WindowsClient $true } | Should -Throw '*Bastion*'
            }
        }
    }

    Context 'Azure VMs behind Bastion' {
        It 'routes a Windows VM with a Bastion host to az network bastion rdp' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'AzureVM'; $t.Name = 'vm-app-01'; $t.Id = '/subs/s/rg/vm-app-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
                $t.OsType = 'Windows'; $t.BastionName = 'bas-hub'; $t.BastionResourceGroup = 'rg-net'; $t.BastionSameVNet = $true
                $r = Resolve-StagecoachRoute -Target $t -Method Auto -WindowsClient $true
                $r.Method | Should -Be 'Rdp'
                ($r.Arguments -join ' ') | Should -Match '^network bastion rdp '
                $r.Arguments | Should -Contain 'bas-hub'
                $r.Arguments | Should -Contain '--target-resource-id'
            }
        }

        It 'uses Entra ID (AAD) auth for Bastion SSH when no local user is given' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'AzureVM'; $t.Name = 'vm-lnx-01'; $t.Id = '/subs/s/rg/vm-lnx-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
                $t.OsType = 'Linux'; $t.BastionName = 'bas-hub'; $t.BastionResourceGroup = 'rg-net'; $t.BastionSameVNet = $true
                $r = Resolve-StagecoachRoute -Target $t -Method Auto -WindowsClient $true
                $r.Method | Should -Be 'Ssh'
                $r.Interactive | Should -BeTrue
                ($r.Arguments -join ' ') | Should -Match '--auth-type AAD'
            }
        }

        It 'switches Bastion SSH to password auth when a local user is given' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'AzureVM'; $t.Name = 'vm-lnx-01'; $t.Id = '/subs/s/rg/vm-lnx-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
                $t.OsType = 'Linux'; $t.BastionName = 'bas-hub'; $t.BastionResourceGroup = 'rg-net'
                $r = Resolve-StagecoachRoute -Target $t -Method Ssh -LocalUser 'opsadmin' -WindowsClient $true
                ($r.Arguments -join ' ') | Should -Match '--auth-type password'
                ($r.Arguments -join ' ') | Should -Match '--username opsadmin'
            }
        }

        It 'falls back from RDP to a 3389 tunnel on a non-Windows client' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'AzureVM'; $t.Name = 'vm-app-01'; $t.Id = '/subs/s/rg/vm-app-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
                $t.OsType = 'Windows'; $t.BastionName = 'bas-hub'; $t.BastionResourceGroup = 'rg-net'
                $r = Resolve-StagecoachRoute -Target $t -Method Rdp -TunnelPort 55123 -WindowsClient $false
                $r.Method | Should -Be 'Tunnel'
                ($r.Arguments -join ' ') | Should -Match '^network bastion tunnel '
                ($r.Arguments -join ' ') | Should -Match '--resource-port 3389'
                ($r.Arguments -join ' ') | Should -Match '--port 55123'
            }
        }

        It 'tunnels port 22 for Linux targets' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'AzureVM'; $t.Name = 'vm-lnx-01'; $t.Id = '/subs/s/rg/vm-lnx-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
                $t.OsType = 'Linux'; $t.BastionName = 'bas-hub'; $t.BastionResourceGroup = 'rg-net'
                $r = Resolve-StagecoachRoute -Target $t -Method Tunnel -TunnelPort 55124 -WindowsClient $true
                ($r.Arguments -join ' ') | Should -Match '--resource-port 22'
            }
        }
    }

    Context 'Azure VMs without Bastion (direct)' {
        It 'uses mstsc for direct RDP when an IP exists' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'AzureVM'; $t.Name = 'vm-edge-01'; $t.Id = '/subs/s/rg/vm-edge-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
                $t.OsType = 'Windows'; $t.PublicIpAddress = '20.1.2.3'
                $r = Resolve-StagecoachRoute -Target $t -Method Rdp -WindowsClient $true
                $r.Tool | Should -Be 'mstsc.exe'
                $r.Arguments | Should -Be @('/v:20.1.2.3')
            }
        }

        It 'throws a helpful error when there is no Bastion and no IP' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'AzureVM'; $t.Name = 'vm-iso-01'; $t.Id = '/subs/s/rg/vm-iso-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
                $t.OsType = 'Windows'
                { Resolve-StagecoachRoute -Target $t -Method Rdp -WindowsClient $true } | Should -Throw '*Bastion*'
            }
        }

        It 'uses az ssh vm for direct SSH' {
            InModuleScope AzureStagecoach {
                $t = [StagecoachTarget]::new()
                $t.Kind = 'AzureVM'; $t.Name = 'vm-lnx-02'; $t.Id = '/subs/s/rg/vm-lnx-02'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
                $t.OsType = 'Linux'; $t.PrivateIpAddress = '10.0.0.5'
                $r = Resolve-StagecoachRoute -Target $t -Method Ssh -WindowsClient $true
                ($r.Arguments -join ' ') | Should -Match '^ssh vm '
                $r.Interactive | Should -BeTrue
            }
        }
    }
}
