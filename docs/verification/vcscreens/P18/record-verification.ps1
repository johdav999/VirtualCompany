param()
$ErrorActionPreference='Stop'
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$accepted=@(
    @{file='p18-api-regression-accepted.trx';suite='API'},
    @{file='p18-api-controls-final-accepted.trx';suite='API'},
    @{file='p18-sql-final-accepted.trx';suite='API'},
    @{file='p18-source-access-accepted.trx';suite='API'},
    @{file='p18-grounding-accepted.trx';suite='Grounding'},
    @{file='p18-web-final-accepted.trx';suite='Web'},
    @{file='p18-wire-final-accepted.trx';suite='Wire'}
)
$allChecks=@()
$runs=@($accepted|ForEach-Object {
    $spec=$_
    [xml]$trx=Get-Content -LiteralPath (Join-Path $PSScriptRoot $spec.file) -Raw
    $counter=$trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
    $results=@($trx.TestRun.Results.UnitTestResult)
    $allChecks+=@($results|ForEach-Object {[pscustomobject]@{suite=$spec.suite;name=$_.testName;outcome=$_.outcome}})
    if([int]$counter.GetAttribute('failed') -ne 0 -or [int]$counter.GetAttribute('notExecuted') -ne 0 -or @($results|Where-Object outcome -ne 'Passed').Count -ne 0){throw "Run is not accepted: $($spec.file)"}
    [pscustomobject]@{file=$spec.file;suite=$spec.suite;total=[int]$counter.GetAttribute('total');passed=[int]$counter.GetAttribute('passed');failed=[int]$counter.GetAttribute('failed');skipped=[int]$counter.GetAttribute('notExecuted');start=$trx.TestRun.Times.start;finish=$trx.TestRun.Times.finish}
})
$summary=[ordered]@{
    recordedUtc=[datetime]::UtcNow
    implementation='Implemented locally; available automatic verification complete; full browser acceptance Blocked'
    runs=$runs
    countsOverlap=$true
    distinctApiChecks=@($allChecks|Where-Object suite -eq 'API'|Select-Object -ExpandProperty name|Sort-Object -Unique).Count
    nativeP18Checks=22
    distinctWebChecks=@($allChecks|Where-Object suite -eq 'Web'|Select-Object -ExpandProperty name|Sort-Object -Unique).Count
    distinctWireChecks=@($allChecks|Where-Object suite -eq 'Wire'|Select-Object -ExpandProperty name|Sort-Object -Unique).Count
    distinctGroundingChecks=@($allChecks|Where-Object suite -eq 'Grounding'|Select-Object -ExpandProperty name|Sort-Object -Unique).Count
    sqlServer='Six GUID-isolated checks: P11/P13/P16 migration round-trips/backfill/uniqueness, P12 review serialization, P18 policy concurrency and P17 period/scope/export. No production database.'
    apiBuild='Passed: build-api-final.log'
    webBuild='Passed within final 224-check rendered and 37-check wire runs'
    uatBuild='Passed: build-uat-final.log'
    modelCheck='No changes since last migration: model-check.log; final settlement correction changes no model'
    diffCheck='Passed: diff-check.log'
    renewal='renewal-source-reconciliation.json; real queue/owners/outbox, controlled AI analysis/contribution runner/recording adapter, exact one decision and version inputs; one native send and zero agent-attributed sends'
    sourceTiming='211 broader checks precede final queue and post-approval reassignment settlement corrections. Final 60-check API/native/work/decision/report run rechecks those affected owners, all 22 P18 cases, actual Finance editor activation/revocation and deterministic renewal setup. Six SQL, 18 source-access, five grounding, 224 Web and 37 wire checks cover their unchanged source/authority boundaries; no source schema or Web change follows their accepted runs. Final API/UAT builds follow the last production correction.'
    diagnostics='Earlier failed/superseded TRX and compiler runs are discovery evidence only, excluded from these counts. Repeat runs are deduplicated by test identity within each suite.'
    browser='Blocked before tabs: node_repl kernel exit 1; Windows helper_unknown_error applying deny-read ACLs; browser-blocker.json'
    graphicalListKeyboardNarrowReferencePhysical='Final browser traversal, focus/narrow/reference comparison and physical CSV save Blocked/Unverified; rendered checks do not establish them'
    liveProviderDeployedNativePhysicalStatutory='Unverified; recording-provider confirmation and isolated SQL do not substitute for these gates'
    liveModel='Autonomous LLM coordination and writing quality unverified; deterministic analysis and controlled contribution runner explicitly scoped'
    human='Pending; test-fixture review is not release approval'
    release2='Reviewable package in ../release-2; full acceptance Blocked; P19 not started'
    preservation='preservation-verification.json: all entry files retained, unchanged earlier evidence, only audited shared additions/corrections'
    cleanup='cleanup.json and sql-cleanup-inventory.json; no detached P18 host; read-only GUID scenario inventory'
}
$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json')
$allChecks|Sort-Object suite,name -Unique|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'accepted-check-inventory.json')
$summary|ConvertTo-Json -Depth 8
