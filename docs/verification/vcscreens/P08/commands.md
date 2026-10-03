# P08 repeatable commands

Run from the same checkout after reading scoped instructions. These are focused suites; retain all prior working-tree changes.

```powershell
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~VirtualCompany.Api.Tests.Support" --logger "trx;LogFileName=p08-api-final.trx" --results-directory docs/verification/vcscreens/P08
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~SupportOperationalJourneyTests|FullyQualifiedName~TodayWorkspaceComponentTests|FullyQualifiedName~WorkspaceNavigationTests" --logger "trx;LogFileName=p08-web-final.trx" --results-directory docs/verification/vcscreens/P08
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore --filter "FullyQualifiedName~SupportOperationalWireContractTests|FullyQualifiedName~TodayPriorityWireContractTests" --logger "trx;LogFileName=p08-wire.trx" --results-directory docs/verification/vcscreens/P08
dotnet test tests/VirtualCompany.SupportGrounding.Tests/VirtualCompany.SupportGrounding.Tests.csproj -p:NuGetAudit=false --logger "trx;LogFileName=p08-grounding.trx" --results-directory docs/verification/vcscreens/P08
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore
dotnet build tests/VirtualCompany.Workspace.Uat/VirtualCompany.Workspace.Uat.csproj --no-restore
git diff --check
```

The grounding restore needed network access after concrete NU1301 signature-feed socket failure; the controlled test rerun passed. No package versions changed. Historical `p08-api.trx` is the overly broad failed selection; final results are `p08-api-final.trx`. Historical `p08-web-recovery.trx` selected no tests before adding the explicit Compile item; it is not acceptance evidence.

API foreground: `dotnet tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Workspace.Uat.dll --urls http://localhost:5319`. Automated startup used direct `Start-Process dotnet ... -PassThru -WindowStyle Hidden` with immediate PID record and separate bounded health command. Web from `src/VirtualCompany.Web`: Development, ApiBaseUrl `http://localhost:5319`, DevelopmentAuth enabled, subject/email `p01-owner`/`p01-owner@example.com`; `dotnet bin/Debug/net9.0/VirtualCompany.Web.dll --urls http://localhost:5079` in a managed terminal. Member uses subject/email `p01-member`/`p01-member@example.com` on 5080. Check listeners first and stop only verified owned PIDs. No repeated detached Web launches after failure.

Fixture health is `/_uat/health`; general `/health` is degraded because optional voice pilots are disabled, not because Support/database readiness failed. Authenticated Support reads use X-Dev-Auth-Subject/Email and X-Company-Id in this disposable adapter. Raw authorized report/case/knowledge payloads and `source-reconciliation.json` record the exercised source state. The reconciliation CSV is a projection of that API snapshot, not a physically downloaded browser artifact.
