#Requires -Version 7.0

[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0',
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipArchive,
    [switch]$Installer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repoRoot 'artifacts'
$publishDirectory = Join-Path $outputRoot "publish-$Runtime"
$archivePath = Join-Path $outputRoot "Stagecoach-$Version-$Runtime.zip"

function Invoke-DotNet {
    param([Parameter(Mandatory)] [string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

foreach ($project in @('src/Stagecoach.App/Stagecoach.App.csproj', 'src/Stagecoach.AskPass/Stagecoach.AskPass.csproj')) {
    Invoke-DotNet @('publish', (Join-Path $repoRoot $project), '--configuration', 'Release', '--runtime', $Runtime,
        '--self-contained', 'true', '--output', $publishDirectory, "-p:Version=$Version", '-p:DebugType=None', '-p:DebugSymbols=false')
}
Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -Recurse | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE.txt')

if (-not $SkipArchive) {
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$archivePath.sha256" -Value "$hash  $(Split-Path -Leaf $archivePath)" -Encoding utf8NoBOM
}

if ($Installer) {
    $msiVersion = ($Version -split '-', 2)[0]
    Invoke-DotNet @('build', (Join-Path $repoRoot 'installer/Stagecoach.Installer.wixproj'), '--configuration', 'Release',
        "-p:ReleaseVersion=$Version", "-p:MsiVersion=$msiVersion", "-p:PublishDirectory=$publishDirectory")
}

Write-Output $publishDirectory
