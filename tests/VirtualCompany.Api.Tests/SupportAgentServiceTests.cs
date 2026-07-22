using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Support;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SupportAgentServiceTests
{
    [Fact]
    public async Task Support_case_lifecycle_creates_lists_resolves_and_reopens_case()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Support Company"));
        await dbContext.SaveChangesAsync();
        var service = new SupportCaseService(dbContext, new CaptureAuditEventWriter());

        var created = await service.CreateCaseAsync(
            companyId,
            userId,
            new CreateSupportCaseRequest("Invoice question", "Can you explain this invoice?", "Manual", "buyer@example.test"),
            CancellationToken.None);
        var listed = await service.ListCasesAsync(companyId, new SupportCaseListQuery(Search: "Invoice"), CancellationToken.None);
        var resolved = await service.ResolveAsync(companyId, userId, created.Id, new ResolveSupportCaseRequest("Explained invoice lines.", "Resolved"), CancellationToken.None);
        var reopened = await service.ReopenAsync(companyId, userId, created.Id, new SupportActionRequest("Customer replied."), CancellationToken.None);

        Assert.Single(listed.Items);
        Assert.Equal(SupportCaseStatuses.Resolved, resolved!.Status);
        Assert.Equal(SupportCaseStatuses.Reopened, reopened!.Status);
        Assert.Contains(reopened.Events, x => x.EventType == SupportCaseEventTypes.Reopened);
    }

    [Fact]
    public async Task Mailbox_ingestion_deduplicates_provider_message_and_triages_billing_case()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Support Company"));
        await dbContext.SaveChangesAsync();
        var audit = new CaptureAuditEventWriter();
        var caseService = new SupportCaseService(dbContext, audit);
        var triage = new SupportTriageService(dbContext, audit);
        var ingestion = new SupportMailboxIngestionService(dbContext, caseService, triage);
        var input = new SupportMailboxMessageInput(
            null,
            null,
            "customer@example.test",
            "Customer",
            "support@example.test",
            "Invoice payment problem",
            "The invoice payment looks wrong and this is urgent.",
            "provider-message-1",
            "provider-thread-1",
            DateTime.UtcNow);

        var first = await ingestion.IngestMessageAsync(companyId, input, CancellationToken.None);
        var second = await ingestion.IngestMessageAsync(companyId, input, CancellationToken.None);
        var supportCase = await dbContext.SupportCases.SingleAsync(x => x.Id == first.SupportCaseId);

        Assert.True(first.CreatedCase);
        Assert.False(first.Deduplicated);
        Assert.True(second.Deduplicated);
        Assert.Equal(first.SupportCaseId, second.SupportCaseId);
        Assert.Equal(SupportCaseCategories.Billing, supportCase.Category);
        Assert.Equal(SupportPriorities.High, supportCase.Priority);
    }

    [Fact]
    public async Task Mailbox_ingestion_creates_separate_cases_for_unrelated_messages_without_thread_identity()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Support Company"));
        await dbContext.SaveChangesAsync();
        var audit = new CaptureAuditEventWriter();
        var ingestion = new SupportMailboxIngestionService(
            dbContext,
            new SupportCaseService(dbContext, audit),
            new SupportTriageService(dbContext, audit));

        var first = await ingestion.IngestMessageAsync(
            companyId,
            new SupportMailboxMessageInput(null, null, "first@example.test", "First", "support@example.test",
                "Account access", "I cannot sign in.", "provider-message-1", null, DateTime.UtcNow.AddMinutes(-1)),
            CancellationToken.None);
        var second = await ingestion.IngestMessageAsync(
            companyId,
            new SupportMailboxMessageInput(null, null, "second@example.test", "Second", "support@example.test",
                "Product question", "What does the product include?", "provider-message-2", null, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(first.CreatedCase);
        Assert.True(second.CreatedCase);
        Assert.NotEqual(first.SupportCaseId, second.SupportCaseId);
        Assert.Equal(2, await dbContext.SupportCases.CountAsync());
    }

    [Fact]
    public async Task Mailbox_ingestion_appends_messages_with_the_same_provider_thread()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Support Company"));
        await dbContext.SaveChangesAsync();
        var audit = new CaptureAuditEventWriter();
        var ingestion = new SupportMailboxIngestionService(
            dbContext,
            new SupportCaseService(dbContext, audit),
            new SupportTriageService(dbContext, audit));

        var first = await ingestion.IngestMessageAsync(
            companyId,
            new SupportMailboxMessageInput(null, null, "customer@example.test", "Customer", "support@example.test",
                "Account access", "I cannot sign in.", "provider-message-1", "provider-thread-1", DateTime.UtcNow.AddMinutes(-1)),
            CancellationToken.None);
        var reply = await ingestion.IngestMessageAsync(
            companyId,
            new SupportMailboxMessageInput(null, null, "customer@example.test", "Customer", "support@example.test",
                "Re: Account access", "I still need help.", "provider-message-2", "provider-thread-1", DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(first.CreatedCase);
        Assert.False(reply.CreatedCase);
        Assert.Equal(first.SupportCaseId, reply.SupportCaseId);
        Assert.Single(await dbContext.SupportCases.ToListAsync());
        Assert.Equal(2, await dbContext.SupportMessages.CountAsync());
    }

    [Fact]
    public async Task Knowledge_retrieval_excludes_implementation_prompts_and_prefers_product_information()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Virtual Company"));
        AddIndexedKnowledge(dbContext, companyId, "services", "Implementation requirements: create support workflows for accounting and sales. Deliverable: production code.");
        AddIndexedKnowledge(dbContext, companyId, "product-catalog", "Virtual Company combines finance workflows, sales workflows, customer support, approvals, and AI-assisted agents. Important actions remain reviewable by people and the product is not completely automated.");
        await dbContext.SaveChangesAsync();
        var supportCase = await new SupportCaseService(dbContext, new CaptureAuditEventWriter()).CreateCaseAsync(
            companyId,
            userId,
            new CreateSupportCaseRequest("Product functionality", "What company functions does it support? Accounting, sales, and automation?", "Email"),
            CancellationToken.None);

        var context = await new SupportKnowledgeContextProvider(dbContext).RetrieveAsync(companyId, supportCase.Id, CancellationToken.None);

        Assert.Contains(context.Sources, x => x.IsTrusted && x.Label == "product-catalog");
        Assert.DoesNotContain(context.Sources, x => x.Label == "services");
    }

    [Fact]
    public async Task Draft_generation_uses_governed_reasoning_instead_of_copying_source_chunks()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Virtual Company"));
        dbContext.Agents.Add(new Agent(agentId, companyId, "support", "Ben", "Support Manager", "Support", null, AgentSeniority.Lead, AgentStatus.Active));
        await dbContext.SaveChangesAsync();
        var audit = new CaptureAuditEventWriter();
        var supportCase = await new SupportCaseService(dbContext, audit).CreateCaseAsync(
            companyId,
            userId,
            new CreateSupportCaseRequest("Product functionality", "What functions does Virtual Company support, and is it completely automated?", "Email", "customer@example.test"),
            CancellationToken.None);
        var context = new SupportKnowledgeContext(
            supportCase.Id,
            [new SupportKnowledgeSourceReference("knowledge_chunk", "product-catalog", Guid.NewGuid(),
                "Virtual Company supports finance, sales, support, and approvals with human review for important actions.", .9m, true, Guid.NewGuid(), "product-catalog:1")],
            [],
            [],
            .9m,
            "Product information found.");
        const string expected = "Hello Johan,\n\nVirtual Company supports finance, sales, customer support, and approval workflows. It uses AI to prepare and recommend work, but important decisions remain reviewable by people, so it is not completely automated.\n\nBest regards,\nBen\nVirtual Company Support";
        var reasoningGateway = new StubAgentReasoningGateway(expected);
        var service = new SupportReplyDraftService(
            dbContext,
            audit,
            new StubSupportOutboundEmailSender(),
            new StubSupportKnowledgeContextProvider(context),
            new SupportKnowledgeGapService(dbContext, audit),
            reasoningGateway: reasoningGateway);

        var draft = await service.GenerateDraftAsync(companyId, userId, supportCase.Id, new GenerateSupportReplyDraftRequest(), CancellationToken.None);

        Assert.Equal(expected, draft!.DraftBody);
        Assert.DoesNotContain("implementation requirements", draft.DraftBody, StringComparison.OrdinalIgnoreCase);
        Assert.False(reasoningGateway.LastRequest!.IncludeClaims);
    }

    [Fact]
    public async Task Sla_monitor_marks_overdue_case_as_breached()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        var createdUtc = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        dbContext.Companies.Add(new Company(companyId, "Support Company"));
        dbContext.SupportCases.Add(new SupportCase(Guid.NewGuid(), companyId, "SUP-20260501-0001", "Urgent issue", "Help now", "Email", createdUtc: createdUtc));
        await dbContext.SaveChangesAsync();
        var monitor = new SupportSlaMonitor(dbContext, NullLogger<SupportSlaMonitor>.Instance);

        var result = await monitor.RunAsync(createdUtc.AddDays(4), CancellationToken.None);
        var supportCase = await dbContext.SupportCases.SingleAsync();

        Assert.Equal(1, result.CasesScanned);
        Assert.Equal(1, result.BreachesCreated);
        Assert.True(supportCase.IsSlaBreached);
        Assert.Contains(await dbContext.SupportCaseEvents.ToListAsync(), x => x.EventType == SupportCaseEventTypes.SlaBreached);
    }

    [Fact]
    public async Task Support_tool_executes_refund_handoff_and_requires_agent_identity()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Support Company"));
        await dbContext.SaveChangesAsync();
        var audit = new CaptureAuditEventWriter();
        var caseService = new SupportCaseService(dbContext, audit);
        var supportCase = await caseService.CreateCaseAsync(companyId, userId, new CreateSupportCaseRequest("Refund request", "Please refund the duplicate charge.", "Manual"), CancellationToken.None);
        var draftService = new SupportReplyDraftService(dbContext, audit, new StubSupportOutboundEmailSender(), new SupportKnowledgeContextProvider(dbContext), new SupportKnowledgeGapService(dbContext, audit));
        var toolService = new SupportToolActionService(
            caseService,
            new SupportTriageService(dbContext, audit),
            draftService,
            new SupportRefundWorkflowService(dbContext, audit),
            new SupportKnowledgeGapService(dbContext, audit),
            audit);

        var denied = await toolService.ExecuteAsync(companyId, Guid.Empty, new SupportToolActionRequest("ClassifySupportCase", supportCase.Id, new Dictionary<string, string?>()), CancellationToken.None);
        var refund = await toolService.ExecuteAsync(
            companyId,
            Guid.NewGuid(),
            new SupportToolActionRequest(
                "RequestSupportRefund",
                supportCase.Id,
                new Dictionary<string, string?>
                {
                    ["amount"] = "125.50",
                    ["currency"] = "SEK",
                    ["explanation"] = "Duplicate charge confirmed by support."
                }),
            CancellationToken.None);

        Assert.False(denied.Succeeded);
        Assert.Equal("denied", denied.Status);
        Assert.True(refund.Succeeded);
        Assert.Equal(SupportCaseStatuses.AwaitingApproval, (await dbContext.SupportCases.SingleAsync(x => x.Id == supportCase.Id)).Status);
        Assert.Single(await dbContext.SupportRefundRequests.ToListAsync());
    }

    [Fact]
    public async Task Refund_approval_outcome_is_tenant_scoped_and_idempotent()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.AddRange(new Company(companyId, "Support Company"), new Company(otherCompanyId, "Other Company"));
        var customerId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(customerId, companyId, "Customer", "customer"));
        dbContext.FinanceInvoices.Add(new FinanceInvoice(
            invoiceId,
            companyId,
            customerId,
            "INV-SUPPORT-1",
            DateTime.UtcNow.AddDays(-10),
            DateTime.UtcNow.AddDays(-1),
            200m,
            "SEK",
            "paid",
            settlementStatus: FinanceSettlementStatuses.Paid,
            paidAmount: 200m));
        await dbContext.SaveChangesAsync();
        var audit = new CaptureAuditEventWriter();
        var cases = new SupportCaseService(dbContext, audit);
        var supportCase = await cases.CreateCaseAsync(
            companyId,
            userId,
            new CreateSupportCaseRequest("Refund request", "Please refund the duplicate charge.", "Manual"),
            CancellationToken.None);
        var workflow = new SupportRefundWorkflowService(dbContext, audit);
        var requested = await workflow.RequestRefundAsync(
            companyId,
            userId,
            supportCase.Id,
            new CreateSupportRefundRequest(125.50m, "SEK", "duplicate_charge", "Duplicate charge confirmed.", InvoiceId: invoiceId),
            CancellationToken.None);
        var finance = new SupportRefundFinanceService(dbContext, audit, TimeProvider.System);
        var handler = new SupportRefundApprovalOutcomeHandler(dbContext, audit, finance);

        var wrongTenant = await handler.ProcessAsync(otherCompanyId, requested!.ApprovalRequestId!.Value, "approved", userId, "Approved.", CancellationToken.None);
        var processed = await handler.ProcessAsync(companyId, requested.ApprovalRequestId.Value, "approved", userId, "Approved.", CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var duplicate = await handler.ProcessAsync(companyId, requested.ApprovalRequestId.Value, "approved", userId, "Approved.", CancellationToken.None);
        var persisted = await dbContext.SupportRefundRequests.SingleAsync(x => x.Id == requested.Id);
        var events = await dbContext.SupportCaseEvents.Where(x => x.SupportCaseId == supportCase.Id && x.EventType == SupportCaseEventTypes.ApprovalResolved).ToListAsync();

        Assert.False(wrongTenant);
        Assert.True(processed);
        Assert.False(duplicate);
        Assert.NotNull(persisted.FinanceActionReferenceId);
        Assert.Equal(SupportRefundRequestStatuses.Queued, persisted.Status);
        Assert.Single(events);
        Assert.Contains(audit.Events, x => x.Action == "support.refund.approval_resolved");
    }

    [Fact]
    public async Task Send_draft_uses_outbound_sender_and_persists_provider_message_ids()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Support Company"));
        AddIndexedLoginKnowledge(dbContext, companyId);
        await dbContext.SaveChangesAsync();
        var audit = new CaptureAuditEventWriter();
        var caseService = new SupportCaseService(dbContext, audit);
        var supportCase = await caseService.CreateCaseAsync(
            companyId,
            userId,
            new CreateSupportCaseRequest("Login help", "I cannot login.", "Email", "customer@example.test"),
            CancellationToken.None);
        var sender = new StubSupportOutboundEmailSender("provider-message-2", "provider-thread-2");
        var draftService = new SupportReplyDraftService(dbContext, audit, sender, new SupportKnowledgeContextProvider(dbContext), new SupportKnowledgeGapService(dbContext, audit));
        var draft = await draftService.GenerateDraftAsync(companyId, userId, supportCase.Id, new GenerateSupportReplyDraftRequest("Helpful"), CancellationToken.None);
        await draftService.ApproveDraftAsync(companyId, userId, draft!.Id, new SupportActionRequest(), CancellationToken.None);

        var sent = await draftService.SendDraftAsync(
            companyId,
            userId,
            draft.Id,
            new SendSupportReplyDraftRequest(
                ToEmail: "customer@example.test",
                Subject: "Re: Login help",
                OriginalMessageId: "provider-message-1",
                ProviderThreadId: "provider-thread-1"),
            CancellationToken.None);
        var outbound = await dbContext.SupportMessages.SingleAsync(x => x.SupportCaseId == supportCase.Id && x.Direction == SupportMessageDirections.Outbound);

        Assert.Equal(SupportCaseStatuses.WaitingForCustomer, sent!.Status);
        Assert.Equal("provider-message-2", outbound.ProviderMessageId);
        Assert.Equal("provider-thread-2", outbound.ProviderThreadId);
        Assert.Equal("customer@example.test", sender.LastRequest!.ToEmail);
    }

    [Fact]
    public async Task Send_draft_failure_persists_failure_summary_without_outbound_message()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateContext(connection, companyId, userId);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Support Company"));
        AddIndexedLoginKnowledge(dbContext, companyId);
        await dbContext.SaveChangesAsync();
        var audit = new CaptureAuditEventWriter();
        var caseService = new SupportCaseService(dbContext, audit);
        var supportCase = await caseService.CreateCaseAsync(
            companyId,
            userId,
            new CreateSupportCaseRequest("Login help", "I cannot login.", "Email", "customer@example.test"),
            CancellationToken.None);
        var draftService = new SupportReplyDraftService(dbContext, audit, new FailingSupportOutboundEmailSender(), new SupportKnowledgeContextProvider(dbContext), new SupportKnowledgeGapService(dbContext, audit));
        var draft = await draftService.GenerateDraftAsync(companyId, userId, supportCase.Id, new GenerateSupportReplyDraftRequest("Helpful"), CancellationToken.None);
        await draftService.ApproveDraftAsync(companyId, userId, draft!.Id, new SupportActionRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => draftService.SendDraftAsync(
            companyId,
            userId,
            draft.Id,
            new SendSupportReplyDraftRequest(
                ToEmail: "customer@example.test",
                Subject: "Re: Login help",
                OriginalMessageId: "provider-message-1",
                ProviderThreadId: "provider-thread-1"),
            CancellationToken.None));
        var failedDraft = await dbContext.SupportReplyDrafts.SingleAsync(x => x.Id == draft.Id);

        Assert.Equal("Support reply could not be sent through the connected mailbox.", failedDraft.SendFailureSummary);
        Assert.Empty(await dbContext.SupportMessages.Where(x => x.SupportCaseId == supportCase.Id && x.Direction == SupportMessageDirections.Outbound).ToListAsync());
        Assert.Contains(audit.Events, x => x.Action == "support.reply.send_failed");
    }

    private static void AddIndexedLoginKnowledge(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var documentId = Guid.NewGuid();
        var document = new CompanyKnowledgeDocument(
            documentId,
            companyId,
            "Login support policy",
            CompanyKnowledgeDocumentType.Policy,
            $"companies/{companyId:N}/knowledge/login-support.txt",
            null,
            "login-support.txt",
            "text/plain",
            ".txt",
            128,
            accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
        document.MarkScanClean();
        document.MarkProcessing();
        document.MarkProcessed();
        document.MarkIndexed("account access help customer recovery", 1, 1, "test-provider", "test", "v1", 3, "login-support-v1");

        dbContext.CompanyKnowledgeDocuments.Add(document);
        dbContext.CompanyKnowledgeChunks.Add(new CompanyKnowledgeChunk(
            Guid.NewGuid(),
            companyId,
            documentId,
            1,
            0,
            "For account access help, verify the account email and provide the secure account recovery steps.",
            "[1,0,0]",
            "test-provider",
            "test",
            "v1",
            3,
            sourceReference: "login-support:1"));
    }

    private static void AddIndexedKnowledge(VirtualCompanyDbContext dbContext, Guid companyId, string title, string content)
    {
        var documentId = Guid.NewGuid();
        var document = new CompanyKnowledgeDocument(
            documentId,
            companyId,
            title,
            CompanyKnowledgeDocumentType.Reference,
            $"companies/{companyId:N}/knowledge/{title}.md",
            null,
            $"{title}.md",
            "text/markdown",
            ".md",
            content.Length,
            accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
        document.MarkScanClean();
        document.MarkProcessing();
        document.MarkProcessed();
        document.MarkIndexed(content, 1, 1, "test-provider", "test", "v1", 3, $"{title}-v1");
        dbContext.CompanyKnowledgeDocuments.Add(document);
        dbContext.CompanyKnowledgeChunks.Add(new CompanyKnowledgeChunk(
            Guid.NewGuid(), companyId, documentId, 1, 0, content, "[1,0,0]", "test-provider", "test", "v1", 3,
            sourceReference: $"{title}:1"));
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection, Guid companyId, Guid userId) =>
        new(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options,
            new TestCompanyContextAccessor(companyId, userId));

    private sealed class CaptureAuditEventWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];

        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class StubSupportKnowledgeContextProvider(SupportKnowledgeContext context) : ISupportKnowledgeContextProvider
    {
        public Task<SupportKnowledgeContext> RetrieveAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken) =>
            Task.FromResult(context);
    }

    private sealed class StubAgentReasoningGateway(string summary) : IAgentReasoningGateway
    {
        public AgentReasoningRequest? LastRequest { get; private set; }

        public Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AgentReasoningResult(
                Guid.NewGuid(), AgentAiRunStatuses.Completed, request.SchemaVersion, summary, [], .9m, [], [], [],
                request.Sources.Select(x => x.Id).ToArray()));
        }

        public Task<AgentReasoningResult?> GetRunAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentReasoningResult?>(null);
    }

    private sealed class StubSupportOutboundEmailSender : ISupportOutboundEmailSender
    {
        private readonly string _providerMessageId;
        private readonly string? _providerThreadId;

        public StubSupportOutboundEmailSender(string providerMessageId = "sent-message", string? providerThreadId = "sent-thread")
        {
            _providerMessageId = providerMessageId;
            _providerThreadId = providerThreadId;
        }

        public SupportOutboundEmailSendRequest? LastRequest { get; private set; }

        public Task<SupportOutboundEmailSendResult> SendReplyAsync(SupportOutboundEmailSendRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request));

        private SupportOutboundEmailSendResult Send(SupportOutboundEmailSendRequest request)
        {
            LastRequest = request;
            return new SupportOutboundEmailSendResult("test", request.MailboxConnectionId ?? Guid.NewGuid(), _providerMessageId, _providerThreadId, "sent");
        }
    }

    private sealed class FailingSupportOutboundEmailSender : ISupportOutboundEmailSender
    {
        public Task<SupportOutboundEmailSendResult> SendReplyAsync(SupportOutboundEmailSendRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider rejected the reply.");
    }

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid companyId, Guid userId)
        {
            CompanyId = companyId;
            UserId = userId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IsResolved => CompanyId.HasValue && UserId.HasValue;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }

        public void SetCompanyId(Guid? companyId)
        {
            CompanyId = companyId;
        }

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}

