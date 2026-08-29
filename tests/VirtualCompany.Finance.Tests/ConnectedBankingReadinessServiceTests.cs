using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class ConnectedBankingReadinessServiceTests
{
    private static readonly DateTime AsOfUtc = new(2026, 8, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Readiness_is_company_scoped_and_blocks_on_integrity_ambiguity_and_control_differences()
    {
        await using var db = await CreateDatabaseAsync();
        var companyId = Guid.NewGuid();
        var foreignCompanyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        AddOperationalIssues(db, companyId, actorId);
        AddAmbiguousExecution(db, foreignCompanyId, Guid.NewGuid(), AsOfUtc.AddDays(-10));
        var periodId = Guid.NewGuid();
        db.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "2026",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var differenceJournalId = Guid.NewGuid();
        var service = Service(db, companyId, new ControlAccountReconciliationDto(
            companyId,
            periodId,
            false,
            [new ControlAccountReconciliationLineDto("bank", Guid.NewGuid(), "1930", "Operating cash",
                "SEK", 10_000m, 9_500m, 500m, false, [differenceJournalId])]));

        var result = await service.GetAsync(new GetConnectedBankingReadinessQuery(
            companyId, ConnectedBankingCapacityProfileKeys.Small, AsOfUtc), default);

        Assert.False(result.IsReady);
        Assert.Equal(ConnectedBankingReadinessStatuses.Blocked, result.Status);
        Assert.Equal(12, result.Checks.Count);
        Assert.Equal(ConnectedBankingCapacityProfileKeys.Small, result.ProfileKey);
        Assert.Equal(1, result.Volumes.Single(x =>
            x.Resource == ConnectedBankingCapacityResourceKeys.Connections).CurrentCount);
        Assert.Equal(3, result.Volumes.Single(x =>
            x.Resource == ConnectedBankingCapacityResourceKeys.PaymentBatches).CurrentCount);

        AssertCheck(result, ConnectedBankingReadinessCheckKeys.ConsentExpiry,
            ConnectedBankingReadinessStatuses.Blocked);
        AssertCheck(result, ConnectedBankingReadinessCheckKeys.FeedGaps,
            ConnectedBankingReadinessStatuses.Blocked);
        AssertCheck(result, ConnectedBankingReadinessCheckKeys.FeedLag,
            ConnectedBankingReadinessStatuses.Attention);
        AssertCheck(result, ConnectedBankingReadinessCheckKeys.UnreconciledAging,
            ConnectedBankingReadinessStatuses.Attention);
        AssertCheck(result, ConnectedBankingReadinessCheckKeys.Suspense,
            ConnectedBankingReadinessStatuses.Attention);
        AssertCheck(result, ConnectedBankingReadinessCheckKeys.StaleApprovals,
            ConnectedBankingReadinessStatuses.Attention);
        var ambiguous = AssertCheck(result, ConnectedBankingReadinessCheckKeys.AmbiguousSubmissions,
            ConnectedBankingReadinessStatuses.Blocked);
        Assert.Equal(1, ambiguous.Count);
        AssertCheck(result, ConnectedBankingReadinessCheckKeys.RejectedInstructions,
            ConnectedBankingReadinessStatuses.Attention);
        AssertCheck(result, ConnectedBankingReadinessCheckKeys.UnsettledBatches,
            ConnectedBankingReadinessStatuses.Attention);
        AssertCheck(result, ConnectedBankingReadinessCheckKeys.WorkerBacklog,
            ConnectedBankingReadinessStatuses.Attention);
        var control = AssertCheck(result, ConnectedBankingReadinessCheckKeys.ControlAccountDifferences,
            ConnectedBankingReadinessStatuses.Blocked);
        Assert.Equal(500m, control.Value);
        Assert.Contains(differenceJournalId, control.SubjectIds);
    }

    [Fact]
    public async Task Healthy_evidence_is_ready_and_profile_capacity_is_exposed()
    {
        await using var db = await CreateDatabaseAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var connection = new BankConnection(connectionId, companyId, "test", "SE|Healthy",
            "Healthy Bank", actorId, AsOfUtc.AddDays(-30));
        connection.Activate(AsOfUtc.AddDays(90), BankConnectionHealthStatuses.Healthy, AsOfUtc.AddDays(-30));
        var checkpoint = CreateCheckpoint(companyId, actorId, connectionId, bankAccountId, AsOfUtc.AddDays(-30));
        CompleteCheckpoint(checkpoint, AsOfUtc.AddMinutes(-5));
        db.AddRange(connection, checkpoint,
            new FiscalPeriod(periodId, companyId, "2026",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var service = Service(db, companyId, new ControlAccountReconciliationDto(
            companyId, periodId, true,
            [new ControlAccountReconciliationLineDto("bank", Guid.NewGuid(), "1930", "Operating cash",
                "SEK", 10_000m, 10_000m, 0m, true, [])]));

        var result = await service.GetAsync(new GetConnectedBankingReadinessQuery(
            companyId, ConnectedBankingCapacityProfileKeys.Medium, AsOfUtc), default);

        Assert.True(result.IsReady);
        Assert.Equal(ConnectedBankingReadinessStatuses.Ready, result.Status);
        Assert.Equal(ConnectedBankingCapacityProfileKeys.Medium, result.ProfileKey);
        Assert.All(result.Checks, check => Assert.Equal(ConnectedBankingReadinessStatuses.Ready, check.Status));
        Assert.Contains(result.Objectives, objective => objective.Key == "webhook_acceptance_p95");
        Assert.Contains(result.Volumes, volume =>
            volume.Resource == ConnectedBankingCapacityResourceKeys.FeedTransactions &&
            volume.SupportedCount == 2_500_000);
    }

    [Fact]
    public async Task Resolved_tenant_context_rejects_cross_company_read_before_queries_run()
    {
        await using var db = await CreateDatabaseAsync();
        var companyId = Guid.NewGuid();
        var service = Service(db, companyId, new ControlAccountReconciliationDto(
            companyId, Guid.NewGuid(), true, []));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            new GetConnectedBankingReadinessQuery(Guid.NewGuid(), AsOfUtc: AsOfUtc), default));
    }

    private static void AddOperationalIssues(VirtualCompanyDbContext db, Guid companyId, Guid actorId)
    {
        var connectionId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var connection = new BankConnection(connectionId, companyId, "test", "SE|Blocked",
            "Blocked Bank", actorId, AsOfUtc.AddDays(-60));
        connection.Activate(AsOfUtc.AddDays(-1), BankConnectionHealthStatuses.Healthy, AsOfUtc.AddDays(-60));
        var checkpoint = CreateCheckpoint(companyId, actorId, connectionId, bankAccountId, AsOfUtc.AddDays(-30));
        CompleteCheckpoint(checkpoint, AsOfUtc.AddDays(-2));
        checkpoint.Queue(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 29), actorId, null,
            "readiness-expired-lease", AsOfUtc.AddHours(-2));
        Assert.True(checkpoint.TryClaim("dead-worker", AsOfUtc.AddHours(-2), TimeSpan.FromMinutes(5)));
        var gap = new BankFeedGap(Guid.NewGuid(), companyId, checkpoint.Id, BankFeedGapKinds.MissingRange,
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 21), BankFeedReasonCodes.MissingRange,
            "Provider coverage omitted a date range.", AsOfUtc.AddDays(-1));
        var transaction = new BankTransaction(Guid.NewGuid(), companyId, bankAccountId,
            AsOfUtc.AddDays(-20), AsOfUtc.AddDays(-20), 500m, "SEK", "Old bank row", "Counterparty",
            importSource: "test-feed", rowIdentity: "old-row", rowContentHash: new string('a', 64));
        var suspense = new BankTransactionPostingStateRecord(Guid.NewGuid(), companyId, transaction.Id,
            BankTransactionMatchingStatuses.Unmatched, BankTransactionPostingStates.Suspense, 0,
            AsOfUtc.AddDays(-19), handlingMode: BankReconciliationHandlingModes.Suspense,
            suspenseLedgerEntryId: Guid.NewGuid());
        var staleApproval = new PaymentBatchApprovalBinding(Guid.NewGuid(), companyId, Guid.NewGuid(),
            Guid.NewGuid(), 1, new string('b', 64), actorId, AsOfUtc.AddDays(-5));

        db.AddRange(connection, checkpoint, gap, transaction, suspense, staleApproval);
        AddAmbiguousExecution(db, companyId, actorId, AsOfUtc.AddDays(-5));
        AddRejectedExecution(db, companyId, actorId, AsOfUtc.AddDays(-5));
        AddUnsettledExecution(db, companyId, actorId, AsOfUtc.AddDays(-5));
    }

    private static BankFeedCheckpoint CreateCheckpoint(Guid companyId, Guid actorId, Guid connectionId,
        Guid bankAccountId, DateTime createdUtc)
    {
        var discoveredId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();
        return new BankFeedCheckpoint(Guid.NewGuid(), companyId, connectionId, discoveredId, mappingId, 1,
            bankAccountId, "test", $"account-{actorId:N}", $"access-{actorId:N}", createdUtc);
    }

    private static void CompleteCheckpoint(BankFeedCheckpoint checkpoint, DateTime completedUtc)
    {
        checkpoint.Queue(DateOnly.FromDateTime(completedUtc.AddDays(-30)), DateOnly.FromDateTime(completedUtc),
            null, null, "readiness-initial-sync", completedUtc.AddMinutes(-2));
        Assert.True(checkpoint.TryClaim("readiness-worker", completedUtc.AddMinutes(-1), TimeSpan.FromMinutes(5)));
        checkpoint.Complete("readiness-worker", completedUtc, TimeSpan.FromMinutes(15));
    }

    private static void AddAmbiguousExecution(VirtualCompanyDbContext db, Guid companyId, Guid actorId,
        DateTime createdUtc)
    {
        var (batch, execution) = CreateExecution(companyId, actorId, createdUtc, 'c');
        execution.RequireReconciliation("provider_timeout", "Provider outcome is ambiguous.", createdUtc.AddMinutes(1));
        db.AddRange(batch, execution);
    }

    private static void AddRejectedExecution(VirtualCompanyDbContext db, Guid companyId, Guid actorId,
        DateTime createdUtc)
    {
        var (batch, execution) = CreateExecution(companyId, actorId, createdUtc, 'd');
        execution.Reject("provider_rejected", "Provider rejected the instruction.", createdUtc.AddMinutes(1));
        db.AddRange(batch, execution);
    }

    private static void AddUnsettledExecution(VirtualCompanyDbContext db, Guid companyId, Guid actorId,
        DateTime createdUtc)
    {
        var (batch, execution) = CreateExecution(companyId, actorId, createdUtc, 'e');
        execution.BeginSubmission(createdUtc.AddMinutes(1));
        execution.RecordSubmission("provider-payment", null, "ACSP", false, true, true,
            createdUtc.AddMinutes(2));
        db.AddRange(batch, execution);
    }

    private static (PaymentBatch Batch, PaymentBatchExecution Execution) CreateExecution(
        Guid companyId, Guid actorId, DateTime createdUtc, char hashCharacter)
    {
        actorId = actorId == Guid.Empty ? Guid.NewGuid() : actorId;
        var batch = new PaymentBatch(Guid.NewGuid(), companyId, $"READY-{Guid.NewGuid():N}",
            "Readiness payment", DateOnly.FromDateTime(createdUtc), $"create-{Guid.NewGuid():N}",
            new string(hashCharacter, 64), actorId, createdUtc);
        var execution = new PaymentBatchExecution(Guid.NewGuid(), companyId, batch.Id, 1,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", new string(hashCharacter, 64),
            $"execute-{Guid.NewGuid():N}", actorId, "readiness-test", createdUtc);
        return (batch, execution);
    }

    private static ConnectedBankingReadinessService Service(VirtualCompanyDbContext db, Guid companyId,
        ControlAccountReconciliationDto control)
    {
        var reporting = DispatchProxy.Create<IAccountingReportingService, ReportingProxy>();
        ((ReportingProxy)(object)reporting).Control = control;
        return new ConnectedBankingReadinessService(db, reporting,
            Options.Create(new ConnectedBankingReadinessOptions()), new FixedTimeProvider(AsOfUtc),
            new Context(companyId));
    }

    private static ConnectedBankingReadinessCheckDto AssertCheck(ConnectedBankingReadinessReadModel result,
        string key, string status)
    {
        var check = Assert.Single(result.Checks, item => item.Key == key);
        Assert.Equal(status, check.Status);
        return check;
    }

    private static async Task<VirtualCompanyDbContext> CreateDatabaseAsync()
    {
        var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private class ReportingProxy : DispatchProxy
    {
        public ControlAccountReconciliationDto Control { get; set; } = default!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IAccountingReportingService.GetControlAccountReconciliationAsync)
                ? Task.FromResult(Control)
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
