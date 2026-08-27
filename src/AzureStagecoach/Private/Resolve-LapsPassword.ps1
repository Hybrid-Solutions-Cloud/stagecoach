#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-LapsPassword {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$DeviceId
    )

    try {
        # Acquire Microsoft Graph token via az
        $graphToken = az account get-access-token --resource-type ms-graph --query accessToken -o tsv 2>$null
        if (-not $graphToken) {
            Write-Verbose "Unable to acquire Microsoft Graph token for LAPS lookup."
            return $null
        }

        $headers = @{
            'Authorization' = "Bearer $graphToken"
            'Content-Type'  = 'application/json'
        }

        $uri = "https://graph.microsoft.com/v1.0/directory/deviceLocalCredentials/$DeviceId`?`$select=credentials"
        $response = Invoke-RestMethod -Uri $uri -Method Get -Headers $headers -ErrorAction Stop

        if ($response.credentials -and $response.credentials.Count -gt 0) {
            $latestCred = $response.credentials | Sort-Object -Property refreshDateTime -Descending | Select-Object -First 1
            if ($latestCred.passwordBase64) {
                $decodedBytes = [System.Convert]::FromBase64String($latestCred.passwordBase64)
                $plainPassword = [System.Text.Encoding]::UTF8.GetString($decodedBytes)
                return [pscustomobject]@{
                    Source   = 'EntraLAPS'
                    Username = $latestCred.accountName
                    Password = $plainPassword
                }
            }
        }
    }
    catch {
        Write-Verbose "LAPS password retrieval failed or access was denied (403): $_"
    }

    return $null
}

