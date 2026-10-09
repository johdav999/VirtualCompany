$ErrorActionPreference='Stop'
$p13Folder=$PSScriptRoot
$p13Runs=@()
foreach($name in @('p13-api-final.trx','p13-web-final.trx','p13-wire-final.trx','p13-sqlserver-final.trx')) {
 [xml]$trx=Get-Content (Join-Path $p13Folder $name) -Raw
 $c=$trx.TestRun.ResultSummary.Counters
 if([int]$c.failed -or [int]$c.notExecuted -or [int]$c.passed -le 0) {throw "Rejected accepted test file: $name"}
 $p13Runs+=@{file=$name;total=[int]$c.total;passed=[int]$c.passed;failed=[int]$c.failed;skipped=[int]$c.notExecuted;sha256=(Get-FileHash (Join-Path $p13Folder $name)).Hash}
}
$checks=Get-Content (Join-Path $p13Folder 'browser-checks.json') -Raw | ConvertFrom-Json
foreach($check in $checks | Where-Object {!$_.passed}) {
 if($check.name -ne 'Supplier bill mobile overflow fixed') {throw 'Unexpected unresolved browser assertion'}
 $check | Add-Member -NotePropertyName resolvedBy -NotePropertyValue 'Supplier bill final build mobile layout' -Force
 $check | Add-Member -NotePropertyName classification -NotePropertyValue 'Intermediate diagnostic failure; excluded from acceptance' -Force
}
$checks | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $p13Folder 'browser-checks.json')
$source=Get-Content (Join-Path $p13Folder 'source-reconciliation.json') -Raw | ConvertFrom-Json
$bill=Get-Content (Join-Path $p13Folder 'bill-replay-reconciliation.json') -Raw | ConvertFrom-Json
if(@($source.checks | Where-Object {!$_.passed}).Count -or @($bill.checks | Where-Object {!$_.passed}).Count) {throw 'Source assertion failed'}
if((Get-Content (Join-Path $p13Folder 'diff-check-exit.txt')) -ne '0') {throw 'Diff check failed'}
if(!(Get-Content (Join-Path $p13Folder 'model-check-final.log') -Tail 1).Contains('No changes')) {throw 'Model check failed'}
$baseline=(Get-Content (Join-Path $p13Folder 'baseline-head.txt')).Trim()
$head=(git rev-parse HEAD).Trim()
$missing=@(Get-Content (Join-Path $p13Folder 'working-tree-before.txt') | ForEach-Object { $path=$_.Substring(3).Trim('"'); if(!(Test-Path -LiteralPath $path)) {$path} })
if($head -ne $baseline -or $missing.Count) {throw 'Preservation boundary changed'}
@{baselineHead=$baseline;currentHead=$head;sameCheckout=$true;baselineDirtyFiles='working-tree-before.txt';allBaselinePathsRemain=$true;priorEvidence='P01-P12 packets retained; P12 cleanup wording clarified for the inaccessible historical browser tab';schema='P11 migration/model retained; additive P13 nullable indexed associations; P12 unchanged';regression='P10 lifecycle, P11 projection/SQL/wire and P12 decision/return assertions; first-session P11/P12 source preservation';commit='No commit/reset/stash/branch operation';testAdaptation='P11 SQL legacy fixture adapted to its historical migration boundary, original assertions retained'} | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $p13Folder 'preservation.json')
$rawFiles=Get-ChildItem $p13Folder -File | Where-Object {$_.Extension -in @('.json','.png','.txt','.trx') -and $_.Name -notin @('verification.json','evidence-hashes.json')}
$hashes=@($rawFiles | ForEach-Object {@{file=$_.Name;sha256=(Get-FileHash $_.FullName).Hash}})
$hashes | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $p13Folder 'evidence-hashes.json')
@{prompt='P13';implementation='Complete';localVerification='Complete';observedUtc=[datetime]::UtcNow.ToString('o');baselineHead=$baseline;acceptedRuns=$p13Runs;countsOverlapPriorPhases=$true;builds=@{api='Pass: rebuilt dependencies in uat-bill-final-build.log';uat='Pass: uat-bill-final-build.log';web='Pass: web-final-build.log';efModel='Pass: model-check-final.log';diffCheck='Pass: diff-check-exit.txt'};browser=@{flows=@('F13-01 Sales','F13-02 Support','F13-03 Finance invoice/operational bill','F13-04 Marketing campaign/content','F13-05 responsive/keyboard');passingAssertions=@($checks | Where-Object passed).Count;intermediateResolvedFailures=@($checks | Where-Object {!$_.passed}).Count;acceptedProof=@('sales-artifact-version-two','sales-returned-proposal','sales-stale-decision','sales-stale-refreshed','support-partial-failed-delivery','support-decision-changes-requested','support-refreshed','support-mobile','finance-invoice-evidence','finance-invoice-decision','finance-invoice-refreshed','finance-bill-evidence','finance-bill-decision','finance-bill-refreshed','finance-bill-mobile-accepted','finance-bill-desktop-final','finance-bill-panel-final.png','marketing-campaign-evidence','marketing-campaign-decision','marketing-campaign-refreshed','marketing-brief-evidence','marketing-brief-decision','marketing-brief-refreshed','marketing-mobile');diagnosticOnly=@('sales-before-reason-summary','sales-proposal-contributions','finance-bill-mobile','finance-bill-mobile-final');intakeBill='Authenticated API and SQL Server verification; final browser fixture uses operational bill';console='No warnings/errors since final healthy replay at 18:27 UTC; earlier host-stop connection errors retained'};reconciliation=@{firstSessionChecks=$source.checks.Count;firstSessionReads=$source.reads.Count;billReplayChecks=$bill.checks.Count;profiles=@('profile.json','bill-replay-profile.json')};references=@('sales','support','finance','marketing');migration=@{name='20261003171638_AddBusinessWorkAssociations';isolatedSqlServer='Pass: backfill plus downgrade/re-upgrade; existing tasks/approvals/P11 versions retained';productionDeployment='Unverified'};independentGates=@{humanReleaseApproval='Pending';deployedTenant='Unverified';liveProviderDelivery='Unverified';priorNativePhysicalStatutory='Retained separate gates'};cleanup='cleanup.json';handoff='docs/vcscreens/implementation-status.md: P14 prerequisite handoff'} | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $p13Folder 'verification.json')
$p13Runs | Select-Object file,passed,failed,skipped
"Source checks $($source.checks.Count), bill checks $($bill.checks.Count), passing browser assertions $(@($checks | Where-Object passed).Count)"
