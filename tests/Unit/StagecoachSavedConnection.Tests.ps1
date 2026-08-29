#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

BeforeAll {
    $modulePath = Join-Path -Path $PSScriptRoot -ChildPath '../../src/AzureStagecoach/AzureStagecoach.psd1'
    Import-Module $modulePath -Force
}

Describe 'Saved connections (previous logins)' {
    BeforeEach {
        $script:storeDir = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('n'))
        New-Item -Path $script:storeDir -ItemType Directory | Out-Null
        Mock -ModuleName 'AzureStagecoach' -CommandName 'Get-StagecoachHome' -MockWith { $script:storeDir }
    }

    It 'round-trips a saved login and never stores a password field' {
        InModuleScope AzureStagecoach {
            $t = [StagecoachTarget]::new()
            $t.Id = '/subs/s/rg/vm-a'; $t.Name = 'vm-a'; $t.ResourceGroup = 'rg'; $t.Kind = 'AzureVM'; $t.OsType = 'Windows'
            Save-StagecoachConnectionProfile -Target $t -Method 'Rdp' -Username 'CORP\kt'
        }

        $saved = @(Get-StagecoachSavedConnection)
        $saved.Count | Should -Be 1
        $saved[0].TargetName | Should -Be 'vm-a'
        $saved[0].Method | Should -Be 'Rdp'
        $saved[0].Username | Should -Be 'CORP\kt'
        $saved[0].PSObject.Properties.Name | Should -Not -Contain 'Password'
    }

    It 'orders saved logins most recently used first and counts uses' {
        InModuleScope AzureStagecoach {
            $a = [StagecoachTarget]::new(); $a.Id = '/subs/s/rg/vm-a'; $a.Name = 'vm-a'; $a.ResourceGroup = 'rg'; $a.Kind = 'AzureVM'; $a.OsType = 'Windows'
            $b = [StagecoachTarget]::new(); $b.Id = '/subs/s/rg/vm-b'; $b.Name = 'vm-b'; $b.ResourceGroup = 'rg'; $b.Kind = 'AzureVM'; $b.OsType = 'Linux'
            Save-StagecoachConnectionProfile -Target $a -Method 'Rdp' -Username 'u1'
            Start-Sleep -Milliseconds 20
            Save-StagecoachConnectionProfile -Target $b -Method 'Ssh' -Username ''
            Start-Sleep -Milliseconds 20
            Save-StagecoachConnectionProfile -Target $a -Method 'Rdp' -Username 'u1'
        }

        $saved = @(Get-StagecoachSavedConnection)
        $saved.Count | Should -Be 2
        $saved[0].TargetName | Should -Be 'vm-a'
        $saved[0].UseCount | Should -Be 2
        $saved[1].TargetName | Should -Be 'vm-b'
    }

    It 'removes a saved login by name' {
        InModuleScope AzureStagecoach {
            $a = [StagecoachTarget]::new(); $a.Id = '/subs/s/rg/vm-a'; $a.Name = 'vm-a'; $a.ResourceGroup = 'rg'; $a.Kind = 'AzureVM'; $a.OsType = 'Windows'
            Save-StagecoachConnectionProfile -Target $a -Method 'Rdp' -Username 'u1'
        }

        Remove-StagecoachSavedConnection -Name 'vm-a' -Confirm:$false
        @(Get-StagecoachSavedConnection).Count | Should -Be 0
    }
}

Describe 'Connect-StagecoachVM (launch behavior)' {
    BeforeEach {
        $script:storeDir = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('n'))
        New-Item -Path $script:storeDir -ItemType Directory | Out-Null
        Mock -ModuleName 'AzureStagecoach' -CommandName 'Get-StagecoachHome' -MockWith { $script:storeDir }
        Mock -ModuleName 'AzureStagecoach' -CommandName 'Start-Process' -MockWith { [pscustomobject]@{ Id = 4242 } }
    }

    It 'launches a Bastion tunnel detached and saves the login' {
        $session = InModuleScope AzureStagecoach {
            $t = [StagecoachTarget]::new()
            $t.Kind = 'AzureVM'; $t.Name = 'vm-app-01'; $t.Id = '/subs/s/rg/vm-app-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
            $t.OsType = 'Windows'; $t.BastionName = 'bas-hub'; $t.BastionResourceGroup = 'rg-net'; $t.BastionSameVNet = $true
            Connect-StagecoachVM -Target $t -Method Tunnel -TunnelPort 55200
        }

        "$($session.State)" | Should -Be 'Active'
        $session.HelperProcessId | Should -Be 4242
        $session.Method | Should -Be 'Tunnel'
        Should -Invoke -ModuleName 'AzureStagecoach' -CommandName 'Start-Process' -Times 1

        $saved = @(Get-StagecoachSavedConnection)
        $saved.Count | Should -Be 1
        $saved[0].TargetName | Should -Be 'vm-app-01'
        $saved[0].Method | Should -Be 'Tunnel'
    }

    It 'honors -NoSave' {
        InModuleScope AzureStagecoach {
            $t = [StagecoachTarget]::new()
            $t.Kind = 'AzureVM'; $t.Name = 'vm-app-01'; $t.Id = '/subs/s/rg/vm-app-01'; $t.ResourceGroup = 'rg'; $t.SubscriptionId = 'sub1'
            $t.OsType = 'Windows'; $t.BastionName = 'bas-hub'; $t.BastionResourceGroup = 'rg-net'
            Connect-StagecoachVM -Target $t -Method Tunnel -TunnelPort 55201 -NoSave
        } | Out-Null

        @(Get-StagecoachSavedConnection).Count | Should -Be 0
    }
}
