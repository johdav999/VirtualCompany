# Replay

Use this same dirty checkout; preserve P01–P24 and do not reset/stash/move their work. Normal PowerShell and in-app automation fail during Windows deny-read ACL initialization; the verified shell and fresh Edge substitutes are recorded.

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
$env:DOTNET_TieredCompilation='0'
$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION='Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False'
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~QuarterlyPlanning|FullyQualifiedName~MonthlyReviewSnapshotIntegrationTests'
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~QuarterlyPlanningJourneyTests|FullyQualifiedName~CompanyPeriodOverviewJourneyTests|FullyQualifiedName~MonthlyReviewJourneyTests'
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~QuarterlyPlanningWireTests
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build tests/VirtualCompany.Workspace.Uat/VirtualCompany.Workspace.Uat.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build
```

Migration creation already ran through the migration project and API startup project. Do not regenerate it. `BuildProjectReferences=false` in some recorded runs reused already-built native dependencies. The API test and UAT build compile production API dependencies.

Browser replay: verify 5345/5105 unused; start `VirtualCompany.Workspace.Uat.dll` and `VirtualCompany.Web.dll` directly with hidden `Start-Process -PassThru`, record PIDs immediately and perform separate readiness checks limited to 30 seconds. Web uses its native content root, Development, `ApiBaseUrl=http://localhost:5345/`, subject `p19-owner`, email `p19-owner@example.test`, provider `dev-header`. Read fresh `/_uat/p25/profile`, use the actual quarter route for readiness, then run `node docs/verification/vcscreens/P25/verify-browser.mjs`. Stop only exact currently verified owned PIDs/DLLs/ports. Historical PIDs are evidence, not cleanup instructions.
