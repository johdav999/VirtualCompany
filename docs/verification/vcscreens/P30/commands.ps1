# Run from the repository root. SQL tests use only GUID-owned disposable databases.
# Set VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION to an authorized local test instance first.
$ErrorActionPreference='Stop'
$env:DOTNET_PROCESSOR_COUNT='2'
$env:DOTNET_TieredCompilation='0'
$p30Out='docs/verification/vcscreens/P30'
$p30ApiFilter='FullyQualifiedName~WeeklyWorkspace|FullyQualifiedName~MonthlyReview|FullyQualifiedName~SalesManagement|FullyQualifiedName~MarketingManagement|FullyQualifiedName~FinanceRollingPlanning|FullyQualifiedName~SupportQuality|FullyQualifiedName~QuarterlyPlanning|FullyQualifiedName~AnnualPlanning|FullyQualifiedName~StrategicScenario|FullyQualifiedName~DecisionWork|FullyQualifiedName~BriefingCadence|FullyQualifiedName~BriefingScheduler|FullyQualifiedName~BriefingPreference|FullyQualifiedName~BriefingUpdate|FullyQualifiedName~ExecutionControl|FullyQualifiedName~Release2Policy|FullyQualifiedName~TodayWorkspace|FullyQualifiedName~Release3IntegratedReview'
if(!$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION){throw 'Supply the authorized disposable SQL Server test connection; do not accept skipped migrations.'}
dotnet build tests/VirtualCompany.Workspace.Uat/VirtualCompany.Workspace.Uat.csproj --no-restore
if($LASTEXITCODE){throw 'UAT/API build failed'}
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore
if($LASTEXITCODE){throw 'Web build failed'}
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-build --no-restore --filter $p30ApiFilter --logger 'trx;LogFileName=release3-api-sql.trx' --results-directory "$p30Out/test-results"
if($LASTEXITCODE){throw 'Native regression failed'}
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter 'FullyQualifiedName~WeeklyWorkspace|FullyQualifiedName~MonthlyReview|FullyQualifiedName~SalesManagement|FullyQualifiedName~MarketingManagement|FullyQualifiedName~FinanceRollingPlanning|FullyQualifiedName~SupportQuality|FullyQualifiedName~QuarterlyPlanning|FullyQualifiedName~AnnualPlanning|FullyQualifiedName~StrategicScenario|FullyQualifiedName~DecisionWork|FullyQualifiedName~BriefingCadence|FullyQualifiedName~CompanyPeriodOverview|FullyQualifiedName~MonthlyWorkspace|FullyQualifiedName~TodayWorkspace|FullyQualifiedName~ExecutionControlJourney|FullyQualifiedName~TaskPolicyJourney' --logger 'trx;LogFileName=release3-web.trx' --results-directory "$p30Out/test-results"
if($LASTEXITCODE){throw 'Rendered regression failed'}
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore --logger 'trx;LogFileName=release3-wire.trx' --results-directory "$p30Out/test-results"
if($LASTEXITCODE){throw 'Wire regression failed'}
dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj --no-restore --filter 'FullyQualifiedName~FinanceRollingPlanningCalculation|FullyQualifiedName~StrategicScenarioCalculation' --logger 'trx;LogFileName=release3-finance.trx' --results-directory "$p30Out/test-results"
if($LASTEXITCODE){throw 'Finance calculation regression failed'}
dotnet test tests/VirtualCompany.SupportGrounding.Tests/VirtualCompany.SupportGrounding.Tests.csproj --no-restore --filter 'FullyQualifiedName~SupportCapacityCalculation' --logger 'trx;LogFileName=release3-support.trx' --results-directory "$p30Out/test-results"
if($LASTEXITCODE){throw 'Support calculation regression failed'}
# Model-check command and browser commands are independent of the test databases.
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --no-build
if($LASTEXITCODE){throw 'EF model check failed'}
# Start ./docs/verification/vcscreens/P30/start-hosts.ps1 only after builds/tests finish.
# With Node and Playwright available: node $p30Out/verify-integrated-browser.mjs
# Then node $p30Out/browser/P22/verify-browser.mjs through P29 in order.
# Finally node $p30Out/verify-exports-and-polish.mjs and run stop-hosts.ps1.
