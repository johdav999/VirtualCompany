# P28 replay commands and environment

Repository: `C:\Users\Johan\source\repos\Virtual Company`, .NET SDK 9.0.317 / net9.0, Windows PowerShell, local isolated SQL Server Express. HEAD stays `9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b`; uncommitted earlier phase work remains in this checkout.

The normal shell and in-app browser fail before execution while applying Windows deny-read ACLs. Shell builds/tests used the approved execution fallback after that concrete failure. In-app browser acceptance is unavailable; the browser script launches a fresh headless Microsoft Edge against the actual native Web/composed API. It does not operate a user's tab or claim deployed/provider/human acceptance.

For native builds/tests set `DOTNET_PROCESSOR_COUNT=1` and `DOTNET_TieredCompilation=0`. Commands use `--no-restore -m:1 -p:UseSharedCompilation=false`. The initial full API build compiled all affected native dependencies. Subsequent focused builds use `-p:BuildProjectReferences=false` only after those native outputs exist. Stop owned browser hosts before changing their outputs; do not overlap API test builds and UAT reference builds.

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
$env:DOTNET_TieredCompilation='0'
dotnet build src/VirtualCompany.Infrastructure.Operations/VirtualCompany.Infrastructure.Operations.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false
$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION='Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False'
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false --filter 'FullyQualifiedName~DecisionWork|FullyQualifiedName~ApprovalDecisionApiIntegrationTests'
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false --filter 'FullyQualifiedName~DecisionWorkJourney|FullyQualifiedName~MonthlyReviewJourney|FullyQualifiedName~QuarterlyPlanningJourney|FullyQualifiedName~AnnualPlanningJourney|FullyQualifiedName~StrategicScenarioJourney|FullyQualifiedName~DecisionReview'
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false --filter 'FullyQualifiedName~DecisionWorkWire|FullyQualifiedName~MonthlyReviewWire|FullyQualifiedName~QuarterlyPlanningWire|FullyQualifiedName~AnnualPlanningWire|FullyQualifiedName~StrategicScenarioWire'
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build
dotnet build tests/VirtualCompany.Workspace.Uat/VirtualCompany.Workspace.Uat.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false
```

Migration generation: `dotnet ef migrations add AddDecisionWorkOrigins --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build`. Generated migration/model were reviewed, migration/API outputs rebuilt, then the model check passed. Real SQL tests migrate isolated databases, round-trip only the new tables, preserve native scenario evidence, check typed-source company foreign keys and exercise simultaneous confirmations and review submissions. They do not change the production database.

For the browser, verify ports 5348/5108 are free, then launch `dotnet` directly with `Start-Process -PassThru -WindowStyle Hidden`. UAT DLL: `tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Workspace.Uat.dll --urls http://localhost:5348` from repository root. Web DLL: `src/VirtualCompany.Web/bin/Debug/net9.0/VirtualCompany.Web.dll --urls http://localhost:5108` from its project directory. Set `ASPNETCORE_ENVIRONMENT=Development`, `ApiBaseUrl=http://localhost:5348/`, `DevelopmentAuth__Subject=p19-owner`, `DevelopmentAuth__Email=p19-owner@example.test`, `DevelopmentAuth__Provider=dev-header`. Double underscores are required. Record returned PIDs immediately in `owned-hosts.json`; readiness polls are a separate command bounded to 30 seconds with short request timeouts.

Run `node docs/verification/vcscreens/P28/verify-browser.mjs` with the installed Playwright runtime. The script obtains fresh `/_uat/p28/profile` fixture IDs; it makes no production requests. After acceptance, stop only recorded PIDs whose command lines and listener ownership still match the expected DLLs. Cleanup evidence is recorded separately. Initial failed fixture/host captures and diagnostics remain historical, not accepted results.
