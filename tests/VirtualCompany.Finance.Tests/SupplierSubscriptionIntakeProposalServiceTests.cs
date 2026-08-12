using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class SupplierSubscriptionIntakeProposalServiceTests
{
    [Fact]
    public async Task Recording_same_source_fingerprint_reuses_existing_proposal()
    {
        await using var fixture = await ProposalFixture.CreateAsync();
        var seed = await fixture.SeedSourceAsync();
        var service = fixture.CreateProposalService();
        var command = fixture.CreateRecordCommand(seed.CompanyId, seed.MessageId, seed.AttachmentId, seed.SupplierId, "source-a");

        var first = await service.RecordAsync(command, CancellationToken.None);
        var second = await service.RecordAsync(command, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await fixture.DbContext.SupplierSubscriptionIntakeProposals.IgnoreQueryFilters().Where(x => x.CompanyId == seed.CompanyId).ToListAsync());
        Assert.Contains(fixture.AuditEvents, x => x.Action == "supplier_subscription.proposal.detected");
        Assert.Contains(fixture.AuditEvents, x => x.Action == "supplier_subscription.proposal.duplicate_suppressed" && x.Outcome == "skipped");
    }

    [Fact]
    public async Task Accepted_proposal_creates_draft_subscription_and_records_link()
    {
        await using var fixture = await ProposalFixture.CreateAsync();
        var seed = await fixture.SeedSourceAsync();
        var service = fixture.CreateProposalService();
        var proposal = await service.RecordAsync(fixture.CreateRecordCommand(seed.CompanyId, seed.MessageId, seed.AttachmentId, seed.SupplierId, "accept-source"), CancellationToken.None);

        var subscription = await service.AcceptAsync(
            new AcceptSupplierSubscriptionIntakeProposalCommand(
                seed.CompanyId,
                proposal.Id,
                proposal.Terms,
                seed.UserId,
                "Finance user",
                "Looks like the supplier agreement."),
            CancellationToken.None);

        Assert.Equal("draft", subscription.Status);
        var stored = await fixture.DbContext.SupplierSubscriptionIntakeProposals.IgnoreQueryFilters().SingleAsync(x => x.Id == proposal.Id);
        Assert.Equal(SupplierSubscriptionIntakeProposalStatuses.Accepted, stored.Status);
        Assert.Equal(subscription.Id, stored.AcceptedSubscriptionId);
    }

    [Fact]
    public async Task Invalid_cross_company_supplier_is_rejected_on_record()
    {
        await using var fixture = await ProposalFixture.CreateAsync();
        var companyA = await fixture.SeedSourceAsync();
        var companyB = await fixture.SeedSourceAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateProposalService().RecordAsync(
            fixture.CreateRecordCommand(companyA.CompanyId, companyA.MessageId, companyA.AttachmentId, companyB.SupplierId, "cross-company"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Rejected_proposal_stays_auditable_without_creating_subscription()
    {
        await using var fixture = await ProposalFixture.CreateAsync();
        var seed = await fixture.SeedSourceAsync();
        var service = fixture.CreateProposalService();
        var proposal = await service.RecordAsync(fixture.CreateRecordCommand(seed.CompanyId, seed.MessageId, seed.AttachmentId, seed.SupplierId, "reject-source"), CancellationToken.None);

        var rejected = await service.RejectAsync(new RejectSupplierSubscriptionIntakeProposalCommand(seed.CompanyId, proposal.Id, "Not a subscription agreement.", seed.UserId, "Finance user"), CancellationToken.None);

        Assert.Equal(SupplierSubscriptionIntakeProposalStatuses.Rejected, rejected.Status);
        Assert.Equal("Not a subscription agreement.", rejected.DecisionReason);
        Assert.Empty(await fixture.DbContext.SupplierSubscriptions.IgnoreQueryFilters().Where(x => x.CompanyId == seed.CompanyId).ToListAsync());
    }

    [Fact]
    public async Task Query_filters_scope_proposals_by_company()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var seedContext = CreateContext(connection, new TestCompanyContextAccessor(null, null)))
        {
            await seedContext.Database.EnsureCreatedAsync();
            var fixture = new ProposalFixture(connection, seedContext);
            var sourceA = await fixture.SeedSourceAsync(companyAId);
            var sourceB = await fixture.SeedSourceAsync(companyBId);
            seedContext.SupplierSubscriptionIntakeProposals.Add(fixture.CreateProposal(sourceA.CompanyId, sourceA.MessageId, sourceA.AttachmentId, sourceA.SupplierId, "filter-a"));
            seedContext.SupplierSubscriptionIntakeProposals.Add(fixture.CreateProposal(sourceB.CompanyId, sourceB.MessageId, sourceB.AttachmentId, sourceB.SupplierId, "filter-b"));
            await seedContext.SaveChangesAsync();
        }

        var accessor = new TestCompanyContextAccessor(companyAId, Guid.NewGuid());
        await using var scopedContext = CreateContext(connection, accessor);
        Assert.Single(await scopedContext.SupplierSubscriptionIntakeProposals.ToListAsync());
        accessor.SetCompanyId(companyBId);
        Assert.Single(await scopedContext.SupplierSubscriptionIntakeProposals.ToListAsync());
    }

    [Fact]
    public async Task Classifier_creates_review_proposal_for_subscription_agreement_attachment()
    {
        await using var fixture = await ProposalFixture.CreateAsync();
        var seed = await fixture.SeedSourceAsync(attachmentText: "Supplier: Cloud Supplier\nAgreement reference: CLOUD-2026\nMonthly subscription 100 SEK. Billing day 31. Notice period 30 days. Start 2026-01-01. Automatically renews.");

        var result = await fixture.CreateClassifier().ClassifyAsync(new ClassifySupplierSubscriptionSourceCommand(seed.CompanyId, seed.MessageId, seed.UserId, "Laura"), CancellationToken.None);

        Assert.Equal(1, result.ProposalCount);
        var proposal = await fixture.DbContext.SupplierSubscriptionIntakeProposals.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId);
        Assert.Equal(SupplierSubscriptionIntakeProposalClassifications.Agreement, proposal.Classification);
        Assert.Equal(seed.SupplierId, proposal.MatchedCounterpartyId);
        Assert.Equal(100m, proposal.ExpectedAmount);
        Assert.Equal(SupplierSubscriptionCadences.Monthly, proposal.Cadence);
    }

    [Fact]
    public async Task Classifier_treats_recurring_receipt_as_evidence_without_creating_subscription()
    {
        await using var fixture = await ProposalFixture.CreateAsync();
        var seed = await fixture.SeedSourceAsync(subject: "Payment receipt", attachmentName: "receipt.pdf", attachmentText: "Receipt for paid monthly service. Payment received 100 SEK. Thank you.");

        var result = await fixture.CreateClassifier().ClassifyAsync(new ClassifySupplierSubscriptionSourceCommand(seed.CompanyId, seed.MessageId, seed.UserId, "Laura"), CancellationToken.None);

        Assert.Equal(0, result.ProposalCount);
        Assert.Equal(1, result.ReceiptEvidenceCount);
        Assert.Empty(await fixture.DbContext.SupplierSubscriptionIntakeProposals.IgnoreQueryFilters().Where(x => x.CompanyId == seed.CompanyId).ToListAsync());
    }

    [Fact]
    public async Task Classifier_ignores_normal_supplier_invoice_text()
    {
        await using var fixture = await ProposalFixture.CreateAsync();
        var seed = await fixture.SeedSourceAsync(subject: "Invoice attached", attachmentName: "invoice.pdf", attachmentText: "Invoice number INV-1. Due date 2026-02-15. Bankgiro 123-4567. Amount 100 SEK.");

        var result = await fixture.CreateClassifier().ClassifyAsync(new ClassifySupplierSubscriptionSourceCommand(seed.CompanyId, seed.MessageId, seed.UserId, "Laura"), CancellationToken.None);

        Assert.Equal(0, result.ProposalCount);
        Assert.Empty(await fixture.DbContext.SupplierSubscriptionIntakeProposals.IgnoreQueryFilters().Where(x => x.CompanyId == seed.CompanyId).ToListAsync());
    }

    [Fact]
    public async Task Classifier_extracts_swedish_supplier_subscription_agreement_terms()
    {
        await using var fixture = await ProposalFixture.CreateAsync();
        var seed = await fixture.SeedSourceAsync(
            subject: "Nytt leverantorsavtal",
            attachmentName: "avtal.pdf",
            attachmentText: "Leverantor: Cloud Supplier\nAvtalsnummer: SE-2026\nAbonnemang arsvis 1 200 SEK. Faktureras den 15. Uppsagningstid 60 dagar. Startdatum 2026-03-01. Fornyas automatiskt.");

        var result = await fixture.CreateClassifier().ClassifyAsync(new ClassifySupplierSubscriptionSourceCommand(seed.CompanyId, seed.MessageId, seed.UserId, "Laura"), CancellationToken.None);

        Assert.Equal(1, result.ProposalCount);
        var proposal = await fixture.DbContext.SupplierSubscriptionIntakeProposals.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId);
        Assert.Equal(seed.SupplierId, proposal.MatchedCounterpartyId);
        Assert.Equal("se-2026", proposal.ContractReference);
        Assert.Equal(1200m, proposal.ExpectedAmount);
        Assert.Equal(SupplierSubscriptionCadences.Yearly, proposal.Cadence);
        Assert.Equal(15, proposal.BillingDay);
        Assert.Equal(60, proposal.NoticePeriodDays);
        Assert.True(proposal.AutoRenews);
    }

    [Fact]
    public async Task Classifier_leaves_ambiguous_supplier_match_for_human_review()
    {
        await using var fixture = await ProposalFixture.CreateAsync();
        var seed = await fixture.SeedSourceAsync(attachmentText: "Cloud Supplier and Cloud Backup recurring service agreement. Monthly subscription 100 SEK. Billing day 10.");
        fixture.DbContext.FinanceCounterparties.Add(new FinanceCounterparty(Guid.NewGuid(), seed.CompanyId, "Cloud Backup", "supplier", taxId: "445566-7788"));
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateClassifier().ClassifyAsync(new ClassifySupplierSubscriptionSourceCommand(seed.CompanyId, seed.MessageId, seed.UserId, "Laura"), CancellationToken.None);

        Assert.Equal(1, result.ProposalCount);
        var proposal = await fixture.DbContext.SupplierSubscriptionIntakeProposals.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId);
        Assert.Null(proposal.MatchedCounterpartyId);
        Assert.Equal(SupplierSubscriptionIntakeProposalStatuses.NeedsReview, proposal.Status);
    }
    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection, ICompanyContextAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options, accessor);

    private sealed class ProposalFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly RecordingAuditWriter _audit = new();
        public VirtualCompanyDbContext DbContext { get; }
        public IReadOnlyList<AuditEventWriteRequest> AuditEvents => _audit.Events;

        public ProposalFixture(SqliteConnection connection, VirtualCompanyDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
        }

        public static async Task<ProposalFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var dbContext = CreateContext(connection, new TestCompanyContextAccessor(null, null));
            await dbContext.Database.EnsureCreatedAsync();
            return new ProposalFixture(connection, dbContext);
        }

        public ISupplierSubscriptionIntakeProposalService CreateProposalService()
        {
            var subscriptionService = new SupplierSubscriptionService(DbContext, _audit, NullLogger<SupplierSubscriptionService>.Instance);
            return new SupplierSubscriptionIntakeProposalService(DbContext, subscriptionService, _audit, NullLogger<SupplierSubscriptionIntakeProposalService>.Instance);
        }

        public ISupplierSubscriptionDocumentClassifier CreateClassifier() =>
            new SupplierSubscriptionDocumentClassifier(DbContext, CreateProposalService(), NullLogger<SupplierSubscriptionDocumentClassifier>.Instance);

        public RecordSupplierSubscriptionIntakeProposalCommand CreateRecordCommand(Guid companyId, Guid messageId, Guid attachmentId, Guid supplierId, string fingerprint) =>
            new(
                companyId,
                messageId,
                attachmentId,
                null,
                fingerprint,
                SupplierSubscriptionIntakeProposalClassifications.Agreement,
                SupplierSubscriptionIntakeProposalStatuses.NeedsReview,
                88,
                "Agreement terms were detected in the supplier contract attachment.",
                "Cloud Supplier",
                "556677-8899",
                new SupplierSubscriptionProposalTermsDto(
                    supplierId,
                    "Cloud platform agreement",
                    "SEK",
                    100m,
                    SupplierSubscriptionCadences.Monthly,
                    31,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                    0m,
                    5,
                    null,
                    "CLOUD-2026",
                    "Monthly cloud platform agreement.",
                    30,
                    true,
                    null),
                null,
                Guid.NewGuid(),
                "Laura");

        public SupplierSubscriptionIntakeProposal CreateProposal(Guid companyId, Guid messageId, Guid attachmentId, Guid supplierId, string fingerprint)
        {
            var command = CreateRecordCommand(companyId, messageId, attachmentId, supplierId, fingerprint);
            return new SupplierSubscriptionIntakeProposal(
                Guid.NewGuid(),
                command.CompanyId,
                command.SourceEmailMessageSnapshotId,
                command.SourceEmailAttachmentSnapshotId,
                command.SourceDocumentId,
                command.SourceFingerprint,
                command.Classification,
                command.Status,
                command.ConfidenceScore,
                command.EvidenceSummary,
                command.SupplierName,
                command.SupplierOrgNumber,
                command.Terms.CounterpartyId,
                command.Terms.Name,
                command.Terms.Currency,
                command.Terms.ExpectedAmount,
                command.Terms.Cadence,
                command.Terms.BillingDay,
                command.Terms.StartDateUtc,
                command.Terms.EndDateUtc,
                command.Terms.NextExpectedBillDateUtc,
                command.Terms.AmountTolerance,
                command.Terms.DateToleranceDays,
                command.Terms.NoticePeriodDays,
                command.Terms.AutoRenews,
                command.Terms.ContractReference,
                command.Terms.Description);
        }

        public async Task<SourceSeed> SeedSourceAsync(Guid? requestedCompanyId = null, string subject = "Your subscription agreement", string attachmentName = "cloud-agreement.pdf", string attachmentText = "Monthly subscription 100 SEK.")
        {
            var companyId = requestedCompanyId ?? Guid.NewGuid();
            var userId = Guid.NewGuid();
            var supplierId = Guid.NewGuid();
            var mailboxId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var messageId = Guid.NewGuid();
            var attachmentId = Guid.NewGuid();
            DbContext.Companies.Add(new Company(companyId, $"Company {companyId:N}"));
            DbContext.Users.Add(new User(userId, $"user-{userId:N}@example.com", "Finance User", "test", userId.ToString("N")));
            DbContext.FinanceCounterparties.Add(new FinanceCounterparty(supplierId, companyId, "Cloud Supplier", "supplier", taxId: "556677-8899"));
            DbContext.MailboxConnections.Add(new MailboxConnection(mailboxId, companyId, userId, MailboxProvider.Gmail, $"finance-{companyId:N}@example.com"));
            DbContext.EmailIngestionRuns.Add(new EmailIngestionRun(runId, companyId, mailboxId, userId, MailboxProvider.Gmail, DateTime.UtcNow));
            var message = new EmailMessageSnapshot(
                messageId,
                companyId,
                mailboxId,
                runId,
                $"message-{messageId:N}",
                "billing@supplier.example",
                "Cloud Supplier",
                subject,
                DateTime.UtcNow,
                "INBOX",
                "Invoices",
                "provider-body-ref",
                "Subscription agreement attached.",
                BillSourceType.PdfAttachment,
                EmailCandidateDecision.NotCandidate,
                [BillDetectionRuleMatch.AttachmentPresent],
                "Supported attachment was found.");
            message.Attachments.Add(new EmailAttachmentSnapshot(attachmentId, companyId, messageId, "att-1", attachmentName, "application/pdf", 1000, $"hash-{attachmentId:N}", "provider-ref", BillSourceType.PdfAttachment, attachmentText));
            DbContext.EmailMessageSnapshots.Add(message);
            await DbContext.SaveChangesAsync();
            return new SourceSeed(companyId, userId, supplierId, messageId, attachmentId);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed record SourceSeed(Guid CompanyId, Guid UserId, Guid SupplierId, Guid MessageId, Guid AttachmentId);

    private sealed class RecordingAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid? companyId, Guid? userId)
        {
            CompanyId = companyId;
            UserId = userId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; }
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
}

