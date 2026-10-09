$ErrorActionPreference='Stop'
$packet=$PSScriptRoot
$company='11111111-1111-1111-1111-111111111111'
$agent='b5c08109-516b-43cc-ad87-f810ad571215'
$task='92bfd0c2-bd08-4b76-9d3d-fd681697b463'
$base="http://127.0.0.1:5319/api/companies/$company"
$headers=@{'X-Dev-Auth-Subject'='p01-owner';'X-Dev-Auth-Email'='p01-owner@example.com'}
function Read-Source($path,$file) {
    $response=Invoke-WebRequest "$base/$path" -Headers $headers -UseBasicParsing -TimeoutSec 15
    $response.Content|Set-Content -LiteralPath (Join-Path $packet $file)
    return $response.Content|ConvertFrom-Json
}
$before=Get-Content (Join-Path $packet 'source-before.json') -Raw|ConvertFrom-Json
$after=Read-Source "authority-explanation?agentId=$agent&workKind=task&workId=$task" 'source-after.json'
$settings=Read-Source "authority-explanation?agentId=$agent" 'source-settings.json'
$repeated=Read-Source "authority-explanation?agentId=$agent&workKind=task&workId=$task" 'source-repeated.json'
$config=Read-Source 'operating/configuration' 'source-configuration-final.json'
$grants=@(Read-Source "finance/autonomy/grants?agentId=$agent" 'source-grants-final.json')
$caps=Read-Source "agents/$agent/capabilities" 'source-capabilities-final.json'
$work=Read-Source "agent-work/task/$task" 'source-work-final.json'
$checks=[System.Collections.Generic.List[object]]::new()
function Check($name,$pass) {$checks.Add([pscustomobject]@{check=$name;pass=[bool]$pass})}
Check 'Exact company, task and agent identity' ($after.companyId-eq$company -and $after.workKind-eq'task' -and $after.workId-eq$task -and $after.agents[0].id-eq$agent)
Check 'Company intent and version match owner configuration' ($after.companyIntent-eq$config.autonomyLevel -and $after.configurationVersion-eq$config.version)
Check 'Task volume matches company configuration' (($after.companyLimits|Where-Object label -eq 'Tasks per cycle').value-eq[string]$config.maximumTasksPerCycle)
Check 'Monetary budget preserves decimal owner value' ([decimal](($after.companyLimits|Where-Object label -eq 'Monetary budget per cycle').value)-eq$config.maximumMonetaryBudgetPerCycle)
Check 'Agent authority fingerprint matches capability owner' ($after.agents[0].authorityHash-eq$caps.authorityHash -and $after.agents[0].authorityVersion-eq$caps.authorityVersion)
Check 'Agent profile matches owner without company level conversion' ($after.agents[0].profileLevel-eq$caps.autonomyLevel)
Check 'Work and settings action receipts are identical' (($after.agents[0].actions|ConvertTo-Json -Depth 10 -Compress)-eq($settings.agents[0].actions|ConvertTo-Json -Depth 10 -Compress))
Check 'Work uses actual dispatch decision policy' ($after.taskPolicy.reasonCode-eq'within_autonomy' -and $after.taskType-eq'finance_review')
Check 'Unbound settings policy explicitly not evaluated' ($settings.taskPolicy.reasonCode-eq'task_policy_not_bound')
Check 'Expired grant reason from Finance evaluator' ($after.agents[0].grants[0].check.reasonCode-eq'finance_autonomy_grant_expired')
Check 'Grant version and recorded expiry match Finance owner' ($after.agents[0].grants[0].version-eq$grants[0].versions[0].versionNumber -and $after.agents[0].grants[0].expiresUtc-eq$grants[0].versions[0].expiresUtc)
Check 'Grant amount and volume match owner bounds' ([decimal](($after.agents[0].grants[0].limits|Where-Object label -eq 'Amount per run').value)-eq$grants[0].versions[0].maximumAmountPerRun -and [int](($after.agents[0].grants[0].limits|Where-Object label -eq 'Records per run').value)-eq$grants[0].versions[0].maximumRecordsPerRun)
Check 'Expiry changes projection without changing company version' ($before.projectionHash-ne$after.projectionHash -and $before.configurationVersion-eq$after.configurationVersion -and $before.agents[0].grants[0].check.reasonCode-eq'finance_autonomy_evidence_stale')
Check 'Repeated unchanged read has stable projection fingerprint' ($after.projectionHash-eq$repeated.projectionHash)
Check 'Unknown configured tool remains explicitly unsupported' (@($after.agents[0].actions|Where-Object {$_.toolName-eq'finance.removed_tool' -and $_.check.state-eq'not_implemented' -and $_.check.reasonCode-eq'configured_tool_outside_catalogue'}).Count-eq1)
Check 'Unknown configured tool is absent from executable owner catalog' (@($caps.effectiveTools|Where-Object toolName -eq 'finance.removed_tool').Count-eq0)
Check 'Integration restriction remains explicit' (@($after.agents[0].actions|Where-Object {$_.toolName-eq'list_transactions' -and $_.check.state-eq'integration_unavailable'}).Count-eq1)
Check 'Finance execute still requires human review' (@($after.agents[0].actions|Where-Object {$_.toolName-eq'categorize_transaction' -and $_.check.reviewRequired}).Count-eq1)
Check 'Company external mandatory human review retained' (($after.companyLimits|Where-Object label -eq 'Mandatory review').value.Contains('always require human review'))
Check 'Read leaves work planned' ($work.kind-eq'task' -and $work.id-eq$task -and $work.state-eq'planned')
$noStore=Invoke-WebRequest "$base/authority-explanation?agentId=$agent" -Headers $headers -UseBasicParsing
Check 'Authority transport explicitly no-store' ([string]$noStore.Headers['Cache-Control'] -match 'no-store')
function Status($url,$method,$auth) {try {(Invoke-WebRequest $url -Method $method -Headers $auth -UseBasicParsing -TimeoutSec 15).StatusCode} catch {if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode};throw}}
Check 'Cross-company nonmember forbidden' ((Status "http://127.0.0.1:5319/api/companies/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/authority-explanation" 'GET' $headers)-eq403)
Check 'Invalid work pair rejected' ((Status "$base/authority-explanation?workKind=task" 'GET' $headers)-eq400)
Check 'Settings endpoint has no POST mutation' ((Status "$base/authority-explanation" 'POST' $headers)-eq405)
$dual=@{'X-Dev-Auth-Subject'='p01-dual';'X-Dev-Auth-Email'='p01-dual@example.com'}
Check 'Sales and Marketing lens cannot view Finance agent' ((Status "$base/authority-explanation?agentId=$agent" 'GET' $dual)-eq404)
$result=[pscustomobject]@{companyId=$company;agentId=$agent;taskId=$task;checks=$checks;passed=@($checks|Where-Object pass).Count;failed=@($checks|Where-Object {-not $_.pass}).Count;beforeHash=$before.projectionHash;afterHash=$after.projectionHash;configurationVersion=$after.configurationVersion}
$result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $packet 'source-reconciliation.json')
$checks|Where-Object {-not $_.pass}|Format-Table
"Source checks: $($result.passed) passed, $($result.failed) failed"
if($result.failed){exit 1}
