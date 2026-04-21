using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSeedBackfillOrchestratorTests
{
    [Fact]
    public async Task Backfill_rerun_skips_companies_with_active_finance_seed_work()
    {
        await using var connection = await OpenConnectionAsync();
        var services = CreateSchedulerServices(connection);

        await using var setupScope = services.CreateAsyncScope();
        var setupContext = setupScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await setupContext.Database.EnsureCreatedAsync();

        var notSeededCompany = new Company(Guid.NewGuid(), "Not Seeded Company");
        var partialCompany = new Company(Guid.NewGuid(), "Partial Seed Company");
        partialCompany.SetFinanceSeedStatus(FinanceSeedingState.Seeding, DateTime.UtcNow);

        setupContext.Companies.AddRange(notSeededCompany, partialCompany);
        setupContext.FinanceAccounts.Add(new FinanceAccount(
            Guid.NewGuid(),
            partialCompany.Id,
            "1000",
            "Cash",
            "asset",
            "USD",
            1200m,
            DateTime.UtcNow.AddDays(-30)));
        await setupContext.SaveChangesAsync();

        await using var runScope = services.CreateAsyncScope();
        var runContext = runScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var scheduler = runScope.ServiceProvider.GetRequiredService<IFinanceSeedBackfillExecutionScheduler>();
        var orchestrator = CreateOrchestrator(
            runContext,
            scheduler,
            new NoOpDelayStrategy(),
            new FinanceSeedBackfillWorkerOptions
            {
                ScanPageSize = 10,
                EnqueueBatchSize = 10,
                MaxCompaniesPerRun = 10,
                MaxConcurrentEnqueues = 2,
                DelayBetweenBatchesMs = 1
            });

        var firstRun = await orchestrator.RunAsync(CancellationToken.None);
        var secondRun = await orchestrator.RunAsync(CancellationToken.None);

        Assert.Equal(FinanceSeedBackfillRunStatus.Completed, firstRun.Status);
        Assert.Equal(2, firstRun.QueuedCount);
        Assert.Equal(0, firstRun.FailedCount);

        Assert.Equal(FinanceSeedBackfillRunStatus.Completed, secondRun.Status);
        Assert.Equal(0, secondRun.QueuedCount);
        Assert.Equal(2, secondRun.SkippedCount);

        var executions = await runContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .Where(x => x.ExecutionType == BackgroundExecutionType.FinanceSeed)
            .ToListAsync();

        Assert.Equal(2, executions.Count);

        var secondRunAttempts = await runContext.FinanceSeedBackfillAttempts
            .IgnoreQueryFilters()
            .Where(x => x.RunId == secondRun.RunId)
            .OrderBy(x => x.CompanyId)
            .ToListAsync();

        Assert.Equal(2, secondRunAttempts.Count);
        Assert.All(secondRunAttempts, attempt => Assert.Equal(FinanceSeedBackfillAttemptStatus.Skipped, attempt.Status));
        Assert.All(secondRunAttempts, attempt => Assert.Equal(FinanceSeedBackfillSkipReasons.ActiveExecution, attempt.SkipReason));
    }

    [Fact]
    public async Task Backfill_scans_not_seeded_and_eligible_partial_companies_and_reconciles_operator_counts_end_to_end()
    {
        await using var connection = await OpenConnectionAsync();
        var services = CreateSchedulerServices(connection);

        var startedUtc = new DateTime(2026, 4, 17, 9, 0, 0, DateTimeKind.Utc);

        await using (var setupScope = services.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            await setupContext.Database.EnsureCreatedAsync();

            var notSeededCompany = new Company(Guid.NewGuid(), "Queued Not Seeded Company");
            var partialCompany = new Company(Guid.NewGuid(), "Queued Partial Company");
            partialCompany.SetFinanceSeedStatus(FinanceSeedingState.Seeding, startedUtc);

            var failedCompany = new Company(Guid.NewGuid(), "Skipped Failed Company");
            failedCompany.SetFinanceSeedStatus(FinanceSeedingState.Failed, startedUtc);

            var activeExecutionCompany = new Company(Guid.NewGuid(), "Skipped Active Execution Company");
            activeExecutionCompany.SetFinanceSeedStatus(FinanceSeedingState.Seeding, startedUtc);

            setupContext.Companies.AddRange(notSeededCompany, partialCompany, failedCompany, activeExecutionCompany);
            setupContext.FinanceAccounts.Add(new FinanceAccount(
                Guid.NewGuid(),
                partialCompany.Id,
                "1000",
                "Cash",
                "asset",
                "USD",
                1250m,
                startedUtc.AddDays(-30)));
            setupContext.BackgroundExecutions.Add(new BackgroundExecution(
                Guid.NewGuid(),
                activeExecutionCompany.Id,
                BackgroundExecutionType.FinanceSeed,
                BackgroundExecutionRelatedEntityTypes.FinanceSeed,
                activeExecutionCompany.Id.ToString("D"),
                "finance-seed-active",
                $"finance-seed:{activeExecutionCompany.Id:N}",
                maxAttempts: 5));
            await setupContext.SaveChangesAsync();
        }

        FinanceSeedBackfillRunDto run;
        await using (var orchestratorScope = services.CreateAsyncScope())
        {
            var dbContext = orchestratorScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var orchestrator = orchestratorScope.ServiceProvider.GetRequiredService<IFinanceSeedBackfillOrchestrator>();
            run = await orchestrator.RunAsync(CancellationToken.None);
        }

        Assert.Equal(FinanceSeedBackfillRunStatus.Completed, run.Status);
        Assert.Equal(4, run.ScannedCount);
        Assert.Equal(2, run.QueuedCount);
        Assert.Equal(2, run.SkippedCount);
        Assert.Equal(0, run.FailedCount);

        await using (var runnerContext = CreateContext(connection))
        {
            var runner = CreateRunner(runnerContext);
            var handled = await runner.RunDueAsync(CancellationToken.None);
            Assert.Equal(2, handled);
        }

        await using var verificationContext = CreateContext(connection);
        var persistedRun = await verificationContext.FinanceSeedBackfillRuns
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == run.RunId);
        var attempts = await verificationContext.FinanceSeedBackfillAttempts
            .IgnoreQueryFilters()
            .Where(x => x.RunId == run.RunId)
            .OrderBy(x => x.CompanyId)
            .ToListAsync();

        Assert.Equal(4, attempts.Count);
        Assert.Equal(2, persistedRun.SucceededCount);
        Assert.Equal(2, persistedRun.SkippedCount);
        Assert.Equal(0, persistedRun.FailedCount);
        Assert.Equal(2, attempts.Count(x => x.Status == FinanceSeedBackfillAttemptStatus.Succeeded));

        Assert.Contains(attempts, attempt => attempt.Status == FinanceSeedBackfillAttemptStatus.Skipped && attempt.SkipReason == FinanceSeedBackfillSkipReasons.ActiveExecution);
        Assert.Contains(attempts, attempt => attempt.Status == FinanceSeedBackfillAttemptStatus.Skipped && attempt.SkipReason == FinanceSeedBackfillSkipReasons.IneligibleState);
        Assert.Equal(2, await verificationContext.BackgroundExecutions.IgnoreQueryFilters().CountAsync(x => x.ExecutionType == BackgroundExecutionType.FinanceSeed && x.Status == BackgroundExecutionStatus.Succeeded));
    }

    [Fact]
    public async Task Backfill_rerun_after_completed_seed_skips_already_seeded_company_and_reconciles_run_metrics()
    {
        await using var connection = await OpenConnectionAsync();
        var services = CreateSchedulerServices(connection);

        await using var setupScope = services.CreateAsyncScope();
        var setupContext = setupScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await setupContext.Database.EnsureCreatedAsync();

        var company = new Company(Guid.NewGuid(), "Completed Seed Company");
        setupContext.Companies.Add(company);
        await setupContext.SaveChangesAsync();

        FinanceSeedBackfillRunDto firstRun;
        FinanceSeedDatasetCounts firstSeedSnapshot;
        await using (var firstRunScope = services.CreateAsyncScope())
        {
            var dbContext = firstRunScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var scheduler = firstRunScope.ServiceProvider.GetRequiredService<IFinanceSeedBackfillExecutionScheduler>();
            var orchestrator = CreateOrchestrator(
                dbContext,
                scheduler,
                new NoOpDelayStrategy(),
                new FinanceSeedBackfillWorkerOptions
                {
                    ScanPageSize = 10,
                    EnqueueBatchSize = 10,
                    MaxCompaniesPerRun = 10,
                    MaxConcurrentEnqueues = 2,
                    DelayBetweenBatchesMs = 1
                });

            firstRun = await orchestrator.RunAsync(CancellationToken.None);
        }

        await using (var runnerContext = CreateContext(connection))
        {
            var runner = CreateRunner(runnerContext);
            var handled = await runner.RunDueAsync(CancellationToken.None);
            Assert.Equal(1, handled);
        }

        await using (var verificationContext = CreateContext(connection))
        {
            var persistedFirstRun = await verificationContext.FinanceSeedBackfillRuns
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == firstRun.RunId);

            Assert.Equal(FinanceSeedBackfillRunStatus.Completed, persistedFirstRun.Status);
            Assert.Equal(1, persistedFirstRun.ScannedCount);
            Assert.Equal(1, persistedFirstRun.QueuedCount);
            Assert.Equal(1, persistedFirstRun.SucceededCount);
            Assert.Equal(0, persistedFirstRun.SkippedCount);
            Assert.Equal(0, persistedFirstRun.FailedCount);
            Assert.Equal(1, await verificationContext.BackgroundExecutions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == company.Id));
            firstSeedSnapshot = await CaptureSeedCountsAsync(verificationContext, company.Id);
        }

        FinanceSeedBackfillRunDto secondRun;
        await using (var secondRunScope = services.CreateAsyncScope())
        {
            var dbContext = secondRunScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var scheduler = secondRunScope.ServiceProvider.GetRequiredService<IFinanceSeedBackfillExecutionScheduler>();
            var orchestrator = CreateOrchestrator(dbContext, scheduler, new NoOpDelayStrategy(), new FinanceSeedBackfillWorkerOptions());
            secondRun = await orchestrator.RunAsync(CancellationToken.None);
        }

        Assert.Equal(FinanceSeedBackfillRunStatus.Completed, secondRun.Status);
        Assert.Equal(0, secondRun.QueuedCount);
        Assert.Equal(1, secondRun.SkippedCount);

        await using var secondVerificationContext = CreateContext(connection);
        var secondRunAttempts = await secondVerificationContext.FinanceSeedBackfillAttempts
            .IgnoreQueryFilters()
            .Where(x => x.RunId == secondRun.RunId)
            .ToListAsync();

        var skippedAttempt = Assert.Single(secondRunAttempts);
        Assert.Equal(FinanceSeedBackfillAttemptStatus.Skipped, skippedAttempt.Status);
        Assert.Equal(FinanceSeedBackfillSkipReasons.AlreadySeeded, skippedAttempt.SkipReason);

        var secondSeedSnapshot = await CaptureSeedCountsAsync(secondVerificationContext, company.Id);
        Assert.Equal(firstSeedSnapshot, secondSeedSnapshot);
        Assert.Equal(1, await secondVerificationContext.BackgroundExecutions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == company.Id));
    }

    [Fact]
    public async Task Backfill_records_failure_timestamps_and_error_details_and_recovers_partial_company_on_rerun()
    {
        await using var connection = await OpenConnectionAsync();
        var services = CreateSchedulerServices(connection);
        var seededAtUtc = new DateTime(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc);

        Guid healthyCompanyId;
        Guid partialCompanyId;

        await using (var setupScope = services.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            await setupContext.Database.EnsureCreatedAsync();

            var healthyCompany = new Company(Guid.NewGuid(), "Healthy Rerun Company");
            var partialCompany = new Company(Guid.NewGuid(), "Recoverable Partial Company");
            partialCompany.SetFinanceSeedStatus(FinanceSeedingState.Seeding, seededAtUtc);

            setupContext.Companies.AddRange(healthyCompany, partialCompany);
            setupContext.FinanceAccounts.Add(new FinanceAccount(
                Guid.NewGuid(),
                partialCompany.Id,
                "1000",
                "Cash",
                "asset",
                "USD",
                900m,
                seededAtUtc.AddDays(-14)));
            await setupContext.SaveChangesAsync();

            healthyCompanyId = healthyCompany.Id;
            partialCompanyId = partialCompany.Id;
        }

        FinanceSeedBackfillRunDto firstRun;
        await using (var firstRunScope = services.CreateAsyncScope())
        {
            var orchestrator = firstRunScope.ServiceProvider.GetRequiredService<IFinanceSeedBackfillOrchestrator>();
            firstRun = await orchestrator.RunAsync(CancellationToken.None);
        }

        await using (var failingRunnerContext = CreateContext(connection))
        {
            var runner = CreateRunner(
                failingRunnerContext,
                new SelectiveThrowingFinanceSeedBootstrapService(
                    failingRunnerContext,
                    partialCompanyId,
                    "Configured finance seed bootstrap failure for rerun recovery."));
            var handled = await runner.RunDueAsync(CancellationToken.None);
            Assert.Equal(2, handled);
        }

        FinanceSeedDatasetCounts healthySnapshotAfterFirstRun;
        await using (var firstVerificationContext = CreateContext(connection))
        {
            var persistedRun = await firstVerificationContext.FinanceSeedBackfillRuns
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == firstRun.RunId);
            var partialAttempt = await firstVerificationContext.FinanceSeedBackfillAttempts
                .IgnoreQueryFilters()
                .SingleAsync(x => x.RunId == firstRun.RunId && x.CompanyId == partialCompanyId);

            Assert.Equal(FinanceSeedBackfillRunStatus.CompletedWithErrors, persistedRun.Status);
            Assert.Equal(2, persistedRun.ScannedCount);
            Assert.Equal(2, persistedRun.QueuedCount);
            Assert.Equal(1, persistedRun.SucceededCount);
            Assert.Equal(1, persistedRun.FailedCount);
            Assert.Equal(FinanceSeedBackfillAttemptStatus.Failed, partialAttempt.Status);
            Assert.NotEqual(default, partialAttempt.StartedUtc);
            Assert.NotNull(partialAttempt.CompletedUtc);
            Assert.True(partialAttempt.CompletedUtc >= partialAttempt.StartedUtc);
            Assert.Contains("rerun recovery", partialAttempt.ErrorDetails ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            var partialCompany = await firstVerificationContext.Companies.IgnoreQueryFilters().SingleAsync(x => x.Id == partialCompanyId);
            Assert.Equal(FinanceSeedingState.Failed, partialCompany.FinanceSeedStatus);
            healthySnapshotAfterFirstRun = await CaptureSeedCountsAsync(firstVerificationContext, healthyCompanyId);
        }

        FinanceSeedBackfillRunDto secondRun;
        await using (var secondRunScope = services.CreateAsyncScope())
        {
            var orchestrator = secondRunScope.ServiceProvider.GetRequiredService<IFinanceSeedBackfillOrchestrator>();
            secondRun = await orchestrator.RunAsync(CancellationToken.None);
        }

        await using (var recoveryRunnerContext = CreateContext(connection))
        {
            var runner = CreateRunner(recoveryRunnerContext);
            var handled = await runner.RunDueAsync(CancellationToken.None);
            Assert.Equal(1, handled);
        }

        await using var recoveryVerificationContext = CreateContext(connection);
        var recoveredAttempt = await recoveryVerificationContext.FinanceSeedBackfillAttempts
            .IgnoreQueryFilters()
            .SingleAsync(x => x.RunId == secondRun.RunId && x.CompanyId == partialCompanyId);
        var recoveredCompany = await recoveryVerificationContext.Companies.IgnoreQueryFilters().SingleAsync(x => x.Id == partialCompanyId);
        var healthySnapshotAfterSecondRun = await CaptureSeedCountsAsync(recoveryVerificationContext, healthyCompanyId);
        var recoveredSnapshot = await CaptureSeedCountsAsync(recoveryVerificationContext, partialCompanyId);

        Assert.Equal(FinanceSeedBackfillAttemptStatus.Succeeded, recoveredAttempt.Status);
        Assert.Equal(FinanceSeedingState.Seeded, recoveredCompany.FinanceSeedStatus);
        Assert.Equal(healthySnapshotAfterFirstRun, healthySnapshotAfterSecondRun);
        Assert.True(recoveredSnapshot.AccountCount > 1);
        Assert.True(recoveredSnapshot.TransactionCount > 0);
        Assert.True(recoveredSnapshot.BankAccountCount > 0);
        Assert.True(recoveredSnapshot.BankTransactionCount > 0);
        Assert.Equal(1, await recoveryVerificationContext.BackgroundExecutions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == healthyCompanyId));
        Assert.Equal(2, await recoveryVerificationContext.BackgroundExecutions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == partialCompanyId));
    }

    [Fact]
    public async Task Backfill_upgrades_seeded_company_missing_bank_accounts_and_transactions_without_replacing_finance_data()
    {
        await using var connection = await OpenConnectionAsync();
        var services = CreateSchedulerServices(connection);

        var companyId = Guid.NewGuid();
        var financeAccountId = Guid.NewGuid();
        var financeTransactionId = Guid.NewGuid();

        await using (var setupScope = services.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            await setupContext.Database.EnsureCreatedAsync();

            var company = new Company(companyId, "Banking Upgrade Company");
            company.SetFinanceSeedStatus(
                FinanceSeedingState.Seeded,
                new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc));
            setupContext.Companies.Add(company);

            setupContext.FinanceAccounts.Add(new FinanceAccount(
                financeAccountId,
                companyId,
                "1000",
                "Operating Cash",
                "asset",
                "USD",
                5000m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            setupContext.FinanceCounterparties.Add(new FinanceCounterparty(
                Guid.NewGuid(),
                companyId,
                "Northwind Analytics",
                "customer",
                "ap@northwind.example",
                "Net30",
                null,
                0m,
                "bank_transfer",
                "1100"));
            setupContext.FinanceTransactions.Add(new FinanceTransaction(
                financeTransactionId,
                companyId,
                financeAccountId,
                null,
                null,
                null,
                new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc),
                "customer_payment",
                1500m,
                "USD",
                "Seeded finance transaction",
                $"seeded-finance:{companyId:N}",
                null));
            setupContext.FinanceBalances.Add(new FinanceBalance(
                Guid.NewGuid(),
                companyId,
                financeAccountId,
                new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                6500m,
                "USD"));
            setupContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(
                Guid.NewGuid(),
                companyId,
                "USD",
                10000m,
                5000m,
                true,
                -10000m,
                10000m,
                90,
                30));
            await setupContext.SaveChangesAsync();
        }

        await using (var orchestratorScope = services.CreateAsyncScope())
        {
            var orchestrator = orchestratorScope.ServiceProvider.GetRequiredService<IFinanceSeedBackfillOrchestrator>();
            var run = await orchestrator.RunAsync(CancellationToken.None);
            Assert.Equal(1, run.QueuedCount);
            Assert.Equal(0, run.SkippedCount);
        }

        await using (var runnerContext = CreateContext(connection))
        {
            var runner = CreateRunner(runnerContext);
            var handled = await runner.RunDueAsync(CancellationToken.None);
            Assert.Equal(1, handled);
        }

        await using var verificationContext = CreateContext(connection);
        Assert.Equal(1, await verificationContext.FinanceTransactions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId));
        Assert.NotNull(await verificationContext.FinanceTransactions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == financeTransactionId));
        Assert.True(await verificationContext.CompanyBankAccounts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId));
        Assert.True(await verificationContext.BankTransactions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId));
    }

    [Fact]
    public async Task Backfill_seeded_counterparties_include_finance_master_data_defaults()
    {
        await using var connection = await OpenConnectionAsync();
        var services = CreateSchedulerServices(connection);

        var companyId = Guid.NewGuid();

        await using (var setupScope = services.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Companies.Add(new Company(companyId, "Counterparty Backfill Company"));
            await setupContext.SaveChangesAsync();
        }

        await using (var orchestratorScope = services.CreateAsyncScope())
        {
            var orchestrator = orchestratorScope.ServiceProvider.GetRequiredService<IFinanceSeedBackfillOrchestrator>();
            await orchestrator.RunAsync(CancellationToken.None);
        }

        await using (var runnerContext = CreateContext(connection))
        {
            var runner = CreateRunner(runnerContext);
            var handled = await runner.RunDueAsync(CancellationToken.None);
            Assert.Equal(1, handled);
        }

        await using var verificationContext = CreateContext(connection);
        var counterparties = await verificationContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Name)
            .ToListAsync();

        Assert.NotEmpty(counterparties);
        Assert.All(counterparties, counterparty =>
        {
            Assert.False(string.IsNullOrWhiteSpace(counterparty.PaymentTerms));
            Assert.NotNull(counterparty.CreditLimit);
            Assert.False(string.IsNullOrWhiteSpace(counterparty.PreferredPaymentMethod));
            Assert.False(string.IsNullOrWhiteSpace(counterparty.DefaultAccountMapping));
        });
        Assert.Contains(counterparties, x => x.CounterpartyType == "customer" && x.DefaultAccountMapping == "1100");
        Assert.Contains(counterparties, x => (x.CounterpartyType == "supplier" || x.CounterpartyType == "vendor") && x.DefaultAccountMapping == "2000");
    }

    [Fact]
    public async Task Backfill_honors_rate_limit_batch_delay_and_concurrency_limits()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var companies = Enumerable.Range(0, 5)
            .Select(index => new Company(Guid.NewGuid(), $"Batch Company {index}"))
            .ToArray();
        dbContext.Companies.AddRange(companies);
        await dbContext.SaveChangesAsync();

        var delayStrategy = new RecordingDelayStrategy();
        var scheduler = new TrackingScheduler();
        var orchestrator = CreateOrchestrator(
            dbContext,
            scheduler,
            delayStrategy,
            new FinanceSeedBackfillWorkerOptions
            {
                ScanPageSize = 10,
                EnqueueBatchSize = 2,
                MaxCompaniesPerRun = 10,
                MaxConcurrentEnqueues = 2,
                RateLimitCount = 2,
                RateLimitWindowSeconds = 1,
                DelayBetweenBatchesMs = 25
            });

        var run = await orchestrator.RunAsync(CancellationToken.None);

        Assert.Equal(5, run.QueuedCount);
        Assert.Equal(0, run.FailedCount);
        Assert.Equal(2, delayStrategy.Delays.Count);
        Assert.True(scheduler.MaxObservedConcurrency <= 2);

        var batchDelays = delayStrategy.Delays.Count(x => x == TimeSpan.FromMilliseconds(25));
        var pacingDelays = delayStrategy.Delays.Count(x => x == TimeSpan.FromMilliseconds(500));

        Assert.Equal(2, batchDelays);
        Assert.Equal(4, pacingDelays);
    }

    [Fact]
    public async Task Backfill_records_failures_and_continues_when_one_enqueue_fails()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var companies = Enumerable.Range(0, 3)
            .Select(index => new Company(Guid.NewGuid(), $"Failure Company {index}"))
            .ToArray();
        dbContext.Companies.AddRange(companies);
        await dbContext.SaveChangesAsync();

        var failingCompanyId = companies[1].Id;
        var scheduler = new FailingScheduler(failingCompanyId);
        var orchestrator = CreateOrchestrator(
            dbContext,
            scheduler,
            new NoOpDelayStrategy(),
            new FinanceSeedBackfillWorkerOptions
            {
                ScanPageSize = 10,
                EnqueueBatchSize = 10,
                MaxCompaniesPerRun = 10,
                MaxConcurrentEnqueues = 3
            });

        var run = await orchestrator.RunAsync(CancellationToken.None);

        Assert.Equal(FinanceSeedBackfillRunStatus.CompletedWithErrors, run.Status);
        Assert.Equal(3, run.ScannedCount);
        Assert.Equal(2, run.QueuedCount);
        Assert.Equal(1, run.FailedCount);

        var attempts = await dbContext.FinanceSeedBackfillAttempts
            .IgnoreQueryFilters()
            .Where(x => x.RunId == run.RunId)
            .OrderBy(x => x.CompanyId)
            .ToListAsync();

        Assert.Equal(3, attempts.Count);
        Assert.Equal(2, attempts.Count(x => x.Status == FinanceSeedBackfillAttemptStatus.Queued));

        var failedAttempt = Assert.Single(attempts.Where(x => x.Status == FinanceSeedBackfillAttemptStatus.Failed));
        Assert.Equal(failingCompanyId, failedAttempt.CompanyId);
        Assert.Contains("configured backfill enqueue failure", failedAttempt.ErrorDetails ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Query_service_projects_retry_metadata_from_background_execution()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(Guid.NewGuid(), "Retry Projection Company");
        dbContext.Companies.Add(company);

        var run = new FinanceSeedBackfillRun(Guid.NewGuid(), DateTime.UtcNow, "{}");
        dbContext.FinanceSeedBackfillRuns.Add(run);

        var execution = new BackgroundExecution(
            Guid.NewGuid(),
            company.Id,
            BackgroundExecutionType.FinanceSeed,
            BackgroundExecutionRelatedEntityTypes.FinanceSeed,
            run.Id.ToString("D"),
            "finance-seed-backfill-retry",
            $"finance-seed-backfill:{company.Id:N}",
            maxAttempts: 5);
        execution.StartAttempt("finance-seed-backfill-retry", 2, 5);
        execution.ScheduleRetry(
            DateTime.UtcNow.AddSeconds(30),
            BackgroundExecutionFailureCategory.ExternalDependencyTimeout,
            nameof(TimeoutException),
            "Transient timeout while dispatching the finance seed job.");

        var attempt = new FinanceSeedBackfillAttempt(
            Guid.NewGuid(),
            run.Id,
            company.Id,
            DateTime.UtcNow,
            FinanceSeedingState.NotSeeded);
        attempt.MarkQueued(execution.Id, execution.IdempotencyKey, DateTime.UtcNow, FinanceSeedingState.Seeding);

        dbContext.BackgroundExecutions.Add(execution);
        dbContext.FinanceSeedBackfillAttempts.Add(attempt);
        await dbContext.SaveChangesAsync();

        var queryService = new FinanceSeedBackfillQueryService(dbContext);
        var projectedAttempt = Assert.Single(await queryService.GetAttemptsAsync(run.Id, CancellationToken.None));

        Assert.Equal(FinanceSeedBackfillAttemptStatus.Queued, projectedAttempt.Status);
        Assert.Equal(1, projectedAttempt.RetryCount);
        Assert.Equal(nameof(TimeoutException), projectedAttempt.ErrorCode);
        Assert.Equal(execution.Id, projectedAttempt.BackgroundExecutionId);
        Assert.Equal(execution.IdempotencyKey, projectedAttempt.IdempotencyKey);
        Assert.Contains("Transient timeout", projectedAttempt.ErrorDetails ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static FinanceSeedBackfillOrchestrator CreateOrchestrator(
        VirtualCompanyDbContext dbContext,
        IFinanceSeedBackfillExecutionScheduler scheduler,
        IFinanceSeedBackfillDelayStrategy delayStrategy,
        FinanceSeedBackfillWorkerOptions options) =>
        new(
            dbContext,
            scheduler,
            delayStrategy,
            Options.Create(options),
            TimeProvider.System,
            NullLogger<FinanceSeedBackfillOrchestrator>.Instance);

    private static ServiceProvider CreateSchedulerServices(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddDbContext<VirtualCompanyDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<IBackgroundExecutionIdentityFactory, DefaultBackgroundExecutionIdentityFactory>();
        services.AddSingleton<IFinanceSeedBackfillExecutionScheduler, FinanceSeedBackfillExecutionScheduler>();
        return services.BuildServiceProvider();
    }

    private static CompanyFinanceSeedJobRunner CreateRunner(
        VirtualCompanyDbContext dbContext,
        IFinanceSeedBootstrapService bootstrapService) =>
        CreateRunnerCore(dbContext, bootstrapService);

    private static CompanyFinanceSeedJobRunner CreateRunner(VirtualCompanyDbContext dbContext) => CreateRunnerCore(dbContext, new CompanyFinanceSeedBootstrapService(dbContext));

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static CompanyFinanceSeedJobRunner CreateRunnerCore(VirtualCompanyDbContext dbContext, IFinanceSeedBootstrapService bootstrapService) => new(
            dbContext,
            bootstrapService,
            new BackgroundJobExecutor(
                NullLogger<BackgroundJobExecutor>.Instance,
                new DefaultBackgroundJobFailureClassifier(),
                new DefaultBackgroundExecutionIdentityFactory()),
            new ExponentialBackgroundExecutionRetryPolicy(Options.Create(new BackgroundExecutionOptions
            {
                BaseRetryDelaySeconds = 0,
                MaxRetryDelaySeconds = 0
            })),
            new CompanyExecutionScopeFactory(new RequestCompanyContextAccessor()),
            new NoOpFinanceSeedTelemetry(),
            new AuditEventWriter(dbContext),
            Options.Create(new FinanceSeedBackfillWorkerOptions()),
            Options.Create(new FinanceSeedWorkerOptions
            {
                BatchSize = 10,
                ClaimTimeoutSeconds = 300
            }),
            TimeProvider.System,
            NullLogger<CompanyFinanceSeedJobRunner>.Instance);

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);

    private static async Task<FinanceSeedDatasetCounts> CaptureSeedCountsAsync(VirtualCompanyDbContext dbContext, Guid companyId) =>
        new(
            await dbContext.FinanceAccounts.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceCounterparties.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceTransactions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceBalances.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceInvoices.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceBills.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinancePolicyConfigurations.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.CompanyBankAccounts.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.BankTransactions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceSeedAnomalies.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().CountAsync(x =>
                x.CompanyId == companyId &&
                x.StorageKey != null &&
                x.StorageKey.StartsWith("seed-finance/")));

    private sealed class NoOpDelayStrategy : IFinanceSeedBackfillDelayStrategy
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingDelayStrategy : IFinanceSeedBackfillDelayStrategy
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingScheduler : IFinanceSeedBackfillExecutionScheduler
    {
        private int _currentConcurrency;
        private int _maxObservedConcurrency;

        public int MaxObservedConcurrency => _maxObservedConcurrency;

        public async Task<FinanceSeedBackfillScheduleResult> ScheduleAsync(
            FinanceSeedBackfillScheduleRequest request,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _currentConcurrency);
            while (true)
            {
                var snapshot = _maxObservedConcurrency;
                if (current <= snapshot)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref _maxObservedConcurrency, current, snapshot) == snapshot)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(5, cancellationToken);
                return FinanceSeedBackfillScheduleResult.Queued(
                    request.CompanyId,
                    request.SeedStateBefore,
                    DateTime.UtcNow,
                    Guid.NewGuid(),
                    $"finance-seed-backfill:{request.CompanyId:N}");
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
            }
        }
    }

    private sealed class FailingScheduler : IFinanceSeedBackfillExecutionScheduler
    {
        private readonly Guid _failingCompanyId;

        public FailingScheduler(Guid failingCompanyId)
        {
            _failingCompanyId = failingCompanyId;
        }

        public Task<FinanceSeedBackfillScheduleResult> ScheduleAsync(
            FinanceSeedBackfillScheduleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                request.CompanyId == _failingCompanyId
                    ? FinanceSeedBackfillScheduleResult.Failed(
                        request.CompanyId,
                        request.SeedStateBefore,
                        DateTime.UtcNow,
                        "Configured backfill enqueue failure.")
                    : FinanceSeedBackfillScheduleResult.Queued(
                        request.CompanyId,
                        request.SeedStateBefore,
                        DateTime.UtcNow,
                        Guid.NewGuid(),
                        $"finance-seed-backfill:{request.CompanyId:N}"));
    }

    private sealed class SelectiveThrowingFinanceSeedBootstrapService : IFinanceSeedBootstrapService
    {
        private readonly VirtualCompanyDbContext _dbContext;
        private readonly Guid _failingCompanyId;
        private readonly string _message;

        public SelectiveThrowingFinanceSeedBootstrapService(
            VirtualCompanyDbContext dbContext,
            Guid failingCompanyId,
            string message)
        {
            _dbContext = dbContext;
            _failingCompanyId = failingCompanyId;
            _message = message;
        }

        public Task<FinanceSeedBootstrapResultDto> GenerateAsync(FinanceSeedBootstrapCommand command, CancellationToken cancellationToken)
        {
            if (command.CompanyId == _failingCompanyId)
            {
                throw new InvalidOperationException(_message);
            }

            return new CompanyFinanceSeedBootstrapService(_dbContext).GenerateAsync(command, cancellationToken);
        }
    }

    private sealed record FinanceSeedDatasetCounts(
        int AccountCount,
        int CounterpartyCount,
        int TransactionCount,
        int BalanceCount,
        int InvoiceCount,
        int BillCount,
        int PolicyConfigurationCount,
        int BankAccountCount,
        int BankTransactionCount,
        int AnomalyCount,
        int SeedDocumentCount);

    private sealed class NoOpFinanceSeedTelemetry : IFinanceSeedTelemetry
    {
        public Task TrackAsync(string eventName, FinanceSeedTelemetryContext context, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}