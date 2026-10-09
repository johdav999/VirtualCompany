param([string]$BaseUrl='http://127.0.0.1:5319')
$ErrorActionPreference='Stop'
if (([uri]$BaseUrl).Host -notin @('127.0.0.1','localhost')) { throw 'Disposable localhost fixture only.' }
$p=Get-Content (Join-Path $PSScriptRoot 'profile.json') -Raw | ConvertFrom-Json
$c=$p.companyId
$checks=[System.Collections.Generic.List[object]]::new()
$reads=[System.Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}); if (!$Passed) { throw $Name } }
function Read-Fixture([string]$Path,[string]$Name,[string]$Subject='p01-owner',[int]$Expected=200) {
 $r=Invoke-WebRequest "$BaseUrl$Path" -Headers @{'X-Dev-Auth-Subject'=$Subject;'X-Dev-Auth-Email'="$Subject@example.com"} -SkipHttpErrorCheck -TimeoutSec 15
 $reads.Add(@{path=$Path;subject=$Subject;status=[int]$r.StatusCode})
 Check "Read $Name" ([int]$r.StatusCode -eq $Expected)
 if ($Expected -ne 200) { return }
 [IO.File]::WriteAllText((Join-Path $PSScriptRoot "$Name.json"),$r.Content)
 return ($r.Content | ConvertFrom-Json -Depth 70)
}
foreach($kind in @('deal','case','invoice','bill','campaign','brief')) {
 $b=Read-Fixture "/api/companies/$c/agent-work/business/$kind/$($p.records.$kind)" "source-$kind"
 Check "$kind exact record and task" ($b.recordId -eq $p.records.$kind -and $b.companyId -eq $c -and @($b.work | Where-Object id -eq $p.tasks.$kind).Count -eq 1)
 $a=Read-Fixture "/api/companies/$c/approvals/$($p.approvals.$kind)" "decision-$kind"
 $expected=@{deal='stale';case='changes_requested';invoice='rejected';bill='pending';campaign='changes_requested';brief='approved'}[$kind]
 Check "$kind decision state" ($a.status -eq $expected -and $a.targetEntityId -eq $p.tasks.$kind)
 Check "$kind same decision and reason in business view" (@($b.decisions | Where-Object {$_.approvalId -eq $a.id -and $_.reason -eq $a.rationaleSummary}).Count -eq 1)
 if($kind -eq 'case') { Check 'Support draft review and failed delivery retained' ($b.artifacts[0].recordedState -eq 'needs_review' -and $b.artifacts[0].executionState -eq 'failed') }
 if($kind -eq 'campaign') { Check 'Marketing content and delivery distinct' (@($b.artifacts | Where-Object {$_.kind -eq 'channel_action' -and $_.executionState -eq 'retry_scheduled'}).Count -eq 1 -and @($b.artifacts | Where-Object {$_.kind -eq 'content_brief' -and $_.recordedState -eq 'submitted'}).Count -eq 1) }
 Read-Fixture "/api/companies/22222222-2222-2222-2222-222222222222/agent-work/business/$kind/$($p.records.$kind)" "foreign-$kind" 'p01-owner' 404
 if($kind -eq 'deal') {
  $prior=@($b.work | Where-Object title -eq 'P11 renewal proposal review')[0]
  $e=Read-Fixture "/api/companies/$c/agent-work/task/$($prior.id)/collaboration" 'p11-preserved'
  Check 'P11 six versions and three handoffs pending' ($e.artifacts.Count -eq 6 -and $e.handoffs.Count -eq 3 -and $e.outcomeState -eq 'awaiting_approval')
 }
}
$p12=Read-Fixture '/_uat/p12/profile' 'p12-fixture-profile'
foreach($key in @('approve','reject','changes','stale','expired','delivery')) {
 $a=Read-Fixture "/api/companies/$c/approvals/$($p12.$key)" "p12-preserved-$key"
 Check "P12 $key decision untouched" ($a.status -eq 'pending' -and !$a.steps[0].decidedAt)
}
@{observedUtc=[datetime]::UtcNow.ToString('o');scope='Authenticated GET reconciliation before final bill replay host restart; no mutation/provider command.';checks=$checks;reads=$reads} | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $PSScriptRoot 'source-reconciliation.json')
"$($checks.Count) checks passed across $($reads.Count) reads."
