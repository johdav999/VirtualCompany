param([string]$Base='http://127.0.0.1:5317')
$ErrorActionPreference='Stop'
$profile=Invoke-RestMethod "$Base/_uat/p17/profile" -TimeoutSec 10
$headers=@{'X-Dev-Auth-Subject'='p01-owner';'X-Dev-Auth-Email'='p01-owner@example.com'}
$checks=[System.Collections.Generic.List[object]]::new()
function Check([string]$name,[bool]$passed){$checks.Add([pscustomobject]@{name=$name;passed=$passed});if(!$passed){throw "Source check failed: $name"}}
function Read([string]$path){Invoke-RestMethod "$Base$path" -Headers $headers -TimeoutSec 15}
$route="/api/companies/$($profile.company)/agent-supervision"
$work=Read $route
Check 'company and timezone are owning values' ($work.query.companyId -eq $profile.company -and $work.timezone -eq 'Europe/Stockholm')
Check 'company work is one initiative, contributions separate' (@($work.rows|Where-Object metric -eq 'work_in_progress').Count -eq 1 -and @($work.rows|Where-Object metric -eq 'contributions').Count -eq 1)
$outcomes=Read ($route+'?view=outcomes')
foreach($metric in @('provider_confirmed','business_outcome','execution_failure')){
    $detail=@($outcomes.rows|Where-Object metric -eq $metric);$measure=$outcomes.measures|Where-Object code -eq $metric
    Check "$metric reconciles with exact included rows" ($measure.count -eq 1 -and $detail.Count -eq $measure.count)
}
Check 'payment outcome is native batch, unattributed, not a prepared output' (@($outcomes.rows|Where-Object workId -eq $profile.batch|Where-Object agentId).Count -eq 0 -and @($outcomes.rows|Where-Object metric -eq 'prepared_output').Count -eq 0)
$bottleneck=Read ($route+'?view=bottlenecks&metric=approval_wait&responsibility=finance&taskType=finance_review')
$interval=@($bottleneck.rows)[0]
Check 'bottleneck identifies exact work and decision' ($bottleneck.rows.Count -eq 1 -and $interval.workId -eq $profile.initiative -and $interval.approvalId -eq $profile.approval -and $interval.secondsInPeriod -gt 0 -and $interval.ongoing)
$detail=Read "/api/companies/$($profile.company)/agent-work/initiative/$($profile.initiative)"
Check 'owning work remains awaiting approval' ($detail.state -eq 'awaiting_approval')
$approval=Read "/api/companies/$($profile.company)/approvals/$($profile.approval)"
Check 'exact native decision has current review and retained work identity' ($approval.id -eq $profile.approval -and $approval.targetEntityId -eq $profile.task -and $approval.status -eq 'pending' -and $null -ne $approval.review)
$history=Read "/api/companies/$($profile.company)/agent-work/initiative/$($profile.initiative)/collaboration"
Check 'retained contribution versions are available from owning work' (@($history.artifacts).Count -ge 2)
$export=Read ($route+'/export?view=bottlenecks&metric=approval_wait&responsibility=finance&taskType=finance_review')
Check 'fresh authorized CSV and returned report are one snapshot' ($export.report.rows.Count -eq 1 -and $export.report.rows[0].key -eq $interval.key -and $export.content.Contains($export.report.snapshotHash) -and $export.content.Contains($profile.approval))
$agent=Read ($route+"?view=outcomes&agentId=$($profile.agent)")
Check 'agent attribution remains unavailable for company payment outcome' ($null -eq ($agent.measures|Where-Object code -eq 'business_outcome').count -and @($agent.rows|Where-Object workId -eq $profile.batch).Count -eq 0)
$authority=Read ($route+'?view=authority')
Check 'native retained denial is traceable to actual work' (($authority.measures|Where-Object code -eq 'policy_exceptions').count -eq 1 -and $authority.rows[0].workId -eq $profile.initiative)
foreach($path in @("/api/companies/22222222-2222-2222-2222-222222222222/agent-supervision?agentId=$($profile.agent)",($route+'?agentId=99999999-9999-9999-9999-999999999999'))){
    $response=Invoke-WebRequest "$Base$path" -Headers $headers -SkipHttpErrorCheck -TimeoutSec 15
    Check 'foreign agent scope returns no report' ($response.StatusCode -eq 404)
}
$denied=Invoke-WebRequest "$Base$route/export" -Headers @{'X-Dev-Auth-Subject'='p01-member';'X-Dev-Auth-Email'='p01-member@example.com'} -SkipHttpErrorCheck -TimeoutSec 15
Check 'unpermitted company/member cannot export counts' ($denied.StatusCode -eq 403)
[pscustomobject]@{recordedUtc=[DateTime]::UtcNow;profile=$profile;checks=$checks;count=$checks.Count;source='Real composed provider-free HTTP host; synthetic retained stage records, no browser/provider acceptance'}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $PSScriptRoot 'http-source-reconciliation.json')
"Passed $($checks.Count) HTTP source checks"
