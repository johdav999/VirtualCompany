$ErrorActionPreference='Stop'
$packet='docs/verification/vcscreens/P24'
function Results($names){
    $latest=@{}
    foreach($name in $names){
        [xml]$trx=Get-Content -LiteralPath "$packet/test-results/$name"
        foreach($result in $trx.TestRun.Results.UnitTestResult){$latest[$result.testName]=[pscustomobject]@{name=$result.testName;outcome=$result.outcome;file=$name}}
    }
    $rows=@($latest.Values | Sort-Object name)
    if(@($rows | Where-Object outcome -ne 'Passed').Count){throw 'Latest test results include an unresolved failure or skip.'}
    return $rows
}
$api=Results @('api-complete.trx','api-final.trx','support-native.trx')
$web=Results @('web-accepted-final.trx')
$wire=Results @('wire-current.trx','wire-affected-final.trx')
$rules=Results @('support-rules-final.trx')
$browser=Get-Content -LiteralPath "$packet/browser-accepted.json" -Raw | ConvertFrom-Json
if(@($browser.results | Where-Object passed -ne $true).Count -or $browser.results.Count -ne 4 -or $browser.errors.Count){throw 'Browser acceptance incomplete.'}
$verification=@{
    phase='P24';timeUtc=[DateTime]::UtcNow.ToString('O');implementation='Complete locally';humanApproval='Pending'
    api=@{count=$api.Count;sqlCount=@($api | Where-Object name -match 'SqlServerTests').Count;results=$api}
    web=@{count=$web.Count;results=$web};wire=@{count=$wire.Count;results=$wire};rules=@{count=$rules.Count;results=$rules}
    browser=@{passed=4;errors=0;mode='Fresh disposable headless Edge; real Web and composed fixture API';artifact='browser-accepted.json'}
    supersession=@{file='api-complete.trx';diagnosticFailure='Same_instant_committed_state_sequence_reproduces_reopening_and_empty_samples_remain_unavailable';fixedBy='api-final.trx';reason='Exclude imported current-state baselines from lifecycle cohorts.'}
    builds=@{api='Final native dependencies compiled successfully by api-final.log';web='web-build-accepted.log';uat='uat-build-final.log';model='model-check.log: no pending changes'}
    sourceRevalidation='Full typed wire 52 passes plus two affected final-source contract passes. Final API 17 passes supersede earlier affected results; other 38 broad passes and 14 native Support regressions retained.'
    limitations=@('User in-app browser initialization ACL failure','Deployed migration unverified','Provider acceptance unverified','Physical CSV saving unverified','Human approval pending')
}
$verification | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath "$packet/verification.json"
$entry=Get-Content -LiteralPath "$packet/entry-manifest.json" -Raw | ConvertFrom-Json
$baseline=@{};foreach($item in $entry.files){$baseline[$item.path]=$item.sha256}
$paths=@(rg --files --hidden --no-ignore src tests) | ForEach-Object {$_.Replace('\','/')} | Where-Object {$_ -notmatch '(^|/)(bin|obj|node_modules|artifacts|\.codex-build|\.codex|\.git|tmp)(/|$)'}
$paths+=@('docs/vcscreens/implementation-status.md','docs/vcscreens/screen-report-register.md','docs/ui-route-inventory.md')
$changes=foreach($path in $paths | Sort-Object -Unique){$hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;if(!$baseline.ContainsKey($path) -or $baseline[$path] -ne $hash){[pscustomobject]@{path=$path;sha256=$hash;previousSha256=$baseline[$path];change=if($baseline.ContainsKey($path)){'Modified since P24 entry'}else{'Added since P24 entry'}}}}
@{timeUtc=[DateTime]::UtcNow.ToString('O');entryHead=$entry.head;files=@($changes);scope='P24 entry-relative source/test changes; excludes runtime/build outputs and unchanged P11-P23 work.'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$packet/source-manifest.json"
@{api=$api.Count;sql=@($api | Where-Object name -match 'SqlServerTests').Count;web=$web.Count;wire=$wire.Count;rules=$rules.Count;sources=@($changes).Count} | ConvertTo-Json
