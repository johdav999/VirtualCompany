using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class PaymentExecutionDomainTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unknown_provider_write_requires_reconciliation_and_known_reference_before_status_reads()
    {
        var execution = Create();
        execution.BeginSubmission(Now.AddMinutes(1));
        execution.RequireReconciliation("submission_ambiguous",
            "The provider write outcome is unknown.", Now.AddMinutes(2));

        Assert.Equal(PaymentExecutionStatuses.ReconciliationRequired, execution.Status);
        Assert.False(execution.UpdatesExpected);
        Assert.False(execution.CanCancelAtProvider);

        execution.AttachProviderReference("provider-payment-1", Now.AddMinutes(3));

        Assert.Equal(PaymentExecutionStatuses.Processing, execution.Status);
        Assert.Equal("provider-payment-1", execution.ProviderPaymentId);
        Assert.True(execution.UpdatesExpected);
        Assert.False(execution.CanCancelAtProvider);
    }

    [Fact]
    public void Provider_completion_is_distinct_from_booked_bank_settlement()
    {
        var execution = Create();
        execution.BeginSubmission(Now.AddMinutes(1));
        execution.RecordSubmission("provider-payment-1", new Uri("https://bank.test/authorize"),
            "RCVD", false, true, false, Now.AddMinutes(2));
        Assert.Equal(PaymentExecutionStatuses.AwaitingAuthorization, execution.Status);
        Assert.Null(execution.SettledUtc);

        execution.ApplyProviderStatus("ACSC", true, false, false, null, null, Now.AddMinutes(3));
        Assert.Equal(PaymentExecutionStatuses.ProviderCompleted, execution.Status);
        Assert.NotNull(execution.ProviderCompletedUtc);
        Assert.Null(execution.SettledUtc);

        execution.MarkSettled(Now.AddMinutes(4));
        Assert.Equal(PaymentExecutionStatuses.Settled, execution.Status);
        Assert.NotNull(execution.SettledUtc);
    }

    [Fact]
    public void Terminal_provider_creation_can_complete_without_an_authorization_link()
    {
        var execution = Create();
        execution.BeginSubmission(Now.AddMinutes(1));

        execution.RecordSubmission("provider-payment-terminal", null, "ACSC", true, false,
            false, Now.AddMinutes(2));

        Assert.Equal(PaymentExecutionStatuses.ProviderCompleted, execution.Status);
        Assert.Null(execution.ProviderAuthorizationUri);
        Assert.False(execution.UpdatesExpected);
    }

    [Fact]
    public void Malformed_http_success_can_retain_provider_identity_only_for_reconciliation()
    {
        var execution = Create();
        execution.BeginSubmission(Now.AddMinutes(1));

        execution.RequireProviderReconciliation("provider-payment-retained", "submission_ambiguous",
            "The provider response could not be normalized.", Now.AddMinutes(2));

        Assert.Equal("provider-payment-retained", execution.ProviderPaymentId);
        Assert.Equal(PaymentExecutionStatuses.ReconciliationRequired, execution.Status);
        Assert.False(execution.UpdatesExpected);
    }

    [Fact]
    public void Persistence_model_enforces_tenant_idempotency_provider_replay_and_settlement_uniqueness()
    {
        using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:").Options);

        var execution = Entity<PaymentBatchExecution>(db);
        AssertIndex(execution, true, nameof(PaymentBatchExecution.CompanyId),
            nameof(PaymentBatchExecution.BatchId), nameof(PaymentBatchExecution.InstructionSetVersion));
        AssertIndex(execution, true, nameof(PaymentBatchExecution.CompanyId),
            nameof(PaymentBatchExecution.BusinessIdempotencyKey));
        Assert.Contains(execution.GetIndexes(), x => x.IsUnique &&
            Names(x).SequenceEqual(new[] { nameof(PaymentBatchExecution.ProviderKey), nameof(PaymentBatchExecution.ProviderPaymentId) }) &&
            !string.IsNullOrWhiteSpace(x.GetFilter()));
        Assert.True(execution.FindProperty(nameof(PaymentBatchExecution.Version))!.IsConcurrencyToken);
        Assert.True(execution.FindProperty(nameof(PaymentBatchExecution.RowVersion))!.IsConcurrencyToken);

        var webhook = Entity<PaymentProviderWebhookReceipt>(db);
        AssertIndex(webhook, true, nameof(PaymentProviderWebhookReceipt.ProviderKey),
            nameof(PaymentProviderWebhookReceipt.WebhookId));

        var settlement = Entity<PaymentBatchSettlement>(db);
        AssertIndex(settlement, true, nameof(PaymentBatchSettlement.CompanyId),
            nameof(PaymentBatchSettlement.ExecutionId));
        AssertIndex(settlement, true, nameof(PaymentBatchSettlement.CompanyId),
            nameof(PaymentBatchSettlement.BankTransactionId));
    }

    [Fact]
    public async Task Execution_reads_fail_before_data_access_when_company_context_does_not_match()
    {
        var requestedCompany = Guid.NewGuid();
        var service = new PaymentBatchExecutionService(null!, null!, null!, null!, null!, null!, null!,
            new MismatchedCompanyContext(Guid.NewGuid()), TimeProvider.System);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            new GetPaymentBatchExecutionQuery(requestedCompany, Guid.NewGuid()), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetForBatchAsync(
            new GetPaymentBatchExecutionForBatchQuery(requestedCompany, Guid.NewGuid()), CancellationToken.None));
    }

    private static PaymentBatchExecution Create() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "enable-banking", new string('a', 64),
        "business-key", Guid.NewGuid(), "correlation", Now);
    private static IEntityType Entity<T>(VirtualCompanyDbContext db) =>
        db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} missing.");
    private static void AssertIndex(IEntityType entity, bool unique, params string[] properties) =>
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique == unique && Names(x).SequenceEqual(properties));
    private static IEnumerable<string> Names(IReadOnlyIndex index) => index.Properties.Select(x => x.Name);

    private sealed class MismatchedCompanyContext(Guid companyId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; } = companyId;
        public Guid? UserId => Guid.NewGuid();
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) { }
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { }
    }
}
