using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
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
    public async Task Send_draft_uses_outbound_sender_and_persists_provider_message_ids()
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

