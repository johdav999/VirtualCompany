param([string]$BaseUrl='http://127.0.0.1:5316')
$ErrorActionPreference='Stop'
if($BaseUrl -ne 'http://127.0.0.1:5316'){throw 'This verifier is limited to the owned local P16 fixture host.'}
$p16Headers=@{'X-Dev-Auth-Subject'='p01-owner';'X-Dev-Auth-DisplayName'='P01 Owner';'X-Dev-Auth-Email'='p01-owner@example.com'}
$p16Profile=Invoke-RestMethod "$BaseUrl/_uat/p16/profile"
if($p16Profile.companyId -ne '16161616-1616-1616-1616-161616161616'){throw 'Unexpected fixture company.'}
$p16Route="$BaseUrl/api/companies/$($p16Profile.companyId)/execution-controls"
$p16Checks=[System.Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed){if(!$Passed){throw "P16 source check failed: $Name"};$p16Checks.Add(@{name=$Name;passed=$true})}
function ReadScope([bool]$Scoped){$p16Suffix=if($Scoped){"?agentId=$($p16Profile.agentId)"}else{''};Invoke-RestMethod "$p16Route$p16Suffix" -Headers $p16Headers}
function PreviewChange([bool]$Scoped,[bool]$Pause){
    $p16Before=ReadScope $Scoped
    $p16Change=@{commandId=[guid]::NewGuid();agentId=$(if($Scoped){$p16Profile.agentId}else{$null});pause=$Pause;expectedVersion=$p16Before.version;expectedCompanyVersion=$p16Before.companyVersion;reason='P16 bounded HTTP acceptance'}
    $p16Preview=Invoke-RestMethod "$p16Route/preview" -Method Post -Headers $p16Headers -ContentType 'application/json' -Body ($p16Change|ConvertTo-Json)
    @{before=$p16Before;preview=$p16Preview;apply=@{change=$p16Change;previewHash=$p16Preview.previewHash}}
}
function ApplyChange($Reviewed){Invoke-RestMethod "$p16Route/apply" -Method Post -Headers $p16Headers -ContentType 'application/json' -Body ($Reviewed.apply|ConvertTo-Json -Depth 6)}
$p16Reviewed=PreviewChange $true $true
$p16NoEffect=ReadScope $true
Check 'Scoped preview has no control mutation' (!$p16NoEffect.paused -and $p16NoEffect.version -eq $p16Reviewed.before.version)
$p16Paused=ApplyChange $p16Reviewed
Check 'Scoped pause is durable with actor history' ($p16Paused.paused -and @($p16Paused.history).Count -eq 1 -and $p16Paused.history[0].actor -eq 'P01 Owner')
$p16Duplicate=ApplyChange $p16Reviewed
Check 'Duplicate apply retains one revision and receipt' ($p16Duplicate.version -eq $p16Paused.version -and @($p16Duplicate.history).Count -eq 1)
$p16Dispatch=Invoke-RestMethod "$BaseUrl/_uat/p16/dispatch" -Method Post
Check 'Paused real operating work cannot claim' ($p16Dispatch.claimed -eq 0)
$p16Second=Invoke-RestMethod "$p16Route`?agentId=$($p16Profile.secondAgentId)" -Headers $p16Headers
Check 'Other agent scope remains unpaused' (!$p16Second.paused -and !$p16Second.companyPaused)
$p16Denied=Invoke-WebRequest "$p16Route/preview" -Method Post -Headers @{'X-Dev-Auth-Subject'='p01-member'} -ContentType 'application/json' -Body ($p16Reviewed.apply.change|ConvertTo-Json) -SkipHttpErrorCheck
Check 'Unauthorized actor cannot preview or apply control' ($p16Denied.StatusCode -in @(401,403))
$p16Resumed=ApplyChange (PreviewChange $true $false)
Check 'Scoped resume retains current control history' (!$p16Resumed.paused -and @($p16Resumed.history).Count -eq 2)
$p16Dispatch=Invoke-RestMethod "$BaseUrl/_uat/p16/dispatch" -Method Post
Check 'Real retained native task executes after eligible resume' ($p16Dispatch.completed -eq 1)
$p16Completed=ReadScope $true
Check 'Owning dispatch reports confirmed internal completion' (@($p16Completed.work|Where-Object {$_.id -eq $p16Profile.taskId -and $_.state -eq 'Completed internal work'}).Count -eq 1)
$p16Global=ApplyChange (PreviewChange $false $true)
Check 'Company pause overrides scoped state' ($p16Global.companyPaused -and (ReadScope $true).companyPaused)
$p16ScopeResume=ApplyChange (PreviewChange $true $false)
Check 'Scoped resume does not clear company pause' (!$p16ScopeResume.paused -and $p16ScopeResume.companyPaused)
$p16GlobalResume=ApplyChange (PreviewChange $false $false)
Check 'Company resume retains recorded completion' (!$p16GlobalResume.companyPaused -and @($p16GlobalResume.work|Where-Object {$_.state -eq 'Completed internal work'}).Count -ge 1)
$p16Again=Invoke-RestMethod "$BaseUrl/_uat/p16/dispatch" -Method Post
Check 'Completed work is not re-executed on resume' ($p16Again.claimed -eq 0)
@{recordedUtc=[DateTime]::UtcNow.ToString('o');profile=$p16Profile;checks=$p16Checks;browser='Blocked';providerEffects='None requested';finalScopedView=(ReadScope $true)}|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'http-source-reconciliation.json')
Write-Output "P16 HTTP source checks passed: $($p16Checks.Count)"
