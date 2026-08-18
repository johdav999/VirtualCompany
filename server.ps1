$sqlPassword = if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SA_PASSWORD)) { "YourStrong!Passw0rd" } else { $env:VC_SQL_SA_PASSWORD }
$env:VC_SQL_SA_PASSWORD = $sqlPassword
$composeFile = Join-Path $PSScriptRoot "docker-compose.yml"
$docker = "docker"
$dockerCommandTimeoutSeconds = 15

function Invoke-DockerCommand
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$Activity,

        [int]$TimeoutSeconds = $dockerCommandTimeoutSeconds
    )

    Write-Host "$Activity..."
    $standardOutputPath = [System.IO.Path]::GetTempFileName()
    $standardErrorPath = [System.IO.Path]::GetTempFileName()
    $process = $null

    try
    {
        $process = Start-Process `
            -FilePath $docker `
            -ArgumentList $Arguments `
            -NoNewWindow `
            -PassThru `
            -RedirectStandardOutput $standardOutputPath `
            -RedirectStandardError $standardErrorPath

        if (-not $process.WaitForExit([Math]::Max(1, $TimeoutSeconds) * 1000))
        {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit(5000) | Out-Null
            throw "$Activity timed out after $TimeoutSeconds seconds. Docker Desktop is not responding; restart Docker Desktop and try again."
        }

        $standardOutput = Get-Content -LiteralPath $standardOutputPath -Raw -ErrorAction SilentlyContinue
        $standardError = Get-Content -LiteralPath $standardErrorPath -Raw -ErrorAction SilentlyContinue
        if (-not [string]::IsNullOrWhiteSpace($standardOutput))
        {
            Write-Host $standardOutput.TrimEnd()
        }

        if (-not [string]::IsNullOrWhiteSpace($standardError))
        {
            Write-Host $standardError.TrimEnd()
        }

        if ($process.ExitCode -ne 0)
        {
            throw "$Activity failed with exit code $($process.ExitCode)."
        }
    }
    finally
    {
        Remove-Item -LiteralPath $standardOutputPath, $standardErrorPath -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue))
{
    $defaultDockerPath = "C:\Program Files\Docker\Docker\resources\bin\docker.exe"
    if (Test-Path $defaultDockerPath)
    {
        $docker = $defaultDockerPath
    }
    else
    {
        throw "Docker CLI is not installed or not available on PATH."
    }
}

Invoke-DockerCommand -Arguments "info" -Activity "Checking Docker Desktop"

$escapedComposeFile = '"' + $composeFile.Replace('"', '\"') + '"'
Invoke-DockerCommand `
    -Arguments "compose -f $escapedComposeFile up -d sqlserver" `
    -Activity "Starting the Virtual Company SQL Server container" `
    -TimeoutSeconds 60

$maxAttempts = 30
Write-Host "Waiting for SQL Server on localhost:1433..."
for ($attempt = 1; $attempt -le $maxAttempts; $attempt++)
{
    $tcpClient = $null
    try
    {
        $tcpClient = [System.Net.Sockets.TcpClient]::new()
        $connectTask = $tcpClient.ConnectAsync("127.0.0.1", 1433)
        if ($connectTask.Wait(1000) -and $tcpClient.Connected)
        {
            Write-Host "SQL Server is accepting TCP connections."
            break
        }
    }
    catch
    {
    }
    finally
    {
        if ($null -ne $tcpClient)
        {
            $tcpClient.Dispose()
        }
    }

    if ($attempt -eq $maxAttempts)
    {
        throw "SQL Server container did not become reachable on localhost:1433 within 30 seconds."
    }

    Start-Sleep -Seconds 1
}

$connectionString = "Server=localhost,1433;Database=virtualcompany;User Id=sa;Password=$sqlPassword;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True"
Write-Host "Development startup applies pending EF Core migrations before hosted workers start."
Write-Host "Building and starting the API on http://localhost:5301..."
& (Join-Path $PSScriptRoot "run-api.ps1") -ConnectionString $connectionString
exit $LASTEXITCODE
