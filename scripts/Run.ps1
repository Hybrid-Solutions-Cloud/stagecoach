#Requires -Version 7.0

[CmdletBinding()]
param([ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& dotnet run --project (Join-Path $repoRoot 'src/Stagecoach.App/Stagecoach.App.csproj') --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "Stagecoach exited with code $LASTEXITCODE." }
