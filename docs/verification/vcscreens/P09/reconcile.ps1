param(
    [string]$BaseUrl = 'http://localhost:5319',
    [string]$OutputPath = (Join-Path $PSScriptRoot 'reconciliation'),
    [switch]$AfterDailyJourney
)
$ErrorActionPreference = 'Stop'
if ($BaseUrl -notmatch '^http://localhost:[0-9]+$') { throw 'Use only the disposable local UAT adapter.' }
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
$north = '11111111-1111-1111-1111-111111111111'
$south = '22222222-2222-2222-2222-222222222222'
$ledger = '77777777-7777-7777-7777-777777777777'
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Condition) {
    $results.Add([pscustomobject]@{check=$Name; passed=$Condition})
    if (!$Condition) { throw "Reconciliation failed: $Name" }
}
function Read([string]$Name, [string]$Route, [string]$Company = $north, [string]$Subject = 'p01-owner', [int]$Expected = 200) {
    $headers = @{'X-Company-Id'=$Company; 'X-Dev-Auth-Subject'=$Subject; 'X-Dev-Auth-Email'="$Subject@example.com"}
    $response = Invoke-WebRequest -Uri ($BaseUrl + $Route) -Headers $headers -TimeoutSec 30 -SkipHttpErrorCheck
    $response.Content | Set-Content -LiteralPath (Join-Path $OutputPath "$Name.json") -Encoding utf8
    Check "$Name HTTP $Expected (actual $($response.StatusCode))" ($response.StatusCode -eq $Expected)
    if ($Expected -eq 200) { return ($response.Content | ConvertFrom-Json -Depth 50) }
}
function Sum($Rows, [string]$Property) { return [decimal](($Rows | Measure-Object -Property $Property -Sum).Sum) }
try {
    $today = Read 'today' "/api/companies/$north/workspace/today?lens=company&refresh=true"
    $pipeline = Read 'sales-pipeline' '/api/sales/operational/opportunities?forecast=false&days=30'
    $forecast = Read 'sales-forecast' '/api/sales/operational/opportunities?forecast=true&days=30'
    foreach ($total in $pipeline.totals) {
        Check "Sales $($total.currency) pipeline rows" ($total.grossAmount -eq (Sum @($pipeline.rows | Where-Object currency -eq $total.currency) 'amount'))
    }
    foreach ($total in $forecast.totals) {
        Check "Sales $($total.currency) forecast rows" ($total.expectedAmount -eq (Sum @($forecast.rows | Where-Object currency -eq $total.currency) 'expectedAmount'))
    }
    $weighted = [decimal]0
    foreach ($row in @($pipeline.rows | Where-Object currency -eq $today.sales.currency)) { $weighted += $row.amount * $row.stageProbability }
    Check 'Today Sales stage weighted meaning' ($today.sales.forecastRevenue -eq $weighted)
    Check 'Forecast remains risk adjusted' (($forecast.totals | Where-Object currency -eq 'SEK').expectedAmount -eq 4050)
    $finance = Read 'finance' "/api/companies/$north/finance/operational-report"
    foreach ($total in $finance.totals) {
        Check "Finance $($total.currency) receivable rows" ($total.receivables -eq (Sum @($finance.receivables | Where-Object currency -eq $total.currency) 'remainingAmount'))
        Check "Finance $($total.currency) payable rows" ($total.payables -eq (Sum @($finance.payables | Where-Object currency -eq $total.currency) 'remainingAmount'))
        Check "Finance $($total.currency) projection" ($null -eq $total.startingCash -or $total.projectedCash -eq ($total.startingCash + $total.expectedInflows - $total.expectedOutflows))
    }
    $support = Read 'support-backlog' '/api/support/reports?view=backlog'
    $unresolved = Read 'support-unresolved' '/api/support/reports?view=unresolved'
    $aging = Read 'support-aging' '/api/support/reports?view=aging'
    $sla = Read 'support-sla' '/api/support/reports?view=sla'
    $ids = ($support.cases.case.id | Sort-Object) -join ','
    Check 'Support backlog unresolved aging identities' ($ids -eq (($unresolved.cases.case.id | Sort-Object) -join ',') -and $ids -eq (($aging.cases.case.id | Sort-Object) -join ','))
    Check 'Support Today source counts' ($today.support.openCases -eq $support.cases.Count -and $today.support.slaAtRisk -eq $support.atRisk -and $today.support.slaBreached -eq $support.breached)
    Check 'SLA rows exclude missing targets' ($sla.cases.Count -eq ($sla.atRisk + $sla.breached))
    $marketing = Read 'marketing' '/api/marketing/operational/report?fromUtc=2026-09-02&toUtc=2026-10-03'
    $campaign = $marketing.campaigns | Where-Object id -eq '05050505-0505-0505-0505-050505050505'
    $spend = @($marketing.spend | Where-Object { $_.campaignId -eq $campaign.id -and $_.currency -eq $campaign.currency })
    Check 'Marketing known same currency spend rows' ($campaign.knownSpend -eq (Sum $spend 'cost') -and $campaign.knownSpend -eq 120 -and $campaign.budget -eq 100)
    Check 'Marketing unknown cost stays a gap' ($campaign.unknownCostTouches -eq 1)
    Check 'Marketing provider retry is not delivery' (@($marketing.deliveries | Where-Object { $_.state -eq 'retry_scheduled' -and !$_.providerReference }).Count -eq 1)
    Read 'dual-sales' '/api/sales/operational/opportunities' $north 'p01-dual' | Out-Null
    Read 'dual-marketing' '/api/marketing/operational/report?fromUtc=2026-09-02&toUtc=2026-10-03' $north 'p01-dual' | Out-Null
    Read 'dual-support-denied' '/api/support/reports?view=backlog' $north 'p01-dual' 403 | Out-Null
    Read 'member-support-denied' '/api/support/reports?view=backlog' $north 'p01-member' 403 | Out-Null
    Read 'member-finance-denied' "/api/companies/$north/finance/operational-report" $north 'p01-member' 403 | Out-Null
    Read 'foreign-support-denied' '/api/support/cases/08080808-0808-0808-0808-080808080804' $south 'p01-owner' 404 | Out-Null
    $close = Read 'accounting-close' "/api/companies/$ledger/finance/close-workspace?fiscalPeriodId=07070707-0707-0707-0707-070707070901" $ledger
    if ($AfterDailyJourney) {
        $activity = Read 'sales-completed' '/api/sales/operational/activities?status=completed'
        Check 'Sales reviewed commitment persisted' (@($activity.commitments | Where-Object { $_.summary -like 'P09 verify*' -and $_.status -eq 'completed' }).Count -eq 1)
        $review = Read 'marketing-review' '/api/marketing/operational/review?campaignId=05050505-0505-0505-0505-050505050505'
        Check 'Marketing revision invalidates original' (@($review.content | Where-Object { $_.status -eq 'rejected' -and $_.version -eq 3 }).Count -eq 1)
        $case = Read 'support-handoff' '/api/support/cases/08080808-0808-0808-0808-080808080804'
        Check 'Support assignment reason persisted' (@($case.events | Where-Object summary -like '*P09 specialist must verify*').Count -eq 1 -and $case.assignedAgentId -eq '08080808-0808-0808-0808-080808080801')
    }
} finally {
    [pscustomobject]@{observedUtc=[DateTime]::UtcNow; environment='Disposable SQLite real API'; afterDailyJourney=[bool]$AfterDailyJourney; checks=$results; physicalCsvSave='Unverified'; providerDelivery='Unverified'} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputPath 'results.json') -Encoding utf8
    Get-ChildItem -LiteralPath $OutputPath -Filter '*.json' | Where-Object Name -ne 'hashes.json' | ForEach-Object {
        [pscustomobject]@{file=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputPath 'hashes.json') -Encoding utf8
}
$results | Format-Table -AutoSize
