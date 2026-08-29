#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Load Classes
$classFiles = Get-ChildItem -Path (Join-Path -Path $PSScriptRoot -ChildPath 'Classes') -Filter '*.ps1'
foreach ($file in $classFiles) {
    . $file.FullName
}

# Load Private Functions
$privateFiles = Get-ChildItem -Path (Join-Path -Path $PSScriptRoot -ChildPath 'Private') -Filter '*.ps1'
foreach ($file in $privateFiles) {
    . $file.FullName
}

# Load Public Functions
$publicFiles = Get-ChildItem -Path (Join-Path -Path $PSScriptRoot -ChildPath 'Public') -Filter '*.ps1'
foreach ($file in $publicFiles) {
    . $file.FullName
}

# Export Public Functions
Export-ModuleMember -Function @(
    'Start-Stagecoach',
    'Connect-StagecoachVM',
    'Connect-StagecoachAccount',
    'Get-StagecoachInventory',
    'Get-StagecoachSavedConnection',
    'Remove-StagecoachSavedConnection',
    'Test-StagecoachPrerequisite',
    'Enable-StagecoachArcSsh',
    'Install-StagecoachOpenSsh',
    'Get-StagecoachCredential'
)
