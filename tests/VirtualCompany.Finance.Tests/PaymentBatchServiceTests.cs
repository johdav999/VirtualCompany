using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class PaymentBatchServiceTests
{
    [Fact]
    public async Task Lifecycle_creates_immutable_instructions_enforces_dual_approval_and_has_no_bank_side_effect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eligible = await fixture.Service.ListEligibleObligationsAsync(
            new(fixture.CompanyId), CancellationToken.None);
        var candidate = Assert.Single(eligible);
        Assert.True(candidate.IsEligible);

        var created = await fixture.Service.CreateAsync(new(fixture.CompanyId, "September supplier run",
            fixture.PlannedDate, "create-1", fixture.CreatorId), CancellationToken.None);
        var replay = await fixture.Service.CreateAsync(new(fixture.CompanyId, "September supplier run",
            fixture.PlannedDate, "create-1", fixture.CreatorId), CancellationToken.None);
        Assert.True(replay.Summary.IsIdempotentReplay);
        Assert.Equal(created.Summary.Id, replay.Summary.Id);

        var idempotencyConflict = await Assert.ThrowsAsync<PaymentBatchException>(() =>
            fixture.Service.CreateAsync(new(fixture.CompanyId, "Changed payload", fixture.PlannedDate,
                "create-1", fixture.CreatorId), CancellationToken.None));
        Assert.Equal(PaymentBatchReasonCodes.IdempotencyConflict, idempotencyConflict.ReasonCode);

        var added = await fixture.Service.AddObligationAsync(new(fixture.CompanyId, created.Summary.Id,
            PaymentBatchObligationTypes.SupplierPaymentProposal, fixture.ProposalId,
            created.Summary.Version, "add-1", fixture.CreatorId), CancellationToken.None);
        Assert.Equal(1250m, Assert.Single(added.Summary.Totals).Amount);
        Assert.Equal(1, added.Summary.ObligationCount);
        Assert.Equal("•••• 567", Assert.Single(added.Obligations).MaskedDestination);

        var versionConflict = await Assert.ThrowsAsync<PaymentBatchException>(() =>
            fixture.Service.ValidateAsync(new(fixture.CompanyId, created.Summary.Id,
                created.Summary.Version, "validate-stale", fixture.CreatorId), CancellationToken.None));
        Assert.Equal(PaymentBatchReasonCodes.VersionConflict, versionConflict.ReasonCode);
        Assert.Equal(added.Summary.Version, versionConflict.CurrentVersion);

        var validated = await fixture.Service.ValidateAsync(new(fixture.CompanyId, created.Summary.Id,
            added.Summary.Version, "validate-1", fixture.CreatorId), CancellationToken.None);
        Assert.Equal(PaymentBatchStatuses.Validated, validated.Summary.Status);
        Assert.True(validated.Validation!.IsValid);
        Assert.NotNull(validated.ExportArtifactHash);
        var instruction = Assert.Single(validated.Instructions);
        Assert.Equal(PaymentInstructionStatuses.Draft, instruction.Status);
        Assert.Equal(1250m, instruction.Amount);

        var submitCommand = new SubmitPaymentBatchCommand(fixture.CompanyId, created.Summary.Id,
            validated.Summary.Version, "submit-1", fixture.CreatorId);
        var submitted = await fixture.Service.SubmitAsync(submitCommand, CancellationToken.None);
        Assert.Equal(PaymentBatchStatuses.AwaitingApproval, submitted.Summary.Status);
        var submitReplay = await fixture.Service.SubmitAsync(submitCommand, CancellationToken.None);
        Assert.True(submitReplay.Summary.IsIdempotentReplay);
        Assert.Equal(1, await fixture.Db.PaymentBatchApprovalBindings.IgnoreQueryFilters()
            .CountAsync(x => x.BatchId == created.Summary.Id));
        var submitConflict = await Assert.ThrowsAsync<PaymentBatchException>(() =>
            fixture.Service.SubmitAsync(submitCommand with { ExpectedVersion = submitted.Summary.Version },
                CancellationToken.None));
        Assert.Equal(PaymentBatchReasonCodes.IdempotencyConflict, submitConflict.ReasonCode);

        var creatorDecision = await Assert.ThrowsAsync<PaymentBatchException>(() =>
            fixture.Service.ApproveAsync(new(fixture.CompanyId, created.Summary.Id,
                submitted.Summary.Version, "creator cannot approve", "approve-creator",
                fixture.CreatorId), CancellationToken.None));
        Assert.Equal(PaymentBatchReasonCodes.SegregationOfDuties, creatorDecision.ReasonCode);

        var firstApproval = await fixture.Service.ApproveAsync(new(fixture.CompanyId, created.Summary.Id,
            submitted.Summary.Version, "first independent review", "approve-1",
            fixture.FirstApproverId), CancellationToken.None);
        Assert.Equal(PaymentBatchStatuses.AwaitingApproval, firstApproval.Summary.Status);

        var duplicateApprover = await Assert.ThrowsAsync<PaymentBatchException>(() =>
            fixture.Service.ApproveAsync(new(fixture.CompanyId, created.Summary.Id,
                firstApproval.Summary.Version, "same approver", "approve-1-again",
                fixture.FirstApproverId), CancellationToken.None));
        Assert.Equal(PaymentBatchReasonCodes.SegregationOfDuties, duplicateApprover.ReasonCode);

        var approved = await fixture.Service.ApproveAsync(new(fixture.CompanyId, created.Summary.Id,
            firstApproval.Summary.Version, "second independent review", "approve-2",
            fixture.SecondApproverId), CancellationToken.None);
        Assert.Equal(PaymentBatchStatuses.Approved, approved.Summary.Status);
        Assert.Equal(PaymentInstructionStatuses.Approved, Assert.Single(approved.Instructions).Status);
        Assert.Contains("nothing is sent to a bank", approved.InternalApprovalNotice, StringComparison.OrdinalIgnoreCase);

        var readiness = await fixture.Service.CheckSendReadinessAsync(
            new(fixture.CompanyId, created.Summary.Id), CancellationToken.None);
        Assert.True(readiness.IsReady);
        Assert.Equal(PaymentBatchReasonCodes.Ready, readiness.ReasonCode);

        fixture.Db.ChangeTracker.Clear();
        var proposal = await fixture.Db.SupplierInvoicePaymentProposals.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == fixture.ProposalId);
        var bill = await fixture.Db.FinanceBills.IgnoreQueryFilters().SingleAsync(x => x.Id == fixture.BillId);
        Assert.Equal(SupplierInvoicePaymentProposalStatuses.ReadyForPayment, proposal.Status);
        Assert.Equal(0m, bill.PaidAmount);
        Assert.Empty(await fixture.Db.BankTransactions.IgnoreQueryFilters().ToListAsync());
        Assert.Contains(fixture.Audit.Events, x => x.Action == AuditEventActions.PaymentBatchApproved && x.Outcome == AuditEventOutcomes.Approved);
    }

    [Fact]
    public async Task Beneficiary_change_invalidates_pending_approval_and_generated_artifacts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var submitted = await fixture.CreateSubmittedBatchAsync();

        await fixture.Service.RegisterBeneficiaryAsync(new(fixture.CompanyId, "supplier", fixture.SupplierId,
            "Nordic Supplies AB", PaymentRails.Bankgiro, "8765432", "•••• 432", "SEK",
            "beneficiary-check-2", new string('b', 64), fixture.CreatorId), CancellationToken.None);

        var readiness = await fixture.Service.CheckSendReadinessAsync(
            new(fixture.CompanyId, submitted.Summary.Id), CancellationToken.None);
        Assert.False(readiness.IsReady);
        Assert.Equal(PaymentBatchReasonCodes.ApprovalStale, readiness.ReasonCode);
        var invalidated = await fixture.Service.GetAsync(new(fixture.CompanyId, submitted.Summary.Id),
            CancellationToken.None);
        Assert.NotNull(invalidated);
        Assert.Equal(PaymentBatchStatuses.Draft, invalidated!.Summary.Status);
        Assert.Null(invalidated.ExportArtifactHash);
        Assert.Empty(invalidated.Instructions);
        Assert.Equal(PaymentBatchApprovalBindingStatuses.Stale, invalidated.Approval!.Status);
        Assert.Contains(fixture.Audit.Events, x => x.Action == AuditEventActions.PaymentBatchChanged && x.Outcome == AuditEventOutcomes.Blocked);
    }

    [Fact]
    public async Task Service_rejects_cross_company_reads_and_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Accessor.SetCompanyId(Guid.NewGuid());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ListAsync(
            new(fixture.CompanyId), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.CreateAsync(
            new(fixture.CompanyId, "Forbidden", fixture.PlannedDate, "forbidden", fixture.CreatorId),
            CancellationToken.None));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext db, PaymentBatchService service,
            Context accessor, AuditStub audit, Guid companyId, Guid creatorId, Guid firstApproverId,
            Guid secondApproverId, Guid supplierId, Guid billId, Guid proposalId, DateOnly plannedDate)
        {
            _connection = connection; Db = db; Service = service; Accessor = accessor; Audit = audit;
            CompanyId = companyId; CreatorId = creatorId; FirstApproverId = firstApproverId;
            SecondApproverId = secondApproverId; SupplierId = supplierId; BillId = billId;
            ProposalId = proposalId; PlannedDate = plannedDate;
        }

        public VirtualCompanyDbContext Db { get; }
        public PaymentBatchService Service { get; }
        public Context Accessor { get; }
        public AuditStub Audit { get; }
        public Guid CompanyId { get; }
        public Guid CreatorId { get; }
        public Guid FirstApproverId { get; }
        public Guid SecondApproverId { get; }
        public Guid SupplierId { get; }
        public Guid BillId { get; }
        public Guid ProposalId { get; }
        public DateOnly PlannedDate { get; }

        public async Task<PaymentBatchDetailDto> CreateSubmittedBatchAsync()
        {
            var created = await Service.CreateAsync(new(CompanyId, "Supplier run", PlannedDate,
                $"create-{Guid.NewGuid():N}", CreatorId), CancellationToken.None);
            var added = await Service.AddObligationAsync(new(CompanyId, created.Summary.Id,
                PaymentBatchObligationTypes.SupplierPaymentProposal, ProposalId, created.Summary.Version,
                $"add-{Guid.NewGuid():N}", CreatorId), CancellationToken.None);
            var validated = await Service.ValidateAsync(new(CompanyId, created.Summary.Id,
                added.Summary.Version, $"validate-{Guid.NewGuid():N}", CreatorId), CancellationToken.None);
            return await Service.SubmitAsync(new(CompanyId, created.Summary.Id,
                validated.Summary.Version, $"submit-{Guid.NewGuid():N}", CreatorId), CancellationToken.None);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid(); var creatorId = Guid.NewGuid();
            var firstApproverId = Guid.NewGuid(); var secondApproverId = Guid.NewGuid();
            var supplierId = Guid.NewGuid(); var billId = Guid.NewGuid(); var proposalId = Guid.NewGuid();
            var accountId = Guid.NewGuid(); var now = new DateTime(2026, 8, 28, 8, 0, 0, DateTimeKind.Utc);
            var accessor = new Context(companyId, creatorId);
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection).Options, accessor);
            await db.Database.EnsureCreatedAsync();

            var supplier = new FinanceCounterparty(supplierId, companyId, "Nordic Supplies AB",
                "supplier", createdUtc: now, updatedUtc: now);
            var account = new FinanceAccount(accountId, companyId, "1930", "Operating account",
                FinanceAccountClassValues.Asset, "SEK", 5000m, now, accountClass: FinanceAccountClassValues.Asset,
                normalBalance: FinanceNormalBalanceValues.Debit, effectiveFrom: new DateOnly(2026, 1, 1),
                isPostingEnabled: true);
            var bill = new FinanceBill(billId, companyId, supplierId, "SUP-2026-1042", now,
                new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc), 1250m, "SEK", "approved",
                createdUtc: now, updatedUtc: now);
            var proposal = new SupplierInvoicePaymentProposal(proposalId, companyId, billId, supplierId,
                supplier.Name, 1250m, "SEK", bill.DueUtc, "OCR-1042", creatorId, now);
            proposal.MarkReadyForPayment(creatorId, now, "Supplier invoice was independently approved.");
            db.AddRange(new Company(companyId, "Payment batch test company"), supplier, account,
                new CompanyBankAccount(Guid.NewGuid(), companyId, accountId, "Operating account", "Test Bank",
                    "•••• 1930", "SEK", isPrimary: true, createdUtc: now, updatedUtc: now),
                new FinanceBalance(Guid.NewGuid(), companyId, accountId, now, 5000m, "SEK"), bill, proposal);
            await db.SaveChangesAsync();

            var audit = new AuditStub(); var options = new PaymentBatchPolicyOptions { DualApprovalThreshold = 1000m };
            var clock = new FixedTimeProvider(now);
            var service = new PaymentBatchService(db,
                new PaymentBatchEligibilityPolicy(Options.Create(options)), audit, accessor,
                Options.Create(options), new PaymentBatchTelemetry(), clock);
            await service.RegisterBeneficiaryAsync(new(companyId, "supplier", supplierId,
                supplier.Name, PaymentRails.Bankgiro, "1234567", "•••• 567", "SEK",
                "beneficiary-check-1", new string('a', 64), creatorId), CancellationToken.None);
            return new(connection, db, service, accessor, audit, companyId, creatorId,
                firstApproverId, secondApproverId, supplierId, billId, proposalId,
                new DateOnly(2026, 8, 31));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    internal sealed class AuditStub : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        { Events.Add(auditEvent); return Task.CompletedTask; }
    }

    internal sealed class Context(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; } = userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
