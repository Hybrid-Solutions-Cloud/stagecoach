# Module manifest for module 'AzureStagecoach'

@{
    RootModule = 'AzureStagecoach.psm1'
    ModuleVersion = '0.1.0'
    CompatiblePSEditions = @('Core')
    GUID = '9b671ef3-a63e-436f-8706-e789326e7b1a'
    Author = 'Kristopher Turner / Stagecoach Engineering'
    CompanyName = 'Hybrid Solutions Cloud'
    Copyright = '(c) 2026 Hybrid Solutions Cloud. All rights reserved.'
    Description = 'One-click Entra ID-authenticated RDP/SSH launcher for Azure VMs, Arc servers, and Bastion.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'Get-StagecoachInventory',
        'Get-StagecoachCredential',
        'Connect-StagecoachVM',
        'Start-Stagecoach'
    )
    CmdletsToExport = @()
    VariablesToExport = '*'
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('Azure', 'Arc', 'Bastion', 'RDP', 'SSH', 'Stagecoach')
            ProjectUri = 'https://labs.hybridsolutions.cloud/stagecoach/'
            LicenseUri = 'https://github.com/Hybrid-Solutions-Cloud/stagecoach/blob/main/LICENSE'
            ReleaseNotes = 'Initial release of Stagecoach: one-click RDP/SSH launcher for Azure VMs behind Bastion, Azure Arc-enabled servers (Domain and Workgroup), and direct-reachable VMs.'
        }
    }
}
