param([string]$BaseUrl='http://127.0.0.1:5319')
$ErrorActionPreference='Stop'
if (([uri]$BaseUrl).Host -notin @('127.0.0.1','localhost')) { throw 'Only disposable localhost fixtures are supported.' }
$company='11111111-1111-1111-1111-111111111111'
$profile=Get-Content (Join-Path $PSScriptRoot 'profile.json') -Raw | ConvertFrom-Json
$checks=[System.Collections.Generic.List[object]]::new()
$reads=[System.Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}); if (!$Passed) { throw $Name } }
function Read-Fixture([string]$Path,[string]$Name,[string]$Subject='p01-owner',[int]$Expected=200) {
    $r=Invoke-WebRequest "$BaseUrl$Path" -Headers @{'X-Dev-Auth-Subject'=$Subject;'X-Dev-Auth-Email'="$Subject@example.com"} -SkipHttpErrorCheck -TimeoutSec 15
    $reads.Add(@{path=$Path;subject=$Subject;status=[int]$r.StatusCode})
    Check "Read $Name" ([int]$r.StatusCode -eq $Expected)
    if ($Expected -ne 200) { return }
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot "$Name.json"),$r.Content)
    return ($r.Content | ConvertFrom-Json -Depth 40)
}
foreach ($name in @('approve','reject','changes','stale','expired','delivery')) {
    $a=Read-Fixture "/api/companies/$company/approvals/$($profile.$name)" "approval-$name"
    $expected=@{approve='approved';reject='rejected';changes='changes_requested';stale='stale';expired='pending';delivery='approved'}[$name]
    Check "$name exact identity and status" ($a.id -eq $profile.$name -and $a.status -eq $expected -and $a.companyId -eq $company)
    if ($name -in @('approve','reject','changes','delivery')) {
        Check "$name reviewer time recorded" ($a.steps[0].decidedAt -and $a.steps[0].decidedByUserId -and $a.steps[0].decidedByUserId -eq $a.requiredUserId -and $a.steps[0].reviewerName)
    }
    if ($name -in @('reject','changes','stale')) { Check "$name original work blocked" ($a.review.executionStatus -eq 'blocked') }
    if ($name -eq 'expired') { Check 'Expired proposal has no permitted decision' (!$a.review.canDecide -and [datetime]$a.review.expiresAt -lt [datetime]::UtcNow) }
    if ($name -eq 'delivery') { Check 'Approval separate from scheduled owner workflow' ($a.review.executionStatus -eq 'scheduled') }
    if ($name -eq 'approve') {
        $work=Read-Fixture "/api/companies/$company/agent-work/task/$($a.targetEntityId)" 'approved-agent-work'
        Check 'Completed review retains same approval link' (@($work.relatedRecords | Where-Object {$_.route -like "*itemId=$($a.id)*"}).Count -eq 1)
    }
}
$p11=Get-Content (Join-Path $PSScriptRoot 'p11-approval.json') -Raw | ConvertFrom-Json
$original=Read-Fixture "/api/companies/$company/approvals/$($p11.id)" 'p11-approval-preserved'
Check 'P11 human review remains pending' ($original.status -eq 'pending' -and !$original.steps[0].decidedAt)
$collaboration=Read-Fixture "/api/companies/$company/agent-work/task/$($p11.targetEntityId)/collaboration" 'p11-collaboration-preserved'
Check 'P11 immutable artifacts and handoffs retained' ($collaboration.artifacts.Count -eq 6 -and $collaboration.handoffs.Count -eq 3 -and $collaboration.outcomeState -eq 'awaiting_approval')
Read-Fixture "/api/companies/$company/approvals/$($p11.id)" 'restricted-transitive-review' 'p01-dual' 404
Read-Fixture "/api/companies/22222222-2222-2222-2222-222222222222/approvals/$($profile.approve)" 'foreign-review' 'p01-owner' 404
$delivery=Get-Content (Join-Path $PSScriptRoot 'controlled-delivery.json') -Raw | ConvertFrom-Json
Check 'Controlled adapter called once across two dispatcher runs' ($delivery.calls -eq 1 -and $delivery.status -eq 'scheduled' -and $delivery.externalEventId -eq 'p12-controlled-event')
@{observedUtc=[datetime]::UtcNow.ToString('o');scope='Synthetic localhost fixtures; authenticated GET only. No decisions or provider commands.';checks=$checks;reads=$reads} | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $PSScriptRoot 'source-reconciliation.json')
"$($checks.Count) checks passed across $($reads.Count) authenticated reads."
