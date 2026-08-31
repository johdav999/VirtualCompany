[CmdletBinding()]
param(
    [ValidateSet('hermetic', 'sqlserver', 'accounting-performance', 'advanced-ledger', 'audit-package', 'close-compliance-proof', 'connected-banking-failure', 'connected-banking-recovery', 'connected-banking-performance', 'docker-migration-restore', 'browser', 'real-provider', 'all')]
    [string]$Lane = 'hermetic',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ResultsRoot = (Join-Path $PSScriptRoot "..\artifacts\test-matrix\$(Get-Date -Format 'yyyyMMdd-HHmmss')"),
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$resultsPath = [System.IO.Path]::GetFullPath($ResultsRoot, $repositoryRoot)
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

$hermeticProjects = @(
    'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj',
    'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj',
    'tests/VirtualCompany.Infrastructure.Mailbox.Tests/VirtualCompany.Infrastructure.Mailbox.Tests.csproj',
    'tests/VirtualCompany.Infrastructure.Platform.Tests/VirtualCompany.Infrastructure.Platform.Tests.csproj',
    'tests/VirtualCompany.SalesSource.Tests/VirtualCompany.SalesSource.Tests.csproj',
    'tests/VirtualCompany.SupportGrounding.Tests/VirtualCompany.SupportGrounding.Tests.csproj',
    'tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj',
    'tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj'
)

function Invoke-TestProject {
    param([string]$Project, [string]$Category, [string]$Filter)

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($Project)
    $safeCategory = $Category -replace '[^A-Za-z0-9._-]', '-'
    $resultName = "$projectName-$safeCategory"
    $projectResults = Join-Path $resultsPath $resultName
    New-Item -ItemType Directory -Path $projectResults -Force | Out-Null
    $started = [DateTime]::UtcNow
    $arguments = @('test', $Project, '--configuration', $Configuration, '--nologo', '-p:NuGetAudit=false', '--logger', "trx;LogFileName=$resultName.trx", '--results-directory', $projectResults)
    if ($NoBuild) { $arguments += '--no-build' }
    if ($NoRestore) { $arguments += '--no-restore' }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) { $arguments += @('--filter', $Filter) }

    # Keep the native test runner's output visible without allowing PowerShell to
    # mix its individual output lines into the structured result collection.
    & dotnet @arguments | Out-Host
    $exitCode = $LASTEXITCODE
    $finished = [DateTime]::UtcNow
    $trxPath = Join-Path $projectResults "$resultName.trx"
    [pscustomobject]@{
        project = $Project
        category = $Category
        startedUtc = $started.ToString('O')
        durationSeconds = [Math]::Round(($finished - $started).TotalSeconds, 3)
        outcome = if ($exitCode -eq 0) { 'passed' } else { 'failed' }
        exitCode = $exitCode
        trx = if (Test-Path -LiteralPath $trxPath) { [System.IO.Path]::GetRelativePath($repositoryRoot, $trxPath) } else { $null }
        failureCategory = if ($exitCode -eq 0) { $null } elseif (Test-Path -LiteralPath $trxPath) { 'test-failure' } else { 'toolchain-or-prerequisite' }
    }
}

Push-Location $repositoryRoot
try {
    $results = @()
    if ($Lane -in @('hermetic', 'all')) {
        foreach ($project in $hermeticProjects) {
            $results += Invoke-TestProject -Project $project -Category 'hermetic' -Filter 'Category!=SqlServer&Category!=AccountingPerformance'
        }
    }

    if ($Lane -in @('sqlserver', 'all')) {
        if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION)) {
            foreach ($project in @('tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj', 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj')) {
                $results += [pscustomobject]@{ project = $project; category = 'sqlserver'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Set VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION to a dedicated disposable SQL Server instance.' }
            }
        }
        else {
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj' -Category 'sqlserver' -Filter 'Category=SqlServer'
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'sqlserver' -Filter 'Category=SqlServer'
        }
    }

    if ($Lane -in @('accounting-performance', 'all')) {
        if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION) -or
            $env:VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE -notin @('small', 'medium')) {
            $results += [pscustomobject]@{ project = 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj'; category = 'accounting-performance'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Set VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION and VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE=small|medium.' }
        }
        else {
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'accounting-performance' -Filter 'Category=AccountingPerformance'
        }
    }

    if ($Lane -eq 'advanced-ledger') {
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj' -Category 'advanced-ledger-hermetic' -Filter 'FullyQualifiedName~AdvancedLedgerGoldenScenarioTests|FullyQualifiedName~AccountingAdministrationServiceTests|FullyQualifiedName~AccountingOperationsTests|FullyQualifiedName~AccountingStatutoryReadinessTests|FullyQualifiedName~AccountingCloseServiceTests|FullyQualifiedName~AccountingCloseGovernanceDomainTests|FullyQualifiedName~ExchangeRateServiceTests|FullyQualifiedName~ForeignCurrencySettlementPolicyTests|FullyQualifiedName~CurrencyRevaluation|FullyQualifiedName~AccountingAllocation|FullyQualifiedName~AccountingSchedule|FullyQualifiedName~FixedAsset'
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'advanced-ledger-api' -Filter 'FullyQualifiedName~ReportingPeriodClose|FullyQualifiedName~AccountingCloseApiSurfaceTests|FullyQualifiedName~AccountingCloseGovernanceApiSurfaceTests|FullyQualifiedName~AccountingDimension|FullyQualifiedName~AccountingSchedule|FullyQualifiedName~CurrencyRevaluation|FullyQualifiedName~FixedAsset|FullyQualifiedName~AccountingGovernance'
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj' -Category 'advanced-ledger-web' -Filter 'FullyQualifiedName~AdvancedAccountingWorkspace|FullyQualifiedName~AccountingSchedules|FullyQualifiedName~FixedAssets'

        if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION)) {
            $results += [pscustomobject]@{ project = 'Advanced-ledger SQL Server recovery and concurrency'; category = 'advanced-ledger-sqlserver'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Set VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION to a dedicated disposable SQL Server instance and run the SQL Server categories. This is a release stop.' }
        }
        else {
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj' -Category 'advanced-ledger-sqlserver' -Filter 'Category=SqlServer'
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'advanced-ledger-sqlserver' -Filter 'Category=SqlServer'
        }

        if ($env:VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE -notin @('small', 'medium')) {
            $results += [pscustomobject]@{ project = 'Advanced-ledger production-shaped reporting'; category = 'advanced-ledger-performance'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Set VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE=small|medium with SQL Server and retain timings/query evidence. This is a release stop.' }
        }
        else {
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'advanced-ledger-performance' -Filter 'Category=AccountingPerformance'
        }

        $results += [pscustomobject]@{ project = 'Advanced-ledger Docker migration/restore'; category = 'advanced-ledger-docker-recovery'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'operator-evidence-required'; prerequisite = 'Run the documented Docker SQL/object restore rehearsal and compare the advanced recovery checksum. Automated success is not inferred.' }
        $results += [pscustomobject]@{ project = 'Advanced-ledger authenticated browser UAT'; category = 'advanced-ledger-browser'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'operator-evidence-required'; prerequisite = 'Run English/Swedish desktop and narrow UAT against an owned host and retain screenshots plus recovery-state results.' }
        $results += [pscustomobject]@{ project = 'Advanced-ledger qualified accounting review'; category = 'advanced-ledger-professional-review'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'human-review-pending'; prerequisite = 'A qualified reviewer must approve the exact frozen jurisdiction-specific scope and hashes. Engineering checks cannot satisfy this lane.' }
    }

    if ($Lane -in @('audit-package', 'all')) {
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj' -Category 'audit-package-hermetic' -Filter 'FullyQualifiedName~AuditPackageTests'
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'audit-package-authorization' -Filter 'FullyQualifiedName~AuditPackageAuthorizationTests'
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj' -Category 'audit-package-accountant-surface' -Filter 'FullyQualifiedName~AuditPackagesSurfaceTests'
        $results += [pscustomobject]@{ project = 'Audit-package coordinated SQL/object restore'; category = 'audit-package-recovery'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'operator-evidence-required'; prerequisite = 'Restore an exact database backup and matching object-store snapshot in an isolated environment, run package verification, and retain the database/object/manifest/item hash comparison.' }
        $results += [pscustomobject]@{ project = 'Audit-package production-shaped capacity'; category = 'audit-package-performance'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'operator-evidence-required'; prerequisite = 'Run the configured maximum ledger pages, documents, document bytes, and package bytes against production-shaped non-sensitive data and retain duration, memory, retry, and package-size evidence.' }
        $results += [pscustomobject]@{ project = 'Audit-package authenticated accountant UAT'; category = 'audit-package-browser'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'operator-evidence-required'; prerequisite = 'Run signed-in accountant and finance-approver flows against an owned host, including incomplete evidence, independent approval, one-time download, and verification.' }
        $results += [pscustomobject]@{ project = 'Audit-package qualified accounting and retention review'; category = 'audit-package-professional-review'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'human-review-pending'; prerequisite = 'A qualified accountant and records-management owner must approve the exact frozen evidence scope, retention, and legal-hold boundary.' }
    }

    if ($Lane -eq 'close-compliance-proof') {
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj' -Category 'close-compliance-hermetic' -Filter 'FullyQualifiedName~CloseComplianceReleaseReadinessPolicyTests|FullyQualifiedName~SwedishAccountingReleaseGoldenScenarioTests|FullyQualifiedName~AdvancedLedgerGoldenScenarioTests|FullyQualifiedName~FinancialReportSuiteDomainTests|FullyQualifiedName~ComplianceObligationDomainTests|FullyQualifiedName~AccountingCloseServiceTests|FullyQualifiedName~AccountingCloseGovernanceDomainTests|FullyQualifiedName~AuditPackageTests|FullyQualifiedName~YearEndRolloverDomainTests'
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'close-compliance-api-isolation' -Filter 'FullyQualifiedName~CloseComplianceReleaseReadinessApiSurfaceTests|FullyQualifiedName~AccountingCloseApiSurfaceTests|FullyQualifiedName~AccountingCloseGovernanceApiSurfaceTests|FullyQualifiedName~AccountantCollaborationIsolationIntegrationTests|FullyQualifiedName~AccountantCollaborationTests|FullyQualifiedName~AuditPackageAuthorizationTests|FullyQualifiedName~YearEndRolloverAuthorizationTests'
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj' -Category 'close-compliance-web-contract' -Filter 'FullyQualifiedName~AccountingCloseWorkspace|FullyQualifiedName~AccountantPortfolio|FullyQualifiedName~ComplianceCalendar|FullyQualifiedName~AuditPackages|FullyQualifiedName~YearEnd'

        if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION)) {
            $results += [pscustomobject]@{ project = 'Close/compliance fresh, upgrade, concurrency, and rollback SQL proof'; category = 'close-compliance-sqlserver'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Set VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION to a dedicated disposable SQL Server and retain fresh-install, upgrade, migration, concurrency, and rollback TRX evidence. This is a release stop.' }
        }
        else {
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'close-compliance-sqlserver' -Filter 'Category=SqlServer&FullyQualifiedName~AccountingCloseMigrationSqlServerTests|Category=SqlServer&FullyQualifiedName~AccountingIntegrityScenarioTests|Category=SqlServer&FullyQualifiedName~Migration'
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj' -Category 'close-compliance-sqlserver' -Filter 'Category=SqlServer'
        }

        if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION) -or
            $env:VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE -notin @('small', 'medium')) {
            $results += [pscustomobject]@{ project = 'Close/compliance production-shaped supported-volume and SLO proof'; category = 'close-compliance-capacity'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Run small or medium accounting performance against dedicated SQL Server, including large report and package generation, and retain duration, memory, retry, queue, and artifact-size evidence. This is a release stop.' }
        }
        else {
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'close-compliance-capacity' -Filter 'Category=AccountingPerformance'
        }

        $results += [pscustomobject]@{ project = 'Close/compliance Docker migration and coordinated SQL/object recovery'; category = 'close-compliance-recovery'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'operator-evidence-required'; prerequisite = 'Run fresh and upgraded Docker migrations; interrupt and restart workers; restore matching SQL and object snapshots; inject missing/corrupt objects; verify package manifests, original readiness hash, source links, and the post-restore correction path. This is a release stop.' }
        $results += [pscustomobject]@{ project = 'Close/compliance authenticated EN/SV browser UAT'; category = 'close-compliance-browser'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'operator-evidence-required'; prerequisite = 'Run finance and accountant flows on an owned authenticated host in English and Swedish, desktop and narrow layouts, keyboard/screen-reader checks, and Stockholm timezone/date boundaries. This is a release stop.' }
        $results += [pscustomobject]@{ project = 'Close/compliance submission-provider scope review'; category = 'close-compliance-provider-scope'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'operator-evidence-required'; prerequisite = 'Confirm and approve the implemented export_and_manual_evidence_only boundary. Do not claim direct authority submission or provider proof. This is a release stop.' }
        $results += [pscustomobject]@{ project = 'Close/compliance qualified professional review'; category = 'close-compliance-professional-review'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'human-review-pending'; prerequisite = 'A qualified Swedish accountant must approve the exact frozen policy pack, VAT/compliance scope, close outputs, evidence hashes, and exceptions. Engineering checks cannot satisfy this lane.' }
    }

    if ($Lane -in @('connected-banking-failure', 'all')) {
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj' -Category 'connected-banking-failure' -Filter 'FullyQualifiedName~BankFeedSynchronizationTests|FullyQualifiedName~EnableBankingProviderContractTests|FullyQualifiedName~PaymentExecutionDomainTests|FullyQualifiedName~ConnectedBankingReadinessServiceTests|FullyQualifiedName~ConnectedBankingRecoveryVerificationServiceTests'
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj' -Category 'connected-banking-failure' -Filter 'FullyQualifiedName~BankConnectionsAuthorizationTests|FullyQualifiedName~PaymentBatchAuthorizationTests|FullyQualifiedName~PaymentExecutionAuthorizationTests|FullyQualifiedName~TreasuryWorkspaceAuthorizationTests|FullyQualifiedName~ConnectedBankingReadinessApiIntegrationTests'
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj' -Category 'connected-banking-failure' -Filter 'FullyQualifiedName~BankConnectionsSurfaceTests|FullyQualifiedName~BankReconciliationSurfaceTests|FullyQualifiedName~PaymentBatchSurfaceTests|FullyQualifiedName~PaymentExecutionSurfaceTests|FullyQualifiedName~TreasuryWorkspaceSurfaceTests|FullyQualifiedName~FinanceApiClientTreasuryWorkspaceTests'
    }

    if ($Lane -in @('connected-banking-recovery', 'all')) {
        $results += Invoke-TestProject -Project 'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj' -Category 'connected-banking-recovery-hermetic' -Filter 'FullyQualifiedName~BankFeedSynchronizationTests.Expired_lease_resumes_from_committed_cursor_after_interrupted_page_without_gap_or_duplicate|FullyQualifiedName~ConnectedBankingRecoveryVerificationServiceTests'
        if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION)) {
            $results += [pscustomobject]@{ project = 'Connected-banking SQL recovery'; category = 'connected-banking-recovery-sqlserver'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Set VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION to run BankFeedSqlServerIntegrationTests and PaymentExecutionSqlServerIntegrationTests against a dedicated disposable SQL Server instance.' }
        }
        else {
            $results += Invoke-TestProject -Project 'tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj' -Category 'connected-banking-recovery-sqlserver' -Filter 'FullyQualifiedName~BankFeedSqlServerIntegrationTests|FullyQualifiedName~PaymentExecutionSqlServerIntegrationTests'
        }
    }

    if ($Lane -in @('connected-banking-performance', 'all')) {
        if ([string]::IsNullOrWhiteSpace($env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION) -or
            $env:VIRTUALCOMPANY_CONNECTED_BANKING_PERF_PROFILE -notin @('small', 'medium')) {
            $results += [pscustomobject]@{ project = 'Connected-banking production-shaped capacity'; category = 'connected-banking-performance'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Set VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION and VIRTUALCOMPANY_CONNECTED_BANKING_PERF_PROFILE=small|medium, then run the production-shaped generator and record its signed result using the connected-banking capacity runbook. The lane remains a release stop until that fixture is available.' }
        }
        else {
            $results += [pscustomobject]@{ project = 'Connected-banking production-shaped capacity'; category = 'connected-banking-performance'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'test-fixture-not-configured'; prerequisite = 'The profile and SQL Server are configured, but a production-shaped provider/feed volume generator has not been supplied to this checkout. Do not treat this lane as passing.' }
        }
    }

    if ($Lane -in @('docker-migration-restore', 'all')) {
        $results += [pscustomobject]@{ project = 'Docker migration and restore'; category = 'docker-migration-restore'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Start the documented Docker SQL Server environment and execute the restore/migration verification runbook.' }
    }

    if ($Lane -in @('browser', 'all')) {
        $results += [pscustomobject]@{ project = 'Browser smoke suite'; category = 'browser'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Run only against an owned local host; do not depend on an already-running developer host.' }
    }

    if ($Lane -in @('real-provider', 'all')) {
        $results += [pscustomobject]@{ project = 'Real provider checks'; category = 'real-provider'; startedUtc = [DateTime]::UtcNow.ToString('O'); durationSeconds = 0; outcome = 'not-run'; exitCode = $null; trx = $null; failureCategory = 'prerequisite-not-configured'; prerequisite = 'Opt-in only with dedicated non-production credentials and explicit operator approval.' }
    }

    $releaseDecision = if ($Lane -eq 'close-compliance-proof') {
        if (@($results | Where-Object { $_.outcome -ne 'passed' }).Count -eq 0) { 'go' } else { 'no_go' }
    } else { 'not_evaluated' }
    $canonicalResults = $results | ConvertTo-Json -Depth 6 -Compress
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $evidenceChecksum = [System.Convert]::ToHexString(
            $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($canonicalResults))).ToLowerInvariant()
    }
    finally { $sha256.Dispose() }

    $manifest = [pscustomobject]@{
        schemaVersion = 2
        generatedUtc = [DateTime]::UtcNow.ToString('O')
        lane = $Lane
        repository = $repositoryRoot.Path
        noBuild = [bool]$NoBuild
        noRestore = [bool]$NoRestore
        releaseDecision = $releaseDecision
        evidenceChecksum = $evidenceChecksum
        releaseStops = @($results | Where-Object { $_.outcome -ne 'passed' } | ForEach-Object { $_.category })
        results = $results
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resultsPath 'matrix-manifest.json') -Encoding utf8
    $results | Format-Table project, category, outcome, durationSeconds, trx -AutoSize

    if (($results | Where-Object { $_.outcome -eq 'failed' }).Count -gt 0) { exit 1 }
    if ($Lane -eq 'close-compliance-proof' -and $releaseDecision -eq 'no_go') { exit 2 }
}
finally {
    Pop-Location
}
