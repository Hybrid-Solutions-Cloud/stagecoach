#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the Stagecoach quality gate: PSScriptAnalyzer + Pester unit tests.
.EXAMPLE
    pwsh ./scripts/Test.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Path $PSScriptRoot -Parent

foreach ($moduleName in @('Pester', 'PSScriptAnalyzer')) {
    if (-not (Get-Module -ListAvailable -Name $moduleName)) {
        Write-Host "Installing $moduleName..."
        Install-Module -Name $moduleName -Force -Scope CurrentUser -SkipPublisherCheck
    }
}

Write-Host '── PSScriptAnalyzer ──────────────────────────' -ForegroundColor Cyan
$findings = Invoke-ScriptAnalyzer -Path (Join-Path $repoRoot 'src') -Recurse -Severity Warning, Error
if ($findings) {
    $findings | Format-Table RuleName, Severity, ScriptName, Line, Message -AutoSize
}
else {
    Write-Host 'Clean.' -ForegroundColor Green
}

Write-Host '── Pester ────────────────────────────────────' -ForegroundColor Cyan
$result = Invoke-Pester -Path (Join-Path $repoRoot 'tests/Unit') -PassThru -Output Detailed

if (($findings | Where-Object Severity -eq 'Error') -or $result.FailedCount -gt 0) {
    throw "Quality gate failed: $($result.FailedCount) test failure(s), $(@($findings | Where-Object Severity -eq 'Error').Count) analyzer error(s)."
}
Write-Host 'Quality gate passed.' -ForegroundColor Green
