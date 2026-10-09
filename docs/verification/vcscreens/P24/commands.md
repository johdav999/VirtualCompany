# Replay commands

Use the same dirty checkout. Normal shell startup failed at the Windows `apply deny-read ACLs` helper; the working shell fallback was used after that concrete failure. No commit/reset/stash/worktree transfer is performed.

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION='Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False'
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~SupportQuality|FullyQualifiedName~SupportOperational|FullyQualifiedName~MonthlyReview|FullyQualifiedName~MonthlyWorkspace|FullyQualifiedName~FinanceRollingPlanningIntegrationTests'
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~SupportAgentServiceTests|FullyQualifiedName~SupportAgentDecisionServiceTests'
dotnet test tests/VirtualCompany.SupportGrounding.Tests/VirtualCompany.SupportGrounding.Tests.csproj --no-restore -m:1 --filter 'FullyQualifiedName~SupportCapacityCalculationTests'
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~SupportQualityJourneyTests|FullyQualifiedName~SupportOperationalJourneyTests|FullyQualifiedName~MonthlyReviewJourneyTests|FullyQualifiedName~CompanyPeriodOverviewJourneyTests|FullyQualifiedName~FinanceRollingPlanningJourneyTests'
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build tests/VirtualCompany.Workspace.Uat/VirtualCompany.Workspace.Uat.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build
git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check
```

Creation already ran: `dotnet ef migrations add AddSupportQualityPlanning --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build`; do not regenerate it. SQL tests create and dispose GUID-named `virtualcompany_accounting_scenario_` databases. BuildProjectReferences=false was used only with already-built dependencies to avoid rewriting test/host assemblies in use. Final API dependencies were built by the accepted affected API command, and runtime DLL hashes reconcile in `runtime-dll-parity.json`.

Browser replay: verify ports 5344/5104 unused; start hidden owned DLL hosts with Start-Process -PassThru; save PIDs/DLLs/ports before bounded readiness checks. API is `VirtualCompany.Workspace.Uat.dll`; Web is `VirtualCompany.Web.dll` with its native content root, Development environment, `ApiBaseUrl=http://localhost:5344/`, `DevelopmentAuth__Subject=p19-owner`, email `p19-owner@example.test`, provider `dev-header`. Read fresh `/_uat/p24/profile` instead of reusing identities. Wait for owned API and the actual Web route (there is no `/health` Web endpoint) before `node docs/verification/vcscreens/P24/verify-browser.mjs`. Stop only processes whose current command matches the recorded PID/DLL/port. Never treat old packet PIDs as stop instructions.

Accepted results use the latest result per test name. The broader API run has 54 passes and one diagnostic failure fixed by the final affected 17-pass run; `verification.json` lists the superseded failure explicitly. Initial compile/browser attempts are retained but excluded. The full 52-pass wire run is supplemented by the two affected final-source contract passes. No whole-repository suite or deployed tenant acceptance is claimed.
