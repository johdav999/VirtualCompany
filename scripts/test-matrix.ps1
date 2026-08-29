[CmdletBinding()]
param(
    [ValidateSet('hermetic', 'sqlserver', 'accounting-performance', 'connected-banking-failure', 'connected-banking-recovery', 'connected-banking-performance', 'docker-migration-restore', 'browser', 'real-provider', 'all')]
    [string]$Lane = 'hermetic',
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
    $projectResults = Join-Path $resultsPath $projectName
    New-Item -ItemType Directory -Path $projectResults -Force | Out-Null
    $started = [DateTime]::UtcNow
    $arguments = @('test', $Project, '--configuration', 'Release', '--nologo', '-p:NuGetAudit=false', '--logger', "trx;LogFileName=$projectName.trx", '--results-directory', $projectResults)
    if ($NoBuild) { $arguments += '--no-build' }
    if ($NoRestore) { $arguments += '--no-restore' }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) { $arguments += @('--filter', $Filter) }

    # Keep the native test runner's output visible without allowing PowerShell to
    # mix its individual output lines into the structured result collection.
    & dotnet @arguments | Out-Host
    $exitCode = $LASTEXITCODE
    $finished = [DateTime]::UtcNow
    $trxPath = Join-Path $projectResults "$projectName.trx"
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
            $results += Invoke-TestProject -Project $project -Category 'hermetic' -Filter 'Category!=SqlServer'
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

    $manifest = [pscustomobject]@{
        schemaVersion = 1
        generatedUtc = [DateTime]::UtcNow.ToString('O')
        lane = $Lane
        repository = $repositoryRoot.Path
        noBuild = [bool]$NoBuild
        noRestore = [bool]$NoRestore
        results = $results
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resultsPath 'matrix-manifest.json') -Encoding utf8
    $results | Format-Table project, category, outcome, durationSeconds, trx -AutoSize

    if (($results | Where-Object { $_.outcome -eq 'failed' }).Count -gt 0) { exit 1 }
}
finally {
    Pop-Location
}
