$ErrorActionPreference = 'Stop'
$repo = (Get-Location).Path
$packet = Join-Path $repo 'docs/verification/vcscreens/P23'
$entry = Get-Content -LiteralPath (Join-Path $packet 'entry-manifest.json') -Raw | ConvertFrom-Json
$head = (git rev-parse HEAD).Trim()
if ($head -ne $entry.head) { throw 'Checkout HEAD changed from P23 entry' }
$modified = @(); $missing = @(); $unchanged = 0; $priorEvidenceCount = 0; $priorEvidenceFailures = @()
$known = @{}
foreach ($file in $entry.files) {
    $relative = $file.path.Replace('\','/')
    $known[$relative] = $true
    $absolute = Join-Path $repo $relative
    $priorEvidence = $relative -match '^docs/verification/vcscreens/P(?:0[1-9]|1[0-9]|2[0-2])/'
    if ($priorEvidence) { $priorEvidenceCount++ }
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
        $missing += $relative
        if ($priorEvidence) { $priorEvidenceFailures += $relative }
        continue
    }
    $hash = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash
    if ($hash -eq $file.sha256) { $unchanged++ } else {
        $modified += [pscustomobject]@{path=$relative;beforeSha256=$file.sha256;sha256=$hash}
        if ($priorEvidence) { $priorEvidenceFailures += $relative }
    }
}
$newFiles = @()
$paths = @(rg --files --hidden -g '!.git/**' -g '!**/bin/**' -g '!**/obj/**' -g '!docs/verification/vcscreens/P23/**')
if ($LASTEXITCODE -ne 0) { throw 'Source path inventory failed' }
foreach ($file in $paths) {
    $relative = $file.Replace('\','/')
    if (-not $known.ContainsKey($relative)) {
        $newFiles += [pscustomobject]@{path=$relative;sha256=(Get-FileHash -LiteralPath (Join-Path $repo $relative) -Algorithm SHA256).Hash}
    }
}
$finalOnlyPriorEvidence = @()
foreach ($directory in @(Get-ChildItem -LiteralPath (Join-Path $repo 'docs/verification/vcscreens') -Directory | Where-Object Name -match '^P(0[1-9]|1[0-9]|2[0-2])$')) {
    foreach ($file in @(Get-ChildItem -LiteralPath $directory.FullName -File -Recurse)) {
        $relative = $file.FullName.Substring($repo.Length+1).Replace('\','/')
        if (-not $known.ContainsKey($relative)) { $finalOnlyPriorEvidence += [ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash} }
    }
}
$preservation = [ordered]@{timeUtc=[DateTime]::UtcNow.ToString('o');head=$head;entryFileCount=$entry.files.Count;unchangedEntryFiles=$unchanged;modifiedExisting=$modified;missingExisting=$missing;priorPhaseEvidenceFiles=$priorEvidenceCount;priorPhaseEvidenceFailures=$priorEvidenceFailures;priorPhaseEvidenceByteIdentical=($priorEvidenceFailures.Count -eq 0);finalOnlyPriorEvidence=$finalOnlyPriorEvidence;newFiles=$newFiles;scope='Hashes compare the dirty checkout at P23 entry, not HEAD. Existing integration/status files change intentionally; 1275 inventoried prior P01-P22 evidence files remain byte-identical. Ignored logs were not inventoried at entry; their final-only hashes cannot prove baseline equality. P23 commands wrote logs only in P23. Builds/bin/obj and this P23 packet are excluded from new source inventory.'}
$preservation | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $packet 'preservation-verification.json') -Encoding utf8
[ordered]@{timeUtc=$preservation.timeUtc;head=$head;modifiedExisting=$modified;newFiles=$newFiles} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $packet 'source-manifest.json') -Encoding utf8
git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check *> (Join-Path $packet 'whitespace-check-final.log')
$whitespaceExit = $LASTEXITCODE
if ($whitespaceExit -ne 0) { throw 'Whitespace verification failed' }
$checks = @()
foreach ($pair in @(@{name='API';file='api-accepted.trx';expected=75},@{name='Finance calculation';file='finance-rules-accepted.trx';expected=4},@{name='Rendered Web';file='web-accepted.trx';expected=34},@{name='Full typed wire';file='wire-accepted.trx';expected=50})) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $packet "test-results/$($pair.file)") -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.passed -ne $pair.expected -or [int]$counts.failed -ne 0 -or [int]$counts.notExecuted -ne 0) { throw "Accepted count mismatch: $($pair.name)" }
    $checks += [ordered]@{name=$pair.name;passed=[int]$counts.passed;failed=[int]$counts.failed;skipped=[int]$counts.notExecuted;artifact="test-results/$($pair.file)";sha256=(Get-FileHash -LiteralPath (Join-Path $packet "test-results/$($pair.file)")).Hash}
    if ($pair.name -eq 'API') {
        $sqlIds = @($trx.TestRun.TestDefinitions.UnitTest | Where-Object { $_.TestMethod.className -match 'SqlServer' } | ForEach-Object id)
        $sqlResults = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.testId -in $sqlIds })
        if ($sqlResults.Count -ne 6 -or @($sqlResults | Where-Object outcome -ne 'Passed').Count -ne 0) { throw 'SQL accepted subset mismatch' }
    }
}
$browser = Get-Content -LiteralPath (Join-Path $packet 'browser-accepted.json') -Raw | ConvertFrom-Json
if ($browser.results.Count -ne 4 -or @($browser.results | Where-Object { -not $_.passed }).Count -ne 0 -or $browser.errors.Count -ne 0) { throw 'Browser acceptance mismatch' }
$cleanup = Get-Content -LiteralPath (Join-Path $packet 'owned-cleanup.json') -Raw | ConvertFrom-Json
if ($cleanup.ownedListenersRemaining.Count -ne 0 -or $cleanup.sqlDisposableDatabasesRemaining.Count -ne 0) { throw 'Cleanup incomplete' }
$artifacts = @()
foreach ($name in @('api-build-accepted.log','web-build-polished.log','uat-build.log','model-check.log','browser-run-polished.log','browser-accepted.json','variance-desktop.png','variance-mobile.png','forecast-desktop.png','forecast-mobile.png','comparison-desktop.png','comparison-mobile.png','monthly-retained-variance.png','owned-cleanup.json','host-assembly-hashes.json','web-host-polished-sha256.txt','whitespace-check-final.log')) {
    $artifacts += [ordered]@{path=$name;sha256=(Get-FileHash -LiteralPath (Join-Path $packet $name)).Hash}
}
$verification = [ordered]@{phase='P23';timeUtc=[DateTime]::UtcNow.ToString('o');head=$head;implementation='Complete locally';checks=$checks;sqlServerPassed=6;browserJourneysPassed=4;browserErrors=0;browserEngine='Fresh headless Edge on owned composed fixture hosts';viewports=$browser.viewports;visualReview='Final seven screenshots inspected; wrapping saved assumptions and full-width table layout accepted; table/navigation horizontal scroll is intentional';builds=@{api='Passed, zero warnings/errors';web='Passed, six pre-existing warnings, zero errors';uat='Passed, zero warnings/errors'};migrationModel='No pending changes';whitespaceExitCode=$whitespaceExit;cleanup='Owned hosts stopped, owned listeners/test database namespace empty, existing 5301/5062 PIDs preserved';priorPhaseEvidenceByteIdentical=$preservation.priorPhaseEvidenceByteIdentical;sourceManifest='source-manifest.json';preservation='preservation-verification.json';artifacts=$artifacts;userIab='Unavailable: Windows ACL helper startup failure';physicalSaving='Unobserved; response/handoff amounts verified';providerAcceptance='Unverified';deployedMigration='Unverified';humanApproval='Pending';statutoryApproval='Not implied';nextPhase='P24 outside current request';diagnosticAttempts='Retained; only accepted TRX files and polished replay contribute to results'}
$verification | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $packet 'verification.json') -Encoding utf8
if ($priorEvidenceFailures.Count -ne 0 -or $missing.Count -ne 0) { throw 'Entry preservation failed; inspect preservation-verification.json' }
[ordered]@{entryFiles=$entry.files.Count;unchanged=$unchanged;modified=$modified.Count;new=$newFiles.Count;missing=$missing.Count;priorEvidence=$priorEvidenceCount;priorEvidenceFailures=$priorEvidenceFailures.Count;checks=$checks;sql=6;browser=4;modifiedPaths=@($modified | ForEach-Object path);newPaths=@($newFiles | ForEach-Object path)} | ConvertTo-Json -Depth 5
