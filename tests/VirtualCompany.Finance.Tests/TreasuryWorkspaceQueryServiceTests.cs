using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class TreasuryWorkspaceQueryServiceTests
{
    private static readonly DateTime AsOfUtc = new(2026, 8, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Read_model_is_tenant_scoped_bounded_and_reports_source_freshness_and_gap_recovery()
    {
        await using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid();
        var foreignCompanyId = Guid.NewGuid();
        var ownedAccountIds = new HashSet<Guid>();

        for (var index = 0; index < 51; index++)
        {
            var account = AddConnectedAccount(
                db,
                companyId,
                $"{index:D2} operating",
                index == 0 ? AsOfUtc.AddHours(-8) : AsOfUtc.AddMinutes(-30),
                addGap: index == 1);
            ownedAccountIds.Add(account);
        }
        AddConnectedAccount(db, foreignCompanyId, "foreign account", AsOfUtc.AddMinutes(-5), addGap: true);
        AddPaymentOutcome(db, companyId, ownedAccountIds.First(), ambiguous: true);
        AddPaymentOutcome(db, companyId, ownedAccountIds.First(), ambiguous: false);
        await db.SaveChangesAsync();

        var service = new TreasuryWorkspaceQueryService(
            db,
            Dashboard(companyId),
            CashPosition(companyId),
            new TreasuryWorkspacePolicy(),
            new FixedTimeProvider(AsOfUtc),
            new TreasuryWorkspaceTelemetry(),
            new Context(companyId));

        var timer = Stopwatch.StartNew();
        var result = await service.GetAsync(new GetTreasuryWorkspaceQuery(
            companyId,
            AsOfUtc,
            HorizonDays: 14,
            ExceptionLimit: 12,
            TaskLimit: 8,
            CanEdit: true,
            CanApprove: false), default);
        timer.Stop();

        Assert.Equal(50, result.Accounts.Count);
        Assert.True(result.IsTruncated);
        Assert.All(result.Accounts, account => Assert.Contains(account.CompanyBankAccountId, ownedAccountIds));
        Assert.All(result.Accounts, account =>
        {
            Assert.Equal("bank_feed_balance", account.EvidenceSource);
            Assert.NotNull(account.EvidenceUtc);
        });
        Assert.Contains(result.Accounts, account => account.EvidenceState == TreasuryWorkspaceEvidenceStates.Stale);
        var gapAccount = Assert.Single(result.Accounts, account => account.AccountName == "01 operating");
        var recovery = Assert.Single(gapAccount.AllowedActions, action =>
            action.ReasonCode == TreasuryWorkspaceReasonCodes.FeedGapOpen);
        Assert.True(recovery.IsAllowed);
        Assert.Contains("gapId=", recovery.NavigationTarget, StringComparison.Ordinal);
        Assert.Contains(result.Exceptions, item => item.Kind == "bank_feed_gap");
        Assert.Equal(1, result.PaymentWork.ReconciliationRequired);
        Assert.Equal(1, result.PaymentWork.Rejected);
        Assert.Equal(0, result.PaymentWork.Settled);
        var ambiguous = Assert.Single(result.PaymentWork.Items,
            item => item.Status == PaymentExecutionStatuses.ReconciliationRequired);
        Assert.Contains("ambiguous", ambiguous.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"/finance/payments/batches/{ambiguous.BatchId:D}",
            ambiguous.ReviewAction.NavigationTarget, StringComparison.Ordinal);
        Assert.Equal(14, result.Liquidity.HorizonDays);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"Bounded query took {timer.Elapsed}.");
    }

    [Fact]
    public async Task Read_model_rejects_a_company_outside_the_resolved_tenant_before_loading_sources()
    {
        await using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
        var scopedCompanyId = Guid.NewGuid();
        var service = new TreasuryWorkspaceQueryService(
            db,
            Dashboard(scopedCompanyId),
            CashPosition(scopedCompanyId),
            new TreasuryWorkspacePolicy(),
            new FixedTimeProvider(AsOfUtc),
            new TreasuryWorkspaceTelemetry(),
            new Context(scopedCompanyId));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            new GetTreasuryWorkspaceQuery(Guid.NewGuid()), default));
    }

    private static Guid AddConnectedAccount(
        VirtualCompanyDbContext db,
        Guid companyId,
        string name,
        DateTime observedUtc,
        bool addGap)
    {
        var userId = Guid.NewGuid();
        var financeAccount = new FinanceAccount(Guid.NewGuid(), companyId, $"19{name[..2]}",
            name, "asset", "SEK", 0m, AsOfUtc.AddYears(-1));
        var bankAccount = new CompanyBankAccount(Guid.NewGuid(), companyId, financeAccount.Id,
            name, "Test Bank", "•••• 1234", "SEK");
        var connection = new BankConnection(Guid.NewGuid(), companyId, "test", $"institution-{Guid.NewGuid():N}",
            name, userId, AsOfUtc.AddYears(-1));
        connection.Activate(AsOfUtc.AddDays(30), BankConnectionHealthStatuses.Healthy, AsOfUtc.AddYears(-1));
        var discovered = new BankDiscoveredAccount(Guid.NewGuid(), companyId, connection.Id,
            $"provider-{Guid.NewGuid():N}", name, "•••• 1234", "SEK",
            BankAccountOwnershipStatuses.Verified, "Verified test ownership", AsOfUtc.AddYears(-1),
            $"access-{Guid.NewGuid():N}");
        var mapping = new BankAccountMapping(Guid.NewGuid(), companyId, discovered.Id, bankAccount.Id, 1,
            userId, "Explicit test mapping", AsOfUtc.AddYears(-1));
        var checkpoint = new BankFeedCheckpoint(Guid.NewGuid(), companyId, connection.Id, discovered.Id,
            mapping.Id, mapping.Version, bankAccount.Id, "test", discovered.ProviderAccountId,
            discovered.ProviderAccessReference!, AsOfUtc.AddMinutes(-40));
        checkpoint.Queue(DateOnly.FromDateTime(AsOfUtc.AddDays(-30)), DateOnly.FromDateTime(AsOfUtc),
            null, null, "treasury-test", AsOfUtc.AddMinutes(-40));
        Assert.True(checkpoint.TryClaim("treasury-test", AsOfUtc.AddMinutes(-39), TimeSpan.FromMinutes(5)));
        checkpoint.Complete("treasury-test", AsOfUtc.AddMinutes(-38), TimeSpan.FromMinutes(15));
        var balance = new BankFeedBalanceSnapshot(Guid.NewGuid(), companyId, checkpoint.Id, Guid.NewGuid(),
            "closing", 100_000m, "SEK", observedUtc, DateOnly.FromDateTime(observedUtc), null, observedUtc);

        db.AddRange(financeAccount, bankAccount, connection, discovered, mapping, checkpoint, balance);
        if (addGap)
        {
            db.BankFeedGaps.Add(new BankFeedGap(Guid.NewGuid(), companyId, checkpoint.Id, "missing_range",
                DateOnly.FromDateTime(AsOfUtc.AddDays(-3)), DateOnly.FromDateTime(AsOfUtc.AddDays(-2)),
                "provider_gap", "The provider omitted a retained date range.", AsOfUtc.AddDays(-1)));
        }
        return bankAccount.Id;
    }

    private static void AddPaymentOutcome(
        VirtualCompanyDbContext db,
        Guid companyId,
        Guid bankAccountId,
        bool ambiguous)
    {
        var actorId = Guid.NewGuid();
        var batch = new PaymentBatch(Guid.NewGuid(), companyId,
            ambiguous ? "AMB-001" : "REJ-001",
            ambiguous ? "Ambiguous payment" : "Rejected payment",
            DateOnly.FromDateTime(AsOfUtc),
            $"create-{Guid.NewGuid():N}",
            new string(ambiguous ? 'a' : 'b', 64),
            actorId,
            AsOfUtc.AddHours(-2));
        var execution = new PaymentBatchExecution(Guid.NewGuid(), companyId, batch.Id, 1,
            Guid.NewGuid(), Guid.NewGuid(), bankAccountId, "test",
            new string(ambiguous ? 'c' : 'd', 64),
            $"execute-{Guid.NewGuid():N}",
            actorId,
            "treasury-test",
            AsOfUtc.AddHours(-1));
        if (ambiguous)
            execution.RequireReconciliation("provider_timeout",
                "The provider outcome is ambiguous and retained evidence must be reconciled before any retry.", AsOfUtc);
        else
            execution.Reject("provider_rejected", "The provider rejected the instruction.", AsOfUtc);
        db.AddRange(batch, execution);
    }

    private static IDashboardFinanceSnapshotService Dashboard(Guid companyId)
    {
        var proxy = DispatchProxy.Create<IDashboardFinanceSnapshotService, DashboardProxy>();
        ((DashboardProxy)(object)proxy).Value = new DashboardFinanceSnapshotDto(
            companyId, 100_000m, 30_000m, 20_000m, 0m, 20_000m, "SEK", AsOfUtc, 14,
            100_000m, 10_000m, 300, "healthy", true,
            new FinancialHealthSummaryDto("healthy", 90, "stable", "Healthy", 0, 0, 0,
                100_000m, 30_000m, 20_000m, 0m, 20_000m, "SEK"), [], []);
        return proxy;
    }

    private static IFinanceReadService CashPosition(Guid companyId)
    {
        var proxy = DispatchProxy.Create<IFinanceReadService, FinanceProxy>();
        ((FinanceProxy)(object)proxy).Value = new FinanceCashPositionDto(
            companyId, AsOfUtc, 100_000m, "SEK", 10_000m, 300,
            new FinanceCashPositionThresholdsDto(30, 14, 50_000m, 20_000m, "SEK"),
            new FinanceCashPositionAlertStateDto(false, "healthy", false, false, null, null, "Healthy"),
            new FinanceWorkflowOutputSchemaDto("cash_position", "healthy", "Monitor", "Healthy", 1m, "test"));
        return proxy;
    }

    private class DashboardProxy : DispatchProxy
    {
        public DashboardFinanceSnapshotDto Value { get; set; } = default!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Task.FromResult(Value);
    }

    private class FinanceProxy : DispatchProxy
    {
        public FinanceCashPositionDto Value { get; set; } = default!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IFinanceReadService.GetCashPositionAsync)
                ? Task.FromResult(Value)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class Context(Guid companyId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => null;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
    }
}
