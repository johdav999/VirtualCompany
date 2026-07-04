@echo off
setlocal EnableExtensions

if /I "%~1"=="/persist" goto persist

set "FinanceSeedBackfill__Enabled=true"
set "FinanceSeedWorker__Enabled=true"
set "FinanceInitialization__MissingDatasetBehavior=trigger_seed"
set "SimulationFeatures__BackendExecutionEnabled=true"
set "SimulationFeatures__BackgroundJobsEnabled=true"
set "FinanceUi__SourceFilter=all"
set "FinanceIntegrations__StartupSync__Enabled=false"
set "FinanceIntegrations__Fortnox__Enabled=false"
set "FinanceIntegrations__Fortnox__ClientId="
set "FinanceIntegrations__Fortnox__ClientSecret="
set "FinanceIntegrations__Fortnox__RedirectUri=http://localhost:5301/finance/integrations/fortnox/callback"

echo Simulated finance mode is enabled for this command window.
echo Start the API from this same command window so it inherits these values.
echo Use "enable-simulated-finance-mode.bat /persist" to write the values to your user environment instead.
echo.
cmd /k
goto end

:persist
setx FinanceSeedBackfill__Enabled "true"
setx FinanceSeedWorker__Enabled "true"
setx FinanceInitialization__MissingDatasetBehavior "trigger_seed"
setx SimulationFeatures__BackendExecutionEnabled "true"
setx SimulationFeatures__BackgroundJobsEnabled "true"
setx FinanceUi__SourceFilter "all"
setx FinanceIntegrations__StartupSync__Enabled "false"
setx FinanceIntegrations__Fortnox__Enabled "false"
setx FinanceIntegrations__Fortnox__ClientId ""
setx FinanceIntegrations__Fortnox__ClientSecret ""
setx FinanceIntegrations__Fortnox__RedirectUri "http://localhost:5301/finance/integrations/fortnox/callback"

echo Simulated finance mode was written to your user environment.
echo Open a new terminal before starting the API so it sees the persisted values.

:end
endlocal
