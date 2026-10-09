$ErrorActionPreference='Stop'
$p30Root=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$p30Runs=@(); $p30Cases=@{}
foreach($p30File in (Get-ChildItem (Join-Path $PSScriptRoot 'test-results') -Filter '*.trx' | Sort-Object LastWriteTimeUtc)) {
    $p30Xml=[xml](Get-Content -LiteralPath $p30File.FullName -Raw)
    $p30Counter=$p30Xml.TestRun.ResultSummary.Counters
    $p30Runs+=@{file='test-results/'+$p30File.Name;finished=$p30Xml.TestRun.Times.finish;total=[int]$p30Counter.total;passed=[int]$p30Counter.passed;failed=[int]$p30Counter.failed;notExecuted=[int]$p30Counter.notExecuted}
    foreach($p30Result in $p30Xml.TestRun.Results.UnitTestResult){$p30Cases[$p30Result.testName]=@{name=$p30Result.testName;outcome=$p30Result.outcome;file=$p30File.Name}}
}
$p30Unresolved=@($p30Cases.Values|Where-Object outcome -ne 'Passed')
if($p30Unresolved.Count){throw ('Unresolved test outcomes: '+($p30Unresolved.name -join ', '))}
if(!(Test-Path (Join-Path $PSScriptRoot 'test-results/release3-sql-compatibility-fix.trx'))){throw 'Final SQL compatibility recheck is missing'}
$p30BrowserFiles=@('browser-accepted.json','exports-and-polish.json')+@(22..29|ForEach-Object {"browser/P$_/browser-accepted.json"})
$p30Browsers=@()
foreach($p30File in $p30BrowserFiles){
    $p30Data=Get-Content (Join-Path $PSScriptRoot $p30File) -Raw|ConvertFrom-Json
    if(@($p30Data.errors).Count -or !$p30Data.results -or @($p30Data.results|Where-Object {$_.passed -ne $true -and $_.result -notin @('pass','passed')}).Count){throw "Browser acceptance failed: $p30File"}
    $p30Browsers+=@{file=$p30File;groups=@($p30Data.results).Count;pageErrors=@($p30Data.errors).Count}
}
$p30Baseline=Get-Content (Join-Path $PSScriptRoot 'prior-artifact-baseline.json') -Raw|ConvertFrom-Json
$p30Changed=@($p30Baseline|Where-Object {!(Test-Path -LiteralPath $_.path) -or (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256})
if($p30Changed.Count){throw 'Prior evidence changed'}
@{checkedUtc=[DateTime]::UtcNow.ToString('o');inventoried=$p30Baseline.Count;changed=$p30Changed;scope='Prior verification/reference files inventoried at entry; .log files excluded'}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $PSScriptRoot 'preservation-verification.json')
$p30Paths=@(
 'src/VirtualCompany.Web/Components/Dashboard/WeeklyWorkspace.razor.css',
 'src/VirtualCompany.Web/Components/Sales/SalesManagementEvidence.razor',
 'src/VirtualCompany.Web/Components/Sales/SalesManagementEvidence.razor.css',
 'src/VirtualCompany.Web/Pages/Finance/FinanceVariance.razor',
 'src/VirtualCompany.Web/Pages/Finance/FinanceForecastComparison.razor',
 'src/VirtualCompany.Web/Pages/Finance/FinancePlanningPageBase.cs',
 'src/VirtualCompany.Web/Pages/Support/SupportQualityPageBase.cs',
 'tests/VirtualCompany.Api.Tests/Release3IntegratedReviewTests.cs',
 'tests/VirtualCompany.Api.Tests/ExecutionControlSqlServerTests.cs',
 'tests/VirtualCompany.Web.Tests/FinanceRollingPlanningJourneyTests.cs',
 'tests/VirtualCompany.Web.Tests/SupportQualityJourneyTests.cs',
 'tests/VirtualCompany.Workspace.Uat/Program.cs',
 'src/VirtualCompany.Web/bin/Debug/net9.0/VirtualCompany.Web.dll',
 'src/VirtualCompany.Api/bin/Debug/net9.0/VirtualCompany.Api.dll',
 'tests/VirtualCompany.Api.Tests/bin/Debug/net9.0/VirtualCompany.Api.Tests.dll',
 'tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Workspace.Uat.dll')
$p30Manifest=@($p30Paths|ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $p30Root $_) -Algorithm SHA256).Hash}})
@{recordedUtc=[DateTime]::UtcNow.ToString('o');head='9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b';workingTree='Preserved P11-P29 plus P30 uncommitted';files=$p30Manifest;note='Final API test DLL differs from broad-run DLL only by the migration compatibility test repair, verified in its focused rerun. Production backend is unchanged.'}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $PSScriptRoot 'source-manifest.json')
$p30Downloads=@(Get-ChildItem $PSScriptRoot -Filter '*-downloaded.csv'|ForEach-Object {@{file=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$p30Summary=@{
 phase='P30';completedUtc=[DateTime]::UtcNow.ToString('o');implementationReadiness='Complete within recorded local scope';humanApproval='Pending';reviewer=$null;reviewDate=$null
 testRuns=$p30Runs
 distinctLatestOutcomes=@{total=$p30Cases.Count;passed=@($p30Cases.Values|Where-Object outcome -eq 'Passed').Count;failed=0;skipped=0;nativeApi=@($p30Cases.Keys|Where-Object {$_ -like 'VirtualCompany.Api.Tests.*' -and $p30Cases[$_].file -like '*api-sql*' -or $p30Cases[$_].file -like '*sql-compatibility*'}).Count;sqlServer=@($p30Cases.Keys|Where-Object {$_ -match 'SqlServer' -and ($p30Cases[$_].file -like '*api-sql*' -or $p30Cases[$_].file -like '*sql-compatibility*')}).Count;note='Latest result per fully qualified case; overlapping runs are not summed. Broad native run had one stale migration fixture failure; focused final rerun supersedes that case only.'}
 browsers=$p30Browsers;browserJourneyGroups=($p30Browsers|Measure-Object groups -Sum).Sum;browserMode='Fresh installed headless Microsoft Edge; real interactive local Web/composed API; native synthetic test data';viewports=@('1440x1000','390x844');physicalCsvDownloads=$p30Downloads
 builds=@{web='Passed; six pre-existing nullable warnings';apiAndUat='Passed through composed UAT build; zero warnings/errors';testCompilation='Passed; existing analyzer warnings';model='No changes since last migration';diffCheck='Passed'}
 database=@{engine='Local SQL Server Express';scope='GUID-owned disposable test databases; migrations, retention and concurrency';productionMigration='Not run';newP30Migration='None'}
 delivery=@{channel='Native workspace notification inbox';scheduler='Existing native scheduler and update jobs';dispatch='Existing company outbox';recipient='Eligible synthetic absence delegate';duplicateNotification='Suppressed';privateSourceAccess='Not granted';externalProvider='Unverified'}
 preservation=@{priorInventoriedFiles=$p30Baseline.Count;changed=0;scope='Prior evidence/reference files except logs';sameCheckout=$true}
 limits=@('Human approve/revise remains Pending','External-provider receipt and deployed tenant/migration unverified','User IAB session not exercised; headless Edge scope only','Print/statutory and unrelated earlier physical outputs unverified','Missing historical ownership/pipeline/backlog and report coverage remain explicit','Scenario and plan governance grant no execution authority','Earlier Release 1-2 acceptance gates remain independent')
 evidence=@{sourceManifest='source-manifest.json';issues='issue-ledger.md';uat='uat.md';references='design-review.md';formulas='../release-3/formulas-and-reproduction.md';humanDecision='../release-3/approval-checklist.md';cleanup='owned-cleanup-final.json'}
}
$p30Summary|ConvertTo-Json -Depth 10|Set-Content (Join-Path $PSScriptRoot 'verification.json')
$p30Summary.distinctLatestOutcomes|ConvertTo-Json
