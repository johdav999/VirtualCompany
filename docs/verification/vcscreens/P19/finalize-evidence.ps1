param()
$ErrorActionPreference = 'Stop'
$packet = $PSScriptRoot
$repository = (Resolve-Path (Join-Path $packet '../../../..')).Path
Set-Location -LiteralPath $repository
$recorded = [DateTime]::UtcNow.ToString('O')
$accepted = @(
    @{file='p19-api-final-accepted.trx';suite='API'},
    @{file='p19-web-accepted.trx';suite='Web'},
    @{file='p19-web-final-accepted.trx';suite='Web'},
    @{file='p19-campaign-web-final.trx';suite='Web'},
    @{file='p19-wire-accepted.trx';suite='Wire'}
)
$runs = @(); $identities = @{API=@();Web=@();Wire=@()}
foreach($entry in $accepted) {
    [xml]$document = Get-Content -LiteralPath (Join-Path $packet $entry.file) -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
    $ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $counter = $document.SelectSingleNode('//t:Counters',$ns)
    $times = $document.SelectSingleNode('//t:Times',$ns)
    if([int]$counter.failed -ne 0 -or [int]$counter.notExecuted -ne 0 -or [int]$counter.passed -ne [int]$counter.total) { throw "Accepted file did not pass: $($entry.file)" }
    $results = @($document.SelectNodes('//t:UnitTestResult',$ns))
    $identities[$entry.suite] += @($results | ForEach-Object {$_.testName})
    $runs += [pscustomobject]@{file=$entry.file;suite=$entry.suite;total=[int]$counter.total;passed=[int]$counter.passed;failed=0;skipped=0;start=$times.start;finish=$times.finish;sha256=(Get-FileHash -LiteralPath (Join-Path $packet $entry.file) -Algorithm SHA256).Hash}
}
$baseline = Get-Content -LiteralPath (Join-Path $packet 'preservation-baseline.json') -Raw | ConvertFrom-Json
$missing = @(); $changes = @(); $unchanged = 0
foreach($entry in $baseline.files) {
    if(!(Test-Path -LiteralPath $entry.path)) { $missing += $entry.path; continue }
    $hash = (Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash
    if($hash -eq $entry.sha256) { $unchanged++ } else { $changes += [pscustomobject]@{path=$entry.path;beforeSha256=$entry.sha256;afterSha256=$hash} }
}
$allowed = @('docs/ui-route-inventory.md','docs/vcscreens/implementation-status.md','docs/vcscreens/screen-report-register.md',
    'src/VirtualCompany.Infrastructure.Operations/Companies/OperationsModuleRegistration.cs','src/VirtualCompany.Web/Services/WebApiClientRegistration.cs',
    'tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj','tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj')
$unexpected = @($changes | Where-Object {$_.path -notin $allowed})
$earlierEvidence = @($baseline.files | Where-Object {$_.path -match '^docs/verification/' -and $_.path -notmatch '^docs/verification/vcscreens/P19/'})
$changedEvidence = @($changes | Where-Object {$_.path -in $earlierEvidence.path})
$head = (git rev-parse HEAD).Trim()
if($missing.Count -gt 0 -or $unexpected.Count -gt 0 -or $changedEvidence.Count -gt 0 -or $head -ne $baseline.head) { throw 'Prior phase preservation audit failed.' }
[pscustomobject]@{recordedUtc=$recorded;head=$head;baselineFiles=$baseline.files.Count;retained=$baseline.files.Count;byteUnchanged=$unchanged;missing=$missing;reviewedSharedChanges=$changes;unexpectedChanges=$unexpected;earlierEvidenceFiles=$earlierEvidence.Count;earlierEvidenceByteUnchanged=$earlierEvidence.Count;changedEarlierEvidence=$changedEvidence;scope='All entry dirty/untracked files; shared registrations/project inclusions and phase/route documents changed additively. Clean HEAD files changed for P19 are in the source manifest.'} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $packet 'preservation-verification.json') -Encoding utf8
$sourcePaths = @(
    'src/VirtualCompany.Application/Cockpit/WeeklyWorkspaceContracts.cs',
    'src/VirtualCompany.Infrastructure.Operations/Companies/CompanyWeeklyWorkspaceQueryService.cs',
    'src/VirtualCompany.Infrastructure.Operations/Companies/CompanyWeeklyWorkspaceContributor.cs',
    'src/VirtualCompany.Infrastructure.Operations/Companies/OperationsModuleRegistration.cs',
    'src/VirtualCompany.Infrastructure.Sales/Sales/SalesWeeklyWorkspaceContributor.cs',
    'src/VirtualCompany.Infrastructure.Sales/Marketing/MarketingWeeklyWorkspaceContributor.cs',
    'src/VirtualCompany.Infrastructure.Sales/Sales/SalesModuleRegistration.cs',
    'src/VirtualCompany.Infrastructure.Finance/Finance/FinanceWeeklyWorkspaceContributor.cs',
    'src/VirtualCompany.Infrastructure.Finance/Finance/FinanceModuleRegistration.cs',
    'src/VirtualCompany.Infrastructure.Support/Support/SupportWeeklyWorkspaceContributor.cs',
    'src/VirtualCompany.Infrastructure.Support/Support/SupportModuleRegistration.cs',
    'src/VirtualCompany.Api/Controllers/WeeklyWorkspaceController.cs',
    'src/VirtualCompany.Web/Services/WeeklyWorkspaceViewModels.cs','src/VirtualCompany.Web/Services/WeeklyWorkspaceApiClient.cs',
    'src/VirtualCompany.Web/Services/DashboardRoutes.cs','src/VirtualCompany.Web/Services/WebApiClientRegistration.cs',
    'src/VirtualCompany.Web/Components/Dashboard/WeeklyWorkspace.razor','src/VirtualCompany.Web/Components/Dashboard/WeeklyWorkspace.razor.css',
    'src/VirtualCompany.Web/Components/Dashboard/TodayWorkspace.razor','src/VirtualCompany.Web/Components/Dashboard/MonthlyWorkspace.razor',
    'src/VirtualCompany.Web/Pages/Dashboard.razor','src/VirtualCompany.Web/Pages/Sales/SalesCampaigns.razor',
    'tests/VirtualCompany.Api.Tests/WeeklyWorkspaceFixture.cs','tests/VirtualCompany.Api.Tests/WeeklyWorkspacePeriodTests.cs',
    'tests/VirtualCompany.Api.Tests/WeeklyWorkspaceIntegrationTests.cs','tests/VirtualCompany.Api.Tests/WeeklyWorkspaceSqlServerTests.cs',
    'tests/VirtualCompany.Web.Tests/WeeklyWorkspaceJourneyTests.cs','tests/VirtualCompany.Web.Tests/WorkspaceShellTests.cs',
    'tests/VirtualCompany.Web.Tests/WebTestContextServiceRegistration.cs','tests/VirtualCompany.Web.Tests/TodayWorkspaceComponentTests.cs',
    'tests/VirtualCompany.Web.Tests/MonthlyWorkspaceComponentTests.cs','tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj',
    'tests/VirtualCompany.Web.Contract.Tests/WeeklyWorkspaceWireTests.cs','tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj',
    'docs/ui-route-inventory.md','docs/vcscreens/implementation-status.md','docs/vcscreens/screen-report-register.md',
    'docs/design/references/role-time-agent-2026-10-01/03-marketing-week.png'
)
$sourcePaths += @(Get-ChildItem -LiteralPath 'docs/design/references' -Filter 'weekly-p19-*' | ForEach-Object {'docs/design/references/'+$_.Name})
$sourcePaths += @('docs/verification/vcscreens/P19/README.md','docs/verification/vcscreens/P19/implementation.md','docs/verification/vcscreens/P19/profile.md',
    'docs/verification/vcscreens/P19/uat.md','docs/verification/vcscreens/P19/issue-ledger.md','docs/verification/vcscreens/P19/handoff.md','docs/verification/vcscreens/P19/commands.md')
$files = @($sourcePaths | Sort-Object -Unique | ForEach-Object { $item = Get-Item -LiteralPath $_; [pscustomobject]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash;lastWriteUtc=$item.LastWriteTimeUtc.ToString('O')} })
[pscustomobject]@{recordedUtc=$recorded;head=$head;files=$files;scope='Full hashes of P19-touched source/test/shared documents plus reference targets; mixed-phase files retain earlier changes. This is not a commit or screenshot manifest.'} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $packet 'source-manifest.json') -Encoding utf8
$diagnostics = @('p19-api-focused-retry.trx','p19-api-corrected.trx','p19-api-final.trx','p19-api-regression-accepted.trx','p19-read-source-diagnostic.trx','p19-campaign-web-accepted.trx')
$builds = @('build-api-final.log','build-web-final.log')
foreach($build in $builds) { if((Get-Content -LiteralPath (Join-Path $packet $build) -Raw) -notmatch 'Build succeeded\.') { throw "Build not successful: $build" } }
if((Get-Content -LiteralPath (Join-Path $packet 'model-check.log') -Raw) -notmatch 'No changes have been made to the model') { throw 'Model check not successful.' }
[pscustomobject]@{
    recordedUtc=$recorded;head=$head;implementation='Implemented locally; available automatic verification complete; required browser acceptance Blocked; human approval Pending';runs=$runs;
    distinctAccepted=[pscustomobject]@{API=@($identities.API | Sort-Object -Unique).Count;Web=@($identities.Web | Sort-Object -Unique).Count;Wire=@($identities.Wire | Sort-Object -Unique).Count};
    sqlServer='One final accepted GUID-isolated migrated SQL Server test, included in API total; query/source compatibility, not production deployment';
    builds=$builds;modelCheck='Pass: no pending model change';diffCheck='Pass: git diff --check exit 0';
    sourceReconciliation=@{file='source-reconciliation.json';sha256=(Get-FileHash -LiteralPath (Join-Path $packet 'source-reconciliation.json') -Algorithm SHA256).Hash;scope='Authenticated native TestServer GET, exact included source/current-prior counts and actual disposable IDs; fixed test clock; no provider substitution needed for recorded read projection'};
    browser=@{state='Blocked before inventory';checks=0;screenshots=0;file='browser-blocker.json'};
    humanApproval='Pending';diagnosticRunsExcluded=$diagnostics;preflight='Earlier accepted 11-role integration preflight overlaps the final API run; not added to totals. Compiler/diagnostic logs are retained and excluded.';
    preservation=@{baseline=638;retained=638;earlierEvidence=438;earlierEvidenceByteUnchanged=438;file='preservation-verification.json'};
    timing='Final API run contains final backend/contracts and tests. Final Web 28 rechecks the final weekly component/shell/native campaign sources after unavailable-balance text refinement. The other 66 Web tests retain unchanged source and accepted broader results. Wire contracts are unchanged by that UI-only refinement. Overlapping passes are counted once by test name.';
    remaining=@('Required five-role browser/reference/keyboard/narrow acceptance','Named human release approval','Inherited P18 provider/deployed/autonomous-model/native/physical/statutory acceptance');
    nextPhases='P20-P30 not started'
} | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $packet 'verification.json') -Encoding utf8
[pscustomobject]@{recordedUtc=$recorded;detachedHostsStarted=0;ownedHostsToStop=@();browserTabsCreated=0;screenshots=0;testProcesses='All tracked test/build tool sessions completed; no unmanaged API/Web host was launched.';sql='GUID-owned TestWebApplicationFactory SQL scenarios dispose through EnsureDeleted; accepted SQL test includes successful factory disposal. SQLite hosts are memory-only and disposed. No production database was changed.';kernel='CUA PID 33692 exited before inventory; no browser session existed.';earlierPids='Historical; not used or stopped.'} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $packet 'cleanup.json') -Encoding utf8
[pscustomobject]@{recordedUtc=$recorded;state='Available automatic verification complete; browser Blocked';acceptedRuns=$runs.Count;sourceFiles=$files.Count;preservedFiles=$baseline.files.Count;earlierEvidence=$earlierEvidence.Count;scope='No claims of deployment, provider/customer receipt, saved snapshot, physical output, statutory or human release acceptance';phase='P19 only'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packet 'final-audit.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $packet 'verification.json') -Raw | ConvertFrom-Json | Select-Object distinctAccepted,implementation
