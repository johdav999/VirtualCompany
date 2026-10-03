param([string]$BaseUrl = 'http://localhost:5319', [string]$OutputPath = (Join-Path $PSScriptRoot 'reconciliation'))
$ErrorActionPreference = 'Stop'
if ($BaseUrl -notmatch '^http://localhost:[0-9]+$') { throw 'Only the disposable localhost fixture is supported.' }
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
$north = '11111111-1111-1111-1111-111111111111'
$south = '22222222-2222-2222-2222-222222222222'
$checks = [System.Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Condition) {
    $checks.Add([pscustomobject]@{name=$Name; passed=$Condition})
    if (!$Condition) { throw "Failed: $Name" }
}
function Read([string]$Name, [string]$Route, [string]$Company = $north, [string]$Subject = 'p01-owner', [int]$Expected = 200) {
    $response = Invoke-WebRequest -Uri ($BaseUrl + $Route) -Headers @{'X-Company-Id'=$Company;'X-Dev-Auth-Subject'=$Subject;'X-Dev-Auth-Email'="$Subject@example.com"} -TimeoutSec 15 -SkipHttpErrorCheck
    $response.Content | Set-Content -LiteralPath (Join-Path $OutputPath "$Name.json") -Encoding utf8
    Check "$Name HTTP $Expected" ($response.StatusCode -eq $Expected)
    if ($Expected -eq 200) { return ($response.Content | ConvertFrom-Json -Depth 50) }
}
try {
    $board = Read 'finance-board' "/api/companies/$north/agent-work?responsibility=finance&objective=P10&take=100"
    Check 'Seven source states' (@($board.stateCounts.PSObject.Properties).Count -eq 7)
    Check 'Finance filtered outcomes nine' ($board.total -eq 9 -and $board.items.Count -eq 9)
    Check 'Count equals filtered identities' ((($board.stateCounts.PSObject.Properties.Value | Measure-Object -Sum).Sum) -eq $board.total)
    Check 'Each source state represented' (@($board.stateCounts.PSObject.Properties | Where-Object Value -lt 1).Count -eq 0)
    $shared = @($board.items | Where-Object title -eq 'P10 shared renewal review')
    Check 'Shared outcome exactly once' ($shared.Count -eq 1 -and $shared[0].agents.Count -eq 2)
    $detail = Read 'shared-detail' "/api/companies/$north/agent-work/initiative/$($shared[0].id)"
    Check 'Board detail identity and state' ($detail.id -eq $shared[0].id -and $detail.state -eq $shared[0].state)
    Check 'Company outcome active despite completed worker' ($detail.state -eq 'in_progress' -and $detail.currentStep -match 'completed.*company outcome still')
    Check 'Retained review output and evidence' ($detail.outputs.Count -gt 0 -and $detail.evidence.Count -gt 0 -and $null -eq $detail.completedUtc)
    $workerId = ([regex]::Match($detail.workRoute,'taskId=([0-9a-f-]+)')).Groups[1].Value
    $worker = Read 'worker-task' "/api/companies/$north/tasks/$workerId"
    Check 'Owning Work worker completed' ($worker.status -eq 'completed')
    $workerDetail = Read 'worker-detail' "/api/companies/$north/agent-work/task/$workerId"
    Check 'Worker detail points to company outcome' (@($workerDetail.relatedRecords | Where-Object route -match $detail.id).Count -gt 0)
    Check 'Worker suppressed on board' (@($board.items | Where-Object id -eq $workerId).Count -eq 0)
    $blocked = $board.items | Where-Object title -eq 'P10 dependent review'
    $dependency = Read 'dependent-detail' "/api/companies/$north/agent-work/initiative/$($blocked.id)"
    Check 'Blocked named dependency' ($dependency.state -eq 'blocked' -and $dependency.dependency -match 'P10 shared renewal review')
    Check 'Dependency durable link' (@($dependency.relatedRecords | Where-Object route -match $detail.id).Count -eq 1)
    $active = $board.items | Where-Object title -eq 'P10 in_progress review'
    Check 'Draft output does not complete work' ($active.state -eq 'in_progress' -and $active.outputs.Count -eq 1 -and $null -eq $active.completedUtc)
    $paused = $board.items | Where-Object title -eq 'P10 paused outcome'
    Check 'Paused goal distinct from inactive worker' ($paused.state -eq 'paused' -and $detail.state -ne 'paused')
    $first = Read 'page-one' "/api/companies/$north/agent-work?objective=P10%20page&take=24"
    $second = Read 'page-two' "/api/companies/$north/agent-work?objective=P10%20page&take=24&skip=24"
    Check 'Pagination window' ($first.items.Count -eq 24 -and $first.hasNext -and $second.items.Count -eq 2 -and !$second.hasNext -and $first.total -eq 26)
    Check 'Pagination identities disjoint' (@($first.items.id | Where-Object {$_ -in $second.items.id}).Count -eq 0)
    $dual = Read 'dual-board' "/api/companies/$north/agent-work?take=100" $north 'p01-dual'
    Check 'Dual permitted areas only' (@($dual.items | Where-Object responsibility -notin @('sales','marketing')).Count -eq 0)
    Check 'Dual cannot infer Finance counts or people' ($dual.total -eq 28 -and $dual.agents.Count -eq 0 -and 'finance' -notin $dual.responsibilities)
    Read 'dual-finance-hidden' "/api/companies/$north/agent-work/initiative/$($detail.id)" $north 'p01-dual' 404 | Out-Null
    $member = Read 'member-board' "/api/companies/$north/agent-work?take=100" $north 'p01-member'
    Check 'Unassigned member no work or agent counts' ($member.total -eq 0 -and $member.agents.Count -eq 0 -and ($member.stateCounts.PSObject.Properties.Value | Measure-Object -Sum).Sum -eq 0)
    Read 'member-task-hidden' "/api/companies/$north/tasks/$workerId" $north 'p01-member' 404 | Out-Null
    Read 'foreign-company-hidden' "/api/companies/$south/agent-work/initiative/$($detail.id)" $south 'p01-owner' 404 | Out-Null
    Read 'company-header-mismatch' "/api/companies/$north/agent-work" $south 'p01-owner' 400 | Out-Null
    Read 'invalid-filter' "/api/companies/$north/agent-work?state=invalid" $north 'p01-owner' 400 | Out-Null
    $support = Read 'support-board' "/api/companies/$north/agent-work?responsibility=support"
    Check 'New support case planned' (($support.items | Where-Object id -eq '08080808-0808-0808-0808-080808080802').state -eq 'planned')
    Check 'Internal wait actionable' (($support.items | Where-Object id -eq '08080808-0808-0808-0808-080808080804').dependency -match 'internal specialist')
    $reopened = $support.items | Where-Object id -eq '08080808-0808-0808-0808-080808080803'
    Check 'Reopened case active with no current completion' ($reopened.state -eq 'in_progress' -and $null -eq $reopened.completedUtc)
    $case = Read 'support-source' '/api/support/cases/08080808-0808-0808-0808-080808080802'
    Check 'Support owning record new' ($case.status -eq 'new')
    $deal = Read 'deal-detail' "/api/companies/$north/agent-work/deal/44444444-4444-4444-4444-444444444444"
    $dealSource = Read 'deal-source' '/api/sales/deals/44444444-4444-4444-4444-444444444444'
    Check 'Sales owning record same title and state' ($deal.id -eq $dealSource.id -and $deal.title -eq $dealSource.title -and $deal.sourceState -eq $dealSource.status)
    $summary = Read 'dual-summary' "/api/companies/$north/executive-cockpit/agent-staff?year=2026&month=8" $north 'p01-dual'
    Check 'Retained summary hides Finance' (!$summary.finance.canView -and $null -eq $summary.finance.revenue -and $summary.agents.Count -eq 0)
} finally {
    $hashes = @(Get-ChildItem -LiteralPath $OutputPath -Filter '*.json' | Where-Object Name -ne 'results.json' | ForEach-Object { @{file=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
    @{recordedUtc=[DateTime]::UtcNow.ToString('o');source='real authenticated composed API; disposable SQLite';checks=$checks;sourceHashes=$hashes} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $OutputPath 'results.json') -Encoding utf8
}
$checks | Format-Table -AutoSize
