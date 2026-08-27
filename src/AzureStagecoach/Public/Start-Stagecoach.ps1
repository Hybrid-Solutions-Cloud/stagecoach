#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Start-Stagecoach {
    <#
    .SYNOPSIS
        Starts the local Stagecoach desktop command center web server and opens the interface in the default browser.
    .DESCRIPTION
        Starts a lightweight localhost web server on 127.0.0.1 serving stagecoach.html,
        bridging UI actions to AzureStagecoach PowerShell cmdlets, managing identities, and syncing metadata.
    .PARAMETER Port
        The local TCP port to bind (default: 8085).
    .PARAMETER NoBrowser
        If specified, prevents automatically opening the default web browser.
    .EXAMPLE
        Start-Stagecoach
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $false)]
        [int]$Port = 8085,

        [Parameter(Mandatory = $false)]
        [switch]$NoBrowser
    )

    $uiUrl = "http://127.0.0.1:$Port/"

    if (-not $PSCmdlet.ShouldProcess($uiUrl, 'Start Stagecoach Local Web Server')) {
        return
    }

    $webPath = Join-Path -Path $PSScriptRoot -ChildPath '..\Web\stagecoach.html'
    if (-not (Test-Path $webPath)) {
        throw "Could not locate web UI file at '$webPath'."
    }

    Write-Information "[Stagecoach] Initializing listener on $uiUrl..." -InformationAction Continue

    $listener = [System.Net.HttpListener]::new()
    $listener.Prefixes.Add($uiUrl)
    $listener.Start()

    Write-Information "[Stagecoach] Command Center active. Press Ctrl+C in terminal to stop." -InformationAction Continue

    if (-not $NoBrowser) {
        Start-Process $uiUrl
    }

    # In-memory session tracking
    $script:activeSessions = [System.Collections.Generic.Dictionary[string, StagecoachSession]]::new()

    try {
        while ($listener.IsListening) {
            $context = $listener.GetContext()
            $request = $context.Request
            $response = $context.Response

            # Enable CORS for localhost
            $response.Headers.Add('Access-Control-Allow-Origin', '*')
            $response.Headers.Add('Access-Control-Allow-Methods', 'GET, POST, DELETE, OPTIONS')
            $response.Headers.Add('Access-Control-Allow-Headers', 'Content-Type, Authorization')

            if ($request.HttpMethod -eq 'OPTIONS') {
                $response.StatusCode = 204
                $response.Close()
                continue
            }

            $rawPath = $request.Url.AbsolutePath

            # Route: Static UI
            if ($rawPath -eq '/' -or $rawPath -eq '/index.html' -or $rawPath -eq '/stagecoach.html') {
                $htmlContent = [System.IO.File]::ReadAllBytes($webPath)
                $response.ContentType = 'text/html; charset=utf-8'
                $response.ContentLength64 = $htmlContent.Length
                $response.OutputStream.Write($htmlContent, 0, $htmlContent.Length)
                $response.Close()
                continue
            }

            # Route: GET /api/identities (List Entra Accounts & Tenants)
            if ($rawPath -eq '/api/identities' -and $request.HttpMethod -eq 'GET') {
                try {
                    $rawAccounts = az account list -o json 2>$null | ConvertFrom-Json
                    $identities = @()
                    if ($rawAccounts) {
                        $groupedByUser = $rawAccounts | Group-Object -Property { $_.user.name }
                        foreach ($group in $groupedByUser) {
                            $userUpn = $group.Name
                            $tenants = @($group.Group | Select-Object -Property tenantId, @{N = 'tenantName'; E = { $_.name } }, @{N = 'subscriptionId'; E = { $_.id } }, isDefault | Group-Object -Property tenantId | ForEach-Object {
                                    [pscustomobject]@{
                                        TenantId      = $_.Name
                                        Subscriptions = @($_.Group | Select-Object -Property subscriptionId, tenantName)
                                        IsDefault     = ($_.Group | Where-Object { $_.isDefault -eq $true }).Count -gt 0
                                    }
                                })
                            $identities += [pscustomobject]@{
                                AccountName = $userUpn
                                Tenants     = $tenants
                            }
                        }
                    }
                    $json = $identities | ConvertTo-Json -Depth 5
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
                    $response.ContentType = 'application/json; charset=utf-8'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                catch {
                    $errObj = @{ error = $_.Exception.Message } | ConvertTo-Json
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($errObj)
                    $response.StatusCode = 500
                    $response.ContentType = 'application/json'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                $response.Close()
                continue
            }

            # Route: GET /api/inventory (Get Cached Inventory)
            if ($rawPath -eq '/api/inventory' -and $request.HttpMethod -eq 'GET') {
                try {
                    $cached = Get-StagecoachCachedInventory
                    if (-not $cached -or $cached.Count -eq 0) {
                        $cached = @(Get-StagecoachInventory)
                        if ($cached.Count -gt 0) {
                            Save-StagecoachInventory -Targets $cached
                        }
                    }
                    $json = $cached | ConvertTo-Json -Depth 5
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
                    $response.ContentType = 'application/json; charset=utf-8'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                catch {
                    $errObj = @{ error = $_.Exception.Message } | ConvertTo-Json
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($errObj)
                    $response.StatusCode = 500
                    $response.ContentType = 'application/json'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                $response.Close()
                continue
            }

            # Route: POST /api/sync (Force Live Sync Across Tenants)
            if ($rawPath -eq '/api/sync' -and $request.HttpMethod -eq 'POST') {
                try {
                    $freshInventory = @(Get-StagecoachInventory)
                    Save-StagecoachInventory -Targets $freshInventory
                    $json = $freshInventory | ConvertTo-Json -Depth 5
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
                    $response.ContentType = 'application/json; charset=utf-8'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                catch {
                    $errObj = @{ error = $_.Exception.Message } | ConvertTo-Json
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($errObj)
                    $response.StatusCode = 500
                    $response.ContentType = 'application/json'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                $response.Close()
                continue
            }

            # Route: POST /api/credentials (Resolve LAPS / Domain / Key Vault)
            if ($rawPath -eq '/api/credentials' -and $request.HttpMethod -eq 'POST') {
                try {
                    $reader = [System.IO.StreamReader]::new($request.InputStream, $request.ContentEncoding)
                    $bodyJson = $reader.ReadToEnd()
                    $targetData = $bodyJson | ConvertFrom-Json

                    $target = [StagecoachTarget]::new()
                    $target.Id = $targetData.Id
                    $target.Name = $targetData.Name
                    $target.ResourceGroup = $targetData.ResourceGroup
                    $target.SubscriptionId = $targetData.SubscriptionId
                    $target.Kind = [StagecoachTargetKind]::$($targetData.Kind)
                    $target.DomainName = $targetData.DomainName
                    $target.DomainType = [StagecoachDomainType]::$($targetData.DomainType)

                    if ($targetData.Tags) {
                        foreach ($prop in $targetData.Tags.PSObject.Properties) {
                            $target.Tags[$prop.Name] = [string]$prop.Value
                        }
                    }

                    $cred = Get-StagecoachCredential -Target $target
                    $result = if ($cred) { $cred } else { @{ Source = 'None'; Username = ''; Password = '' } }
                    $json = $result | ConvertTo-Json
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)

                    $response.ContentType = 'application/json; charset=utf-8'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                catch {
                    $errObj = @{ error = $_.Exception.Message } | ConvertTo-Json
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($errObj)
                    $response.StatusCode = 500
                    $response.ContentType = 'application/json'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                $response.Close()
                continue
            }

            # Route: POST /api/credentials/save (Write-Back to Key Vault)
            if ($rawPath -eq '/api/credentials/save' -and $request.HttpMethod -eq 'POST') {
                try {
                    $reader = [System.IO.StreamReader]::new($request.InputStream, $request.ContentEncoding)
                    $bodyJson = $reader.ReadToEnd()
                    $saveReq = $bodyJson | ConvertFrom-Json

                    $vault = if ($saveReq.VaultName) { $saveReq.VaultName } else { 'kv-hcs-vault-01' }
                    $secretName = if ($saveReq.SecretName) { $saveReq.SecretName } else { "vm-$($saveReq.TargetName.ToLowerInvariant())-localadmin" }

                    az keyvault secret set --vault-name $vault --name $secretName --value $saveReq.Password -o none 2>$null

                    $json = @{ status = 'Saved'; SecretName = $secretName; Vault = $vault } | ConvertTo-Json
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
                    $response.ContentType = 'application/json; charset=utf-8'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                catch {
                    $errObj = @{ error = $_.Exception.Message } | ConvertTo-Json
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($errObj)
                    $response.StatusCode = 500
                    $response.ContentType = 'application/json'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                $response.Close()
                continue
            }

            # Route: POST /api/connect (Launch Session via PowerShell Cmdlet)
            if ($rawPath -eq '/api/connect' -and $request.HttpMethod -eq 'POST') {
                try {
                    $reader = [System.IO.StreamReader]::new($request.InputStream, $request.ContentEncoding)
                    $bodyJson = $reader.ReadToEnd()
                    $reqData = $bodyJson | ConvertFrom-Json

                    $target = [StagecoachTarget]::new()
                    $target.Id = $reqData.Target.Id
                    $target.Name = $reqData.Target.Name
                    $target.ResourceGroup = $reqData.Target.ResourceGroup
                    $target.Kind = [StagecoachTargetKind]::$($reqData.Target.Kind)
                    $target.DomainName = $reqData.Target.DomainName
                    $target.DomainType = [StagecoachDomainType]::$($reqData.Target.DomainType)

                    $session = Connect-StagecoachVM -Target $target -LocalUser $reqData.Username -Rdp $true
                    $script:activeSessions[$session.SessionId] = $session

                    $json = $session | ConvertTo-Json
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)

                    $response.ContentType = 'application/json; charset=utf-8'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                catch {
                    $errObj = @{ error = $_.Exception.Message } | ConvertTo-Json
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($errObj)
                    $response.StatusCode = 500
                    $response.ContentType = 'application/json'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                $response.Close()
                continue
            }

            # Route: GET /api/sessions (List Active Sessions)
            if ($rawPath -eq '/api/sessions' -and $request.HttpMethod -eq 'GET') {
                $sessionList = @($script:activeSessions.Values)
                $json = $sessionList | ConvertTo-Json
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
                $response.ContentType = 'application/json; charset=utf-8'
                $response.ContentLength64 = $bytes.Length
                $response.OutputStream.Write($bytes, 0, $bytes.Length)
                $response.Close()
                continue
            }

            # Fallback 404
            $response.StatusCode = 404
            $response.Close()
        }
    }
    finally {
        if ($listener -and $listener.IsListening) {
            $listener.Stop()
            $listener.Close()
            Write-Information "[Stagecoach] Server listener stopped." -InformationAction Continue
        }
    }
}
