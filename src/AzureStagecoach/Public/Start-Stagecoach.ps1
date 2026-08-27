#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Start-Stagecoach {
    <#
    .SYNOPSIS
        Starts the local Stagecoach web server and opens the operator interface in the default browser.
    .DESCRIPTION
        Starts a lightweight localhost web server on 127.0.0.1 serving stagecoach.html
        and bridging web UI button clicks to AzureStagecoach PowerShell cmdlets.
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

    Write-Information "[Stagecoach] Server active. Press Ctrl+C in terminal to stop." -InformationAction Continue

    if (-not $NoBrowser) {
        Start-Process $uiUrl
    }

    try {
        while ($listener.IsListening) {
            $context = $listener.GetContext()
            $request = $context.Request
            $response = $context.Response

            # Enable CORS for localhost
            $response.Headers.Add('Access-Control-Allow-Origin', '*')
            $response.Headers.Add('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
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

            # Route: GET /api/inventory (Scan Azure Resource Graph)
            if ($rawPath -eq '/api/inventory' -and $request.HttpMethod -eq 'GET') {
                try {
                    $inventory = @(Get-StagecoachInventory)
                    $json = $inventory | ConvertTo-Json -Depth 5
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

