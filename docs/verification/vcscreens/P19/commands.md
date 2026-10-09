# P19 verification commands and scope

Run from the same repository checkout. Normal shell startup failed before command execution with Windows sandbox `helper_unknown_error: apply deny-read ACLs`; the repository-local shell commands used the tool's approved escalation after that concrete failure. No browser-control bypass was used. No restore/download, new provider, production database or detached host was needed.

Common PowerShell environment:

```powershell
$env:NUGET_PACKAGES='C:/Users/Johan/.nuget/packages'
$env:DOTNET_PROCESSOR_COUNT='1'
$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION='Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False'
$env:VIRTUALCOMPANY_P19_EVIDENCE_DIRECTORY=(Join-Path (Get-Location) 'docs/verification/vcscreens/P19')
```

SQL variable targets the existing local test instance. `TestWebApplicationFactory` creates/drops its own GUID-named database; it never targets a production company database. The evidence variable is optional and saves only the bounded authenticated reconciliation replay.

Final API regression:

```powershell
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false --filter 'FullyQualifiedName~WeeklyWorkspace|FullyQualifiedName~TodayWorkspace|FullyQualifiedName~MonthlyWorkspace|FullyQualifiedName~AgentWorkLifecycleQuery|FullyQualifiedName~SalesOperationalReport|FullyQualifiedName~MarketingOperationalJourney|FullyQualifiedName~FinanceOperationalJourney|FullyQualifiedName~SupportOperationalJourney|FullyQualifiedName~SalesCampaignsIntegration' --logger 'trx;LogFileName=p19-api-final-accepted.trx' --results-directory docs/verification/vcscreens/P19
```

Final compiled API dependencies preceded the tests-only invocation. Earlier diagnostic failures led to corrected weekday wire conversion, missing contributor registrations and fixture/guard scope; they are retained and excluded. The final run passes 102 checks including all 25 weekly period/integration/SQL checks.

Broader Web run followed by the final weekly/shell recheck after the final UI text refinement:

```powershell
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~WeeklyWorkspace|FullyQualifiedName~WorkspaceShell|FullyQualifiedName~WorkspaceNavigation|FullyQualifiedName~TodayWorkspace|FullyQualifiedName~MonthlyWorkspace|FullyQualifiedName~BusinessWork|FullyQualifiedName~NavigationRace' --logger 'trx;LogFileName=p19-web-accepted.trx' --results-directory docs/verification/vcscreens/P19
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~WeeklyWorkspace|FullyQualifiedName~WorkspaceShell' --logger 'trx;LogFileName=p19-web-final-accepted.trx' --results-directory docs/verification/vcscreens/P19
```

The broader accepted run has 92 checks; the final weekly/shell run has 28. Two added native campaign exact-selection/refusal tests pass in `p19-campaign-web-final.trx` and the final 28. Accepted union: 94 distinct Web checks; overlapping executions are not added. `p19-campaign-web-accepted.trx` is a failed fixture-registration diagnostic despite its initial filename, and is excluded.

Full real typed transport:

```powershell
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false --logger 'trx;LogFileName=p19-wire-accepted.trx' --results-directory docs/verification/vcscreens/P19
```

All 42 pass, including five weekly role wires and the retained 37 existing contracts. API/Web contracts did not change during the last UI-only unavailable-balance text refinement.

Final production builds/model/whitespace:

```powershell
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build
git diff --check
& ./docs/verification/vcscreens/P19/finalize-evidence.ps1
```

Final cached production builds have zero warnings/errors; clean compile passes are also retained in the earlier build/test logs with existing repository warnings. Model check reports no pending change. Whitespace exits 0; line-ending notices are retained in its log. Evidence finalizer only reads/hashes repository files and writes this packet's audit/index JSON. It requires every accepted TRX to have zero failures/skips and rejects missing/unexpected baseline changes or a different HEAD. It does not rerun acceptance or change databases.

Browser command `await cua.getState()` failed before tab inventory. Browser/keyboard/narrow/reference replay is required to close the blocked gate; exact failure and zero captures are in `browser-blocker.json`.
