using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;
public sealed class SalesBrowserRoomLifecycleTests
{
    [Fact]
    public async Task Delayed_expiry_after_end_does_not_repeat_participant_revocation()
    {
        await using var f=await RoomFixture.Create();await f.Ready();
        await f.Service.HostTokenAsync(f.Company,f.Actor,f.Room,default);
        await f.Service.EndAsync(f.Company,f.Actor,f.Room,new(Guid.NewGuid(),(await f.View()).Version),default);
        await f.Dispatch("end");var removals=f.Media.Removes;
        f.Clock.Now=f.Clock.Now.AddHours(2);await f.Dispatch("expire");
        Assert.Equal("ended",(await f.View()).State);Assert.Equal(removals,f.Media.Removes);
    }
    [Fact]
    public async Task Exhausted_provider_work_requires_an_authorized_idempotent_retry()
    {
        await using var f = await RoomFixture.Create(); await f.CreateRoom();
        var failed = await f.Db.SalesRoomOperations.IgnoreQueryFilters().SingleAsync(x => x.Action == "provision");
        for (var i = 0; i < 5; i++) { failed.Claim(f.Clock.Now); failed.Retry("provider_unavailable"); }
        await f.Db.SaveChangesAsync();
        var command = new SalesRoomCommand(Guid.NewGuid(), (await f.View()).Version);
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.RetryAsync(f.Company, f.OtherActor, f.Room, failed.Id, command, default));
        await f.Service.RetryAsync(f.Company, f.Actor, f.Room, failed.Id, command, default);
        await f.Service.RetryAsync(f.Company, f.Actor, f.Room, failed.Id, command, default);
        Assert.Equal(2, await f.Db.SalesRoomOperations.IgnoreQueryFilters().CountAsync(x => x.Action == "provision"));
        var replacement = await f.Db.SalesRoomOperations.IgnoreQueryFilters().SingleAsync(x => x.Action == "provision" && x.State == "queued");
        await f.Worker.DispatchAsync(new(f.Company, f.Room, replacement.Id), default);
        Assert.Equal("lobby", (await f.View()).State);
    }
    [Fact]
    public async Task Creation_is_durable_idempotent_and_company_scoped()
    {
        await using var f = await RoomFixture.Create(); var request = new CreateSalesBrowserRoom(Guid.NewGuid(), f.Clock.Now.AddHours(1));
        var first = await f.Service.CreateAsync(f.Company, f.Actor, f.Meeting, request, default);
        var second = await f.Service.CreateAsync(f.Company, f.Actor, f.Meeting, request, default);
        f.Room = first.Id; Assert.Equal(first.Id, second.Id); Assert.Equal("provisioning", second.State); Assert.Equal(0, f.Media.Creates);
        Assert.Equal(2, await f.Db.SalesRoomOperations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await f.Db.CompanyOutboxMessages.IgnoreQueryFilters().CountAsync());
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.GetAsync(Guid.NewGuid(), f.Actor, first.Id, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.GetAsync(f.Company, f.OtherActor, first.Id, default))).Status);
        await f.Dispatch("provision"); Assert.Equal("lobby", (await f.View()).State);
    }
    [Fact]
    public async Task One_use_guest_invitation_has_no_media_until_admission_and_no_stored_secret()
    {
        await using var f = await RoomFixture.Create(); await f.Ready();
        var invite = await f.Invite(); var guest = await f.Service.RedeemAsync(new(invite.Secret, "Customer"), default);
        Assert.Equal("lobby", guest.Participant.AdmissionState);
        Assert.Null((await f.Db.SalesRoomInvitationGrants.IgnoreQueryFilters().SingleAsync()).SecretHash);
        Assert.DoesNotContain(guest.Credential, (await f.Db.SalesRoomParticipants.IgnoreQueryFilters().SingleAsync(p => p.MemberUserId == null)).SessionHash!);
        Assert.Equal("admission_required", (await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.GuestTokenAsync(guest.Credential, f.Room, default))).Code);
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.RedeemAsync(new(invite.Secret, "Again"), default));
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.GuestStatusAsync(guest.Credential, Guid.NewGuid(), default));
        Assert.Equal(2, await f.Db.SalesRoomParticipants.IgnoreQueryFilters().CountAsync());
    }
    [Fact]
    public async Task Removal_denies_access_before_durable_provider_disconnect_and_is_idempotent()
    {
        await using var f = await RoomFixture.Create(); await f.Ready(); var guest = await f.Guest();
        await f.Service.DecideAsync(f.Company, f.Actor, f.Room, guest.Participant.ParticipantId, "admit", new(Guid.NewGuid(), (await f.View()).Version), default);
        var token = await f.Service.GuestTokenAsync(guest.Credential, f.Room, default); Assert.Contains(guest.Participant.ParticipantId.ToString("N"), token.Identity);
        var command = new SalesRoomCommand(Guid.NewGuid(), (await f.View()).Version);
        var pending = await f.Service.DecideAsync(f.Company, f.Actor, f.Room, guest.Participant.ParticipantId, "remove", command, default);
        Assert.Contains(pending.Participants, x => x.Id == guest.Participant.ParticipantId && x.State == "removal_pending");
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.GuestTokenAsync(guest.Credential, f.Room, default));
        await f.Service.DecideAsync(f.Company, f.Actor, f.Room, guest.Participant.ParticipantId, "remove", command, default);
        await f.Dispatch("remove"); Assert.Contains((await f.View()).Participants, x => x.Id == guest.Participant.ParticipantId && x.State == "removed"); Assert.Equal(1, f.Media.Removes);
    }
    [Fact]
    public async Task Stale_host_commands_and_reused_command_ids_do_not_change_admission()
    {
        await using var f = await RoomFixture.Create(); await f.Ready(); var guest = await f.Guest();
        var stale = await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.DecideAsync(f.Company, f.Actor, f.Room, guest.Participant.ParticipantId, "admit", new(Guid.NewGuid(), 0), default));
        Assert.Equal("version_conflict", stale.Code);
        var request = new SalesRoomCommand(Guid.NewGuid(), (await f.View()).Version);
        await f.Service.DecideAsync(f.Company, f.Actor, f.Room, guest.Participant.ParticipantId, "admit", request, default);
        Assert.Equal("command_reused", (await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.DecideAsync(f.Company, f.Actor, f.Room, guest.Participant.ParticipantId, "remove", request, default))).Code);
    }
    [Fact]
    public async Task Consent_is_purpose_specific_versioned_and_audits_the_guest()
    {
        await using var f = await RoomFixture.Create(); await f.Ready(); var guest = await f.Guest();
        var command = new SetSalesRoomConsent(Guid.NewGuid(), guest.Participant.Version, "ai_processing", true, "browser-room-v1");
        var consent = await f.Service.ConsentAsync(guest.Credential, f.Room, command, default);
        Assert.True(consent.AiProcessingAllowed); Assert.False(consent.TranscriptRetentionAllowed);
        await f.Service.ConsentAsync(guest.Credential, f.Room, command, default);
        Assert.Equal(1, await f.Db.SalesRoomConsents.IgnoreQueryFilters().CountAsync());
        Assert.Equal(guest.Participant.ParticipantId, (await f.Db.AuditEvents.IgnoreQueryFilters().SingleAsync(x => x.Action == "sales.browser_room.consent")).ActorId);
        await f.Service.ConsentAsync(guest.Credential, f.Room, new(Guid.NewGuid(), consent.Version, "ai_processing", false, "browser-room-v1"), default);
        Assert.Contains(await f.Db.SalesRoomOperations.IgnoreQueryFilters().ToListAsync(), x => x.Action == "stop_agents");
    }
    [Fact]
    public async Task Admitted_participant_withdrawal_fences_agent_but_preserves_separate_transcript_choice()
    {
        await using var f = await RoomFixture.Create(); await f.Ready();
        await f.Service.HostTokenAsync(f.Company, f.Actor, f.Room, default);
        var guest = await f.Guest();
        await f.Service.DecideAsync(f.Company, f.Actor, f.Room, guest.Participant.ParticipantId, "admit",
            new(Guid.NewGuid(), (await f.View()).Version), default);
        var host = (await f.View()).Participants.Single(x => x.IsOrganizer);
        await f.Service.HostConsentAsync(f.Company, f.Actor, f.Room,
            new(Guid.NewGuid(), host.Version, "ai_processing", true, "browser-room-v1"), default);
        var guestView = await f.Service.ConsentAsync(guest.Credential, f.Room,
            new(Guid.NewGuid(), (await f.Service.GuestStatusAsync(guest.Credential, f.Room, default)).Version,
                "retained_transcript", true, "browser-room-v1"), default);
        guestView = await f.Service.ConsentAsync(guest.Credential, f.Room,
            new(Guid.NewGuid(), guestView.Version, "ai_processing", true, "browser-room-v1"), default);

        f.Db.ChangeTracker.Clear();
        var room = await f.Db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x => x.Id == f.Room);
        var agentId = Guid.NewGuid();
        f.Db.Agents.Add(new Agent(agentId, f.Company, "sales-test", "Nora", "Sales presenter", "Sales", null,
            AgentSeniority.Senior, AgentStatus.Active));
        room.StartAgent(agentId, f.Actor, Guid.NewGuid(), f.Clock.Now.AddSeconds(30), f.Clock.Now);
        var runningTurn = room.AgentTurnGeneration;
        await f.Db.SaveChangesAsync();

        var withdrawn = await f.Service.ConsentAsync(guest.Credential, f.Room,
            new(Guid.NewGuid(), guestView.Version, "ai_processing", false, "browser-room-v1"), default);
        f.Db.ChangeTracker.Clear();
        room = await f.Db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x => x.Id == f.Room);

        Assert.False(withdrawn.AiProcessingAllowed);
        Assert.True(withdrawn.TranscriptRetentionAllowed);
        Assert.Equal(SalesRoomAgentHealthStates.Stopped, room.AgentHealth);
        Assert.True(room.AgentTurnGeneration > runningTurn);
        Assert.Null(room.AgentLeaseOwnerId);
        Assert.Contains(await f.Db.SalesRoomOperations.IgnoreQueryFilters().ToListAsync(), x => x.Action == "stop_agents");
    }
    [Fact]
    public async Task End_retries_are_one_lifecycle_and_late_webhooks_cannot_reopen_it()
    {
        await using var f = await RoomFixture.Create(); await f.Ready(); var guest = await f.Guest();
        var command = new SalesRoomCommand(Guid.NewGuid(), (await f.View()).Version);
        Assert.Equal("ending", (await f.Service.EndAsync(f.Company, f.Actor, f.Room, command, default)).State);
        await f.Service.EndAsync(f.Company, f.Actor, f.Room, command, default);
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.GuestStatusAsync(guest.Credential, f.Room, default));
        await f.Dispatch("end"); Assert.Equal("ended", (await f.View()).State);
        f.Webhook.Value = new("event-one", "participant_joined", f.Media.Reference!, LiveKitSalesRoomMediaTransport.Identity(new(guest.Participant.ParticipantId, false)), new DateTimeOffset(f.Clock.Now).ToUnixTimeSeconds());
        await f.Service.AcceptWebhookAsync("verified fixture", "signed fixture", default); await f.Service.AcceptWebhookAsync("verified fixture", "signed fixture", default);
        Assert.Equal("ended", (await f.View()).State); Assert.Equal(1, await f.Db.SalesRoomProviderEvents.IgnoreQueryFilters().CountAsync());
    }
    [Fact]
    public async Task Expiry_denies_tokens_without_waiting_for_worker_and_cleans_up()
    {
        await using var f = await RoomFixture.Create(); await f.Ready(); var guest = await f.Guest();
        f.Clock.Now = f.Clock.Now.AddHours(2);
        Assert.Equal(410, (await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.GuestTokenAsync(guest.Credential, f.Room, default))).Status);
        await f.Dispatch("expire"); Assert.Equal("ended", (await f.View()).State);
        Assert.All(await f.Db.SalesRoomInvitationGrants.IgnoreQueryFilters().ToListAsync(), x => Assert.True(x.Revoked));
    }
    [Fact]
    public async Task Ambiguous_provision_is_visible_and_reconciles_without_duplicate_room()
    {
        await using var f = await RoomFixture.Create(); await f.CreateRoom(); f.Media.FailAfterCreate = true;
        await f.Dispatch("provision"); Assert.Equal("reconciliation_required", (await f.View()).State);
        var op = await f.Db.SalesRoomOperations.IgnoreQueryFilters().SingleAsync(x => x.Action == "provision"); Assert.Equal("reconciliation_required", op.State);
        f.Clock.Now = f.Clock.Now.AddMinutes(3); await f.Dispatch("provision"); Assert.Equal("lobby", (await f.View()).State); Assert.Equal(1, f.Media.Creates);
    }
    [Fact]
    public async Task Participant_limit_includes_lobby_and_invitation_revocation_removes_redeemed_access()
    {
        await using var f = await RoomFixture.Create(); f.Limits.MaximumParticipants = 2; await f.Ready();
        var invite = await f.Invite(); var guest = await f.Service.RedeemAsync(new(invite.Secret, "First"), default); var second = await f.Invite();
        Assert.Equal(429, (await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.RedeemAsync(new(second.Secret, "Second"), default))).Status);
        await f.Service.RevokeInvitationAsync(f.Company, f.Actor, f.Room, invite.Id, new(Guid.NewGuid(), (await f.View()).Version), default);
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => f.Service.GuestStatusAsync(guest.Credential, f.Room, default));
    }
}
internal sealed class RoomFixture : IAsyncDisposable
{
    public VirtualCompanyDbContext Db { get; private set; } = null!;
    public SalesBrowserRoomService Service { get; private set; } = null!;
    public SalesRoomWorkDispatcher Worker { get; private set; } = null!;
    private SqliteConnection connection = null!;
    public Guid Company = Guid.NewGuid(), Actor = Guid.NewGuid(), OtherActor = Guid.NewGuid(), Meeting, Room;
    public RoomClock Clock = new(); public RoomMedia Media = new(); public RoomWebhook Webhook = new(); public SalesRoomLifecycleOptions Limits = new() { Enabled = true };
    public static async Task<RoomFixture> Create()
    {
        var f = new RoomFixture(); f.connection = new SqliteConnection("Data Source=:memory:"); await f.connection.OpenAsync();
        f.Db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(f.connection).Options); await f.Db.Database.EnsureCreatedAsync();
        f.Db.Companies.Add(new Company(f.Company, "Room test"));
        f.Db.Users.AddRange(new User(f.Actor, "host@example.test", "Host", "test", f.Actor.ToString("N")), new User(f.OtherActor, "other@example.test", "Other", "test", f.OtherActor.ToString("N")));
        f.Db.CompanyMemberships.AddRange(new CompanyMembership(Guid.NewGuid(), f.Company, f.Actor, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
            new CompanyMembership(Guid.NewGuid(), f.Company, f.OtherActor, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
        var customer = Guid.NewGuid(); var contact = Guid.NewGuid(); var lead = Guid.NewGuid(); var account = Guid.NewGuid();
        f.Db.CustomerCompanies.Add(new CustomerCompany(customer, f.Company, "Customer")); f.Db.Contacts.Add(new Contact(contact, f.Company, "Buyer", "buyer@example.test", customer));
        f.Db.Leads.Add(new Lead(lead, f.Company, "Lead", SalesPipelineStage.QualifiedStageId, SalesStatuses.Qualified, contact, customer));
        var external = new ExternalAccountConnection(account, f.Company, f.Actor, ExternalAccountProvider.Google, "host@example.test", "host@example.test", "provider", "external"); external.SetStatus(ExternalConnectionStatus.Active);
        f.Db.ExternalAccountConnections.Add(external); var calendar = new CalendarConnection(account, f.Company, f.Actor, account, ExternalAccountProvider.Google, "host@example.test", "host@example.test"); calendar.SetStatus(ExternalConnectionStatus.Active); f.Db.CalendarConnections.Add(calendar);
        var invitation = new SalesMeetingInvitation(Guid.NewGuid(), f.Company, lead, null, contact, account, ExternalAccountProvider.Google, "host@example.test", "buyer@example.test", "Buyer", "Meeting", "Goal", f.Clock.Now, f.Clock.Now.AddMinutes(30), "Europe/Stockholm", null, false, f.Actor);
        f.Db.SalesMeetingInvitations.Add(invitation);
        var meeting = new SalesMeetingSession(Guid.NewGuid(), f.Company, invitation.Id, lead, null, contact, customer, "Goal", "Audience", 30, null, "test-calendar-event", SalesMeetingConsentStatus.Pending, SalesMeetingRetentionPolicy.Standard, 365, f.Clock.Now, f.Actor, f.Clock.Now);
        f.Meeting = meeting.Id; f.Db.SalesMeetingSessions.Add(meeting); await f.Db.SaveChangesAsync();
        var outbox = new RoomOutbox(f.Db); var mediaOptions = Options.Create(new SalesRoomMediaOptions { Url = "wss://test.livekit.cloud", Enabled = true });
        var monitoredLimits = Options.Create(f.Limits).ToMonitor();
        f.Service = new(f.Db, outbox, f.Media, f.Webhook, monitoredLimits, mediaOptions, f.Clock);
        f.Worker = new(f.Db, outbox, f.Media, f.Media, monitoredLimits, f.Clock); return f;
    }
    public async Task CreateRoom() => Room = (await Service.CreateAsync(Company, Actor, Meeting, new(Guid.NewGuid(), Clock.Now.AddHours(1)), default)).Id;
    public async Task Ready() { await CreateRoom(); await Dispatch("provision"); }
    public Task<SalesBrowserRoomView> View() => Service.GetAsync(Company, Actor, Room, default);
    public async Task<SalesRoomInvitationResult> Invite() => await Service.InviteAsync(Company, Actor, Room, new(Guid.NewGuid(), (await View()).Version, Clock.Now.AddMinutes(30)), default);
    public async Task<SalesRoomGuestSession> Guest() => await Service.RedeemAsync(new((await Invite()).Secret, "Guest"), default);
    public async Task Dispatch(string action) { var op = await Db.SalesRoomOperations.IgnoreQueryFilters().FirstAsync(x => x.Action == action); await Worker.DispatchAsync(new(Company, Room, op.Id), default); }
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
}
internal sealed class RoomClock : TimeProvider { public DateTime Now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc); public override DateTimeOffset GetUtcNow() => new(Now); }
internal sealed class RoomOutbox(VirtualCompanyDbContext db) : ICompanyOutboxEnqueuer
{
    public void Enqueue(Guid companyId, string topic, object payload, string? correlationId = null, DateTime? availableAtUtc = null, string? idempotencyKey = null, string? messageType = null, string? causationId = null, IReadOnlyDictionary<string, string?>? headers = null)
    { db.CompanyOutboxMessages.Add(new CompanyOutboxMessage(Guid.NewGuid(), companyId, topic, System.Text.Json.JsonSerializer.Serialize(payload), availableUtc: availableAtUtc, idempotencyKey: idempotencyKey)); }
}
internal sealed class RoomWebhook : ISalesRoomWebhookVerifier
{ public SalesRoomWebhook Value = new("event", "ignored", "", null, 0); public SalesRoomWebhook Verify(string body, string authorization) => Value; }
internal sealed class RoomMedia : ISalesRoomMediaTransport, ISalesRoomProviderInspection
{
    public int Creates, Removes; public bool FailAfterCreate; public string? Reference; private SalesRoomProviderRoom? room; private readonly HashSet<string> participants = new();
    public SalesRoomMediaReadiness GetReadiness(bool probeNative = false) => new(SalesRoomMediaRoutes.LiveKit, true, true, true, "test", null);
    public Task<SalesRoomProviderRoom> EnsureRoomAsync(SalesRoomMediaScope scope, Guid operationId, int maximumParticipants, CancellationToken ct)
    { if (room == null) { Creates++; Reference = LiveKitSalesRoomMediaTransport.RoomName(scope); room = new(Reference, operationId.ToString("N"), maximumParticipants); if (FailAfterCreate) { FailAfterCreate = false; throw new SalesRoomMediaException("provider_outcome_unknown", true); } } return Task.FromResult(room); }
    public Task<SalesRoomProviderRoom?> InspectRoomAsync(SalesRoomMediaScope scope, CancellationToken ct) => Task.FromResult(room);
    public Task DeleteRoomAsync(SalesRoomMediaScope scope, CancellationToken ct) { room = null; participants.Clear(); return Task.CompletedTask; }
    public SalesRoomMediaToken IssueToken(SalesRoomMediaScope scope, SalesRoomMediaParticipant participant) { var id = LiveKitSalesRoomMediaTransport.Identity(participant); participants.Add(id); return new("wss://test.livekit.cloud", "test-only", id, DateTimeOffset.UtcNow.AddMinutes(2)); }
    public Task RemoveParticipantAsync(SalesRoomMediaScope scope, SalesRoomMediaParticipant participant, CancellationToken ct) { Removes++; participants.Remove(LiveKitSalesRoomMediaTransport.Identity(participant)); return Task.CompletedTask; }
    public Task<IReadOnlyList<string>> ParticipantsAsync(SalesRoomMediaScope scope, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(participants.ToArray());
    public Task<ISalesRoomMediaConnection> ConnectAgentAsync(SalesRoomMediaScope scope, SalesRoomMediaParticipant agent, IReadOnlyCollection<SalesRoomMediaParticipant> humans, CancellationToken ct) => throw new NotSupportedException("No agent is started by lifecycle tests.");
}
