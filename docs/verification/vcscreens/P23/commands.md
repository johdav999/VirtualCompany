# Replay commands

Run from the repository root in the same checkout. Normal shell startup fails before execution at the ACL helper; commands used the approved working-shell fallback. No checkout, reset, stash or commit is performed. Prior evidence is preserved.

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION='Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False'
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~FinanceRollingPlanning|FullyQualifiedName~FinancePlanningPersistence|FullyQualifiedName~FinancePlanningEndpoints|FullyQualifiedName~FinancePlanningContextProjection|FullyQualifiedName~FinancePlanningEntityResolver|FullyQualifiedName~FinancePeriodReporting|FullyQualifiedName~MonthlyReview|FullyQualifiedName~MonthlyWorkspace' --logger 'trx;LogFileName=api-accepted.trx' --results-directory docs/verification/vcscreens/P23/test-results
dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~FinanceRollingPlanningCalculationTests' --logger 'trx;LogFileName=finance-rules-accepted.trx' --results-directory docs/verification/vcscreens/P23/test-results
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~FinanceRollingPlanningJourneyTests|FullyQualifiedName~FinanceOperationalJourneyTests|FullyQualifiedName~MonthlyReviewJourneyTests|FullyQualifiedName~CompanyPeriodOverviewJourneyTests' --logger 'trx;LogFileName=web-accepted.trx' --results-directory docs/verification/vcscreens/P23/test-results
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --logger 'trx;LogFileName=wire-accepted.trx' --results-directory docs/verification/vcscreens/P23/test-results
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build
git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check
```

Creation already ran: `dotnet ef migrations add AddFinanceRollingPlanningRevisions --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build`; do not create it again. Existing initial/diagnostic logs are retained but excluded from accepted results. Finance test assets initially referenced an absent sandbox package cache; `dotnet restore ... --ignore-failed-sources` repaired their local cache paths.

`BuildProjectReferences=false` was used only against already-built dependencies to avoid rewriting DLLs held by another testhost. Do not rebuild a host's assembly while it runs. Build the UAT adapter after API test dependencies compile. Verify ports 5343/5103 unused, start owned DLL hosts with Start-Process -WindowStyle Hidden -PassThru, save PIDs/commands before polling, then run `node docs/verification/vcscreens/P23/verify-browser.mjs`. Web Development identity p19-owner/p19-owner@example.test, provider dev-header, ApiBaseUrl http://localhost:5343/. Adapter uses disposable composed SQLite with disabled workers. Cleanup verifies command line/DLL/port ownership; historical PIDs are never stop instructions.
