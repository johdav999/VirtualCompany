$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$apiFiles = @('p10-api-final-accepted.trx','p10-case-state-final.trx','p10-lifecycle-scope-final.trx')
$acceptedFiles = $apiFiles + @('p10-web-final-accepted.trx','p10-wire-final-accepted.trx')
$runs = @(); $apiNames = @()
foreach ($file in $acceptedFiles) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $PSScriptRoot $file)
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.failed -ne 0 -or [int]$counts.passed -ne [int]$counts.total) { throw "Unaccepted run: $file" }
    $runs += @{file=$file;total=[int]$counts.total;passed=[int]$counts.passed;failed=[int]$counts.failed}
    if ($file -in $apiFiles) { $apiNames += @($trx.TestRun.Results.UnitTestResult.testName) }
}
$builds = @('api-build-final-accepted.txt','web-build-final-accepted.txt','uat-build-final-accepted.txt')
foreach ($file in $builds) { if ((Get-Content -LiteralPath (Join-Path $PSScriptRoot $file) -Raw) -notmatch 'Build succeeded\.') { throw "Unaccepted build: $file" } }
$sources = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'reconciliation/results.json') | ConvertFrom-Json
if (@($sources.checks | Where-Object passed -ne $true).Count) { throw 'Source reconciliation failed.' }
$scopeReplay = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'reconciliation-scope-final/results.json') | ConvertFrom-Json
if ($scopeReplay.checks.Count -ne $sources.checks.Count -or @($scopeReplay.checks | Where-Object passed -ne $true).Count) { throw 'Final scope source replay failed.' }
$captures = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'browser-captures.json') | ConvertFrom-Json
foreach ($capture in $captures.captures) {
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot ('screenshots/'+$capture.file)))) { throw 'Capture missing.' }
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot ('screenshots/'+$capture.name+'.dom.txt')))) { throw 'Capture DOM missing.' }
}
$priorPaths = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'working-tree-before.txt') | Where-Object { $_.Length -gt 3 } | ForEach-Object { $_.Substring(3).Trim('"') })
$missing = @($priorPaths | Where-Object { !(Test-Path -LiteralPath (Join-Path $repository $_)) })
if ($missing.Count) { throw ('Prior path removed: '+($missing -join ', ')) }
$head = (git -C $repository rev-parse HEAD).Trim()
if ($head -ne '37834c7f75d4c1ddd532e1a48464887724a84978') { throw 'Baseline HEAD changed.' }
$priorPackets = @(1..9 | ForEach-Object { 'P'+$_.ToString('00') })
foreach ($packet in $priorPackets) { if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot "../$packet"))) { throw "Prior evidence packet missing: $packet" } }
@{baselineHead=$head;allStartingPathsPresent=$true;startingPaths=$priorPaths.Count;priorEvidencePackets=$priorPackets;changesCommitted=$false;noResetOrCleanup=$true;limit='Starting status records paths, not byte hashes; this does not claim byte-for-byte verification of preexisting modifications.'} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'preservation.json') -Encoding utf8
$cleanup = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'cleanup.json') | ConvertFrom-Json
if (!$cleanup.portsInactive -or !$cleanup.viewportReset) { throw 'Disposable runtime cleanup incomplete.' }
$artifactHashes = @(($acceptedFiles + $builds + @('reconciliation/results.json','reconciliation-scope-final/results.json','browser-captures.json','browser-checks.json','cleanup.json','preservation.json')) | ForEach-Object { @{file=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash} })
@{
    prompt='P10';recordedUtc=[DateTime]::UtcNow.ToString('o');implementation='Complete within P10 scope';localVerification='Passed';humanApproval='Pending';baselineHead=$head
    tests=@{apiDistinct=(@($apiNames | Sort-Object -Unique)).Count;web=72;wire=1;acceptedRuns=$runs;diagnosticRuns='Retained separately; failed and repeated runs are not additional coverage'}
    builds=@{accepted=$builds;nugetAudit='UAT NU1900 metadata warnings; vulnerability lookup unavailable';schema='No P10 entity, schema or migration change'}
    source=@{checks=$sources.checks.Count;passed=$true;script='reconcile.ps1';results='reconciliation/results.json';finalScopeReplay='reconciliation-scope-final/results.json';repeatedChecks=$scopeReplay.checks.Count;window='2,000 rows per family; partial count/filter/page semantics disclosed and tested'}
    browser=@{journeys='F10-01 through F10-09';manifest='browser-captures.json';captures=$captures.captures.Count;actualDesktop='1280 x 720';actualMobile='390 x 844; document width 375';keyboard='State SELECT solid 3px focus outline';fixtures='Multiple disposable sessions; final-source replay IDs may differ from earlier captures';lastAccessCorrection='Authenticated scoped regression verifies unlinked department outcomes and hidden company dependency; unchanged browser fixture journeys retain acceptance'}
    references=@{provider='Built-in ImageGen';board='docs/design/references/agent-work-board-p10-reference.png';detail='docs/design/references/agent-work-detail-p10-reference.png';prompts='Matching saved -prompt.md files';comparison='uat.md'}
    independentGates=@{deployedTenant='Unverified';sqlServer='Unverified';controlledProvider='Unverified';nativeAndPhysicalOutput='Unverified';statutoryApproval='Unverified';namedHumanApproval='Pending'}
    handoff='P11 ready in same preserved uncommitted checkout; implementation.md and implementation-status.md';cleanup='cleanup.json';preservation='preservation.json';artifactHashes=$artifactHashes
} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Encoding utf8
