[CmdletBinding()]
param(
    [string]$ResultsRoot = "artifacts/finance-agent-p1/$(Get-Date -Format 'yyyyMMdd-HHmmss')",
    [string]$UatEvidencePath = ".codex-build/uat/finance-agent-p1-release-2026-09-01/uat-evidence.json",
    [string]$SwedishEvidenceVerifier = "C:/Users/Johan/.codex/skills/swedish-accountant-expert/scripts/verify_virtual_company_evidence.py",
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$resultsPath = [System.IO.Path]::GetFullPath($ResultsRoot, $repositoryRoot)
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

function Get-Sha256Text([string]$Value) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.Convert]::ToHexString(
            $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Value))).ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-WorkingTreeManifestChecksum {
    $content = [System.Text.StringBuilder]::new()
    [void]$content.AppendLine((& git diff --binary HEAD | Out-String))
    $relativeResults = [System.IO.Path]::GetRelativePath($repositoryRoot, $resultsPath)
    foreach ($file in @(& git ls-files --others --exclude-standard) | Sort-Object) {
        if ($file.StartsWith($relativeResults, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($file.StartsWith('artifacts/finance-agent-p1/', [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        $absolute = Join-Path $repositoryRoot $file
        if (Test-Path -LiteralPath $absolute -PathType Leaf) {
            [void]$content.AppendLine("$file|$((Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant())")
        }
    }
    return Get-Sha256Text $content.ToString()
}

function Read-TrxCounts([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ total = 0; executed = 0; passed = 0; failed = 0; skipped = 0 }
    }
    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $skipped = [Math]::Max([int]$counters.notExecuted, $total - $executed)
    return [pscustomobject]@{
        total = $total
        executed = $executed
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = $skipped
    }
}

function Read-TrxTreeCounts([string]$Path) {
    $sum = [ordered]@{ total = 0; executed = 0; passed = 0; failed = 0; skipped = 0 }
    foreach ($trx in @(Get-ChildItem -LiteralPath $Path -Filter '*.trx' -Recurse -File)) {
        $counts = Read-TrxCounts $trx.FullName
        foreach ($name in @('total', 'executed', 'passed', 'failed', 'skipped')) { $sum[$name] += $counts.$name }
    }
    return [pscustomobject]$sum
}

function Invoke-Check {
    param([string]$Name, [string]$FilePath, [string[]]$Arguments, [string]$TrxPath)
    $logPath = Join-Path $resultsPath "$Name.log"
    $started = [DateTime]::UtcNow
    & $FilePath @Arguments 2>&1 | Tee-Object -LiteralPath $logPath | Out-Host
    $exitCode = $LASTEXITCODE
    $counts = if ([string]::IsNullOrWhiteSpace($TrxPath)) {
        [pscustomobject]@{ total = 0; executed = 0; passed = 0; failed = 0; skipped = 0 }
    } else { Read-TrxCounts $TrxPath }
    return [pscustomobject]@{
        name = $Name
        command = "$FilePath $($Arguments -join ' ')"
        startedUtc = $started.ToString('O')
        durationSeconds = [Math]::Round(([DateTime]::UtcNow - $started).TotalSeconds, 3)
        exitCode = $exitCode
        outcome = if ($exitCode -eq 0 -and $counts.failed -eq 0 -and $counts.skipped -eq 0) { 'passed' } else { 'failed' }
        counts = $counts
        log = [System.IO.Path]::GetRelativePath($repositoryRoot, $logPath)
    }
}

function New-PrerequisiteResult([string]$Name, [string]$Command, [string]$Outcome, [string]$Evidence = $null) {
    return [pscustomobject]@{
        name = $Name
        command = $Command
        startedUtc = [DateTime]::UtcNow.ToString('O')
        durationSeconds = 0
        exitCode = $null
        outcome = $Outcome
        counts = [pscustomobject]@{ total = 0; executed = 0; passed = 0; failed = 0; skipped = 0 }
        log = $Evidence
    }
}

$revision = (& git rev-parse HEAD).Trim()
$dirty = @(& git status --porcelain).Count -gt 0
$workingTreeManifestChecksum = Get-WorkingTreeManifestChecksum
$packPath = Join-Path $repositoryRoot 'tests/VirtualCompany.Api.Tests/Fixtures/FinanceNaturalLanguage/finance-natural-language-safety-v1.json'
$pack = Get-Content -LiteralPath $packPath -Raw | ConvertFrom-Json
$packChecksum = (Get-FileHash -LiteralPath $packPath -Algorithm SHA256).Hash.ToLowerInvariant()
$appSettings = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/VirtualCompany.Api/appsettings.json') -Raw | ConvertFrom-Json

Push-Location $repositoryRoot
try {
    $results = @()
    $evaluationTrx = Join-Path $resultsPath 'finance-agent-p1-evaluation.trx'
    $evaluationArguments = @(
        'test', 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj', '--configuration', 'Release',
        '--nologo', '-p:NuGetAudit=false', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '--filter',
        'FullyQualifiedName~FinanceNaturalLanguageSafetyEvaluationTests|FullyQualifiedName~FinanceToolPlannerTests|FullyQualifiedName~FinanceConversationExecutionServiceTests|FullyQualifiedName~SharedAgentReasoningGatewayTests',
        '--logger', 'trx;LogFileName=finance-agent-p1-evaluation.trx', '--results-directory', $resultsPath)
    if ($NoRestore) { $evaluationArguments += '--no-restore' }
    $results += Invoke-Check 'fixed-safety-evaluation' 'dotnet' $evaluationArguments $evaluationTrx

    $uiTrx = Join-Path $resultsPath 'finance-agent-p1-ui.trx'
    $uiArguments = @(
        'test', 'tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj', '--configuration', 'Release',
        '--nologo', '-p:NuGetAudit=false', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '--filter',
        'FullyQualifiedName~FinanceAgentWorkbenchSurfaceTests|FullyQualifiedName~FinanceApiClientConversationRunTests|FullyQualifiedName~FinanceConversationRunContractTests',
        '--logger', 'trx;LogFileName=finance-agent-p1-ui.trx', '--results-directory', $resultsPath)
    if ($NoRestore) { $uiArguments += '--no-restore' }
    $results += Invoke-Check 'authenticated-ui-contracts' 'dotnet' $uiArguments $uiTrx

    $p0Trx = Join-Path $resultsPath 'finance-agent-p0-focused.trx'
    $p0Arguments = @(
        'test', 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj', '--configuration', 'Release',
        '--nologo', '-p:NuGetAudit=false', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '--filter',
        'FullyQualifiedName~FinanceAgentAuthorityMatrixTests|FullyQualifiedName~FinanceAgentAuthorizationServiceTests|FullyQualifiedName~FinanceToolRiskPolicyTests|FullyQualifiedName~FinanceToolExecutionFlowIntegrationTests|FullyQualifiedName~ApprovalDecisionChainTests|FullyQualifiedName~AgentEffectiveAuthorityResolverTests|FullyQualifiedName~FinanceAgentAuthorityMigrationTests',
        '--logger', 'trx;LogFileName=finance-agent-p0-focused.trx', '--results-directory', $resultsPath)
    if ($NoRestore) { $p0Arguments += '--no-restore' }
    $results += Invoke-Check 'p0-safety-gates' 'dotnet' $p0Arguments $p0Trx

    $buildArguments = @('build', 'VirtualCompany.sln', '--configuration', 'Release', '--nologo',
        '-p:NuGetAudit=false', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false')
    if ($NoRestore) { $buildArguments += '--no-restore' }
    $results += Invoke-Check 'release-build' 'dotnet' $buildArguments

    $hermeticPath = Join-Path $resultsPath 'hermetic'
    $matrixArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'test-matrix.ps1'),
        '-Lane', 'hermetic', '-Configuration', 'Release', '-ResultsRoot', $hermeticPath, '-NoBuild')
    if ($NoRestore) { $matrixArguments += '-NoRestore' }
    $hermetic = Invoke-Check 'hermetic-matrix' 'pwsh' $matrixArguments
    $hermetic.counts = Read-TrxTreeCounts $hermeticPath
    if ($hermetic.counts.failed -gt 0 -or $hermetic.counts.skipped -gt 0) { $hermetic.outcome = 'failed' }
    $results += $hermetic

    $results += Invoke-Check 'ef-pending-model' 'dotnet' @(
        'ef', 'migrations', 'has-pending-model-changes',
        '--project', 'src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj',
        '--startup-project', 'src/VirtualCompany.Api/VirtualCompany.Api.csproj', '--configuration', 'Release', '--no-build')

    if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION)) {
        $results += New-PrerequisiteResult 'sqlserver-finance-lanes' 'pwsh scripts/test-matrix.ps1 -Lane sqlserver -Configuration Release -NoBuild' 'prerequisite_missing'
    } else {
        $sqlPath = Join-Path $resultsPath 'sqlserver'
        $sqlArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'test-matrix.ps1'),
            '-Lane', 'sqlserver', '-Configuration', 'Release', '-ResultsRoot', $sqlPath, '-NoBuild')
        if ($NoRestore) { $sqlArguments += '-NoRestore' }
        $sql = Invoke-Check 'sqlserver-finance-lanes' 'pwsh' $sqlArguments
        $sql.counts = Read-TrxTreeCounts $sqlPath
        if ($sql.counts.failed -gt 0 -or $sql.counts.skipped -gt 0) { $sql.outcome = 'failed' }
        $results += $sql
    }

    if (Test-Path -LiteralPath $SwedishEvidenceVerifier -PathType Leaf) {
        $results += Invoke-Check 'swedish-accounting-evidence-verifier' 'python' @($SwedishEvidenceVerifier)
    } else {
        $results += New-PrerequisiteResult 'swedish-accounting-evidence-verifier' 'python <swedish-accountant-expert>/scripts/verify_virtual_company_evidence.py' 'prerequisite_missing'
    }

    $absoluteUat = [System.IO.Path]::GetFullPath($UatEvidencePath, $repositoryRoot)
    if (Test-Path -LiteralPath $absoluteUat -PathType Leaf) {
        $uat = Get-Content -LiteralPath $absoluteUat -Raw | ConvertFrom-Json
        $uatPassed = $uat.status -eq 'passed' -and $uat.authenticated -eq $true -and
            @($uat.locales) -contains 'en' -and @($uat.locales) -contains 'sv-SE' -and
            $uat.restartRecoveryVerified -eq $true
        $results += New-PrerequisiteResult 'authenticated-browser-uat-and-restart-recovery' 'browser UAT evidence validation' $(if ($uatPassed) { 'passed' } else { 'failed' }) ([System.IO.Path]::GetRelativePath($repositoryRoot, $absoluteUat))
    } else {
        $results += New-PrerequisiteResult 'authenticated-browser-uat-and-restart-recovery' 'browser UAT evidence validation' 'prerequisite_missing'
    }

    if ([string]::IsNullOrWhiteSpace($env:SHARED_AGENT_AI_API_KEY)) {
        $results += New-PrerequisiteResult 'optional-live-provider-evaluation' 'live provider evaluation' 'not_applicable'
    } else {
        $results += New-PrerequisiteResult 'optional-live-provider-evaluation' 'live provider evaluation' 'not_run'
    }

    $releaseStops = @($results | Where-Object {
        $_.name -ne 'optional-live-provider-evaluation' -and $_.outcome -ne 'passed'
    } | ForEach-Object { "$($_.name):$($_.outcome)" })
    $manifestCore = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTime]::UtcNow.ToString('O')
        repositoryRevision = $revision
        dirtyWorkingTree = $dirty
        workingTreeManifestChecksum = $workingTreeManifestChecksum
        evaluationPack = [ordered]@{
            version = $pack.packVersion
            checksum = $packChecksum
            caseCount = @($pack.cases).Count
            categoryCount = @($pack.cases.category | Sort-Object -Unique).Count
            languages = @($pack.cases.language | Sort-Object -Unique)
            requiredInvariantCount = @($pack.requiredInvariants).Count
        }
        modelConfiguration = [ordered]@{
            provider = 'openai-compatible'
            model = $appSettings.SharedAgentAi.Model
            promptVersion = $pack.modelConfiguration.promptVersion
            planContractVersion = $pack.modelConfiguration.planContractVersion
            synthesisPromptVersion = $pack.modelConfiguration.synthesisPromptVersion
            synthesisContractVersion = $pack.modelConfiguration.synthesisContractVersion
            temperature = $pack.modelConfiguration.temperature
            maximumPlannerCalls = $pack.modelConfiguration.maximumPlannerCalls
            maximumConversationModelCalls = $pack.modelConfiguration.maximumConversationModelCalls
            maximumToolCalls = $pack.modelConfiguration.maximumToolCalls
            maximumElapsedMilliseconds = $pack.modelConfiguration.maximumElapsedMilliseconds
            maximumEstimatedCost = $pack.modelConfiguration.maximumEstimatedCost
        }
        p1Decision = if ($releaseStops.Count -eq 0) { 'go' } else { 'no_go' }
        technicalClassification = if ($releaseStops.Count -eq 0) { 'technically_verified_for_human_review' } else { 'technical_verification_incomplete' }
        humanAccountingReview = 'human_accountant_review_pending'
        unresolvedBlockers = $releaseStops
        results = $results
        disclaimer = 'Engineering evidence only; not statutory approval or a signed professional opinion.'
    }
    $canonical = $manifestCore | ConvertTo-Json -Depth 10 -Compress
    $manifest = [ordered]@{}
    foreach ($pair in $manifestCore.GetEnumerator()) { $manifest[$pair.Key] = $pair.Value }
    $manifest['manifestCoreChecksum'] = Get-Sha256Text $canonical

    $markdown = @(
        '# Finance natural-language P1 release evidence', '',
        "- Generated UTC: $($manifest.generatedUtc)",
        "- Repository revision: $revision",
        "- Dirty working tree: $dirty",
        "- Working-tree manifest checksum: $workingTreeManifestChecksum",
        "- Evaluation pack: $($pack.packVersion) ($packChecksum)",
        "- Manifest core checksum: $($manifest.manifestCoreChecksum)",
        "- P1 decision: $($manifest.p1Decision)",
        "- Technical classification: $($manifest.technicalClassification)",
        "- Human accounting review: $($manifest.humanAccountingReview)", '',
        '| Checkpoint | Outcome | Passed | Failed | Skipped | Evidence |',
        '| --- | --- | ---: | ---: | ---: | --- |')
    foreach ($result in $results) {
        $markdown += "| $($result.name) | $($result.outcome) | $($result.counts.passed) | $($result.counts.failed) | $($result.counts.skipped) | $($result.log) |"
    }
    $markdown += @('', 'This is engineering evidence only; it is not statutory approval or a signed professional opinion.')
    $evidencePath = Join-Path $resultsPath 'finance-agent-p1-release-evidence.md'
    $markdown | Set-Content -LiteralPath $evidencePath -Encoding utf8
    $manifest['evidenceChecksum'] = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestPath = Join-Path $resultsPath 'finance-agent-p1-manifest.json'
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    $results | Format-Table name, outcome, exitCode, durationSeconds -AutoSize
    Write-Host "P1 decision: $($manifest.p1Decision)"
    Write-Host "Manifest: $manifestPath"
    if ($releaseStops.Count -gt 0) { exit 2 }
}
finally { Pop-Location }
