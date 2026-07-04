@echo off
setlocal EnableExtensions

if /I "%~1"=="/persist" goto persist

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

echo Fortnox-only finance mode is enabled for this command window.
echo.
echo Edit this file or set these two variables before starting the API:
echo   FinanceIntegrations__Fortnox__ClientId
echo   FinanceIntegrations__Fortnox__ClientSecret
echo.
echo Start the API from this same command window so it inherits these values.
echo Use "enable-fortnox-only-mode.bat /persist" to write the values to your user environment instead.
echo.
cmd /k
goto end

:persist
setx FinanceSeedBackfill__Enabled "false"
setx FinanceSeedWorker__Enabled "false"
setx FinanceInitialization__MissingDatasetBehavior "return_not_initialized"
setx SimulationFeatures__BackendExecutionEnabled "false"
setx SimulationFeatures__BackgroundJobsEnabled "false"
setx FinanceUi__SourceFilter "fortnox"
setx FinanceIntegrations__StartupSync__Enabled "true"
setx FinanceIntegrations__Fortnox__Enabled "true"
setx FinanceIntegrations__Fortnox__ClientId "fHvObuuDQ1in"
setx FinanceIntegrations__Fortnox__ClientSecret "qtoKozAtqtreYjmvUcD79ShAF2iFuDnR"
setx FinanceIntegrations__Fortnox__RedirectUri "http://localhost:5301/finance/integrations/fortnox/callback"

echo Fortnox-only finance mode was written to your user environment.
echo Open a new terminal before starting the API so it sees the persisted values.
echo Replace the placeholder Fortnox client id and secret before using this mode.

:end
endlocal
