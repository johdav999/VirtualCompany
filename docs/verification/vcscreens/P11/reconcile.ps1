param([string]$BaseUrl='http://127.0.0.1:5319')
$ErrorActionPreference='Stop'
if ([uri]$BaseUrl -and ([uri]$BaseUrl).Host -notin @('127.0.0.1','localhost')) { throw 'This script reads the disposable localhost fixture only.' }
$company='11111111-1111-1111-1111-111111111111'
$packet=$PSScriptRoot
$checks=[System.Collections.Generic.List[object]]::new()
$reads=[System.Collections.Generic.List[object]]::new()
function Read-Fixture([string]$Path,[string]$Subject='p01-owner',[int]$Expected=200,[string]$Name='') {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $response=Invoke-WebRequest "$BaseUrl$Path" -Headers @{'X-Dev-Auth-Subject'=$Subject;'X-Dev-Auth-Email'="$Subject@example.com"} -SkipHttpErrorCheck -TimeoutSec 10
    $watch.Stop()
    if ([int]$response.StatusCode -ne $Expected) { throw "$Path returned $($response.StatusCode), expected $Expected" }
    $reads.Add(@{path=$Path;subject=$Subject;status=[int]$response.StatusCode;elapsedMs=$watch.ElapsedMilliseconds;bytes=[Text.Encoding]::UTF8.GetByteCount($response.Content)})
    if ($Name) { [IO.File]::WriteAllText((Join-Path $packet "$Name.json"),$response.Content) }
    if ($Expected -eq 200) { return ($response.Content | ConvertFrom-Json -Depth 40) }
}
function Check([string]$Name,[bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}); if(!$Passed){throw "Reconciliation failed: $Name"} }
$board=Read-Fixture "/api/companies/$company/agent-work?objective=P11" -Name 'board-accepted'
Check 'Exactly one owning outcome; completion remains pending' ($board.items.Count -eq 1 -and $board.items[0].state -eq 'awaiting_approval')
$root=$board.items[0].id
$evidence=Read-Fixture "/api/companies/$company/agent-work/task/$root/collaboration" -Name 'collaboration-accepted'
Check 'Six immutable versions across four logical contributions' ($evidence.artifacts.Count -eq 6 -and @($evidence.artifacts | Group-Object parentTaskId,sequence).Count -eq 4)
Check 'Three exact typed edges; human dependency remains pending' ($evidence.handoffs.Count -eq 3 -and $evidence.outcomeState -eq 'awaiting_approval' -and @($evidence.handoffs | Where-Object {!$_.passed}).Count -eq 1)
$proposal=$evidence.artifacts | Where-Object {$_.objective -eq 'P11 proposal revision' -and $_.version -eq 2}
$finance=$evidence.artifacts | Where-Object {$_.agent.name -eq 'P11 Laura'}
$support=$evidence.artifacts | Where-Object {$_.agent.name -eq 'P11 Ben' -and $_.version -eq 2}
Check 'Proposal receives Finance v1 and Support v2 exactly' ($proposal.inputIds.Count -eq 2 -and $proposal.inputIds -contains $finance.id -and $proposal.inputIds -contains $support.id)
Check 'Earlier failure remains recorded; proposal review is not invented' (@($evidence.artifacts | Where-Object {$_.state -eq 'failed' -and !$_.output}).Count -eq 1 -and $proposal.state -eq 'needs_review' -and !$proposal.reviewOutcome)
Check 'Recorded challenge retains disagreement' (@($evidence.artifacts | Where-Object {$_.role -eq 'challenger' -and $_.reviewOutcome -like '*12%*8%*pending*'}).Count -eq 1)
foreach($artifact in $evidence.artifacts) {
    $source=Read-Fixture "/api/companies/$company/agent-work/task/$($artifact.sourceTaskId)"
    Check "Source identity $($artifact.id)" ($source.id -eq $artifact.sourceTaskId)
}
$direct=Read-Fixture "/api/companies/$company/agent-work/task/$($proposal.sourceTaskId)/collaboration"
Check 'Direct worker resolves same six receipts and owning outcome' ((($direct.artifacts.id | Sort-Object) -join ',') -eq (($evidence.artifacts.id | Sort-Object) -join ',') -and $direct.outcomeState -eq $evidence.outcomeState)
$again=Read-Fixture "/api/companies/$company/agent-work/task/$root/collaboration"
Check 'Read and refresh preserve receipt identities' ((($again.artifacts.id | Sort-Object) -join ',') -eq (($evidence.artifacts.id | Sort-Object) -join ','))
$scoped=Read-Fixture "/api/companies/$company/agent-work/task/$root/collaboration" 'p01-dual' -Name 'collaboration-restricted'
Check 'Restricted inputs and all derived output withheld' ($scoped.hasRestrictedEvidence -and $scoped.artifacts.Count -eq 1 -and $scoped.artifacts[0].agent.name -eq 'P11 Alex' -and ($scoped | ConvertTo-Json -Depth 30) -notmatch 'P11 Laura|P11 Ben|8%|P11 terms challenge')
Read-Fixture "/api/companies/$company/agent-work/task/$($finance.sourceTaskId)/collaboration" 'p01-dual' 404
Read-Fixture "/api/companies/$company/agent-work/task/$root/collaboration" 'p01-member' 404
Read-Fixture "/api/companies/22222222-2222-2222-2222-222222222222/agent-work/task/$root/collaboration" 'p01-owner' 404
Read-Fixture "/api/companies/22222222-2222-2222-2222-222222222222/agent-work/task/$root/collaboration" 'p01-dual' 403
Check 'Approval uses canonical Work itemId selection' (@($evidence.relatedRecords | Where-Object {$_.label -eq 'Review approval' -and $_.route -match '&itemId='}).Count -eq 1)
Check 'Owning business record remains linked' (@($evidence.relatedRecords | Where-Object {$_.label -eq 'Open business record' -and $_.route -match '/app/sales/deals/'}).Count -eq 1)
$hashes=@('board-accepted.json','collaboration-accepted.json','collaboration-restricted.json') | ForEach-Object { @{file=$_;sha256=(Get-FileHash (Join-Path $packet $_)).Hash} }
@{observedUtc=[DateTime]::UtcNow.ToString('o');rootId=$root;companyId=$company;reads=$reads;checks=$checks;hashes=$hashes;scope='Disposable synthetic fixture; GET only; production query owner and authorization. No provider or decision commands.'} | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $packet 'source-reconciliation.json')
Write-Output "$($checks.Count) reconciliation checks passed; $($reads.Count) authenticated reads."
