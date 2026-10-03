$ErrorActionPreference = 'Stop'
$packet = $PSScriptRoot
$root = [IO.Path]::GetFullPath((Join-Path $packet '../../../..'))
$files = @('p09-web-final.trx','p09-api.trx','p09-wire.trx','p09-finance.trx',
    'p09-grounding.trx','p09-cash-final.trx','p09-cash-presenter-final.trx',
    'p09-treasury-finance.trx','p09-treasury-web.trx')
$tests = foreach ($file in $files) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $packet $file) -Raw
    $c = $trx.TestRun.ResultSummary.Counters
    if (!$c -or [int]$c.total -eq 0 -or [int]$c.failed -ne 0 -or [int]$c.passed -ne [int]$c.total) {
        throw "Non-passing or empty final test result: $file"
    }
    [pscustomobject]@{results=$file;passed=[int]$c.passed;failed=[int]$c.failed;notExecuted=[int]$c.notExecuted;total=[int]$c.total}
}
$tests | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $packet 'final-test-counts.json') -Encoding utf8
$sources = Get-Content -LiteralPath (Join-Path $packet 'reconciliation/results.json') -Raw | ConvertFrom-Json
if (@($sources.checks).Count -ne 39 -or @($sources.checks | Where-Object { !$_.passed }).Count) { throw 'Source reconciliation failed' }
$hashes = Get-Content -LiteralPath (Join-Path $packet 'reconciliation/hashes.json') -Raw | ConvertFrom-Json
foreach ($hash in $hashes) {
    if ((Get-FileHash -LiteralPath (Join-Path $packet "reconciliation/$($hash.file)") -Algorithm SHA256).Hash -ne $hash.sha256) {
        throw "Source evidence changed: $($hash.file)"
    }
}
$cash = Get-Content -LiteralPath (Join-Path $packet 'treasury-missing-final.json') -Raw | ConvertFrom-Json
if ($cash.liquidity.riskLevel -ne 'missing' -or @($cash.liquidity.projection).Count -ne 0 -or
    @($cash.laura.citations | Where-Object sourceType -eq 'cash_projection').Count -ne 0 -or
    $cash.freshestEvidenceUtc -or $cash.stalestEvidenceUtc -or
    @($cash.exceptions | Where-Object { $_.kind -eq 'liquidity' -and ($null -ne $_.amount -or $_.explanation -match 'Projected cash is') }).Count) {
    throw 'Unsupported final Treasury cash claim'
}
$before = Get-Content -LiteralPath (Join-Path $packet 'working-tree-before.txt') | Where-Object { $_.Length -ge 4 }
$missing = @($before | ForEach-Object { $_.Substring(3).Trim('"') } | Where-Object { !(Test-Path -LiteralPath (Join-Path $root $_)) })
if ($missing.Count) { throw "Pre-existing paths missing: $missing" }
$preservation = [ordered]@{baseline='37834c7f';existingPathsChecked=@($before).Count;missingPaths=$missing;
    priorPacketsUntouched='P01-P08 evidence files were not edited';changes='No reset, checkout, clean, commit or migration';verifiedUtc=[DateTime]::UtcNow.ToString('o')}
$preservation | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $packet 'preservation.json') -Encoding utf8
$builds = foreach ($name in @('api','web','uat')) {
    $log = "$name-build-final.txt"
    $text = Get-Content -LiteralPath (Join-Path $packet $log) -Raw
    if ($text -notmatch 'Build succeeded\.' -or $text -notmatch '0 Error\(s\)') { throw "Build not verified: $name" }
    $warnings = [regex]::Match($text,'(\d+) Warning\(s\)').Groups[1].Value
    [pscustomobject]@{project=$name;result='Passed';warnings=[int]$warnings;errors=0;log=$log}
}
$browser = Get-Content -LiteralPath (Join-Path $packet 'browser-final-checks.json') -Raw | ConvertFrom-Json
$cleanup = Get-Content -LiteralPath (Join-Path $packet 'cleanup.json') -Raw | ConvertFrom-Json
if (@($cleanup.remainingOwnedProcesses).Count -or @($cleanup.remainingTestListeners).Count) { throw 'Runtime cleanup incomplete' }
$verification = [ordered]@{
    phase='P09';baseline='37834c7f';recordedUtc=[DateTime]::UtcNow.ToString('o')
    implementationReadiness='Complete within recorded local Release 1 scope';p10Readiness='Ready in this same uncommitted checkout'
    humanReleaseApproval='Pending';statutoryApproval='Unverified'
    environment='Windows; real Blazor Web/composed authenticated API; disposable SQLite; synthetic owner/dual/member; workers disabled'
    automated=[ordered]@{suites=$tests;passedExecutions=($tests | Measure-Object passed -Sum).Sum;failed=0;fullRepositoryRun=$false;overlap='Cash API overlaps integrated API; Treasury Web overlaps Finance journey/presenter sets. Executions are not distinct-test count.';commands='commands.md';existingCompilationWarnings='Nullable/analyzer warnings retained; adapter NU1900 vulnerability-feed warnings'}
    builds=$builds
    browser=[ordered]@{dailyJourneys='F09-01 through F09-05 locally verified with persisted readback and owning reports';authority='F09-06 dual/member/foreign-company checks verified';coverageAndContext='F09-07 partial/stale/missing/currency/version/retry/returns verified';layout='F09-08 desktop/one-main and earlier confirmed Marketing mobile/keyboard verified';finalChecks=$browser;manifest='browser-captures.json';uat='uat.md';fixtureReset='Original post-action hashed evidence saved before reset; final cash/header fresh fixture'}
    sourceReconciliation=[ordered]@{passed=39;results='reconciliation/results.json';hashes='reconciliation/hashes.json';script='reconcile.ps1';definitions='../release-1/report-reconciliation.md';finalTreasury='treasury-missing-final.json';finalTreasurySha256=(Get-FileHash -LiteralPath (Join-Path $packet 'treasury-missing-final.json') -Algorithm SHA256).Hash}
    persistence=[ordered]@{schemaChanges='None';migration='Not applicable to schema-free P01-P09 screen work';sqlServer='Unverified';actions='Company follow-up and Sales commitment/review, Marketing revision, invoice pending review, Accounting internal task, Support handoff';externalEffects='No publication/customer send/payment/ledger posting/period lock; existing owning controls preserved'}
    issues='issue-ledger.md';reviewPackage='../release-1/README.md';preservation=$preservation;cleanup='cleanup.json'
    openGates=@('Final Today mobile replay: adapter override did not apply','Deployed real tenant/authentication/SQL Server','Controlled provider/media/publication/customer reply/payment settlement','Final physical CSV save and native print/PDF','Named human product/release decisions','Qualified statutory approval')
}
$verification | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $packet 'verification.json') -Encoding utf8
$markdown = @(Get-ChildItem -LiteralPath $packet -Filter '*.md') + @(Get-ChildItem -LiteralPath (Join-Path $packet '../release-1') -Filter '*.md')
$linkCount = 0
foreach ($doc in $markdown) {
    foreach ($match in [regex]::Matches((Get-Content -LiteralPath $doc.FullName -Raw),'\]\(([^)]+)\)')) {
        $target = $match.Groups[1].Value.Trim('<','>')
        if ($target -match '^[a-zA-Z]+:|^#') { continue }
        $target = [Uri]::UnescapeDataString(($target -split '#')[0])
        if (!(Test-Path -LiteralPath (Join-Path $doc.DirectoryName $target))) { throw "Missing link in $($doc.Name): $target" }
        $linkCount++
    }
}
[ordered]@{verifiedUtc=[DateTime]::UtcNow.ToString('o');markdownFiles=$markdown.Count;localLinksChecked=$linkCount;sourceHashesChecked=@($hashes).Count;finalTrxChecked=$tests.Count;result='Passed'} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packet 'package-checks.json') -Encoding utf8
"P09 package verified: $($tests.Count) final suites, $(($tests | Measure-Object passed -Sum).Sum) passing executions, 39 source checks, $linkCount local links."
