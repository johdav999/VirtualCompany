[CmdletBinding()]
param(
    [string]$ResultsRoot = "artifacts/finance-agent-p0/$(Get-Date -Format 'yyyyMMdd-HHmmss')",
    [string]$SwedishEvidenceVerifier,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$resultsPath = [System.IO.Path]::GetFullPath($ResultsRoot, $repositoryRoot)

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
    return [pscustomobject]@{
        total = [int]$counters.total
        executed = [int]$counters.executed
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = [int]$counters.notExecuted
    }
}

function Read-TrxTreeCounts([string]$Path) {
    $sum = [ordered]@{ total = 0; executed = 0; passed = 0; failed = 0; skipped = 0 }
    foreach ($trx in @(Get-ChildItem -LiteralPath $Path -Filter '*.trx' -Recurse -File)) {
        $counts = Read-TrxCounts $trx.FullName
        foreach ($name in @('total', 'executed', 'passed', 'failed', 'skipped')) {
            $sum[$name] += $counts.$name
        }
    }
    return [pscustomobject]$sum
}

function Invoke-Check {
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$TrxPath
    )
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

$revision = (& git rev-parse HEAD).Trim()
$dirty = @(& git status --porcelain).Count -gt 0
$workingTreeManifestChecksum = Get-WorkingTreeManifestChecksum
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

Push-Location $repositoryRoot
try {
    $results = @()
    $focusedTrx = Join-Path $resultsPath 'finance-agent-p0-focused.trx'
    $focusedArguments = @(
        'test', 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj',
        '--configuration', 'Release', '--nologo', '-p:NuGetAudit=false',
        '--filter', 'FullyQualifiedName~FinanceAgentAuthorityMatrixTests|FullyQualifiedName~FinanceAgentAuthorizationServiceTests|FullyQualifiedName~FinanceToolRiskPolicyTests|FullyQualifiedName~FinanceToolExecutionFlowIntegrationTests|FullyQualifiedName~ApprovalDecisionChainTests|FullyQualifiedName~AgentEffectiveAuthorityResolverTests|FullyQualifiedName~FinanceAgentAuthorityMigrationTests',
        '--logger', 'trx;LogFileName=finance-agent-p0-focused.trx', '--results-directory', $resultsPath)
    if ($NoRestore) { $focusedArguments += '--no-restore' }
    $results += Invoke-Check -Name 'focused-p0' -FilePath 'dotnet' -Arguments $focusedArguments -TrxPath $focusedTrx

    $buildArguments = @('build', 'VirtualCompany.sln', '--configuration', 'Release', '--nologo', '-p:NuGetAudit=false')
    if ($NoRestore) { $buildArguments += '--no-restore' }
    $results += Invoke-Check -Name 'release-build' -FilePath 'dotnet' -Arguments $buildArguments

    $hermeticPath = Join-Path $resultsPath 'hermetic'
    $matrixArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $PSScriptRoot 'test-matrix.ps1'), '-Lane', 'hermetic', '-Configuration', 'Release',
        '-ResultsRoot', $hermeticPath, '-NoBuild')
    if ($NoRestore) { $matrixArguments += '-NoRestore' }
    $hermeticResult = Invoke-Check -Name 'hermetic-matrix' -FilePath 'pwsh' -Arguments $matrixArguments
    $hermeticResult.counts = Read-TrxTreeCounts $hermeticPath
    if ($hermeticResult.counts.failed -gt 0 -or $hermeticResult.counts.skipped -gt 0) {
        $hermeticResult.outcome = 'failed'
    }
    $results += $hermeticResult

    $pendingArguments = @('ef', 'migrations', 'has-pending-model-changes',
        '--project', 'src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj',
        '--startup-project', 'src/VirtualCompany.Api/VirtualCompany.Api.csproj',
        '--configuration', 'Release', '--no-build')
    $results += Invoke-Check -Name 'ef-pending-model' -FilePath 'dotnet' -Arguments $pendingArguments

    if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION)) {
        $results += [pscustomobject]@{
            name = 'sqlserver-migration-concurrency-rollback'
            command = 'pwsh scripts/test-matrix.ps1 -Lane sqlserver -Configuration Release -NoBuild'
            startedUtc = [DateTime]::UtcNow.ToString('O')
            durationSeconds = 0
            exitCode = $null
            outcome = 'prerequisite_missing'
            counts = [pscustomobject]@{ total = 0; executed = 0; passed = 0; failed = 0; skipped = 0 }
            log = $null
        }
    }
    else {
        $sqlTrx = Join-Path $resultsPath 'finance-agent-p0-sqlserver.trx'
        $sqlArguments = @(
            'test', 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj',
            '--configuration', 'Release', '--nologo', '-p:NuGetAudit=false', '--no-build',
            '--filter', 'Category=SqlServer&FullyQualifiedName~FinanceToolExecutionFlowIntegrationTests.Sql_server_approval_continuation_is_atomic_and_ambiguous_results_require_reconciliation',
            '--logger', 'trx;LogFileName=finance-agent-p0-sqlserver.trx', '--results-directory', $resultsPath)
        if ($NoRestore) { $sqlArguments += '--no-restore' }
        $results += Invoke-Check -Name 'sqlserver-migration-concurrency-rollback' -FilePath 'dotnet' -Arguments $sqlArguments -TrxPath $sqlTrx
    }

    if ([string]::IsNullOrWhiteSpace($SwedishEvidenceVerifier) -or
        -not (Test-Path -LiteralPath $SwedishEvidenceVerifier -PathType Leaf)) {
        $results += [pscustomobject]@{
            name = 'swedish-accounting-evidence-verifier'
            command = 'python <swedish-accountant-expert>/scripts/verify_virtual_company_evidence.py'
            startedUtc = [DateTime]::UtcNow.ToString('O')
            durationSeconds = 0
            exitCode = $null
            outcome = 'prerequisite_missing'
            counts = [pscustomobject]@{ total = 0; executed = 0; passed = 0; failed = 0; skipped = 0 }
            log = $null
        }
    }
    else {
        $results += Invoke-Check -Name 'swedish-accounting-evidence-verifier' -FilePath 'python' -Arguments @($SwedishEvidenceVerifier)
    }

    $releaseStops = @($results | Where-Object { $_.outcome -ne 'passed' } | ForEach-Object { $_.name })
    $manifestCore = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTime]::UtcNow.ToString('O')
        repositoryRevision = $revision
        dirtyWorkingTree = $dirty
        workingTreeManifestChecksum = $workingTreeManifestChecksum
        p0Decision = if ($releaseStops.Count -eq 0) { 'go' } else { 'no_go' }
        technicalClassification = if ($releaseStops.Count -eq 0) { 'technically_verified_for_human_review' } else { 'technical_verification_incomplete' }
        humanAccountingReview = 'human_accountant_review_pending'
        releaseStops = $releaseStops
        results = $results
        disclaimer = 'Engineering evidence only; not statutory approval or a signed professional opinion.'
    }
    $canonical = $manifestCore | ConvertTo-Json -Depth 8 -Compress
    $manifest = [ordered]@{}
    foreach ($pair in $manifestCore.GetEnumerator()) { $manifest[$pair.Key] = $pair.Value }
    $manifest['evidenceChecksum'] = Get-Sha256Text $canonical
    $manifestPath = Join-Path $resultsPath 'finance-agent-p0-manifest.json'
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    $markdown = @(
        '# Finance agent P0 release evidence', '',
        "- Generated UTC: $($manifest.generatedUtc)",
        "- Repository revision: $revision",
        "- Dirty working tree: $dirty",
        "- Working-tree manifest checksum: $workingTreeManifestChecksum",
        "- Evidence checksum: $($manifest.evidenceChecksum)",
        "- P0 decision: $($manifest.p0Decision)",
        "- Technical classification: $($manifest.technicalClassification)",
        "- Human accounting review: $($manifest.humanAccountingReview)", '',
        '| Checkpoint | Outcome | Passed | Failed | Skipped | Command |',
        '| --- | --- | ---: | ---: | ---: | --- |')
    foreach ($result in $results) {
        $markdown += "| $($result.name) | $($result.outcome) | $($result.counts.passed) | $($result.counts.failed) | $($result.counts.skipped) | ``$($result.command)`` |"
    }
    $markdown += @('', 'This is engineering evidence only; it is not statutory approval or a signed professional opinion.')
    $markdown | Set-Content -LiteralPath (Join-Path $resultsPath 'finance-agent-p0-release-evidence.md') -Encoding utf8

    $results | Format-Table name, outcome, exitCode, durationSeconds -AutoSize
    Write-Host "P0 decision: $($manifest.p0Decision)"
    Write-Host "Manifest: $manifestPath"
    if ($releaseStops.Count -gt 0) { exit 2 }
}
finally {
    Pop-Location
}
