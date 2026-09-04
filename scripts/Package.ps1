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

function Write-Sha256Sidecar {
    param([Parameter(Mandatory)] [string]$Path)
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$Path.sha256" -Value "$hash  $(Split-Path -Leaf $Path)" -Encoding utf8NoBOM
}

if (-not $SkipArchive) {
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
    Write-Sha256Sidecar -Path $archivePath
}

if ($Installer) {
    $msiVersion = ($Version -split '-', 2)[0]
    Invoke-DotNet @('build', (Join-Path $repoRoot 'installer/Stagecoach.Installer.wixproj'), '--configuration', 'Release',
        "-p:ReleaseVersion=$Version", "-p:MsiVersion=$msiVersion", "-p:PublishDirectory=$publishDirectory")

    # The installer was built and then left where WiX put it: it was never copied beside the archive
    # and never got a checksum, so the only published artifact anyone is told to verify had nothing
    # to verify it against unless someone produced the hash by hand.
    $msiName = "Stagecoach-$Version-$Runtime.msi"
    $builtMsi = Join-Path $repoRoot "installer/bin/Release/$msiName"
    if (-not (Test-Path -LiteralPath $builtMsi)) { throw "The installer was not produced at '$builtMsi'." }
    $msiPath = Join-Path $outputRoot $msiName
    Copy-Item -LiteralPath $builtMsi -Destination $msiPath -Force
    Write-Sha256Sidecar -Path $msiPath
}

Write-Output $publishDirectory
