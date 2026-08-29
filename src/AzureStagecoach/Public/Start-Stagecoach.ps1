#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Start-Stagecoach {
    <#
    .SYNOPSIS
        Starts Stagecoach: the local web UI for one-click RDP/SSH to your Azure estate.
    .DESCRIPTION
        Hosts the single-file web UI (stagecoach.html) on 127.0.0.1 with a
        per-launch bearer token and opens it in your browser. From there:
        sign in once with Entra ID, scan your machines, and click to connect.
        Every connect click spawns a pwsh process running Connect-StagecoachVM —
        the browser never executes commands itself (see pmo/plans/stagecoach-design.md §4).
    .PARAMETER Port
        Local TCP port to bind. Default: a random high port.
    .PARAMETER NoBrowser
        Do not open the browser automatically (prints the URL instead).
    .EXAMPLE
        Start-Stagecoach
    #>
    [CmdletBinding()]
    param(
        [ValidateRange(0, 65535)]
        [int]$Port = 0,

        [switch]$NoBrowser
    )

    $webRoot = Join-Path -Path $PSScriptRoot -ChildPath '..' -AdditionalChildPath 'Web'
    $htmlPath = Join-Path -Path $webRoot -ChildPath 'stagecoach.html'
    if (-not (Test-Path -Path $htmlPath)) {
        throw "Web UI not found at '$htmlPath'."
    }

    $token = [System.Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $modulePath = (Get-Module -Name 'AzureStagecoach').Path
    $pwshExe = Join-Path -Path $PSHOME -ChildPath ($IsWindows ? 'pwsh.exe' : 'pwsh')

    # --- start the listener (retry on port collision when auto-picking) -----
    $listener = $null
    $attempts = 0
    do {
        $attempts++
        $bindPort = if ($Port -gt 0) { $Port } else { Get-Random -Minimum 40000 -Maximum 49999 }
        $uiUrl = "http://127.0.0.1:$bindPort/"
        try {
            $listener = [System.Net.HttpListener]::new()
            $listener.Prefixes.Add($uiUrl)
            $listener.Start()
        }
        catch {
            $listener = $null
            if ($Port -gt 0 -or $attempts -ge 5) { throw "Could not bind $uiUrl : $($_.Exception.Message)" }
        }
    } while (-not $listener)

    Write-Information "[stagecoach] UI ready at $uiUrl — Ctrl+C here stops the server (sessions keep running)." -InformationAction Continue
    if (-not $NoBrowser) {
        Start-Process "$uiUrl#$token"
    }
    else {
        Write-Information "[stagecoach] Open: $uiUrl#$token" -InformationAction Continue
    }

    # --- tiny helpers -------------------------------------------------------
    $sendJson = {
        param($Response, $Object, [int]$Status = 200)
        $Response.StatusCode = $Status
        $Response.ContentType = 'application/json; charset=utf-8'
        $json = if ($null -eq $Object) { 'null' } else { ConvertTo-Json -InputObject $Object -Depth 8 }
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        $Response.ContentLength64 = $bytes.Length
        $Response.OutputStream.Write($bytes, 0, $bytes.Length)
        $Response.Close()
    }
    $readBody = {
        param($Request)
        $reader = [System.IO.StreamReader]::new($Request.InputStream, [System.Text.Encoding]::UTF8)
        $raw = $reader.ReadToEnd()
        if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
        return $raw | ConvertFrom-Json
    }
    $escape = { param([string]$s) if ($null -eq $s) { '' } else { $s -replace "'", "''" } }

    $running = $true
    try {
        while ($running -and $listener.IsListening) {
            $context = $listener.GetContext()
            $request = $context.Request
            $response = $context.Response
            $path = $request.Url.AbsolutePath

            try {
                # ---- static: the single-file UI and its vendored bundles ----
                if ($path -eq '/' -and $request.HttpMethod -eq 'GET') {
                    $html = [System.IO.File]::ReadAllText($htmlPath)
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($html)
                    $response.ContentType = 'text/html; charset=utf-8'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                    $response.Close()
                    continue
                }
                if ($path -like '/vendor/*' -and $request.HttpMethod -eq 'GET') {
                    $fileName = [System.IO.Path]::GetFileName($path)
                    $allowed = @('react.production.min.js', 'react-dom.production.min.js', 'htm.umd.js')
                    $filePath = Join-Path -Path $webRoot -ChildPath 'vendor' -AdditionalChildPath $fileName
                    if ($fileName -notin $allowed -or -not (Test-Path $filePath)) {
                        $response.StatusCode = 404; $response.Close(); continue
                    }
                    $bytes = [System.IO.File]::ReadAllBytes($filePath)
                    $response.ContentType = 'text/javascript; charset=utf-8'
                    $response.ContentLength64 = $bytes.Length
                    $response.OutputStream.Write($bytes, 0, $bytes.Length)
                    $response.Close()
                    continue
                }

                # ---- API: same-origin only, per-launch token required -------
                if ($path -notlike '/api/*') {
                    $response.StatusCode = 404; $response.Close(); continue
                }
                if ($request.Headers['X-Stagecoach-Token'] -ne $token) {
                    & $sendJson $response @{ error = 'Invalid or missing token. Reload the page from the Start-Stagecoach window.' } 401
                    continue
                }

                switch -Regex ("$($request.HttpMethod) $path") {
                    '^GET /api/state$' {
                        $prereq = Test-StagecoachPrerequisite
                        & $sendJson $response @{
                            azCliPresent      = $prereq.AzCliPresent
                            loggedIn          = $prereq.LoggedIn
                            account           = $prereq.Account
                            missingExtensions = @($prereq.MissingExtensions)
                            ready             = $prereq.Ready
                            windowsClient     = [bool]$IsWindows
                        }
                    }
                    '^POST /api/extensions/install$' {
                        Test-StagecoachPrerequisite -InstallMissing | Out-Null
                        & $sendJson $response @{ ok = $true }
                    }
                    '^POST /api/login$' {
                        $body = & $readBody $request
                        $loginArgs = @{}
                        if ($body -and $body.PSObject.Properties['tenantId'] -and $body.tenantId) { $loginArgs.TenantId = [string]$body.tenantId }
                        if ($body -and $body.PSObject.Properties['useDeviceCode'] -and $body.useDeviceCode) { $loginArgs.UseDeviceCode = $true }
                        $accounts = Connect-StagecoachAccount @loginArgs
                        & $sendJson $response @{ ok = $true; subscriptions = @($accounts) }
                    }
                    '^GET /api/inventory$' {
                        $refresh = $request.QueryString['refresh'] -eq '1'
                        $inventory = @(if ($refresh) { Get-StagecoachInventory } else { Get-StagecoachInventory -Cached })
                        if ($inventory.Count -eq 0 -and -not $refresh) { $inventory = @(Get-StagecoachInventory) }
                        & $sendJson $response @{ machines = @($inventory) }
                    }
                    '^GET /api/connections$' {
                        & $sendJson $response @{ connections = @(Get-StagecoachSavedConnection) }
                    }
                    '^POST /api/connections/remove$' {
                        $body = & $readBody $request
                        Remove-StagecoachSavedConnection -Name ([string]$body.name) -Confirm:$false
                        & $sendJson $response @{ ok = $true }
                    }
                    '^POST /api/connect$' {
                        $body = & $readBody $request
                        $target = Find-StagecoachTarget -Id ([string]$body.id)
                        $method = [string]$body.method
                        if ($method -notin @('Auto', 'Rdp', 'Ssh', 'Tunnel')) { $method = 'Auto' }
                        $username = ''
                        if ($body.PSObject.Properties['username'] -and $body.username) { $username = [string]$body.username }

                        # Validate the route first so bad picks fail here, with a message,
                        # instead of inside a flashing child window.
                        $routeParams = @{ Target = $target; Method = $method }
                        if ($username) { $routeParams.LocalUser = $username }
                        $route = Resolve-StagecoachRoute @routeParams

                        # The click never runs a command in-browser: spawn pwsh running the cmdlet.
                        $cmd = "Import-Module '$(& $escape $modulePath)'; Connect-StagecoachVM -Id '$(& $escape $target.Id)' -Method $($route.Method) -NoSave"
                        if ($username) { $cmd += " -LocalUser '$(& $escape $username)'" }

                        $spawnArgs = @('-NoProfile')
                        $windowStyle = 'Minimized'
                        if ($route.Interactive) {
                            # SSH session: give the operator a real terminal window.
                            $spawnArgs += '-NoExit'
                            $windowStyle = 'Normal'
                        }
                        $spawnArgs += @('-Command', $cmd)

                        $proc = if ($IsWindows) {
                            Start-Process -FilePath $pwshExe -ArgumentList $spawnArgs -WindowStyle $windowStyle -PassThru
                        }
                        else {
                            Start-Process -FilePath $pwshExe -ArgumentList $spawnArgs -PassThru
                        }

                        if ($route.Interactive -and $proc) {
                            Save-StagecoachSessionRecord -TargetId $target.Id -TargetName $target.Name `
                                -Method $route.Method -ProcessId $proc.Id -LocalPort $route.LocalPort
                        }
                        Save-StagecoachConnectionProfile -Target $target -Method $route.Method -Username $username
                        & $sendJson $response @{
                            ok        = $true
                            method    = $route.Method
                            notes     = @($route.Notes)
                            localPort = $route.LocalPort
                        }
                    }
                    '^GET /api/sessions$' {
                        & $sendJson $response @{ sessions = @(Get-StagecoachSession) }
                    }
                    '^POST /api/sessions/stop$' {
                        $body = & $readBody $request
                        Stop-StagecoachSession -SessionId ([string]$body.sessionId) -Confirm:$false
                        & $sendJson $response @{ ok = $true }
                    }
                    '^POST /api/arc/enable-ssh$' {
                        $body = & $readBody $request
                        $target = Find-StagecoachTarget -Id ([string]$body.id)
                        # The UI shows an explicit confirmation before calling this (Azure write).
                        Enable-StagecoachArcSsh -Target $target -Confirm:$false
                        & $sendJson $response @{ ok = $true }
                    }
                    '^POST /api/openssh/install$' {
                        $body = & $readBody $request
                        $target = Find-StagecoachTarget -Id ([string]$body.id)
                        # The UI shows an explicit confirmation before calling this (Azure write).
                        Install-StagecoachOpenSsh -Target $target -Confirm:$false
                        & $sendJson $response @{ ok = $true }
                    }
                    '^POST /api/shutdown$' {
                        & $sendJson $response @{ ok = $true }
                        $running = $false
                    }
                    default {
                        & $sendJson $response @{ error = "No such endpoint: $($request.HttpMethod) $path" } 404
                    }
                }
            }
            catch {
                $payload = @{ error = $_.Exception.Message }
                if ($env:STAGECOACH_DEBUG) { $payload.stack = $_.ScriptStackTrace }
                try { & $sendJson $response $payload 500 } catch { }
            }
        }
    }
    finally {
        if ($listener) {
            try { $listener.Stop(); $listener.Close() } catch { }
        }
        Write-Information '[stagecoach] Server stopped. Live sessions keep running; see Get-StagecoachSession.' -InformationAction Continue
    }
}
