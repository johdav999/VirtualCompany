# P22 replay commands

Run from the repository root. Normal shell startup currently fails at the Windows deny-read ACL helper; these commands ran in the approved working shell after that verified failure. Set `DOTNET_PROCESSOR_COUNT=1`; use `--no-restore -m:1 -p:UseSharedCompilation=false` for builds/tests. Do not rebuild an assembly while its owned host/testhost holds it.

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION='Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False'
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~MarketingManagement|FullyQualifiedName~MarketingMeasurementPolicyTests|FullyQualifiedName~MarketingPlanPortfolioTests|FullyQualifiedName~MarketingOperationalJourneyTests|FullyQualifiedName~MonthlyReview|FullyQualifiedName~MonthlyWorkspace|FullyQualifiedName~SalesManagement' --logger 'trx;LogFileName=api-final.trx' --results-directory docs/verification/vcscreens/P22/test-results
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~MarketingManagementJourneyTests|FullyQualifiedName~MarketingOperationalJourneyTests|FullyQualifiedName~MonthlyReviewJourneyTests|FullyQualifiedName~SalesManagementJourneyTests|FullyQualifiedName~CompanyPeriodOverviewJourneyTests|FullyQualifiedName~WeeklyWorkspaceJourneyTests' --logger 'trx;LogFileName=web-accepted.trx' --results-directory docs/verification/vcscreens/P22/test-results
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --logger 'trx;LogFileName=wire-final.trx' --results-directory docs/verification/vcscreens/P22/test-results
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build
```

Migration creation already completed with `dotnet ef migrations add AddMarketingManagementBudgetProposals --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build`; do not create it again. Disposable SQL tests apply/revert/reapply through the owning composite migration assembly and factory cleanup.

Browser: build tests/VirtualCompany.Workspace.Uat after dependencies; BuildProjectReferences=false is safe only when they already compile. Verify ports 5342/5102 are unused, launch the adapter and Web DLLs using Start-Process -WindowStyle Hidden -PassThru, record exact PIDs/commands, and poll the profile endpoint with bounded requests. Web environment: Development, ApiBaseUrl=http://localhost:5342/, DevelopmentAuth__Subject=p19-owner, DevelopmentAuth__Email=p19-owner@example.test, DevelopmentAuth__Provider=dev-header. Adapter uses disposable composed SQLite with disabled workers. Execute `node docs/verification/vcscreens/P22/verify-browser.mjs` using the established bundled Playwright dependency. Stop only recorded PIDs whose current command lines still match the P22 DLL/port pair. owned-hosts.json and owned-cleanup.json are historical proof, never instructions to stop reused PIDs.

Whitespace check uses the repository's CRLF-aware convention: `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check`. No Git checkout/reset/stash/commit is performed.
