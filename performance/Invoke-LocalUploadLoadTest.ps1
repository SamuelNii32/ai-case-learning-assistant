[CmdletBinding()]
param(
    [string]$PdfPath,
    [uri]$BaseUri = "http://127.0.0.1:5260/",
    [int[]]$ConcurrencyStages = @(10, 25, 50, 100),
    [int]$UploadPermitLimit = 1000,
    [int]$GlobalPermitLimit = 1000,
    [int]$RateLimitWindowSeconds = 600,
    [double]$StopFailurePercent = 50,
    [int]$MinimumFailuresToStop = 3,
    [int]$MemorySampleMilliseconds = 250,
    [string]$ResultsRoot,
    [switch]$SkipBuild,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http -ErrorAction Stop

if ([string]::IsNullOrWhiteSpace($ResultsRoot)) {
    $ResultsRoot = Join-Path $PSScriptRoot "results"
}

function Assert-LocalUri {
    param([uri]$Uri)

    if ($Uri.Scheme -notin @("http", "https") -or
        -not $Uri.IsLoopback -or
        -not [string]::IsNullOrEmpty($Uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($Uri.Query) -or
        -not [string]::IsNullOrEmpty($Uri.Fragment)) {
        throw "BaseUri must be an HTTP(S) loopback URL without credentials, query, or fragment."
    }
}

function Get-Median {
    param([double[]]$Values)

    if ($Values.Count -eq 0) { return 0 }
    $sorted = @($Values | Sort-Object)
    $middle = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

function Get-Percentile {
    param(
        [double[]]$Values,
        [double]$Percentile
    )

    if ($Values.Count -eq 0) { return 0 }
    $sorted = @($Values | Sort-Object)
    $index = [math]::Max(0, [math]::Ceiling($Percentile * $sorted.Count) - 1)
    return $sorted[$index]
}

function Wait-ForApi {
    param(
        [System.Diagnostics.Process]$Process,
        [uri]$HealthUri
    )

    $healthClient = [System.Net.Http.HttpClient]::new()
    $healthClient.Timeout = [TimeSpan]::FromSeconds(2)
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($Process.HasExited) {
                throw "The local API process exited with code $($Process.ExitCode) before becoming healthy."
            }

            try {
                $response = $healthClient.GetAsync($HealthUri).GetAwaiter().GetResult()
                try {
                    if ($response.IsSuccessStatusCode) { return }
                }
                finally {
                    $response.Dispose()
                }
            }
            catch [System.Net.Http.HttpRequestException] {
                # Expected while Kestrel starts.
            }
            catch [System.Threading.Tasks.TaskCanceledException] {
                # Expected if startup has not completed yet.
            }

            Start-Sleep -Milliseconds 250
        }

        throw "The local API did not become healthy within 45 seconds."
    }
    finally {
        $healthClient.Dispose()
    }
}

Assert-LocalUri $BaseUri

if ($ConcurrencyStages.Count -eq 0 -or @($ConcurrencyStages | Where-Object { $_ -le 0 }).Count -gt 0) {
    throw "ConcurrencyStages must contain positive integers."
}
if ($UploadPermitLimit -le 0 -or $GlobalPermitLimit -le 0 -or $RateLimitWindowSeconds -le 0) {
    throw "Development rate-limit values must be positive."
}
if ($StopFailurePercent -lt 0 -or $StopFailurePercent -gt 100 -or $MinimumFailuresToStop -le 0) {
    throw "Failure-stop settings are invalid."
}
if ($MemorySampleMilliseconds -lt 50) {
    throw "MemorySampleMilliseconds must be at least 50."
}

$totalPlannedRequests = ($ConcurrencyStages | Measure-Object -Sum).Sum
if ($UploadPermitLimit -lt $totalPlannedRequests -or $GlobalPermitLimit -lt $totalPlannedRequests) {
    throw "UploadPermitLimit and GlobalPermitLimit must each cover all $totalPlannedRequests planned requests."
}

if ($ValidateOnly) {
    Write-Host "Harness validation passed: localhost restriction, stages, stop policy, and Development rate-limit settings are valid."
    return
}

if ([string]::IsNullOrWhiteSpace($PdfPath)) {
    throw "PdfPath is required unless ValidateOnly is used."
}
$resolvedPdfPath = (Resolve-Path -LiteralPath $PdfPath).Path
if ([IO.Path]::GetExtension($resolvedPdfPath) -ne ".pdf") {
    throw "PdfPath must refer to a .pdf file."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solutionPath = Join-Path $repoRoot "IngestionApi.sln"
$apiDll = Join-Path $repoRoot "bin\Release\net9.0\IngestionApi.dll"

if (-not $SkipBuild) {
    & dotnet build $solutionPath -c Release
    if ($LASTEXITCODE -ne 0) { throw "The Release build failed." }
}
if (-not (Test-Path -LiteralPath $apiDll -PathType Leaf)) {
    throw "The Release API assembly was not found. Run without SkipBuild first."
}

$secureToken = Read-Host "Bearer token (input is hidden)" -AsSecureString
$tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
try {
    $plainToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPointer)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPointer)
}
if ([string]::IsNullOrWhiteSpace($plainToken)) { throw "A bearer token is required." }
if ($plainToken.StartsWith("Bearer ", [StringComparison]::OrdinalIgnoreCase)) {
    $plainToken = $plainToken.Substring(7).Trim()
}

$timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$resultsDirectory = Join-Path $ResultsRoot $timestamp
[void](New-Item -ItemType Directory -Path $resultsDirectory -Force)

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = "dotnet"
$startInfo.Arguments = '"' + $apiDll + '"'
$startInfo.WorkingDirectory = $repoRoot
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development"
$startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = $BaseUri.AbsoluteUri.TrimEnd("/")
$startInfo.EnvironmentVariables["RateLimiting__Upload__DevelopmentOverride__Enabled"] = "true"
$startInfo.EnvironmentVariables["RateLimiting__Upload__DevelopmentOverride__PermitLimit"] = $UploadPermitLimit.ToString()
$startInfo.EnvironmentVariables["RateLimiting__Upload__DevelopmentOverride__WindowSeconds"] = $RateLimitWindowSeconds.ToString()
$startInfo.EnvironmentVariables["RateLimiting__Global__DevelopmentOverride__Enabled"] = "true"
$startInfo.EnvironmentVariables["RateLimiting__Global__DevelopmentOverride__PermitLimit"] = $GlobalPermitLimit.ToString()
$startInfo.EnvironmentVariables["RateLimiting__Global__DevelopmentOverride__WindowSeconds"] = $RateLimitWindowSeconds.ToString()

$apiProcess = $null
$httpClient = $null
$allRequests = [Collections.Generic.List[object]]::new()
$allStages = [Collections.Generic.List[object]]::new()
$allMemory = [Collections.Generic.List[object]]::new()
$createdUploadIds = [Collections.Generic.List[string]]::new()

$workerScript = {
    param($Stage, $RequestNumber, $Client, $LocalPdfPath, $Ready, $StartGate)

    Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
    [void]$Ready.Signal()
    $StartGate.Wait()
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $statusCode = 0
    $uploadId = $null
    $errorText = $null

    try {
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "uploads")
        $multipart = [Net.Http.MultipartFormDataContent]::new()
        $fileStream = [IO.File]::OpenRead($LocalPdfPath)
        $fileContent = [Net.Http.StreamContent]::new($fileStream)
        $fileContent.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new("application/pdf")
        $multipart.Add($fileContent, "file", [IO.Path]::GetFileName($LocalPdfPath))
        $request.Content = $multipart

        try {
            $response = $Client.SendAsync($request).GetAwaiter().GetResult()
            try {
                $statusCode = [int]$response.StatusCode
                if ($statusCode -eq 201) {
                    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    $parsed = $body | ConvertFrom-Json
                    $uploadId = [string]$parsed.uploadId
                }
            }
            finally {
                $response.Dispose()
            }
        }
        finally {
            $request.Dispose()
        }
    }
    catch {
        $errorText = $_.Exception.GetType().Name + ": " + $_.Exception.Message
    }
    finally {
        $stopwatch.Stop()
    }

    [pscustomobject]@{
        Stage = [int]$Stage
        Request = [int]$RequestNumber
        StatusCode = [int]$statusCode
        Success = ($statusCode -eq 201)
        LatencyMs = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 2)
        UploadId = $uploadId
        Error = $errorText
    }
}

try {
    $apiProcess = [Diagnostics.Process]::Start($startInfo)
    Wait-ForApi -Process $apiProcess -HealthUri ([uri]::new($BaseUri, "health/live"))

    $httpClient = [Net.Http.HttpClient]::new()
    $httpClient.BaseAddress = $BaseUri
    $httpClient.Timeout = [TimeSpan]::FromMinutes(5)
    $httpClient.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $plainToken)
    $plainToken = $null
    $secureToken.Dispose()

    Write-Host "Local API PID: $($apiProcess.Id)"
    Write-Host "Results: $resultsDirectory"

    foreach ($stage in $ConcurrencyStages) {
        Write-Host "Starting synchronized concurrency stage $stage..."
        $ready = [Threading.CountdownEvent]::new($stage)
        $startGate = [Threading.ManualResetEventSlim]::new($false)
        $pool = [RunspaceFactory]::CreateRunspacePool($stage, $stage)
        $pool.Open()
        $workers = [Collections.Generic.List[object]]::new()

        try {
            for ($requestNumber = 1; $requestNumber -le $stage; $requestNumber++) {
                $powerShell = [PowerShell]::Create()
                $powerShell.RunspacePool = $pool
                [void]$powerShell.AddScript($workerScript.ToString())
                [void]$powerShell.AddArgument($stage)
                [void]$powerShell.AddArgument($requestNumber)
                [void]$powerShell.AddArgument($httpClient)
                [void]$powerShell.AddArgument($resolvedPdfPath)
                [void]$powerShell.AddArgument($ready)
                [void]$powerShell.AddArgument($startGate)
                $asyncResult = $powerShell.BeginInvoke()
                $workers.Add([pscustomobject]@{ PowerShell = $powerShell; AsyncResult = $asyncResult })
            }

            if (-not $ready.Wait([TimeSpan]::FromSeconds(30))) {
                throw "Stage $stage workers did not reach the synchronized start gate."
            }

            $stageStopwatch = [Diagnostics.Stopwatch]::StartNew()
            $startGate.Set()
            do {
                $apiProcess.Refresh()
                $allMemory.Add([pscustomobject]@{
                    TimestampUtc = [DateTime]::UtcNow.ToString("O")
                    Stage = $stage
                    ElapsedMs = [math]::Round($stageStopwatch.Elapsed.TotalMilliseconds, 2)
                    WorkingSetMb = [math]::Round($apiProcess.WorkingSet64 / 1MB, 2)
                    PrivateMemoryMb = [math]::Round($apiProcess.PrivateMemorySize64 / 1MB, 2)
                })
                $incomplete = @($workers | Where-Object { -not $_.AsyncResult.IsCompleted }).Count
                if ($incomplete -gt 0) { Start-Sleep -Milliseconds $MemorySampleMilliseconds }
            } while ($incomplete -gt 0)
            $stageStopwatch.Stop()

            $stageResults = [Collections.Generic.List[object]]::new()
            foreach ($worker in $workers) {
                $workerResults = $worker.PowerShell.EndInvoke($worker.AsyncResult)
                foreach ($result in $workerResults) {
                    $stageResults.Add($result)
                    $allRequests.Add($result)
                    if ($result.UploadId) { $createdUploadIds.Add([string]$result.UploadId) }
                }
            }

            $successes = @($stageResults | Where-Object Success).Count
            $httpFailures = @($stageResults | Where-Object { -not $_.Success -and $_.StatusCode -gt 0 }).Count
            $transportFailures = @($stageResults | Where-Object { $_.StatusCode -eq 0 }).Count
            $failures = $httpFailures + $transportFailures
            $failurePercent = if ($stageResults.Count -eq 0) { 100 } else { 100 * $failures / $stageResults.Count }
            $latencies = [double[]]@($stageResults | ForEach-Object { $_.LatencyMs })
            $elapsedSeconds = [math]::Max(0.001, $stageStopwatch.Elapsed.TotalSeconds)

            $stageSummary = [pscustomobject]@{
                Concurrency = $stage
                Requests = $stageResults.Count
                Successes = $successes
                HttpFailures = $httpFailures
                TransportFailures = $transportFailures
                FailurePercent = [math]::Round($failurePercent, 2)
                ThroughputRps = [math]::Round($stageResults.Count / $elapsedSeconds, 2)
                SuccessfulThroughputRps = [math]::Round($successes / $elapsedSeconds, 2)
                MedianLatencyMs = [math]::Round((Get-Median $latencies), 2)
                P95LatencyMs = [math]::Round((Get-Percentile $latencies 0.95), 2)
                DurationSeconds = [math]::Round($elapsedSeconds, 2)
            }
            $allStages.Add($stageSummary)
            $stageSummary | Format-Table -AutoSize | Out-Host

            if ($failures -ge $MinimumFailuresToStop -and $failurePercent -ge $StopFailurePercent) {
                Write-Warning "Stopping before the next stage after sustained failures ($failures failures, $([math]::Round($failurePercent, 2))%)."
                break
            }
        }
        finally {
            foreach ($worker in $workers) { $worker.PowerShell.Dispose() }
            $pool.Dispose()
            $ready.Dispose()
            $startGate.Dispose()
        }
    }
}
finally {
    if ($null -ne $plainToken) { $plainToken = $null }
    if ($null -ne $httpClient) { $httpClient.Dispose() }
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        $apiProcess.Kill()
        [void]$apiProcess.WaitForExit(10000)
    }

    $allRequests | Export-Csv (Join-Path $resultsDirectory "requests.csv") -NoTypeInformation
    $allStages | Export-Csv (Join-Path $resultsDirectory "stages.csv") -NoTypeInformation
    $allMemory | Export-Csv (Join-Path $resultsDirectory "memory.csv") -NoTypeInformation
    $createdUploadIds | Set-Content (Join-Path $resultsDirectory "upload-ids.txt")
    [pscustomobject]@{
        StartedAtUtc = $timestamp
        BaseUri = $BaseUri.AbsoluteUri
        PlannedStages = $ConcurrencyStages
        CompletedStages = @($allStages | ForEach-Object Concurrency)
        DevelopmentRateLimits = @{
            UploadPermitLimit = $UploadPermitLimit
            GlobalPermitLimit = $GlobalPermitLimit
            WindowSeconds = $RateLimitWindowSeconds
        }
        StageResults = $allStages
        CreatedUploadCount = $createdUploadIds.Count
    } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $resultsDirectory "summary.json")
}

Write-Host "Load test complete. Created upload IDs: $($createdUploadIds.Count)"
Write-Host "Results saved to $resultsDirectory"
