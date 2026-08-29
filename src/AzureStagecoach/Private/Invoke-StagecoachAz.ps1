#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-StagecoachAz {
    <#
    .SYNOPSIS
        Runs an Azure CLI command and returns its output, optionally parsed from JSON.
    .DESCRIPTION
        Central az invocation helper: verifies az is on PATH, captures stderr,
        and raises a readable error on non-zero exit. Do not route secret reads
        through this helper (its errors echo the command line).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$AsJson,

        # Return $null instead of throwing when az exits non-zero.
        [switch]$AllowFailure
    )

    $azCmd = Get-Command -Name 'az' -ErrorAction SilentlyContinue
    if (-not $azCmd) {
        throw "Azure CLI ('az') was not found on PATH. Install it from https://aka.ms/azure-cli and run 'az login'."
    }

    $finalArgs = @($Arguments)
    if ($AsJson) {
        $finalArgs += @('--output', 'json', '--only-show-errors')
    }

    $output = & az @finalArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) {
            Write-Verbose "az $($finalArgs -join ' ') failed (tolerated): $(($output | Out-String).Trim())"
            return $null
        }
        throw "az $($finalArgs -join ' ') failed: $(($output | Out-String).Trim())"
    }

    if (-not $AsJson) {
        return $output
    }

    $text = ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }
    return $text | ConvertFrom-Json
}

function Get-StagecoachProp {
    <#
    .SYNOPSIS
        StrictMode-safe property accessor for deserialized JSON objects.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        $Default = $null
    )

    if ($null -eq $InputObject) { return $Default }
    $prop = $InputObject.PSObject.Properties[$Name]
    if ($prop -and $null -ne $prop.Value) { return $prop.Value }
    return $Default
}
