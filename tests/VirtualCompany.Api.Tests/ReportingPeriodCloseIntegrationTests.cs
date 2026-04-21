using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ReportingPeriodCloseIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ReportingPeriodCloseIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Validation_endpoint_returns_all_period_close_blocking_issue_types()
    {
        var seed = await SeedValidationScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, "Finance Owner");

        var response = await client.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/validation", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ReportingPeriodCloseValidationResponse>();
        Assert.NotNull(payload);
        Assert.False(payload!.IsReadyToClose);
        Assert.Contains(payload.BlockingIssues, x => x.Code == ReportingPeriodBlockingIssueCodes.UnpostedSourceDocuments && x.Count == 1);
        Assert.Contains(payload.BlockingIssues, x => x.Code == ReportingPeriodBlockingIssueCodes.UnbalancedJournalEntries && x.Count == 1);
        Assert.Contains(payload.BlockingIssues, x => x.Code == ReportingPeriodBlockingIssueCodes.MissingStatementMappings && x.Count == 1);
    }

    [Fact]
    public async Task Lock_endpoint_rejects_open_periods()
    {
        var seed = await SeedValidationScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, "Finance Owner");

        var response = await client.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.OpenPeriodId:D}/reporting/lock", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        Assert.NotNull(problem);
        Assert.Equal(ReportingPeriodErrorCodes.ReportingPeriodNotClosed, problem!.Code);
    }

    [Fact]
    public async Task Employee_membership_cannot_validate_lock_or_regenerate_reporting_period_operations()
    {
        var seed = await SeedClosedBalancedScenarioAsync();
        using var employeeClient = CreateAuthenticatedClient(seed.EmployeeSubject, seed.EmployeeEmail, "Finance Employee");

        var validationResponse = await employeeClient.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/validation", null);
        Assert.Equal(HttpStatusCode.Forbidden, validationResponse.StatusCode);

        var lockResponse = await employeeClient.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/lock", null);
        Assert.Equal(HttpStatusCode.Forbidden, lockResponse.StatusCode);

        var regenerateResponse = await employeeClient.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/stored-statements/regenerate",
            new { runInBackground = false });
        Assert.Equal(HttpStatusCode.Forbidden, regenerateResponse.StatusCode);
    }

    [Fact]
    public async Task Unlock_endpoint_requires_owner_or_admin_authorization()
    {
        var seed = await SeedValidationScenarioAsync();
        using var ownerClient = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, "Finance Owner");
        using var managerClient = CreateAuthenticatedClient(seed.ManagerSubject, seed.ManagerEmail, "Finance Manager");

        var lockResponse = await ownerClient.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/lock", null);
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);

        var unlockResponse = await managerClient.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/unlock", null);
        Assert.Equal(HttpStatusCode.Forbidden, unlockResponse.StatusCode);
    }

    [Fact]
    public async Task Locked_period_blocks_synchronous_regeneration_and_allows_regeneration_after_authorized_unlock()
    {
        var seed = await SeedClosedBalancedScenarioAsync();
        using var ownerClient = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, "Finance Owner");

        var lockResponse = await ownerClient.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/lock", null);
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);

        var blockedResponse = await ownerClient.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/stored-statements/regenerate",
            new { runInBackground = false });
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);

        var blockedProblem = await blockedResponse.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        Assert.NotNull(blockedProblem);
        Assert.Equal(ReportingPeriodErrorCodes.ReportingPeriodLocked, blockedProblem!.Code);

        var blockedAudit = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.Action == AuditEventActions.ReportingPeriodRegenerationBlocked)
                .OrderByDescending(x => x.OccurredUtc)
                .Select(x => new { x.ActorId, x.ActorType, x.Outcome })
                .FirstAsync());
        Assert.Equal(seed.OwnerUserId, blockedAudit.ActorId);
        Assert.Equal(AuditActorTypes.User, blockedAudit.ActorType);
        Assert.Equal(AuditEventOutcomes.Denied, blockedAudit.Outcome);

        var unlockResponse = await ownerClient.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/unlock", null);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);

        var regenerateResponse = await ownerClient.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/stored-statements/regenerate",
            new { runInBackground = false });
        Assert.Equal(HttpStatusCode.OK, regenerateResponse.StatusCode);

        var result = await regenerateResponse.Content.ReadFromJsonAsync<ReportingPeriodRegenerationResponse>();
        Assert.NotNull(result);
        Assert.False(result!.Queued);
        Assert.True(result.SnapshotCount >= 3);

        var snapshotCount = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.TrialBalanceSnapshots.IgnoreQueryFilters().CountAsync(
                x => x.CompanyId == seed.CompanyId && x.FiscalPeriodId == seed.ClosedPeriodId));
        Assert.True(snapshotCount >= 3);
    }

    [Fact]
    public async Task Locked_period_blocks_background_regeneration_runner_and_preserves_stored_snapshots()
    {
        var seed = await SeedClosedBalancedScenarioAsync(locked: true);
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.BackgroundExecutions.Add(new BackgroundExecution(
                Guid.NewGuid(),
                seed.CompanyId,
                BackgroundExecutionType.FinanceReportRegeneration,
                BackgroundExecutionRelatedEntityTypes.FiscalPeriod,
                seed.ClosedPeriodId.ToString("D"),
                "corr-reporting-regeneration",
                $"finance-report-regeneration:{seed.CompanyId:N}:{seed.ClosedPeriodId:N}",
                3));
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IReportingPeriodRegenerationJobRunner>();
        var handled = await runner.RunDueAsync(CancellationToken.None);
        Assert.Equal(1, handled);

        var verification = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var execution = await dbContext.BackgroundExecutions.IgnoreQueryFilters()
                .SingleAsync(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.ExecutionType == BackgroundExecutionType.FinanceReportRegeneration);
            var snapshotCount = await dbContext.TrialBalanceSnapshots.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == seed.CompanyId && x.FiscalPeriodId == seed.ClosedPeriodId);
            var audit = await dbContext.AuditEvents.IgnoreQueryFilters()
                .SingleAsync(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.Action == AuditEventActions.ReportingPeriodRegenerationBlocked);
            return new { execution.Status, snapshotCount, audit.Outcome };
        });

        Assert.Equal(BackgroundExecutionStatus.Blocked, verification.Status);
        Assert.Equal(0, verification.snapshotCount);
        Assert.Equal(AuditEventOutcomes.Denied, verification.Outcome);
    }

    [Fact]
    public async Task Validation_lock_and_unlock_operations_persist_audit_events_with_actor_and_timestamp()
    {
        var seed = await SeedClosedBalancedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, "Finance Owner");

        var validationResponse = await client.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/validation", null);
        var lockResponse = await client.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/lock", null);
        var unlockResponse = await client.PostAsync($"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.ClosedPeriodId:D}/reporting/unlock", null);

        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);

        var auditEvents = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.AuditEvents.IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == seed.CompanyId &&
                    (x.Action == AuditEventActions.ReportingPeriodCloseValidationExecuted ||
                     x.Action == AuditEventActions.ReportingPeriodLockApplied ||
                     x.Action == AuditEventActions.ReportingPeriodLockRemoved))
                .OrderBy(x => x.OccurredUtc)
                .Select(x => new { x.Action, x.ActorType, x.ActorId, x.OccurredUtc, x.TargetId })
                .ToListAsync());

        Assert.Contains(auditEvents, x => x.Action == AuditEventActions.ReportingPeriodCloseValidationExecuted && x.ActorType == AuditActorTypes.User && x.ActorId == seed.OwnerUserId);
        Assert.Contains(auditEvents, x => x.Action == AuditEventActions.ReportingPeriodLockApplied && x.ActorType == AuditActorTypes.User && x.ActorId == seed.OwnerUserId);
        Assert.Contains(auditEvents, x => x.Action == AuditEventActions.ReportingPeriodLockRemoved && x.ActorType == AuditActorTypes.User && x.ActorId == seed.OwnerUserId);
        Assert.All(auditEvents, x => Assert.Equal(seed.ClosedPeriodId.ToString("D"), x.TargetId));
        Assert.All(auditEvents, x => Assert.True(x.OccurredUtc > DateTime.UtcNow.AddMinutes(-5)));

        var periodAuditState = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.FiscalPeriods.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.Id == seed.ClosedPeriodId)
                .Select(x => new
                {
                    x.LastCloseValidatedUtc,
                    x.LastCloseValidatedByUserId,
                    x.ReportingLockedUtc,
                    x.ReportingLockedByUserId,
                    x.ReportingUnlockedUtc,
                    x.ReportingUnlockedByUserId
                })
                .SingleAsync());

        Assert.NotNull(periodAuditState.LastCloseValidatedUtc);
        Assert.Equal(seed.OwnerUserId, periodAuditState.LastCloseValidatedByUserId);
        Assert.NotNull(periodAuditState.ReportingLockedUtc);
        Assert.Equal(seed.OwnerUserId, periodAuditState.ReportingLockedByUserId);
        Assert.NotNull(periodAuditState.ReportingUnlockedUtc);
        Assert.Equal(seed.OwnerUserId, periodAuditState.ReportingUnlockedByUserId);
    }

    private async Task<SeedContext> SeedValidationScenarioAsync()
    {
        var seed = CreateSeedContext();
        await _factory.SeedAsync(dbContext =>
        {
            var setup = SeedBaseCompanyData(dbContext, seed, closedPeriodLocked: false);

            dbContext.FinancialStatementMappings.AddRange(
                new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, setup.CashAccountId, FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetAssets, FinancialStatementLineClassification.CurrentAsset),
                new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, setup.RevenueAccountId, FinancialStatementType.ProfitAndLoss, FinancialStatementReportSection.ProfitAndLossRevenue, FinancialStatementLineClassification.Revenue));

            var unposted = new LedgerEntry(
                Guid.NewGuid(),
                seed.CompanyId,
                seed.ClosedPeriodId,
                "JE-UNPOSTED",
                new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryStatuses.Draft,
                "Draft invoice posting");
            dbContext.LedgerEntries.Add(unposted);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, unposted.Id, setup.CashAccountId, 100m, 0m, "USD"),
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, unposted.Id, setup.RevenueAccountId, 0m, 100m, "USD"));

            var unbalanced = new LedgerEntry(
                Guid.NewGuid(),
                seed.CompanyId,
                seed.ClosedPeriodId,
                "JE-UNBALANCED",
                new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryStatuses.Posted,
                "Unbalanced closing entry");
            dbContext.LedgerEntries.Add(unbalanced);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, unbalanced.Id, setup.ExpenseAccountId, 150m, 0m, "USD"),
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, unbalanced.Id, setup.CashAccountId, 0m, 125m, "USD"));

            return Task.CompletedTask;
        });

        return seed;
    }

    private async Task<SeedContext> SeedClosedBalancedScenarioAsync(bool locked = false)
    {
        var seed = CreateSeedContext();
        await _factory.SeedAsync(dbContext =>
        {
            var setup = SeedBaseCompanyData(dbContext, seed, closedPeriodLocked: locked);

            dbContext.FinancialStatementMappings.AddRange(
                new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, setup.CashAccountId, FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetAssets, FinancialStatementLineClassification.CurrentAsset),
                new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, setup.EquityAccountId, FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetEquity, FinancialStatementLineClassification.Equity),
                new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, setup.RevenueAccountId, FinancialStatementType.ProfitAndLoss, FinancialStatementReportSection.ProfitAndLossRevenue, FinancialStatementLineClassification.Revenue),
                new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, setup.ExpenseAccountId, FinancialStatementType.ProfitAndLoss, FinancialStatementReportSection.ProfitAndLossOperatingExpenses, FinancialStatementLineClassification.OperatingExpense));

            var capitalEntry = new LedgerEntry(
                Guid.NewGuid(),
                seed.CompanyId,
                seed.ClosedPeriodId,
                "JE-CAPITAL",
                new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryStatuses.Posted,
                "Initial capital");
            dbContext.LedgerEntries.Add(capitalEntry);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, capitalEntry.Id, setup.CashAccountId, 500m, 0m, "USD"),
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, capitalEntry.Id, setup.EquityAccountId, 0m, 500m, "USD"));

            var revenueEntry = new LedgerEntry(
                Guid.NewGuid(),
                seed.CompanyId,
                seed.ClosedPeriodId,
                "JE-REV",
                new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryStatuses.Posted,
                "Revenue");
            dbContext.LedgerEntries.Add(revenueEntry);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, revenueEntry.Id, setup.CashAccountId, 900m, 0m, "USD"),
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, revenueEntry.Id, setup.RevenueAccountId, 0m, 900m, "USD"));

            var expenseEntry = new LedgerEntry(
                Guid.NewGuid(),
                seed.CompanyId,
                seed.ClosedPeriodId,
                "JE-EXP",
                new DateTime(2026, 3, 18, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryStatuses.Posted,
                "Expense");
            dbContext.LedgerEntries.Add(expenseEntry);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, expenseEntry.Id, setup.ExpenseAccountId, 200m, 0m, "USD"),
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, expenseEntry.Id, setup.CashAccountId, 0m, 200m, "USD"));

            return Task.CompletedTask;
        });

        return seed;
    }

    private static SeedSetup SeedBaseCompanyData(
        VirtualCompanyDbContext dbContext,
        SeedContext seed,
        bool closedPeriodLocked)
    {
        dbContext.Users.AddRange(
            new User(seed.OwnerUserId, seed.OwnerEmail, "Finance Owner", "dev-header", seed.OwnerSubject),
            new User(seed.ManagerUserId, seed.ManagerEmail, "Finance Manager", "dev-header", seed.ManagerSubject),
            new User(seed.EmployeeUserId, seed.EmployeeEmail, "Finance Employee", "dev-header", seed.EmployeeSubject));
        dbContext.Companies.Add(new Company(seed.CompanyId, "Reporting Close Company"));
        dbContext.CompanyMemberships.AddRange(
            new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.OwnerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
            new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.ManagerUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
            new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.EmployeeUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));

        var cashAccountId = Guid.NewGuid();
        var equityAccountId = Guid.NewGuid();
        var revenueAccountId = Guid.NewGuid();
        var expenseAccountId = Guid.NewGuid();

        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(cashAccountId, seed.CompanyId, "1000", "Operating Cash", "asset", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(equityAccountId, seed.CompanyId, "3000", "Owner Equity", "equity", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(revenueAccountId, seed.CompanyId, "4000", "Sales Revenue", "revenue", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(expenseAccountId, seed.CompanyId, "6100", "Operating Expense", "expense", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        dbContext.FinanceTransactions.Add(new FinanceTransaction(
            Guid.NewGuid(),
            seed.CompanyId,
            cashAccountId,
            null,
            null,
            null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            "bootstrap",
            0m,
            "USD",
            "Seed completeness transaction",
            $"BOOT-{seed.CompanyId:N}"));
        dbContext.FinanceBalances.Add(new FinanceBalance(
            Guid.NewGuid(),
            seed.CompanyId,
            cashAccountId,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            0m,
            "USD"));
        dbContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(
            Guid.NewGuid(),
            seed.CompanyId,
            "USD",
            1000m,
            1000m,
            true,
            -10000m,
            10000m,
            90,
            30));

        dbContext.FiscalPeriods.AddRange(
            new FiscalPeriod(
                seed.OpenPeriodId,
                seed.CompanyId,
                "FY2026-02",
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FiscalPeriod(
                seed.ClosedPeriodId,
                seed.CompanyId,
                "FY2026-03",
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                isClosed: true,
                closedUtc: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                isReportingLocked: closedPeriodLocked,
                reportingLockedUtc: closedPeriodLocked ? new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc) : null,
                reportingLockedByUserId: closedPeriodLocked ? seed.OwnerUserId : null));

        return new SeedSetup(cashAccountId, equityAccountId, revenueAccountId, expenseAccountId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private static SeedContext CreateSeedContext()
    {
        var companyId = Guid.NewGuid();
        var ownerSubject = $"reporting-owner-{Guid.NewGuid():N}";
        var managerSubject = $"reporting-manager-{Guid.NewGuid():N}";
        var employeeSubject = $"reporting-employee-{Guid.NewGuid():N}";
        return new SeedContext(
            companyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ownerSubject,
            $"{ownerSubject}@example.com",
            managerSubject,
            $"{managerSubject}@example.com",
            employeeSubject,
            $"{employeeSubject}@example.com",
            Guid.NewGuid(),
            Guid.NewGuid());
    }

    private sealed record SeedContext(
        Guid CompanyId,
        Guid OwnerUserId,
        Guid ManagerUserId,
        Guid EmployeeUserId,
        string OwnerSubject,
        string OwnerEmail,
        string ManagerSubject,
        string ManagerEmail,
        string EmployeeSubject,
        string EmployeeEmail,
        Guid OpenPeriodId,
        Guid ClosedPeriodId);

    private sealed record SeedSetup(
        Guid CashAccountId,
        Guid EquityAccountId,
        Guid RevenueAccountId,
        Guid ExpenseAccountId);

    private sealed class ReportingPeriodCloseValidationResponse
    {
        public Guid CompanyId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public bool IsReadyToClose { get; set; }
        public bool IsClosed { get; set; }
        public bool IsReportingLocked { get; set; }
        public List<ReportingPeriodBlockingIssueResponse> BlockingIssues { get; set; } = [];
    }

    private sealed class ReportingPeriodBlockingIssueResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<string> SampleReferences { get; set; } = [];
    }

    private sealed class ReportingPeriodRegenerationResponse
    {
        public Guid CompanyId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public bool Queued { get; set; }
        public Guid? BackgroundExecutionId { get; set; }
        public int SnapshotCount { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    private sealed class ProblemDetailsResponse
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public int? Status { get; set; }
        public string? Instance { get; set; }
        public string? Code { get; set; }
    }
}