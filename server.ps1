$sqlPassword = if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SA_PASSWORD)) { "YourStrong!Passw0rd" } else { $env:VC_SQL_SA_PASSWORD }
$env:VC_SQL_SA_PASSWORD = $sqlPassword
$composeFile = Join-Path $PSScriptRoot "docker-compose.yml"
$docker = "docker"

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

& $docker info *> $null
if ($LASTEXITCODE -ne 0)
{
    throw "Docker Desktop is not running. Start Docker Desktop and try again."
}

& $docker compose -f $composeFile up -d sqlserver
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$maxAttempts = 30
for ($attempt = 1; $attempt -le $maxAttempts; $attempt++)
{
    $tcpClient = $null
    try
    {
        $tcpClient = [System.Net.Sockets.TcpClient]::new()
        $connectTask = $tcpClient.ConnectAsync("127.0.0.1", 1433)
        if ($connectTask.Wait(1000) -and $tcpClient.Connected)
        {
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
& (Join-Path $PSScriptRoot "run-api.ps1") -ConnectionString $connectionString
exit $LASTEXITCODE
