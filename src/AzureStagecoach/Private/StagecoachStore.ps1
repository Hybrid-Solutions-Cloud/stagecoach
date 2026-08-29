#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Local state lives under ~/.stagecoach:
#   inventory.json    — cached discovery results (no credentials, ever)
#   connections.json  — saved logins: target + method + username (no passwords, ever)

function Get-StagecoachHome {
    [CmdletBinding()]
    param()

    $userHome = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile)
    $configDir = Join-Path -Path $userHome -ChildPath '.stagecoach'
    if (-not (Test-Path -Path $configDir)) {
        New-Item -Path $configDir -ItemType Directory -Force | Out-Null
    }
    return $configDir
}

function Read-StagecoachJsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName
    )

    $path = Join-Path -Path (Get-StagecoachHome) -ChildPath $FileName
    if (-not (Test-Path -Path $path)) { return $null }
    try {
        $raw = Get-Content -Path $path -Raw -Encoding utf8
        if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
        return $raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "Could not read '$path' ($($_.Exception.Message)); treating it as empty."
        return $null
    }
}

function Write-StagecoachJsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        $Value
    )

    $path = Join-Path -Path (Get-StagecoachHome) -ChildPath $FileName
    # -AsArray keeps single entries as JSON arrays so reads are shape-stable.
    $Value | ConvertTo-Json -Depth 6 -AsArray | Set-Content -Path $path -Encoding utf8
}

function Save-StagecoachInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [StagecoachTarget[]]$Targets
    )

    Write-StagecoachJsonFile -FileName 'inventory.json' -Value @($Targets)
}

function Get-StagecoachCachedInventory {
    [CmdletBinding()]
    [OutputType([StagecoachTarget[]])]
    param()

    $parsed = Read-StagecoachJsonFile -FileName 'inventory.json'
    if ($null -eq $parsed) { return @() }

    $targets = [System.Collections.Generic.List[StagecoachTarget]]::new()
    foreach ($item in @($parsed)) {
        try {
            $targets.Add((ConvertTo-StagecoachTarget -InputObject $item))
        }
        catch {
            Write-Verbose "Skipping unreadable cached inventory entry: $($_.Exception.Message)"
        }
    }
    return $targets.ToArray()
}

function ConvertTo-StagecoachTarget {
    [CmdletBinding()]
    [OutputType([StagecoachTarget])]
    param(
        [Parameter(Mandatory = $true)]
        $InputObject
    )

    $target = [StagecoachTarget]::new()
    foreach ($prop in $target.PSObject.Properties) {
        if ($prop.Name -eq 'Tags') { continue }
        $value = Get-StagecoachProp -InputObject $InputObject -Name $prop.Name
        if ($null -ne $value -and '' -ne "$value") {
            $target.($prop.Name) = $value
        }
    }

    $tags = Get-StagecoachProp -InputObject $InputObject -Name 'Tags'
    if ($tags) {
        foreach ($tagProp in $tags.PSObject.Properties) {
            $target.Tags[$tagProp.Name] = [string]$tagProp.Value
        }
    }
    return $target
}

function Save-StagecoachConnectionProfile {
    <#
    .SYNOPSIS
        Upserts a saved login (target + method + username — never a password).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [StagecoachTarget]$Target,

        [Parameter(Mandatory = $true)]
        [string]$Method,

        [Parameter(Mandatory = $false)]
        [string]$Username
    )

    $existing = @(Get-StagecoachSavedConnection)
    $others = @($existing | Where-Object { $_.TargetId -ne $Target.Id })
    $previous = $existing | Where-Object { $_.TargetId -eq $Target.Id } | Select-Object -First 1

    $useCount = 1
    if ($previous) {
        $useCount = [int](Get-StagecoachProp -InputObject $previous -Name 'UseCount' -Default 0) + 1
    }

    $entry = [pscustomobject]@{
        TargetId      = $Target.Id
        TargetName    = $Target.Name
        ResourceGroup = $Target.ResourceGroup
        Kind          = "$($Target.Kind)"
        OsType        = $Target.OsType
        Method        = $Method
        Username      = $Username
        LastUsed      = [System.DateTime]::UtcNow.ToString('o')
        UseCount      = $useCount
    }

    Write-StagecoachJsonFile -FileName 'connections.json' -Value (@($entry) + $others)
}
