[CmdletBinding()]
param(
    [string]$ResultsRoot = "artifacts/finance-agent-p2/$(Get-Date -Format 'yyyyMMdd-HHmmss')",
    [string]$P0ManifestPath,
    [string]$P1ManifestPath,
    [string]$BrowserEvidencePath = ".codex-build/uat/finance-agent-p2/browser-evidence.json",
    [string]$RecoveryEvidencePath = ".codex-build/uat/finance-agent-p2/recovery-evidence.json",
    [string]$ProfessionalApprovalPath = ".codex-build/uat/finance-agent-p2/professional-approval.json",
    [string]$ProviderScopeApprovalPath = ".codex-build/uat/finance-agent-p2/provider-scope-approval.json",
    [string]$SwedishEvidenceVerifier = "C:/Users/Johan/.codex/skills/swedish-accountant-expert/scripts/verify_virtual_company_evidence.py",
    [switch]$NoRestore,
    [switch]$FocusedOnly
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
    $relativeResults = [System.IO.Path]::GetRelativePath($repositoryRoot, $resultsPath).Replace('\', '/')
    foreach ($file in @(& git ls-files --others --exclude-standard) | Sort-Object) {
        $normalized = $file.Replace('\', '/')
        if ($normalized.StartsWith($relativeResults, [System.StringComparison]::OrdinalIgnoreCase) -or
            $normalized.StartsWith('artifacts/finance-agent-p2/', [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        $absolute = Join-Path $repositoryRoot $file
        if (Test-Path -LiteralPath $absolute -PathType Leaf) {
            [void]$content.AppendLine("$normalized|$((Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant())")
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
    return [pscustomobject]@{
        total = $total
        executed = $executed
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = [Math]::Max([int]$counters.notExecuted, $total - $executed)
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
        evidence = [System.IO.Path]::GetRelativePath($repositoryRoot, $logPath)
        detail = $null
    }
}

function New-EvidenceResult([string]$Name, [string]$Outcome, [string]$Detail, [string]$Evidence = $null) {
    return [pscustomobject]@{
        name = $Name
        command = 'evidence validation'
        startedUtc = [DateTime]::UtcNow.ToString('O')
        durationSeconds = 0
        exitCode = $null
        outcome = $Outcome
        counts = [pscustomobject]@{ total = 0; executed = 0; passed = 0; failed = 0; skipped = 0 }
        evidence = $Evidence
        detail = $Detail
    }
}

function Resolve-EvidencePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    return [System.IO.Path]::GetFullPath($Path, $repositoryRoot)
}

function Test-RevisionBoundManifest([string]$Name, [string]$Path, [string]$DecisionProperty, [string]$Checksum) {
    $absolute = Resolve-EvidencePath $Path
    if ($null -eq $absolute -or -not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
        return New-EvidenceResult $Name 'prerequisite_missing' 'A current revision-bound manifest was not supplied.'
    }
    $manifest = Get-Content -LiteralPath $absolute -Raw | ConvertFrom-Json
    $decision = $manifest.$DecisionProperty
    $passed = $decision -eq 'go' -and $manifest.workingTreeManifestChecksum -eq $Checksum
    $outcome = if ($passed) { 'passed' } else { 'failed' }
    $detail = "Decision=$decision; checksumMatch=$($manifest.workingTreeManifestChecksum -eq $Checksum)"
    return New-EvidenceResult $Name $outcome $detail ([System.IO.Path]::GetRelativePath($repositoryRoot, $absolute))
}

function Test-BrowserEvidence([string]$Path, [string]$Checksum) {
    $absolute = Resolve-EvidencePath $Path
    if ($null -eq $absolute -or -not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
        return New-EvidenceResult 'authenticated-en-sv-browser-uat' 'prerequisite_missing' 'Authenticated EN/SV desktop, narrow, accessibility, and recovery UAT evidence is missing.'
    }
    $evidence = Get-Content -LiteralPath $absolute -Raw | ConvertFrom-Json
    $requiredFlows = @('ledger', 'close', 'compliance', 'advanced-accounting', 'draft', 'approval', 'recovery')
    $passed = $evidence.status -eq 'passed' -and $evidence.authenticated -eq $true -and
        $evidence.workingTreeManifestChecksum -eq $Checksum -and
        @($evidence.locales) -contains 'en' -and @($evidence.locales) -contains 'sv-SE' -and
        @($evidence.viewports) -contains 'desktop' -and @($evidence.viewports) -contains 'narrow' -and
        $evidence.keyboardVerified -eq $true -and $evidence.screenReaderVerified -eq $true -and
        @($requiredFlows | Where-Object { @($evidence.flows) -notcontains $_ }).Count -eq 0
    $outcome = if ($passed) { 'passed' } else { 'failed' }
    $detail = "Authenticated=$($evidence.authenticated); checksumMatch=$($evidence.workingTreeManifestChecksum -eq $Checksum)"
    return New-EvidenceResult 'authenticated-en-sv-browser-uat' $outcome $detail ([System.IO.Path]::GetRelativePath($repositoryRoot, $absolute))
}

function Test-RecoveryEvidence([string]$Path, [string]$Checksum) {
    $absolute = Resolve-EvidencePath $Path
    if ($null -eq $absolute -or -not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
        return New-EvidenceResult 'sql-object-recovery' 'prerequisite_missing' 'Coordinated SQL/object recovery evidence is missing.'
    }
    $evidence = Get-Content -LiteralPath $absolute -Raw | ConvertFrom-Json
    $passed = $evidence.status -eq 'passed' -and $evidence.workingTreeManifestChecksum -eq $Checksum -and
        $evidence.sqlRestoreVerified -eq $true -and $evidence.objectRestoreVerified -eq $true -and
        $evidence.sourceLinksVerified -eq $true -and $evidence.auditManifestVerified -eq $true -and
        -not [string]::IsNullOrWhiteSpace($evidence.preRecoveryChecksum) -and
        $evidence.preRecoveryChecksum -eq $evidence.postRecoveryChecksum
    $outcome = if ($passed) { 'passed' } else { 'failed' }
    $detail = "SQL=$($evidence.sqlRestoreVerified); objects=$($evidence.objectRestoreVerified); checksumMatch=$($evidence.preRecoveryChecksum -eq $evidence.postRecoveryChecksum)"
    return New-EvidenceResult 'sql-object-recovery' $outcome $detail ([System.IO.Path]::GetRelativePath($repositoryRoot, $absolute))
}

function Test-ApprovalEvidence([string]$Name, [string]$Path, [string]$Checksum) {
    $absolute = Resolve-EvidencePath $Path
    if ($null -eq $absolute -or -not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
        return New-EvidenceResult $Name 'human_approval_pending' 'Attributable approval for the exact working-tree checksum is missing.'
    }
    $evidence = Get-Content -LiteralPath $absolute -Raw | ConvertFrom-Json
    $passed = $evidence.status -eq 'approved' -and $evidence.workingTreeManifestChecksum -eq $Checksum -and
        -not [string]::IsNullOrWhiteSpace($evidence.reviewer) -and -not [string]::IsNullOrWhiteSpace($evidence.reviewedUtc)
    $outcome = if ($passed) { 'passed' } else { 'failed' }
    $detail = "Status=$($evidence.status); checksumMatch=$($evidence.workingTreeManifestChecksum -eq $Checksum)"
    return New-EvidenceResult $Name $outcome $detail ([System.IO.Path]::GetRelativePath($repositoryRoot, $absolute))
}

$revision = (& git rev-parse HEAD).Trim()
$dirty = @(& git status --porcelain).Count -gt 0
$workingTreeManifestChecksum = Get-WorkingTreeManifestChecksum
$catalogueDoc = Join-Path $repositoryRoot 'docs/finance/finance-agent-coverage-catalogue.md'
$designReference = Join-Path $repositoryRoot 'docs/design/references/finance-agent-coverage-reference.png'

Push-Location $repositoryRoot
try {
    $results = @()
    $apiTrx = Join-Path $resultsPath 'finance-agent-p2-api.trx'
    $apiArguments = @(
        'test', 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj', '--configuration', 'Release', '--nologo',
        '-p:NuGetAudit=false', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '--filter',
        'FullyQualifiedName~FinanceAgentCoverageCatalogueTests|FullyQualifiedName~FinanceAgentCoverageEndpointIntegrationTests|FullyQualifiedName~FinanceLedgerAgentReadToolTests|FullyQualifiedName~FinanceCloseComplianceAgentToolTests|FullyQualifiedName~FinanceAdvancedAccountingAgentToolTests|FullyQualifiedName~FinanceAccountingDraftAgentToolTests|FullyQualifiedName~FinanceOperationalProposalAgentToolTests|FullyQualifiedName~FinanceGuardedCommandToolTests|FullyQualifiedName~FinanceToolRiskPolicyTests|FullyQualifiedName~FinanceConversationExecutionServiceTests',
        '--logger', 'trx;LogFileName=finance-agent-p2-api.trx', '--results-directory', $resultsPath)
    if ($NoRestore) { $apiArguments += '--no-restore' }
    $results += Invoke-Check 'catalogue-tools-orchestration' 'dotnet' $apiArguments $apiTrx

    $webTrx = Join-Path $resultsPath 'finance-agent-p2-web.trx'
    $webArguments = @(
        'test', 'tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj', '--configuration', 'Release', '--nologo',
        '-p:NuGetAudit=false', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false', '--filter',
        'FullyQualifiedName~FinanceAgentCoverageSurfaceTests|FullyQualifiedName~FinanceAgentWorkbenchSurfaceTests|FullyQualifiedName~AgentApiClientTests|FullyQualifiedName~LocalizationQualityGateTests',
        '--logger', 'trx;LogFileName=finance-agent-p2-web.trx', '--results-directory', $resultsPath)
    if ($NoRestore) { $webArguments += '--no-restore' }
    $results += Invoke-Check 'coverage-workbench-localization-ui' 'dotnet' $webArguments $webTrx

    $buildArguments = @('build', 'VirtualCompany.sln', '--configuration', 'Release', '--nologo', '-p:NuGetAudit=false', '-m:1', '-p:BuildInParallel=false', '-p:UseSharedCompilation=false')
    if ($NoRestore) { $buildArguments += '--no-restore' }
    $results += Invoke-Check 'release-build' 'dotnet' $buildArguments

    if ($FocusedOnly) {
        foreach ($name in @('hermetic-matrix', 'ef-pending-model', 'sqlserver-lanes', 'small-capacity-and-audit-package')) {
            $results += New-EvidenceResult $name 'not_run' 'FocusedOnly was selected; this mandatory release checkpoint was not executed.'
        }
    }
    else {
        $hermeticPath = Join-Path $resultsPath 'hermetic'
        $matrixArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'test-matrix.ps1'), '-Lane', 'hermetic', '-Configuration', 'Release', '-ResultsRoot', $hermeticPath, '-NoBuild')
        if ($NoRestore) { $matrixArguments += '-NoRestore' }
        $hermetic = Invoke-Check 'hermetic-matrix' 'pwsh' $matrixArguments
        $hermetic.counts = Read-TrxTreeCounts $hermeticPath
        if ($hermetic.counts.failed -gt 0 -or $hermetic.counts.skipped -gt 0) { $hermetic.outcome = 'failed' }
        $results += $hermetic
        $results += Invoke-Check 'ef-pending-model' 'dotnet' @('ef', 'migrations', 'has-pending-model-changes', '--project', 'src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj', '--startup-project', 'src/VirtualCompany.Api/VirtualCompany.Api.csproj', '--configuration', 'Release', '--no-build')

        if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION)) {
            $results += New-EvidenceResult 'sqlserver-lanes' 'prerequisite_missing' 'Set VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION for disposable SQL Server migration, concurrency, and rollback proof.'
            $results += New-EvidenceResult 'small-capacity-and-audit-package' 'prerequisite_missing' 'Set the SQL Server connection and VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE=small for supported-volume proof.'
        }
        else {
            $sqlPath = Join-Path $resultsPath 'sqlserver'
            $sql = Invoke-Check 'sqlserver-lanes' 'pwsh' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'test-matrix.ps1'), '-Lane', 'sqlserver', '-Configuration', 'Release', '-ResultsRoot', $sqlPath, '-NoBuild', '-NoRestore')
            $sql.counts = Read-TrxTreeCounts $sqlPath
            if ($sql.counts.failed -gt 0 -or $sql.counts.skipped -gt 0) { $sql.outcome = 'failed' }
            $results += $sql
            if ($env:VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE -ne 'small') {
                $detail = if ($env:VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE -eq 'medium') { 'Medium is retained as an unsupported candidate result; a small-profile pass is still required.' } else { 'Set VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE=small.' }
                $results += New-EvidenceResult 'small-capacity-and-audit-package' 'prerequisite_missing' $detail
            }
            else {
                $capacityPath = Join-Path $resultsPath 'capacity-small'
                $capacity = Invoke-Check 'small-capacity-and-audit-package' 'pwsh' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'test-matrix.ps1'), '-Lane', 'accounting-performance', '-Configuration', 'Release', '-ResultsRoot', $capacityPath, '-NoBuild', '-NoRestore')
                $capacity.counts = Read-TrxTreeCounts $capacityPath
                if ($capacity.counts.failed -gt 0 -or $capacity.counts.skipped -gt 0) { $capacity.outcome = 'failed' }
                $results += $capacity
            }
        }
    }

    $results += Test-RevisionBoundManifest 'p0-release-gate' $P0ManifestPath 'p0Decision' $workingTreeManifestChecksum
    $results += Test-RevisionBoundManifest 'p1-release-gate' $P1ManifestPath 'p1Decision' $workingTreeManifestChecksum
    $results += Test-RecoveryEvidence $RecoveryEvidencePath $workingTreeManifestChecksum
    $results += Test-BrowserEvidence $BrowserEvidencePath $workingTreeManifestChecksum

    if (Test-Path -LiteralPath $SwedishEvidenceVerifier -PathType Leaf) {
        $results += Invoke-Check 'swedish-accounting-technical-verification' 'python' @($SwedishEvidenceVerifier)
    }
    else {
        $results += New-EvidenceResult 'swedish-accounting-technical-verification' 'prerequisite_missing' 'The deterministic Swedish accounting evidence verifier is unavailable.'
    }
    $results += Test-ApprovalEvidence 'qualified-swedish-accountant-approval' $ProfessionalApprovalPath $workingTreeManifestChecksum
    $results += Test-ApprovalEvidence 'external-provider-scope-approval' $ProviderScopeApprovalPath $workingTreeManifestChecksum

    $releaseStops = @($results | Where-Object { $_.outcome -ne 'passed' } | ForEach-Object { "$($_.name):$($_.outcome)" })
    $manifestCore = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTime]::UtcNow.ToString('O')
        repositoryRevision = $revision
        dirtyWorkingTree = $dirty
        workingTreeManifestChecksum = $workingTreeManifestChecksum
        catalogue = [ordered]@{
            version = 'finance-agent-coverage-v1'
            baselineChecksum = (Get-FileHash -LiteralPath $catalogueDoc -Algorithm SHA256).Hash.ToLowerInvariant()
            designReferenceChecksum = (Get-FileHash -LiteralPath $designReference -Algorithm SHA256).Hash.ToLowerInvariant()
            completionMeasure = 'classified_operations_and_safety_checkpoints_not_percentage_completion'
        }
        capacityClaim = [ordered]@{ supportedProfile = 'small'; mediumProfile = 'unsupported_candidate_until_separately_approved' }
        p2Decision = if ($releaseStops.Count -eq 0) { 'go' } else { 'no_go' }
        technicalClassification = if ($releaseStops.Count -eq 0) { 'release_verified' } else { 'technical_or_human_verification_incomplete' }
        unresolvedExternalOrHumanApprovals = @($results | Where-Object { $_.outcome -in @('human_approval_pending', 'prerequisite_missing') } | ForEach-Object { $_.name })
        releaseStops = $releaseStops
        results = $results
        disclaimer = 'Engineering evidence only; not statutory approval or a signed professional opinion.'
    }
    $canonical = $manifestCore | ConvertTo-Json -Depth 10 -Compress
    $manifest = [ordered]@{}
    foreach ($pair in $manifestCore.GetEnumerator()) { $manifest[$pair.Key] = $pair.Value }
    $manifest['manifestCoreChecksum'] = Get-Sha256Text $canonical

    $markdown = @(
        '# Finance agent P2 release evidence', '',
        "- Generated UTC: $($manifest.generatedUtc)",
        "- Repository revision: $revision",
        "- Dirty working tree: $dirty",
        "- Working-tree manifest checksum: $workingTreeManifestChecksum",
        "- Catalogue version: $($manifest.catalogue.version)",
        "- P2 decision: $($manifest.p2Decision)",
        "- Classification: $($manifest.technicalClassification)", '',
        '| Checkpoint | Outcome | Passed | Failed | Skipped | Evidence / detail |',
        '| --- | --- | ---: | ---: | ---: | --- |')
    foreach ($result in $results) {
        $evidence = if ($result.evidence) { $result.evidence } else { $result.detail }
        $markdown += "| $($result.name) | $($result.outcome) | $($result.counts.passed) | $($result.counts.failed) | $($result.counts.skipped) | $evidence |"
    }
    $markdown += @('', 'Tool counts are not presented as percentage completion. Any failed safety checkpoint remains a release blocker.', '', 'This is engineering evidence only; it is not statutory approval or a signed professional opinion.')
    $evidencePath = Join-Path $resultsPath 'finance-agent-p2-release-evidence.md'
    $markdown | Set-Content -LiteralPath $evidencePath -Encoding utf8
    $manifest['evidenceChecksum'] = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestPath = Join-Path $resultsPath 'finance-agent-p2-manifest.json'
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    $results | Format-Table name, outcome, exitCode, durationSeconds -AutoSize
    Write-Host "P2 decision: $($manifest.p2Decision)"
    Write-Host "Manifest: $manifestPath"
    if ($releaseStops.Count -gt 0) { exit 2 }
}
finally { Pop-Location }
