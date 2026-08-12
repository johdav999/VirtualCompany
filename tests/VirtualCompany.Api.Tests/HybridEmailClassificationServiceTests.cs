using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class HybridEmailClassificationServiceTests
{
    [Fact]
    public async Task Finance_invoice_with_attachment_is_classified_by_deterministic_rules_without_ai()
    {
        await using var fixture = await ClassifierFixture.CreateAsync();
        var summary = new MailboxMessageSummary(
            "finance-message-1",
            "Invoice 1001",
            null,
            null,
            ["invoice.pdf"],
            "billing@supplier.example",
            "Supplier Billing",
            DateTime.UtcNow,
            "invoices",
            "Invoices",
            null,
            [new MailboxAttachmentSummary("att-1", "invoice.pdf", "application/pdf", 1000, UntrustedExtractedText: "Invoice number 1001 amount due 42 SEK")]);

        var result = await fixture.Service.ClassifyAsync(new EmailClassificationRequest(
            fixture.CompanyId,
            MailboxPurpose.Finance,
            "gmail",
            Guid.NewGuid(),
            summary,
            []), CancellationToken.None);

        Assert.Equal(EmailClassificationDomains.Finance, result.Domain);
        Assert.Equal(EmailClassificationIntents.Invoice, result.Intent);
        Assert.Equal(EmailClassificationActions.RouteToFinanceReview, result.RecommendedAction);
        Assert.True(result.UsedDeterministicRules);
        Assert.False(result.UsedAi);
        Assert.Equal(0, fixture.Reasoning.Calls.Count);
    }

    [Fact]
    public async Task Sales_receipt_is_ignored_by_guardrail_without_ai()
    {
        await using var fixture = await ClassifierFixture.CreateAsync(seedSalesAgent: true);
        var message = new MailboxInboundMessage(
            "sales-receipt-1",
            "thread-1",
            "<sales-receipt-1@example.com>",
            "Receipt",
            "Your purchase receipt and order confirmation.",
            null,
            new MailboxAddress("buyer@example.com", "Buyer"),
            [],
            DateTime.UtcNow,
            new Dictionary<string, string>());

        var result = await fixture.Service.ClassifyAsync(new EmailClassificationRequest(
            fixture.CompanyId,
            MailboxPurpose.Sales,
            "gmail",
            Guid.NewGuid(),
            null,
            [message]), CancellationToken.None);

        Assert.Equal(EmailClassificationDomains.Sales, result.Domain);
        Assert.Equal(EmailClassificationIntents.Receipt, result.Intent);
        Assert.Equal(EmailClassificationActions.Ignore, result.RecommendedAction);
        Assert.Equal("receipt", result.IgnoreReason);
        Assert.False(result.UsedAi);
        Assert.Equal(0, fixture.Reasoning.Calls.Count);
    }

    [Fact]
    public async Task Sales_buying_signal_uses_ai_when_agent_is_available()
    {
        await using var fixture = await ClassifierFixture.CreateAsync(seedSalesAgent: true);
        fixture.Reasoning.NextResult = new AgentReasoningResult(
            Guid.NewGuid(),
            AgentAiRunStatuses.Completed,
            "1.0.0",
            "domain=sales; intent=sales_lead; action=create_sales_lead_draft; confidence=0.91; urgency=high; product=enterprise plan; evidence=Buyer asked for pricing this week.",
            [],
            0.91m,
            [],
            [],
            [],
            []);
        var message = new MailboxInboundMessage(
            "sales-message-1",
            "thread-1",
            "<sales-message-1@example.com>",
            "Pricing request",
            "Can we get pricing for the enterprise plan this week?",
            null,
            new MailboxAddress("buyer@example.com", "Buyer"),
            [],
            DateTime.UtcNow,
            new Dictionary<string, string>());

        var result = await fixture.Service.ClassifyAsync(new EmailClassificationRequest(
            fixture.CompanyId,
            MailboxPurpose.Sales,
            "gmail",
            Guid.NewGuid(),
            null,
            [message]), CancellationToken.None);

        Assert.Equal(EmailClassificationDomains.Sales, result.Domain);
        Assert.Equal(EmailClassificationIntents.SalesLead, result.Intent);
        Assert.Equal(EmailClassificationActions.CreateSalesLeadDraft, result.RecommendedAction);
        Assert.Equal(0.91m, result.Confidence);
        Assert.Equal("high", result.Urgency);
        Assert.Equal("enterprise plan", result.ProductOrServiceInterest);
        Assert.True(result.UsedAi);
        var call = Assert.Single(fixture.Reasoning.Calls);
        Assert.Equal(AgentCapabilityIds.SalesLeadIntelligence, call.CapabilityId);
        Assert.Contains("Do not recommend sending, paying, posting, approving, or externally executing anything", call.Instruction);
    }

    [Fact]
    public async Task Sales_buying_signal_overrides_incidental_invoice_language()
    {
        await using var fixture = await ClassifierFixture.CreateAsync(seedSalesAgent: true);
        fixture.Reasoning.NextResult = new AgentReasoningResult(
            Guid.NewGuid(),
            AgentAiRunStatuses.Completed,
            "1.0.0",
            "domain=sales; intent=sales_lead; action=create_sales_lead_draft; confidence=0.93; urgency=medium; product=Virtual Company; evidence=Buyer requested pricing and a demo.",
            [],
            0.93m,
            [],
            [],
            [],
            []);
        var message = new MailboxInboundMessage(
            "mixed-sales-message-1",
            "mixed-sales-thread-1",
            "<mixed-sales-message-1@example.com>",
            "Interested in a subscription for our finance team",
            "We process around 600 supplier invoices each month. Could you send pricing and arrange a demo next week?",
            null,
            new MailboxAddress("buyer@example.com", "Buyer"),
            [],
            DateTime.UtcNow,
            new Dictionary<string, string>());

        var result = await fixture.Service.ClassifyAsync(new EmailClassificationRequest(
            fixture.CompanyId,
            MailboxPurpose.Sales,
            "gmail",
            Guid.NewGuid(),
            null,
            [message]), CancellationToken.None);

        Assert.Equal(EmailClassificationIntents.SalesLead, result.Intent);
        Assert.Equal(EmailClassificationActions.CreateSalesLeadDraft, result.RecommendedAction);
        Assert.Null(result.IgnoreReason);
        Assert.True(result.UsedAi);
        Assert.Contains("sales_buying_signal", result.RuleMatches);
        Assert.Single(fixture.Reasoning.Calls);
    }

    [Fact]
    public async Task Sales_invoice_without_buying_signal_remains_a_hard_ignore()
    {
        await using var fixture = await ClassifierFixture.CreateAsync(seedSalesAgent: true);
        var message = new MailboxInboundMessage(
            "sales-invoice-1",
            "sales-invoice-thread-1",
            "<sales-invoice-1@example.com>",
            "Invoice due",
            "Invoice 1001 has an amount due and payment due Friday.",
            null,
            new MailboxAddress("billing@example.com", "Billing"),
            [],
            DateTime.UtcNow,
            new Dictionary<string, string>());

        var result = await fixture.Service.ClassifyAsync(new EmailClassificationRequest(
            fixture.CompanyId,
            MailboxPurpose.Sales,
            "gmail",
            Guid.NewGuid(),
            null,
            [message]), CancellationToken.None);

        Assert.Equal(EmailClassificationIntents.Invoice, result.Intent);
        Assert.Equal(EmailClassificationActions.Ignore, result.RecommendedAction);
        Assert.Equal("invoice", result.IgnoreReason);
        Assert.False(result.UsedAi);
        Assert.Empty(fixture.Reasoning.Calls);
    }
    [Fact]
    public async Task Ambiguous_sales_message_falls_back_safely_when_ai_is_unavailable()
    {
        await using var fixture = await ClassifierFixture.CreateAsync(seedSalesAgent: true);
        fixture.Reasoning.ThrowOnReason = true;
        var message = new MailboxInboundMessage(
            "sales-message-2",
            "thread-2",
            "<sales-message-2@example.com>",
            "Proposal",
            "We are evaluating options and want a proposal.",
            null,
            new MailboxAddress("buyer@example.com", "Buyer"),
            [],
            DateTime.UtcNow,
            new Dictionary<string, string>());

        var result = await fixture.Service.ClassifyAsync(new EmailClassificationRequest(
            fixture.CompanyId,
            MailboxPurpose.Sales,
            "gmail",
            Guid.NewGuid(),
            null,
            [message]), CancellationToken.None);

        Assert.Equal(EmailClassificationIntents.SalesLead, result.Intent);
        Assert.Equal(EmailClassificationActions.CreateSalesLeadDraft, result.RecommendedAction);
        Assert.True(result.RequiresHumanReview);
        Assert.False(result.UsedAi);
        Assert.Single(fixture.Reasoning.Calls);
    }

    [Fact]
    public async Task Sales_adapter_requires_review_for_low_confidence_lead_classification()
    {
        var classifier = new FixedEmailClassifier(new EmailClassificationResult(
            EmailClassificationDomains.Sales,
            EmailClassificationIntents.SalesLead,
            0.72m,
            "Buying signal exists but needs human review.",
            EmailClassificationActions.CreateSalesLeadDraft,
            RequiresHumanReview: true,
            UsedDeterministicRules: true,
            UsedAi: false,
            ["sales_buying_signal"]));
        var service = new SharedSalesEmailIntentExtractionService(classifier, NullLogger<SharedSalesEmailIntentExtractionService>.Instance);
        var message = new MailboxInboundMessage(
            "review-sales-1",
            "thread-review",
            "<review-sales-1@example.com>",
            "Proposal",
            "We are evaluating options and want a proposal.",
            null,
            new MailboxAddress("buyer@example.com", "Buyer"),
            [],
            DateTime.UtcNow,
            new Dictionary<string, string>());

        var result = await service.ExtractAsync(new SalesEmailIntentExtractionRequest(
            Guid.NewGuid(),
            "gmail",
            Guid.NewGuid(),
            [message]), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(SalesEmailIntentClassifications.Ignore, result!.Classification);
        Assert.Equal(SalesEmailIgnoreReasons.InsufficientSignal, result.IgnoreReason);
        Assert.True(result.UsedFallback);
    }
    private sealed class ClassifierFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ClassifierFixture(SqliteConnection connection, VirtualCompanyDbContext dbContext, HybridEmailClassificationService service, CapturingReasoningGateway reasoning, Guid companyId)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = service;
            Reasoning = reasoning;
            CompanyId = companyId;
        }

        public VirtualCompanyDbContext DbContext { get; }
        public HybridEmailClassificationService Service { get; }
        public CapturingReasoningGateway Reasoning { get; }
        public Guid CompanyId { get; }

        public static async Task<ClassifierFixture> CreateAsync(bool seedSalesAgent = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var dbContext = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
            await dbContext.Database.EnsureCreatedAsync();
            var companyId = Guid.NewGuid();
            dbContext.Companies.Add(new Company(companyId, "Classifier Company"));
            if (seedSalesAgent)
            {
                dbContext.Agents.Add(new Agent(Guid.NewGuid(), companyId, "sales", "Sales", "Sales Manager", "Sales", null, AgentSeniority.Lead, AgentStatus.Active));
            }
            await dbContext.SaveChangesAsync();

            var reasoning = new CapturingReasoningGateway();
            var service = new HybridEmailClassificationService(dbContext, new BillDetectionService(), reasoning, NullLogger<HybridEmailClassificationService>.Instance);
            return new ClassifierFixture(connection, dbContext, service, reasoning, companyId);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedEmailClassifier(EmailClassificationResult result) : IEmailClassificationService
    {
        public Task<EmailClassificationResult> ClassifyAsync(EmailClassificationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
    private sealed class CapturingReasoningGateway : IAgentReasoningGateway
    {
        public List<AgentReasoningRequest> Calls { get; } = [];
        public bool ThrowOnReason { get; set; }
        public AgentReasoningResult? NextResult { get; set; }

        public Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            if (ThrowOnReason)
            {
                throw new InvalidOperationException("AI unavailable.");
            }

            return Task.FromResult(NextResult ?? new AgentReasoningResult(Guid.NewGuid(), AgentAiRunStatuses.Failed, "1.0.0", "failed", [], 0m, [], [], [], []));
        }

        public Task<AgentReasoningResult?> GetRunAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentReasoningResult?>(null);
    }
}