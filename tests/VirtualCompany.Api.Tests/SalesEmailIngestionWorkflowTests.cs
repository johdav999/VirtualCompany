using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Api.Tests;

public sealed class SalesEmailIngestionWorkflowTests
{
    [Fact]
    public async Task Gmail_message_with_sales_intent_creates_lead_activity_links_and_events_once()
    {
        await using var fixture = await SalesEmailFixture.CreateAsync();
        fixture.Provider.AddMessage(new MailboxInboundMessage(
            "gmail-message-1",
            "gmail-thread-1",
            "<gmail-message-1@example.com>",
            "Pricing request",
            "Hi, can we get pricing for the enterprise plan this week?",
            null,
            new MailboxAddress("buyer@acme.com", "Buyer One"),
            [],
            DateTime.UtcNow,
            new Dictionary<string, string>()));

        var result = await fixture.Service.ProcessMessageAsync(
            new ProcessSalesEmailMessageCommand(fixture.CompanyId, fixture.UserId, fixture.ConnectionId, "gmail-message-1"),
            CancellationToken.None);
        var replay = await fixture.Service.ProcessMessageAsync(
            new ProcessSalesEmailMessageCommand(fixture.CompanyId, fixture.UserId, fixture.ConnectionId, "gmail-message-1"),
            CancellationToken.None);

        Assert.Equal(SalesEmailIngestionStatuses.Processed, result.Status);
        Assert.Equal(SalesEmailIngestionStatuses.AlreadyProcessed, replay.Status);
        Assert.True(replay.AlreadyProcessed);
        Assert.Equal(1, await fixture.DbContext.Leads.CountAsync(x => x.CompanyId == fixture.CompanyId));
        Assert.Equal(1, await fixture.DbContext.SalesActivities.CountAsync(x => x.CompanyId == fixture.CompanyId));
        Assert.Equal(2, await fixture.DbContext.SalesEmailLinks.CountAsync(x => x.CompanyId == fixture.CompanyId));
        Assert.Equal(2, fixture.Outbox.Messages.Count);
        Assert.Contains(fixture.Outbox.Messages, x => x.Topic == SalesEmailDomainEvents.EmailReceived);
        Assert.Contains(fixture.Outbox.Messages, x => x.Topic == SalesEmailDomainEvents.LeadDetected);
    }

    [Fact]
    public async Task Microsoft_thread_reuses_existing_thread_lead_without_duplicate_activity_or_events()
    {
        await using var fixture = await SalesEmailFixture.CreateAsync(MailboxProvider.Microsoft365);
        fixture.Provider.AddThread("conversation-1",
        [
            new MailboxInboundMessage(
                "microsoft-message-1",
                "conversation-1",
                "<microsoft-message-1@example.com>",
                "Demo request",
                "We are evaluating your service and would like to book a demo tomorrow.",
                null,
                new MailboxAddress("buyer@contoso.com", "Contoso Buyer"),
                [],
                DateTime.UtcNow.AddMinutes(-5),
                new Dictionary<string, string>())
        ]);

        var first = await fixture.Service.ProcessThreadAsync(
            new ProcessSalesEmailThreadCommand(fixture.CompanyId, fixture.UserId, fixture.ConnectionId, "conversation-1"),
            CancellationToken.None);
        var second = await fixture.Service.ProcessThreadAsync(
            new ProcessSalesEmailThreadCommand(fixture.CompanyId, fixture.UserId, fixture.ConnectionId, "conversation-1"),
            CancellationToken.None);

        Assert.Equal(SalesEmailIngestionStatuses.Processed, first.Status);
        Assert.Equal(SalesEmailIngestionStatuses.AlreadyProcessed, second.Status);
        Assert.Equal(1, await fixture.DbContext.Leads.CountAsync(x => x.CompanyId == fixture.CompanyId));
        Assert.Equal(1, await fixture.DbContext.SalesActivities.CountAsync(x => x.CompanyId == fixture.CompanyId));
        Assert.Equal(2, fixture.Outbox.Messages.Count);
    }

    [Theory]
    [InlineData("newsletter-message", "Weekly newsletter", "View in browser. Unsubscribe from this newsletter.", SalesEmailIgnoreReasons.Newsletter)]
    [InlineData("receipt-message", "Receipt", "Your purchase receipt and order confirmation.", SalesEmailIgnoreReasons.Receipt)]
    [InlineData("invoice-message", "Invoice due", "Invoice amount due and payment due Friday.", SalesEmailIgnoreReasons.Invoice)]
    [InlineData("support-message", "Support ticket", "Support ticket case number. The app is not working.", SalesEmailIgnoreReasons.SupportTicket)]
    public async Task Ignored_messages_persist_auditable_reason(string messageId, string subject, string body, string expectedReason)
    {
        await using var fixture = await SalesEmailFixture.CreateAsync();
        fixture.Provider.AddMessage(new MailboxInboundMessage(
            messageId,
            $"thread-{messageId}",
            $"<{messageId}@example.com>",
            subject,
            body,
            null,
            new MailboxAddress("sender@example.com", "Sender"),
            [],
            DateTime.UtcNow,
            new Dictionary<string, string>()));

        var result = await fixture.Service.ProcessMessageAsync(
            new ProcessSalesEmailMessageCommand(fixture.CompanyId, fixture.UserId, fixture.ConnectionId, messageId),
            CancellationToken.None);

        Assert.Equal(SalesEmailIngestionStatuses.Ignored, result.Status);
        Assert.Equal(expectedReason, result.IgnoreReason);
        var link = await fixture.DbContext.SalesEmailLinks.SingleAsync(x => x.CompanyId == fixture.CompanyId && x.ExternalMessageId == messageId);
        Assert.Equal(SalesStatuses.Ignored, link.Status);
        Assert.Equal(expectedReason, link.IgnoreReason);
        Assert.False(await fixture.DbContext.Leads.AnyAsync(x => x.CompanyId == fixture.CompanyId));
    }

    [Fact]
    public async Task Message_then_thread_processing_for_same_conversation_keeps_one_lead()
    {
        await using var fixture = await SalesEmailFixture.CreateAsync();
        var message = new MailboxInboundMessage(
            "message-thread-shared",
            "shared-thread",
            "<shared@example.com>",
            "Quote request",
            "Please send a quote for your automation service.",
            null,
            new MailboxAddress("buyer@sharedco.com", "Shared Buyer"),
            [],
            DateTime.UtcNow,
            new Dictionary<string, string>());
        fixture.Provider.AddMessage(message);
        fixture.Provider.AddThread("shared-thread", [message]);

        await fixture.Service.ProcessMessageAsync(
            new ProcessSalesEmailMessageCommand(fixture.CompanyId, fixture.UserId, fixture.ConnectionId, "message-thread-shared"),
            CancellationToken.None);
        var threadResult = await fixture.Service.ProcessThreadAsync(
            new ProcessSalesEmailThreadCommand(fixture.CompanyId, fixture.UserId, fixture.ConnectionId, "shared-thread"),
            CancellationToken.None);

        Assert.Equal(SalesEmailIngestionStatuses.AlreadyProcessed, threadResult.Status);
        Assert.Equal(1, await fixture.DbContext.Leads.CountAsync(x => x.CompanyId == fixture.CompanyId));
        Assert.Equal(1, await fixture.DbContext.SalesActivities.CountAsync(x => x.CompanyId == fixture.CompanyId));
        Assert.Equal(2, fixture.Outbox.Messages.Count);
    }

    private sealed class SalesEmailFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SalesEmailFixture(
            SqliteConnection connection,
            VirtualCompanyDbContext dbContext,
            SalesEmailIngestionService service,
            FakeMailboxProviderRegistry provider,
            CapturingOutbox outbox,
            Guid companyId,
            Guid userId,
            Guid connectionId)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = service;
            Provider = provider;
            Outbox = outbox;
            CompanyId = companyId;
            UserId = userId;
            ConnectionId = connectionId;
        }

        public VirtualCompanyDbContext DbContext { get; }
        public SalesEmailIngestionService Service { get; }
        public FakeMailboxProviderRegistry Provider { get; }
        public CapturingOutbox Outbox { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid ConnectionId { get; }

        public static async Task<SalesEmailFixture> CreateAsync(MailboxProvider provider = MailboxProvider.Gmail)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
            var dbContext = new VirtualCompanyDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var companyId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var mailboxConnectionId = Guid.NewGuid();
            dbContext.Companies.Add(new Company(companyId, "Sales Email Company"));
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev", "founder"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.SalesPipelineStages.Add(new SalesPipelineStage(SalesPipelineStage.NewStageId, SalesPipelineStage.SystemCompanyId, "New", 1, isSystem: true));
            var mailboxConnection = new MailboxConnection(mailboxConnectionId, companyId, userId, provider, "sales@example.com");
            mailboxConnection.StoreEncryptedCredentials("access-token", "refresh-token", DateTime.UtcNow.AddHours(1), ["Mail.Read"]);
            mailboxConnection.MarkActive();
            dbContext.MailboxConnections.Add(mailboxConnection);
            await dbContext.SaveChangesAsync();

            var registry = new FakeMailboxProviderRegistry(provider);
            var outbox = new CapturingOutbox();
            var service = new SalesEmailIngestionService(dbContext, registry, new PlaintextFieldEncryption(), outbox, new NullIntentExtractionService(), TimeProvider.System);
            return new SalesEmailFixture(connection, dbContext, service, registry, outbox, companyId, userId, mailboxConnectionId);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class CapturingOutbox : ICompanyOutboxEnqueuer
    {
        public List<(Guid CompanyId, string Topic, string? IdempotencyKey)> Messages { get; } = [];

        public void Enqueue(Guid companyId, string topic, object payload, string? correlationId = null, DateTime? availableAtUtc = null, string? idempotencyKey = null, string? messageType = null, string? causationId = null, IReadOnlyDictionary<string, string?>? headers = null) =>
            Messages.Add((companyId, topic, idempotencyKey));
    }

    private sealed class NullIntentExtractionService : ISalesEmailIntentExtractionService
    {
        public Task<SalesEmailIntentExtractionResult?> ExtractAsync(SalesEmailIntentExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<SalesEmailIntentExtractionResult?>(null);
    }

    private sealed class PlaintextFieldEncryption : IFieldEncryptionService
    {
        public string Encrypt(Guid companyId, string purpose, string plaintext) => plaintext;
        public string Decrypt(Guid companyId, string purpose, string ciphertext) => ciphertext;
    }

    private sealed class FakeMailboxProviderRegistry : IMailboxProviderRegistry
    {
        private readonly FakeMailboxProviderClient _client;

        public FakeMailboxProviderRegistry(MailboxProvider provider)
        {
            _client = new FakeMailboxProviderClient(provider);
        }

        public IMailboxProviderClient Resolve(MailboxProvider provider) =>
            provider == _client.Provider ? _client : throw new InvalidOperationException("Provider is not registered.");

        public void AddMessage(MailboxInboundMessage message) => _client.Messages[message.ProviderMessageId] = message;
        public void AddThread(string threadId, IReadOnlyList<MailboxInboundMessage> messages) => _client.Threads[threadId] = messages;
    }

    private sealed class FakeMailboxProviderClient : IMailboxProviderClient
    {
        public FakeMailboxProviderClient(MailboxProvider provider) => Provider = provider;

        public MailboxProvider Provider { get; }
        public Dictionary<string, MailboxInboundMessage> Messages { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<MailboxInboundMessage>> Threads { get; } = new(StringComparer.Ordinal);

        public Task<MailboxAuthorizationRequest> BuildAuthorizationRequestAsync(MailboxAuthorizationStartRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailboxOAuthTokenResult> ExchangeCodeAsync(MailboxCodeExchangeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailboxOAuthTokenResult> RefreshTokenAsync(MailboxRefreshTokenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailboxAccountProfile> GetAccountProfileAsync(string accessToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(string accessToken, MailboxMessageQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailboxInboundMessage> GetMessageAsync(string accessToken, MailboxMessageFetchRequest request, CancellationToken cancellationToken) => Task.FromResult(Messages[request.MessageId]);
        public Task<MailboxInboundThread> GetThreadAsync(string accessToken, MailboxThreadFetchRequest request, CancellationToken cancellationToken) => Task.FromResult(new MailboxInboundThread(request.ThreadId, Threads[request.ThreadId]));
    }
}
