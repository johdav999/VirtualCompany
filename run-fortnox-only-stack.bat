@echo off
setlocal EnableExtensions

if "%VC_SQL_SA_PASSWORD%"=="" set "VC_SQL_SA_PASSWORD=YourStrong!Passw0rd"

set "FinanceSeedBackfill__Enabled=false"
set "FinanceSeedWorker__Enabled=false"
set "FinanceInitialization__MissingDatasetBehavior=return_not_initialized"
set "SimulationFeatures__BackendExecutionEnabled=false"
set "SimulationFeatures__BackgroundJobsEnabled=false"
set "FinanceUi__SourceFilter=fortnox"
set "FinanceIntegrations__StartupSync__Enabled=true"
set "FinanceIntegrations__Fortnox__Enabled=true"
set "FinanceIntegrations__Fortnox__ClientId=fHvObuuDQ1in"
set "FinanceIntegrations__Fortnox__ClientSecret=qtoKozAtqtreYjmvUcD79ShAF2iFuDnR"
set "FinanceIntegrations__Fortnox__RedirectUri=http://localhost:5301/finance/integrations/fortnox/callback"
set "ConnectionStrings__VirtualCompanyDb=Server=localhost,1433;Database=virtualcompany;User Id=sa;Password=%VC_SQL_SA_PASSWORD%;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True"

echo Starting Virtual Company in Fortnox-only mode.
echo.
echo Stopping any existing Virtual Company API/Web processes...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ports=@(5301,5062); $owners=Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $ports -contains $_.LocalPort } | Select-Object -ExpandProperty OwningProcess -Unique; $projectProcesses=Get-CimInstance Win32_Process -Filter \"name = 'dotnet.exe'\" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -match 'VirtualCompany\.Api\\VirtualCompany\.Api\.csproj' -or $_.CommandLine -match 'VirtualCompany\.Web\\VirtualCompany\.Web\.csproj' }; $ids=@($owners) + @($projectProcesses | Select-Object -ExpandProperty ProcessId); foreach ($id in ($ids | Where-Object { $_ } | Sort-Object -Unique)) { $process=Get-Process -Id $id -ErrorAction SilentlyContinue; if ($process -and $process.ProcessName -in @('dotnet','VirtualCompany.Api','VirtualCompany.Web')) { Stop-Process -Id $id -Force } }"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$deadline=(Get-Date).AddSeconds(15); do { $listeners=Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in 5301,5062 }; if (-not $listeners) { exit 0 }; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $deadline); Write-Error 'Ports 5301 or 5062 are still occupied after stopping existing app processes.'; exit 1"
if errorlevel 1 exit /b %errorlevel%

echo Ensuring SQL Server container is running...
docker info >nul 2>nul
if errorlevel 1 (
  echo Docker Desktop is not running or this terminal cannot access Docker.
  echo Start Docker Desktop, wait for it to finish starting, and run this file again.
  exit /b 1
)

docker compose -f "%~dp0docker-compose.yml" up -d sqlserver
if errorlevel 1 exit /b %errorlevel%

powershell -NoProfile -ExecutionPolicy Bypass -Command "$deadline=(Get-Date).AddSeconds(60); do { $tcp=$null; try { $tcp=[Net.Sockets.TcpClient]::new(); $task=$tcp.ConnectAsync('127.0.0.1',1433); if ($task.Wait(1000) -and $tcp.Connected) { exit 0 } } catch {} finally { if ($tcp) { $tcp.Dispose() } }; Start-Sleep -Seconds 1 } while ((Get-Date) -lt $deadline); Write-Error 'SQL Server did not become reachable on localhost:1433 within 60 seconds.'; exit 1"
if errorlevel 1 exit /b %errorlevel%

echo Finance seed/backfill disabled:
echo   FinanceSeedBackfill__Enabled=%FinanceSeedBackfill__Enabled%
echo   FinanceSeedWorker__Enabled=%FinanceSeedWorker__Enabled%
echo   FinanceInitialization__MissingDatasetBehavior=%FinanceInitialization__MissingDatasetBehavior%
echo.
echo Finance UI source filter:
echo   FinanceUi__SourceFilter=%FinanceUi__SourceFilter%
echo.
echo Finance integration startup sync:
echo   FinanceIntegrations__StartupSync__Enabled=%FinanceIntegrations__StartupSync__Enabled%
echo.
echo Fortnox:
echo   FinanceIntegrations__Fortnox__Enabled=%FinanceIntegrations__Fortnox__Enabled%
echo   FinanceIntegrations__Fortnox__RedirectUri=%FinanceIntegrations__Fortnox__RedirectUri%
echo.

start "VirtualCompany API - Fortnox only" cmd /k "dotnet run --project src\VirtualCompany.Api\VirtualCompany.Api.csproj"
start "VirtualCompany Web - Fortnox only" cmd /k "dotnet run --project src\VirtualCompany.Web\VirtualCompany.Web.csproj"

echo API and Web were launched in separate command windows with Fortnox-only environment variables.
echo Close any previously running API/Web windows so they do not keep serving old configuration.
echo.

endlocal
