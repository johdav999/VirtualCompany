using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Finance.Tests;

public sealed class AdvancedReconciliationServiceTests
{
    [Fact]
    public async Task Proposed_group_retains_explainable_rule_evidence_and_material_review_requirement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var detail = await fixture.Service.CreateGroupAsync(fixture.Command(), default);

        Assert.True(detail.IsBalanced);
        Assert.Equal(15000m, detail.Summary.ExpectedBankTotal);
        Assert.True(detail.Summary.RequiresApproval);
        Assert.Equal(5, detail.ReasonContributions.Count);
        Assert.Contains(detail.ReasonContributions, x => x.FeatureKey == "amount" && x.Contribution == .30m);
        Assert.Equal(2, detail.Nodes.Count);
        Assert.Single(detail.Edges);
        Assert.Single(detail.History);
        Assert.Equal(1, detail.Summary.RuleVersion);
    }

    [Fact]
    public async Task Proposal_business_idempotency_replays_one_group_and_rejects_changed_content()
    {
        await using var fixture = await Fixture.CreateAsync();
        var command = fixture.Command() with { IdempotencyKey = "reconciliation-draft:batch-15000:v4" };

        var first = await fixture.Service.CreateGroupAsync(command, default);
        var replay = await fixture.Service.CreateGroupAsync(command, default);
        var conflict = await Assert.ThrowsAsync<FinanceValidationException>(() => fixture.Service.CreateGroupAsync(
            command with { Reference = "CHANGED-REFERENCE" }, default));

        Assert.Equal(first.Summary.Id, replay.Summary.Id);
        Assert.Single(await fixture.Db.AdvancedReconciliationGroups.IgnoreQueryFilters().ToListAsync());
        Assert.Contains(AdvancedReconciliationReasonCodes.IdempotencyConflict, conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_rule_version_rejects_acceptance_before_any_bank_or_allocation_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var detail = await fixture.Service.CreateGroupAsync(fixture.Command(), default);
        await fixture.Service.CreateRuleVersionAsync(new(fixture.CompanyId, "Rule v2", @"[\s\-_/]+",
            @"[\s\-_/.,]+", ".*", .01m, 10, .30m, .80m, 5000m, fixture.UserId), default);

        var stale = await fixture.Service.GetAsync(new(fixture.CompanyId, detail.Summary.Id), default);
        Assert.NotNull(stale); Assert.True(stale.Summary.IsStale);
        var error = await Assert.ThrowsAsync<FinanceValidationException>(() => fixture.Service.AcceptAsync(new(
            fixture.CompanyId, detail.Summary.Id, detail.Summary.Version, detail.Summary.RuleVersion,
            "Reviewed", fixture.UserId), default));

        Assert.Contains(AdvancedReconciliationReasonCodes.RuleVersionConflict, error.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.Db.BankTransactionPaymentLinks.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await fixture.Db.PaymentAllocations.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(AdvancedReconciliationGroupStatuses.Proposed,
            (await fixture.Db.AdvancedReconciliationGroups.IgnoreQueryFilters().SingleAsync()).Status);
    }

    [Fact]
    public async Task Batch_deposit_acceptance_allocates_exact_receivables_and_retains_accepted_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var detail = await fixture.Service.CreateGroupAsync(fixture.BatchCommand(), default);

        var accepted = await fixture.Service.AcceptAsync(new(fixture.CompanyId, detail.Summary.Id,
            detail.Summary.Version, detail.Summary.RuleVersion, "Authorized batch review", fixture.UserId), default);

        var allocations = await fixture.Db.PaymentAllocations.IgnoreQueryFilters()
            .Where(x => x.CompanyId == fixture.CompanyId).ToListAsync();
        allocations = allocations.OrderBy(x => x.AllocatedAmount).ToList();
        Assert.Equal(2, allocations.Count);
        Assert.Equal(15000m, allocations.Sum(x => x.AllocatedAmount));
        Assert.Equal([6000m, 9000m], allocations.Select(x => x.AllocatedAmount));
        Assert.All(await fixture.Db.FinanceInvoices.IgnoreQueryFilters().Where(x =>
            x.Id == fixture.InvoiceAId || x.Id == fixture.InvoiceBId).ToListAsync(),
            x => Assert.Equal(FinanceSettlementStatuses.Paid, x.SettlementStatus));
        Assert.Single(await fixture.Db.BankTransactionPaymentLinks.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(AdvancedReconciliationGroupStatuses.Accepted, accepted.Summary.Status);
        Assert.Single(accepted.Results, x => x.Outcome == AdvancedReconciliationResultOutcomes.Accepted);
        Assert.Equal(5, accepted.ReasonContributions.Count);
    }

    [Fact]
    public async Task Changed_source_version_rejects_acceptance_atomically()
    {
        await using var fixture = await Fixture.CreateAsync();
        var detail = await fixture.Service.CreateGroupAsync(fixture.Command(), default);
        var bank = await fixture.Db.BankTransactions.IgnoreQueryFilters().SingleAsync(x => x.Id == fixture.BankTransactionId);
        fixture.Db.Entry(bank).Property(x => x.SourceVersion).CurrentValue++;
        await fixture.Db.SaveChangesAsync();
        var queue = await fixture.Service.ListAsync(new(fixture.CompanyId), default);

        var error = await Assert.ThrowsAsync<FinanceValidationException>(() => fixture.Service.AcceptAsync(new(
            fixture.CompanyId, detail.Summary.Id, detail.Summary.Version, detail.Summary.RuleVersion,
            "Reviewed", fixture.UserId), default));

        Assert.Contains(AdvancedReconciliationReasonCodes.RecordVersionConflict, error.Message, StringComparison.Ordinal);
        Assert.True(Assert.Single(queue.Groups).IsStale);
        Assert.Equal(1, queue.Metrics.StaleCount);
        Assert.Empty(await fixture.Db.BankTransactionPaymentLinks.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await fixture.Db.PaymentAllocations.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(AdvancedReconciliationGroupStatuses.Proposed,
            (await fixture.Db.AdvancedReconciliationGroups.IgnoreQueryFilters().SingleAsync()).Status);
    }

    [Fact]
    public async Task Active_company_context_rejects_cross_company_queue_reads()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ListAsync(
            new(Guid.NewGuid()), default));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Candidate_queue_is_bounded_to_500_groups_and_materializes_within_budget()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.CreateGroupAsync(fixture.Command(), default);
        var rule = await fixture.Db.AdvancedReconciliationRules.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        var now = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < 505; index++)
        {
            var group = new AdvancedReconciliationGroup(Guid.NewGuid(), fixture.CompanyId, rule.Id, rule.Version, null,
                $"QUEUE-{index:D4}", "Queue counterparty", "SEK", 100m, .75m, false, fixture.UserId, now.AddTicks(index));
            var bank = new AdvancedReconciliationNode(Guid.NewGuid(), fixture.CompanyId, group.Id, "bank_transaction",
                Guid.NewGuid(), "Bank row", group.Reference, "SEK", 100m, "incoming", null, 0m, 0m, "1", 0);
            var payment = new AdvancedReconciliationNode(Guid.NewGuid(), fixture.CompanyId, group.Id, "payment",
                Guid.NewGuid(), "Payment", group.Reference, "SEK", 100m, null, null, 0m, 0m, "1", 1);
            fixture.Db.AddRange(group, bank, payment,
                new AdvancedReconciliationEdge(Guid.NewGuid(), fixture.CompanyId, group.Id, bank.Id, payment.Id,
                    "bank_payment", 100m));
        }
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var started = Stopwatch.GetTimestamp();
        var workspace = await fixture.Service.ListAsync(new(fixture.CompanyId, Limit: 10000), default);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Equal(500, workspace.Groups.Count);
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"Queue materialization took {elapsed}.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext db, AdvancedReconciliationService service,
            Guid companyId, Guid userId, Guid bankTransactionId, Guid paymentId, Guid invoiceAId, Guid invoiceBId)
        { _connection = connection; Db = db; Service = service; CompanyId = companyId; UserId = userId; BankTransactionId = bankTransactionId; PaymentId = paymentId; InvoiceAId = invoiceAId; InvoiceBId = invoiceBId; }
        public VirtualCompanyDbContext Db { get; }
        public AdvancedReconciliationService Service { get; }
        public Guid CompanyId { get; } public Guid UserId { get; } public Guid BankTransactionId { get; } public Guid PaymentId { get; }
        public Guid InvoiceAId { get; } public Guid InvoiceBId { get; }

        public CreateAdvancedReconciliationGroupCommand Command()
        {
            var bankNode = Guid.NewGuid(); var paymentNode = Guid.NewGuid();
            return new(CompanyId, "BATCH-15000", "Acme AB", "SEK", null, null,
                [new(bankNode, "bank_transaction", BankTransactionId, Sequence: 0), new(paymentNode, "payment", PaymentId, Sequence: 1)],
                [new(Guid.NewGuid(), bankNode, paymentNode, "bank_payment", 15000m)], UserId);
        }

        public CreateAdvancedReconciliationGroupCommand BatchCommand()
        {
            var bankNode = Guid.NewGuid(); var paymentNode = Guid.NewGuid();
            var invoiceANode = Guid.NewGuid(); var invoiceBNode = Guid.NewGuid();
            return new(CompanyId, "BATCH-15000", "Acme AB", "SEK", null, null,
                [new(bankNode, "bank_transaction", BankTransactionId, Sequence: 0),
                    new(paymentNode, "payment", PaymentId, Sequence: 1),
                    new(invoiceANode, "invoice", InvoiceAId, Sequence: 2),
                    new(invoiceBNode, "invoice", InvoiceBId, Sequence: 3)],
                [new(Guid.NewGuid(), bankNode, paymentNode, "bank_payment", 15000m),
                    new(Guid.NewGuid(), paymentNode, invoiceANode, "payment_document", 9000m),
                    new(Guid.NewGuid(), paymentNode, invoiceBNode, "payment_document", 6000m)], UserId);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False"); await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var company = Guid.NewGuid(); var user = Guid.NewGuid(); var now = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
            db.Companies.Add(new Company(company, "Advanced reconciliation company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, user, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            var financeAccount = new FinanceAccount(Guid.NewGuid(), company, "1930", "Operating bank", "asset", "SEK", 0m, now);
            var bankAccount = new CompanyBankAccount(Guid.NewGuid(), company, financeAccount.Id, "Operating account", "Test bank", "•••• 1500", "SEK");
            var transaction = new BankTransaction(Guid.NewGuid(), company, bankAccount.Id, now, now, 15000m, "SEK",
                "BATCH-15000", "Acme AB", importSource: "enablebanking", sourceVersion: 4);
            var payment = new Payment(Guid.NewGuid(), company, PaymentTypes.Incoming, 15000m, "SEK", now,
                PaymentMethods.BankTransfer, PaymentStatuses.Completed, "BATCH-15000", now, now);
            var counterparty = new FinanceCounterparty(Guid.NewGuid(), company, "Acme AB", "customer", "finance@acme.test");
            var invoiceA = new FinanceInvoice(Guid.NewGuid(), company, counterparty.Id, "INV-BATCH-001", now.AddDays(-10),
                now.AddDays(20), 9000m, "SEK", "open", createdUtc: now, updatedUtc: now);
            var invoiceB = new FinanceInvoice(Guid.NewGuid(), company, counterparty.Id, "INV-BATCH-002", now.AddDays(-9),
                now.AddDays(21), 6000m, "SEK", "open", createdUtc: now, updatedUtc: now);
            db.AddRange(financeAccount, bankAccount, transaction, payment, counterparty, invoiceA, invoiceB); await db.SaveChangesAsync();
            var context = new Context(company, user); var audit = new AuditStub();
            var posting = new PostingStub();
            var service = new AdvancedReconciliationService(db, new CompanyBankTransactionService(db, context),
                new FinancePaymentAllocationService(db), posting, audit, context, new FixedTimeProvider(now));
            return new(connection, db, service, company, user, transaction.Id, payment.Id, invoiceA.Id, invoiceB.Id);
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class Context(Guid company, Guid user) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = company; public Guid? UserId { get; } = user; public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
    private sealed class FixedTimeProvider(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
    private sealed class AuditStub : IAuditEventWriter { public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class PostingStub : IAccountingPostingService
    {
        public Task<AccountingPostingPreview> PreviewAsync(PreviewAccountingEntryCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingPostingPreview> PreviewNonAuthoritativeCandidateAsync(PreviewNonAuthoritativeAccountingCandidateCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PostedAccountingJournal> PostAsync(PostAccountingEntryCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PostedAccountingJournal> MaterializeProviderSwitchJournalAsync(MaterializeAccountingProviderSwitchJournalCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PostedAccountingJournal> ReverseAsync(ReverseAccountingEntryCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
