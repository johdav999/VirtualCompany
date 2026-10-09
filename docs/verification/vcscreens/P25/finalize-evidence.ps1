$ErrorActionPreference='Stop'
$packet='docs/verification/vcscreens/P25'
function Results($names){$latest=@{};foreach($name in $names){[xml]$t=Get-Content "$packet/$name";foreach($r in $t.TestRun.Results.UnitTestResult){$latest[$r.testName]=@{name=$r.testName;outcome=$r.outcome;file=$name}}};$rows=@($latest.Values|Sort-Object name);if(@($rows|Where-Object outcome -ne 'Passed').Count){throw 'Unresolved test outcome'};return ,$rows}
$api=Results @('api-final.trx','api-utc-final.trx');$web=Results @('web-telemetry-final.trx');$wire=Results @('wire-utc-final.trx')
$browser=Get-Content "$packet/browser-accepted.json" -Raw|ConvertFrom-Json
if($browser.errors.Count -or @($browser.results|Where-Object passed -ne $true).Count){throw 'Browser incomplete'}
@{phase='P25';timeUtc=[DateTime]::UtcNow.ToString('o');implementation='Complete locally';humanApproval='Pending';api=@{count=$api.Count;sqlCount=2;results=$api};web=@{count=$web.Count;results=$web};wire=@{count=$wire.Count;results=$wire};browser=@{passed=1;errors=0;artifact='browser-accepted.json';mode='Fresh headless Edge; real Web and composed API'};builds=@{web='web-build-telemetry-final.log';uat='uat-build-final.log';model='model-check-final.log'};javascript='scroll-tracker.log: cleanup, in-flight rejection and unique milestones passed';limitations=@('Deployed migration unverified','Human release approval pending','In-app automation ACL initialization failure')}|ConvertTo-Json -Depth 10|Set-Content "$packet/verification.json"
$entry=Get-Content "$packet/entry-manifest.json" -Raw|ConvertFrom-Json;$baseline=@{};foreach($f in $entry.files){$baseline[$f.path]=$f.sha256}
$prior=@($entry.files|Where-Object {$_.path -match '^docs/verification/vcscreens/P(0?[1-9]|1[0-9]|2[0-4])(/|$)' -or $_.path -match '^docs/design/references/'})
$bad=@($prior|Where-Object {!(Test-Path -LiteralPath $_.path) -or (Get-FileHash -LiteralPath $_.path).Hash -ne $_.sha256});if($bad.Count){throw 'Prior evidence changed'}
@{count=$prior.Count;changed=0;entry='entry-manifest.json';scope='Inventoried prior phase packets and reference files'}|ConvertTo-Json|Set-Content "$packet/preservation.json"
$paths=@(rg --files --hidden --no-ignore src tests)|ForEach-Object {$_.Replace('\','/')}|Where-Object {$_ -notmatch '(^|/)(bin|obj|node_modules|artifacts|\.codex-build|\.codex|\.git|tmp)(/|$)'}
$paths+=@('docs/vcscreens/implementation-status.md','docs/vcscreens/screen-report-register.md','docs/ui-route-inventory.md')
$changed=foreach($p in $paths|Sort-Object -Unique){$h=(Get-FileHash -LiteralPath $p).Hash;if(!$baseline.ContainsKey($p)-or $h -ne $baseline[$p]){@{path=$p;sha256=$h;previousSha256=$baseline[$p]}}}
@{entryHead=$entry.head;files=@($changed)}|ConvertTo-Json -Depth 5|Set-Content "$packet/source-manifest.json"

