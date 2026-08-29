# Module manifest for module 'AzureStagecoach'

@{
    RootModule = 'AzureStagecoach.psm1'
    ModuleVersion = '0.2.0'
    CompatiblePSEditions = @('Core')
    GUID = '9b671ef3-a63e-436f-8706-e789326e7b1a'
    Author = 'Kristopher Turner / Stagecoach Engineering'
    CompanyName = 'Hybrid Solutions Cloud'
    Copyright = '(c) 2026 Hybrid Solutions Cloud. All rights reserved.'
    Description = 'One-login Entra ID RDP/SSH launcher for Azure VMs behind Bastion, Azure Arc-enabled servers, and direct-reachable VMs — with saved logins and automatic az extension setup.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'Start-Stagecoach',
        'Connect-StagecoachVM',
        'Connect-StagecoachAccount',
        'Get-StagecoachInventory',
        'Get-StagecoachSavedConnection',
        'Remove-StagecoachSavedConnection',
        'Test-StagecoachPrerequisite',
        'Enable-StagecoachArcSsh',
        'Install-StagecoachOpenSsh',
        'Get-StagecoachSession',
        'Stop-StagecoachSession',
        'Get-StagecoachCredential'
    )
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('Azure', 'Arc', 'Bastion', 'RDP', 'SSH', 'Stagecoach')
            ProjectUri = 'https://labs.hybridsolutions.cloud/stagecoach/'
            LicenseUri = 'https://github.com/Hybrid-Solutions-Cloud/stagecoach/blob/main/LICENSE'
            ReleaseNotes = 'v0.2.0 — console-first rebuild: working Bastion RDP/SSH/tunnel routing, Arc SSH/RDP, saved logins, az extension bootstrap, Arc SSH enablement, and Windows OpenSSH extension install.'
        }
    }
}
