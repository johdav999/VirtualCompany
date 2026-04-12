Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force

$sqlPassword = if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SA_PASSWORD)) { "YourStrong!Passw0rd" } else { $env:VC_SQL_SA_PASSWORD }
$composeFile = Join-Path $PSScriptRoot "docker-compose.yml"

if (-not (Get-Command docker -ErrorAction SilentlyContinue))
{
    throw "Docker CLI is not installed or not available on PATH."
}

& docker info *> $null
if ($LASTEXITCODE -ne 0)
{
    throw "Docker Desktop is not running. Start Docker Desktop and try again."
}

& docker compose -f $composeFile up -d sqlserver
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

$projectDir = "src\VirtualCompany.Api"
$apiDll = "bin\Debug\net9.0\VirtualCompany.Api.dll"

dotnet build "$projectDir\VirtualCompany.Api.csproj" -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5301"
$env:ConnectionStrings__VirtualCompanyDb = "Server=localhost,1433;Database=virtualcompany;User Id=sa;Password=$sqlPassword;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True"

Push-Location $projectDir
try
{
    dotnet $apiDll
}
finally
{
    Pop-Location
}
