using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingSchedulingServiceTests
{
    [Fact]
    public async Task Preparing_invitation_creates_owner_approval_without_calling_calendar_provider()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.CreateForLeadAsync(
            fixture.CompanyId,
            fixture.UserId,
            fixture.LeadId,
            fixture.Request(),
            CancellationToken.None);

        Assert.Equal("waiting_for_approval", result.Status);
        Assert.NotNull(result.ApprovalRequestId);
        Assert.Equal("owner", fixture.Approvals.LastCommand!.RequiredRole);
        Assert.Equal(SalesMeetingApprovalTypes.SendInvitation, fixture.Approvals.LastCommand.ApprovalType);
        Assert.Equal("sales_meeting_invitation", fixture.Approvals.LastCommand.TargetEntityType);
        Assert.Equal(0, fixture.Provider.CreateCalls);
        Assert.Equal(1, await fixture.Db.SalesMeetingInvitations.CountAsync());
        Assert.Contains(await fixture.Db.SalesActivities.ToListAsync(), x => x.LeadId == fixture.LeadId);
    }

    [Fact]
    public async Task Connection_from_another_company_cannot_be_used()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<SalesValidationException>(() =>
            fixture.Service.CreateForLeadAsync(
                fixture.CompanyId,
                fixture.UserId,
                fixture.LeadId,
                fixture.Request(Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains(nameof(CreateSalesMeetingInvitationRequest.CalendarConnectionId), exception.Errors.Keys);
        Assert.Equal(0, fixture.Provider.CreateCalls);
        Assert.Empty(await fixture.Db.SalesMeetingInvitations.ToListAsync());
    }

    [Fact]
    public async Task Approval_creation_failure_removes_unsendable_draft()
    {
        await using var fixture = await Fixture.CreateAsync(approvalFailure: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CreateForLeadAsync(
                fixture.CompanyId,
                fixture.UserId,
                fixture.LeadId,
                fixture.Request(),
                CancellationToken.None));

        Assert.Empty(await fixture.Db.SalesMeetingInvitations.ToListAsync());
        Assert.Equal(0, fixture.Provider.CreateCalls);
    }

    [Fact]
    public async Task Approved_delivery_is_idempotent_and_records_audit_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var invitation = await fixture.CreateApprovedInvitationAsync();
        var outbox = new CapturingOutbox();
        var dispatcher = new SalesMeetingInvitationDeliveryDispatcher(
            fixture.Db,
            new StaticCalendarTokenLeaseService(),
            new CalendarProviderRegistry([fixture.Provider]),
            outbox);
        var message = new SalesMeetingInvitationDeliveryRequestedMessage(
            fixture.CompanyId,
            invitation.Id,
            invitation.IdempotencyKey,
            invitation.ApprovalRequestId?.ToString("N"));

        await dispatcher.DispatchAsync(message, CancellationToken.None);
        await dispatcher.DispatchAsync(message, CancellationToken.None);

        Assert.Equal(1, fixture.Provider.CreateCalls);
        var stored = await fixture.Db.SalesMeetingInvitations.SingleAsync(x => x.Id == invitation.Id);
        Assert.Equal(SalesMeetingInvitationStatus.Scheduled, stored.Status);
        Assert.Equal("event", stored.ExternalEventId);
        Assert.Equal(SalesMeetingConfirmationStatus.Queued, stored.ConfirmationStatus);
        var confirmation = Assert.Single(outbox.Messages);
        Assert.Equal(CompanyOutboxTopics.SalesMeetingConfirmationDeliveryRequested, confirmation.Topic);
        Assert.Equal(stored.ConfirmationIdempotencyKey, confirmation.IdempotencyKey);
        var audit = Assert.Single(await fixture.Db.AuditEvents
            .Where(x => x.TargetType == "sales_meeting_invitation" && x.TargetId == invitation.Id.ToString("D"))
            .ToListAsync());
        Assert.Equal("sales.meeting_invitation.sent", audit.Action);
        Assert.Equal("succeeded", audit.Outcome);
    }
    [Fact]
    public async Task Scheduled_meeting_confirmation_replies_in_the_source_thread_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        var invitation = await fixture.CreateApprovedInvitationAsync();
        var outbox = new CapturingOutbox();
        var calendarDispatcher = new SalesMeetingInvitationDeliveryDispatcher(
            fixture.Db, new StaticCalendarTokenLeaseService(),
            new CalendarProviderRegistry([fixture.Provider]), outbox);
        var calendarMessage = new SalesMeetingInvitationDeliveryRequestedMessage(
            fixture.CompanyId, invitation.Id, invitation.IdempotencyKey, "meeting-test");
        await calendarDispatcher.DispatchAsync(calendarMessage, CancellationToken.None);
        await calendarDispatcher.DispatchAsync(calendarMessage, CancellationToken.None);
        Assert.Equal(1, fixture.Provider.CreateCalls);
        fixture.Db.SalesEmailLinks.Add(new SalesEmailLink(
            Guid.NewGuid(), fixture.CompanyId, "source-message",
            leadId: fixture.LeadId, contactId: invitation.ContactId,
            status: SalesStatuses.Linked, provider: "standard_email",
            mailboxConnectionId: fixture.MailboxConnectionId,
            externalThreadId: "source-thread", internetMessageId: "<source@example.com>",
            linkKind: SalesEmailLinkKinds.Message));
        await fixture.Db.SaveChangesAsync();

        var mailboxProvider = new RecordingMailboxProvider();
        var dispatcher = new SalesMeetingConfirmationDeliveryDispatcher(
            fixture.Db, new StaticMailboxTokenLeaseService(),
            new MailboxProviderRegistry([mailboxProvider]));
        var message = new SalesMeetingConfirmationDeliveryRequestedMessage(
            fixture.CompanyId, invitation.Id, invitation.ConfirmationIdempotencyKey, "meeting-test");

        await dispatcher.DispatchAsync(message, CancellationToken.None);
        await dispatcher.DispatchAsync(message, CancellationToken.None);

        Assert.Equal(1, mailboxProvider.SendCalls);
        Assert.Equal("source-message", mailboxProvider.LastRequest!.OriginalMessageId);
        Assert.Equal("source-thread", mailboxProvider.LastRequest.ProviderThreadId);
        Assert.Contains("calendar invitation has also been sent", mailboxProvider.LastRequest.BodyText);
        var stored = await fixture.Db.SalesMeetingInvitations.SingleAsync(x => x.Id == invitation.Id);
        Assert.Equal(SalesMeetingInvitationStatus.Scheduled, stored.Status);
        Assert.Equal(SalesMeetingConfirmationStatus.Sent, stored.ConfirmationStatus);
        Assert.Equal("confirmation-message", stored.ConfirmationProviderMessageId);
        Assert.Equal(MailboxReplyThreadingMode.HeaderBased, stored.ConfirmationThreadingMode);
        Assert.Contains(await fixture.Db.AuditEvents.ToListAsync(),
            x => x.Action == "sales.meeting_confirmation.sent" &&
                 x.TargetId == invitation.Id.ToString("D"));
    }

    [Fact]
    public async Task Missing_source_thread_is_visible_without_changing_scheduled_meeting()
    {
        await using var fixture = await Fixture.CreateAsync();
        var invitation = await fixture.CreateApprovedInvitationAsync();
        var outbox = new CapturingOutbox();
        var calendarDispatcher = new SalesMeetingInvitationDeliveryDispatcher(
            fixture.Db, new StaticCalendarTokenLeaseService(),
            new CalendarProviderRegistry([fixture.Provider]), outbox);
        await calendarDispatcher.DispatchAsync(
            new SalesMeetingInvitationDeliveryRequestedMessage(
                fixture.CompanyId, invitation.Id, invitation.IdempotencyKey, "meeting-test"),
            CancellationToken.None);
        var mailboxProvider = new RecordingMailboxProvider();
        var dispatcher = new SalesMeetingConfirmationDeliveryDispatcher(
            fixture.Db, new StaticMailboxTokenLeaseService(),
            new MailboxProviderRegistry([mailboxProvider]));

        await dispatcher.DispatchAsync(
            new SalesMeetingConfirmationDeliveryRequestedMessage(
                fixture.CompanyId, invitation.Id, invitation.ConfirmationIdempotencyKey, "meeting-test"),
            CancellationToken.None);

        var stored = await fixture.Db.SalesMeetingInvitations.SingleAsync(x => x.Id == invitation.Id);
        Assert.Equal(SalesMeetingInvitationStatus.Scheduled, stored.Status);
        Assert.Equal(SalesMeetingConfirmationStatus.Unavailable, stored.ConfirmationStatus);
        Assert.Equal(0, mailboxProvider.SendCalls);
    }
    [Fact]
    public async Task Confirmation_requires_thread_correlation_capability()
    {
        await using var fixture = await Fixture.CreateAsync();
        var invitation = await fixture.CreateApprovedInvitationAsync();
        var outbox = new CapturingOutbox();
        var calendarDispatcher = new SalesMeetingInvitationDeliveryDispatcher(
            fixture.Db, new StaticCalendarTokenLeaseService(),
            new CalendarProviderRegistry([fixture.Provider]), outbox);
        await calendarDispatcher.DispatchAsync(
            new SalesMeetingInvitationDeliveryRequestedMessage(
                fixture.CompanyId, invitation.Id, invitation.IdempotencyKey, "thread-capability"),
            CancellationToken.None);

        var mailbox = await fixture.Db.MailboxConnections
            .SingleAsync(x => x.Id == fixture.MailboxConnectionId);
        mailbox.SetCapabilities(MailboxCapability.SendMessages);
        fixture.Db.SalesEmailLinks.Add(new SalesEmailLink(
            Guid.NewGuid(), fixture.CompanyId, "source-without-thread-capability",
            leadId: fixture.LeadId, contactId: invitation.ContactId,
            status: SalesStatuses.Linked, provider: "standard_email",
            mailboxConnectionId: fixture.MailboxConnectionId,
            externalThreadId: "source-thread", internetMessageId: "<source@example.com>",
            linkKind: SalesEmailLinkKinds.Message));
        await fixture.Db.SaveChangesAsync();

        var mailboxProvider = new RecordingMailboxProvider();
        var dispatcher = new SalesMeetingConfirmationDeliveryDispatcher(
            fixture.Db, new StaticMailboxTokenLeaseService(),
            new MailboxProviderRegistry([mailboxProvider]));
        await dispatcher.DispatchAsync(
            new SalesMeetingConfirmationDeliveryRequestedMessage(
                fixture.CompanyId, invitation.Id,
                invitation.ConfirmationIdempotencyKey, "thread-capability"),
            CancellationToken.None);

        var stored = await fixture.Db.SalesMeetingInvitations.SingleAsync(x => x.Id == invitation.Id);
        Assert.Equal(SalesMeetingConfirmationStatus.Unavailable, stored.ConfirmationStatus);
        Assert.Equal(0, mailboxProvider.SendCalls);
    }
    [Fact]
    public void Suggested_slots_exclude_busy_periods_and_stay_within_business_hours()
    {
        var fromUtc = new DateTime(2027, 1, 4, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2027, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var busy = new[]
        {
            new CalendarBusyWindow(
                new DateTime(2027, 1, 4, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 4, 10, 0, 0, DateTimeKind.Utc))
        };

        var slots = SalesMeetingSchedulingService.BuildSuggestedSlots(
            fromUtc, toUtc, 30, TimeZoneInfo.Utc, busy);

        Assert.NotEmpty(slots);
        Assert.Equal(new DateTime(2027, 1, 4, 10, 0, 0, DateTimeKind.Utc), slots[0].StartsUtc);
        Assert.All(slots, slot =>
        {
            Assert.True(slot.StartsUtc.Hour >= 9);
            Assert.True(slot.EndsUtc.Hour <= 17);
            Assert.DoesNotContain(busy, window => window.StartsUtc < slot.EndsUtc && window.EndsUtc > slot.StartsUtc);
        });
    }

    [Fact]
    public void Suggested_slots_skip_weekends()
    {
        var slots = SalesMeetingSchedulingService.BuildSuggestedSlots(
            new DateTime(2027, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            45, TimeZoneInfo.Utc, []);

        Assert.NotEmpty(slots);
        Assert.All(slots, slot => Assert.Equal(DayOfWeek.Monday, slot.StartsUtc.DayOfWeek));
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            VirtualCompanyDbContext db,
            SalesMeetingSchedulingService service,
            CapturingApprovalService approvals,
            RecordingCalendarProvider provider,
            Guid companyId,
            Guid userId,
            Guid leadId,
            Guid calendarConnectionId,
            Guid mailboxConnectionId)
        {
            _connection = connection;
            Db = db;
            Service = service;
            Approvals = approvals;
            Provider = provider;
            CompanyId = companyId;
            UserId = userId;
            LeadId = leadId;
            CalendarConnectionId = calendarConnectionId;
            MailboxConnectionId = mailboxConnectionId;
        }

        public VirtualCompanyDbContext Db { get; }
        public SalesMeetingSchedulingService Service { get; }
        public CapturingApprovalService Approvals { get; }
        public RecordingCalendarProvider Provider { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid LeadId { get; }
        public Guid CalendarConnectionId { get; }
        public Guid MailboxConnectionId { get; }

        public CreateSalesMeetingInvitationRequest Request(Guid? connectionId = null) =>
            new(
                connectionId ?? CalendarConnectionId,
                DateTime.UtcNow.AddDays(2),
                DateTime.UtcNow.AddDays(2).AddMinutes(30),
                "Europe/Stockholm",
                "Virtual Company demo",
                "Product overview and next steps.",
                null,
                true);

        public async Task<SalesMeetingInvitation> CreateApprovedInvitationAsync()
        {
            var approvalId = Guid.NewGuid();
            var contactId = await Db.Contacts.Select(x => (Guid?)x.Id).SingleAsync();
            var invitation = new SalesMeetingInvitation(
                Guid.NewGuid(), CompanyId, LeadId, null, contactId,
                CalendarConnectionId, ExternalAccountProvider.Google, "sales@example.com",
                "customer@example.com", "Customer", "Virtual Company demo",
                "Product overview and next steps.", DateTime.UtcNow.AddDays(2),
                DateTime.UtcNow.AddDays(2).AddMinutes(30), "Europe/Stockholm",
                null, true, UserId);
            invitation.SubmitForApproval(approvalId);
            invitation.MarkApproved(UserId, DateTime.UtcNow);
            var approval = ApprovalRequest.CreateForTarget(
                approvalId, CompanyId, ApprovalTargetEntityType.SalesMeetingInvitation,
                invitation.Id, "user", UserId, SalesMeetingApprovalTypes.SendInvitation,
                new Dictionary<string, JsonNode?> { ["title"] = JsonValue.Create(invitation.Title) },
                "owner", null, []);
            approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, UserId, null);
            Db.SalesMeetingInvitations.Add(invitation);
            Db.ApprovalRequests.Add(approval);
            await Db.SaveChangesAsync();
            return invitation;
        }
        public static async Task<Fixture> CreateAsync(bool approvalFailure = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options;
            var companyContext = new TestCompanyContextAccessor(companyId, userId);
            var db = new VirtualCompanyDbContext(options, companyContext);
            await db.Database.EnsureCreatedAsync();

            var contactId = Guid.NewGuid();
            var leadId = Guid.NewGuid();
            var stageId = Guid.NewGuid();
            var externalAccountConnectionId = Guid.NewGuid();
            var calendarConnectionId = externalAccountConnectionId;
            var mailboxConnectionId = Guid.NewGuid();
            db.Companies.Add(new Company(companyId, "Scheduling Company"));
            db.Users.Add(new User(userId, "owner@example.com", "Owner", "test", userId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(), companyId, userId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.SalesPipelineStages.Add(new SalesPipelineStage(stageId, companyId, "Qualified", 1));
            db.Contacts.Add(new Contact(contactId, companyId, "Customer", "customer@example.com"));
            db.Leads.Add(new Lead(
                leadId, companyId, "Interested customer", stageId,
                SalesStatuses.Qualified, primaryContactId: contactId));
            var externalAccount = new ExternalAccountConnection(
                externalAccountConnectionId, companyId, userId,
                ExternalAccountProvider.Google, "sales@example.com", "Sales",
                "google-account", "external-account:test");
            externalAccount.StoreEncryptedCredentials(
                "encrypted-calendar-access", "encrypted-calendar-refresh",
                DateTime.UtcNow.AddHours(1),
                CalendarOAuthScopes.For(ExternalAccountProvider.Google));
            externalAccount.SetStatus(ExternalConnectionStatus.Active);
            var calendar = new CalendarConnection(
                calendarConnectionId, companyId, userId, externalAccountConnectionId,
                ExternalAccountProvider.Google, "sales@example.com", "Sales");
            calendar.SetStatus(ExternalConnectionStatus.Active);
            db.ExternalAccountConnections.Add(externalAccount);
            db.CalendarConnections.Add(calendar);
            var mailbox = new MailboxConnection(
                mailboxConnectionId, companyId, userId,
                MailboxProvider.StandardEmail, "sales@example.com",
                purpose: MailboxPurpose.Sales);
            mailbox.StoreEncryptedCredentials(
                "encrypted-standard-session", null, null, []);
            mailbox.SetCapabilities(
                MailboxCapability.ReadMessages |
                MailboxCapability.ThreadCorrelation |
                MailboxCapability.SendMessages);
            mailbox.SetStatus(MailboxConnectionStatus.Active);
            db.MailboxConnections.Add(mailbox);
            await db.SaveChangesAsync();

            var provider = new RecordingCalendarProvider();
            var approvals = new CapturingApprovalService(approvalFailure);
            var tokenLease = new StaticCalendarTokenLeaseService();
            var service = new SalesMeetingSchedulingService(
                db,
                approvals,
                tokenLease,
                new CalendarProviderRegistry([provider]));
            return new Fixture(
                connection, db, service, approvals, provider,
                companyId, userId, leadId, calendarConnectionId, mailboxConnectionId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class CapturingApprovalService(bool fail) : IApprovalRequestService
    {
        public CreateApprovalRequestCommand? LastCommand { get; private set; }

        public Task<ApprovalRequestDto> CreateAsync(
            Guid companyId,
            CreateApprovalRequestCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            if (fail) throw new InvalidOperationException("Approval service unavailable.");
            var approval = new ApprovalRequestDto(
                Guid.NewGuid(), companyId, command.TargetEntityType, command.TargetEntityId,
                command.RequestedByActorType, command.RequestedByActorId, command.ApprovalType,
                command.RequiredRole, command.RequiredUserId, "pending", command.ThresholdContext ?? [],
                [], null, null, null, "", "", [], null, DateTime.UtcNow);
            return Task.FromResult(approval);
        }

        public Task<IReadOnlyList<ApprovalRequestDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ApprovalRequestDto> GetAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ApprovalDecisionResultDto> DecideAsync(Guid companyId, ApprovalDecisionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingMailboxProvider : IMailboxProviderClient
    {
        public MailboxProvider Provider => MailboxProvider.StandardEmail;
        public MailboxReplyThreadingMode ReplyThreadingMode => MailboxReplyThreadingMode.HeaderBased;
        public IReadOnlyCollection<string> DefaultScopes { get; } = [];
        public int SendCalls { get; private set; }
        public MailboxReplyExecutionRequest? LastRequest { get; private set; }

        public Uri BuildAuthorizationUrl(MailboxAuthorizationRequest request) => throw new NotSupportedException();
        public Task<MailboxOAuthTokenResult> ExchangeCodeAsync(MailboxTokenExchangeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailboxOAuthTokenResult> RefreshTokenAsync(MailboxRefreshTokenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailboxAccountProfile> GetAccountProfileAsync(string accessToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(string accessToken, MailboxMessageQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MailboxInboundMessage> GetMessageAsync(
            string accessToken, MailboxMessageFetchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MailboxInboundMessage(
                request.MessageId, "source-thread", "<source@example.com>",
                "Interested in a demo", "Can we schedule a demo?", null,
                new MailboxAddress("customer@example.com", "Customer"),
                [new MailboxAddress("sales@example.com", "Sales")],
                DateTime.UtcNow.AddDays(-1), new Dictionary<string, string>()));

        public Task<MailboxReplyExecutionResult> SendReplyAsync(
            string accessToken, MailboxReplyExecutionRequest request,
            CancellationToken cancellationToken)
        {
            SendCalls++;
            LastRequest = request;
            return Task.FromResult(new MailboxReplyExecutionResult(
                "confirmation-message", null, request.ProviderThreadId, "sent"));
        }
    }
    private sealed class CapturingOutbox : ICompanyOutboxEnqueuer
    {
        public List<(string Topic, string? IdempotencyKey)> Messages { get; } = [];

        public void Enqueue(
            Guid companyId, string topic, object payload,
            string? correlationId = null, DateTime? availableAtUtc = null,
            string? idempotencyKey = null, string? messageType = null,
            string? causationId = null,
            IReadOnlyDictionary<string, string?>? headers = null) =>
            Messages.Add((topic, idempotencyKey));
    }
    private sealed class StaticCalendarTokenLeaseService : ICalendarOAuthAccessTokenLeaseService
    {
        public Task<CalendarOAuthAccessTokenLease> AcquireAsync(
            Guid companyId,
            Guid calendarConnectionId,
            IReadOnlyCollection<string> requiredScopes,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CalendarOAuthAccessTokenLease(
                calendarConnectionId, calendarConnectionId, companyId,
                ExternalAccountProvider.Google, "sales@example.com", "access-token",
                DateTime.UtcNow.AddHours(1), requiredScopes, "primary"));
    }
    private sealed class StaticMailboxTokenLeaseService : IMailboxOAuthAccessTokenLeaseService
    {
        public Task<MailboxOAuthAccessTokenLease> AcquireAsync(
            Guid companyId,
            Guid connectionId,
            IReadOnlyCollection<string> requiredScopes,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MailboxOAuthAccessTokenLease(
                connectionId, companyId, MailboxProvider.StandardEmail,
                "sales@example.com", "access-token", DateTime.UtcNow.AddHours(1), requiredScopes));
    }

    private sealed class RecordingCalendarProvider : ICalendarProviderClient
    {
        public int CreateCalls { get; private set; }
        public ExternalAccountProvider Provider => ExternalAccountProvider.Google;
        public IReadOnlyCollection<string> RequiredScopes { get; } =
        [
            "https://www.googleapis.com/auth/calendar.events",
            "https://www.googleapis.com/auth/calendar.events.freebusy"
        ];

        public Task<IReadOnlyList<CalendarBusyWindow>> GetBusyWindowsAsync(
            CalendarProviderContext context,
            DateTime fromUtc,
            DateTime toUtc,
            string timeZoneId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarBusyWindow>>([]);

        public Task<CalendarMeetingCreateResult> CreateMeetingAsync(
            CalendarProviderContext context,
            CalendarMeetingCreateRequest request,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(new CalendarMeetingCreateResult("event", null, null, null));
        }

        public Task<CalendarMeetingCreateResult> UpdateMeetingAsync(
            CalendarProviderContext context,
            CalendarMeetingUpdateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CalendarMeetingCreateResult(request.ExternalEventId, null, null, null));

        public Task CancelMeetingAsync(
            CalendarProviderContext context,
            string externalEventId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid companyId, Guid userId)
        {
            CompanyId = companyId;
            UserId = userId;
            Membership = new ResolvedCompanyMembershipContext(
                Guid.NewGuid(), companyId, userId, "Scheduling Company",
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active);
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IsResolved => Membership is not null;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }

        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}
