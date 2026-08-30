#Requires -Version 7.0

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-DotNet {
    param([Parameter(Mandatory)] [string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

Push-Location $repoRoot
try {
    Invoke-DotNet @('restore', 'Stagecoach.sln')
    Invoke-DotNet @('build', 'Stagecoach.sln', '--configuration', $Configuration, '--no-restore')
    if (-not $SkipTests) {
        Invoke-DotNet @('test', 'Stagecoach.sln', '--configuration', $Configuration, '--no-build', '--logger', 'console;verbosity=minimal')
    }
}
finally { Pop-Location }
