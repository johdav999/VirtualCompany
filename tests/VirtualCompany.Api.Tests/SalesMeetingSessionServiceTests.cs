using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingSessionServiceTests
{
    [Fact]
    public async Task Repeated_create_is_idempotent_and_records_one_creation_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = fixture.Request();

        var first = await fixture.Service.CreateOrUpdateAsync(
            fixture.CompanyId, fixture.UserId, fixture.InvitationId,
            request, "meeting-correlation", CancellationToken.None);
        var second = await fixture.Service.CreateOrUpdateAsync(
            fixture.CompanyId, fixture.UserId, fixture.InvitationId,
            request, "meeting-correlation-retry", CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await fixture.Db.SalesMeetingSessions.CountAsync());
        Assert.Equal(1, await fixture.Db.AuditEvents.CountAsync(x => x.Action == "sales.meeting_session.created"));
        Assert.Equal(fixture.CustomerCompanyId, first.CustomerCompanyId);
        Assert.Equal("provider-event", first.ProviderMeetingId);
        Assert.Equal(fixture.MeetingEndsUtc.AddDays(365), first.RetentionUntilUtc);
    }

    [Fact]
    public async Task Preparation_update_requires_current_version_and_records_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateSessionAsync();

        var updated = await fixture.Service.CreateOrUpdateAsync(
            fixture.CompanyId,
            fixture.UserId,
            fixture.InvitationId,
            fixture.Request(
                goal: "Agree a technical workshop",
                consentStatus: "granted",
                retentionPolicy: "custom",
                retentionDays: 90,
                expectedVersion: created.ConcurrencyVersion),
            "update-correlation",
            CancellationToken.None);

        Assert.Equal(2, updated.ConcurrencyVersion);
        Assert.Equal("granted", updated.ConsentStatus);
        Assert.Equal("custom", updated.RetentionPolicy);
        Assert.Equal(fixture.MeetingEndsUtc.AddDays(90), updated.RetentionUntilUtc);
        Assert.Equal(1, await fixture.Db.AuditEvents.CountAsync(x => x.Action == "sales.meeting_session.preparation_updated"));
    }

    [Fact]
    public async Task Stale_transition_returns_conflict_and_preserves_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateSessionAsync();
        var presenting = await fixture.Service.TransitionAsync(
            fixture.CompanyId, fixture.UserId, created.Id,
            new("presenting", created.ConcurrencyVersion, 0, 0),
            "transition-one", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SalesMeetingSessionConflictException>(() =>
            fixture.Service.TransitionAsync(
                fixture.CompanyId, fixture.UserId, created.Id,
                new("discussion", created.ConcurrencyVersion),
                "transition-stale", CancellationToken.None));

        Assert.Equal(SalesMeetingSessionProblemCodes.Conflict, exception.Code);
        var stored = await fixture.Db.SalesMeetingSessions.AsNoTracking().SingleAsync();
        Assert.Equal(SalesMeetingSessionStatus.Presenting, stored.Status);
        Assert.Equal(presenting!.ConcurrencyVersion, stored.ConcurrencyVersion);
    }

    [Fact]
    public async Task Valid_transitions_persist_resume_state_and_audit_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = await fixture.CreateSessionAsync();
        session = (await fixture.Service.TransitionAsync(
            fixture.CompanyId, fixture.UserId, session.Id,
            new("presenting", session.ConcurrencyVersion, 3, 1),
            "present", CancellationToken.None))!;
        session = (await fixture.Service.TransitionAsync(
            fixture.CompanyId, fixture.UserId, session.Id,
            new("interrupted", session.ConcurrencyVersion, 3, 2, "slide:3:point:2"),
            "interrupt", CancellationToken.None))!;
        session = (await fixture.Service.TransitionAsync(
            fixture.CompanyId, fixture.UserId, session.Id,
            new("answering", session.ConcurrencyVersion),
            "answer", CancellationToken.None))!;
        session = (await fixture.Service.TransitionAsync(
            fixture.CompanyId, fixture.UserId, session.Id,
            new("resuming", session.ConcurrencyVersion),
            "resume", CancellationToken.None))!;

        Assert.Equal("resuming", session.Status);
        Assert.Equal(3, session.CurrentSlideIndex);
        Assert.Equal(2, session.CurrentTalkingPointIndex);
        Assert.Equal("slide:3:point:2", session.ResumeMarker);
        var audits = await fixture.Db.AuditEvents
            .Where(x => x.Action == "sales.meeting_session.transitioned")
            .OrderBy(x => x.OccurredUtc)
            .ToListAsync();
        Assert.Equal(4, audits.Count);
        Assert.All(audits, audit => Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId)));
    }

    [Fact]
    public async Task Another_company_invitation_cannot_be_read_or_used()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Null(await fixture.Service.GetByInvitationAsync(
            fixture.CompanyId, fixture.OtherInvitationId, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.CreateOrUpdateAsync(
            fixture.CompanyId, fixture.UserId, fixture.OtherInvitationId,
            fixture.Request(), null, CancellationToken.None));
        Assert.Empty(await fixture.Db.SalesMeetingSessions.ToListAsync());
    }

    [Fact]
    public async Task Inconsistent_customer_relationships_are_rejected()
    {
        await using var fixture = await Fixture.CreateAsync(inconsistentContactCustomer: true);

        var exception = await Assert.ThrowsAsync<SalesMeetingSessionConflictException>(() =>
            fixture.Service.CreateOrUpdateAsync(
                fixture.CompanyId, fixture.UserId, fixture.InvitationId,
                fixture.Request(), null, CancellationToken.None));

        Assert.Equal(SalesMeetingSessionProblemCodes.InvalidRelationship, exception.Code);
        Assert.Empty(await fixture.Db.SalesMeetingSessions.ToListAsync());
    }

    [Fact]
    public async Task Preset_application_inherits_defaults_and_replaces_legacy_deck_atomically()
    {
        await using var f=await Fixture.CreateAsync();
        var session=await f.CreateSessionAsync();
        var version=await SeedPreset(f);
        var now=DateTime.UtcNow;
        var legacy=new SalesPresentationDeck(Guid.NewGuid(),f.CompanyId,session.Id,version.DefaultPresenterAgentId!.Value,1,"Legacy","old.pptx","application/octet-stream",100,new string('c',64),"old/deck.pptx",null,f.UserId,now);
        legacy.BeginProcessing(now,TimeSpan.Zero);legacy.MarkProcessed(1,"test","1","flattened",1,now);legacy.Activate(now);
        f.Db.Add(legacy);await f.Db.SaveChangesAsync();
        var service=new SalesPresentationRunService(f.Db,null!,TimeProvider.System,f.Service);
        var command=new ApplySalesPresentationPresetCommand(version.PresetId,version.Id,null,null,null,null,null,null,null,false,null);
        var run=await service.ApplyAsync(f.CompanyId,f.UserId,session.Id,command,null,default);
        Assert.NotNull(run);
        Assert.Equal("Reusable goal",run.Goal);Assert.Equal("Business leaders",run.Audience);Assert.Equal(30,run.DurationMinutes);
        Assert.False(run.GoalOverridden);Assert.False(run.DurationOverridden);
        var saved=await f.Db.SalesMeetingSessions.SingleAsync(x=>x.Id==session.Id);
        Assert.Equal(run.Goal,saved.MeetingGoal);Assert.Equal(run.PresenterAgentId,saved.PresenterAgentId);
        Assert.Equal("assisted",saved.PresentationControlMode);Assert.Equal(SalesMeetingConsentStatus.Pending,saved.ConsentStatus);
        Assert.Equal(1,await f.Db.SalesPresentationDecks.CountAsync(x=>x.SessionId==session.Id&&x.IsActive));
        Assert.False(legacy.IsActive);
        Assert.Contains(await f.Db.SalesMeetingArtifacts.ToListAsync(),x=>x.DeckId==run.CompatibilityDeckId&&x.Content=="Explain the product"&&x.ArtifactType==SalesMeetingArtifactType.SlideTalkingPoint);
        var repeated=await service.ApplyAsync(f.CompanyId,f.UserId,session.Id,command,null,default);
        Assert.Equal(run.Id,repeated!.Id);
        Assert.Equal(1,await f.Db.SalesPresentationRuns.CountAsync());
    }

    [Fact]
    public async Task Preset_first_creates_session_without_granting_consent_and_rejects_foreign_version()
    {
        await using var f=await Fixture.CreateAsync();
        var version=await SeedPreset(f);
        var service=new SalesPresentationRunService(f.Db,null!,TimeProvider.System,f.Service);
        var bad=new ApplySalesPresentationPresetCommand(null,Guid.NewGuid(),null,null,null,null,null,null,null,false,null);
        await Assert.ThrowsAsync<SalesPresentationPresetConflictException>(()=>service.ApplyToInvitationAsync(f.CompanyId,f.UserId,f.InvitationId,bad,null,default));
        Assert.Empty(await f.Db.SalesMeetingSessions.ToListAsync());
        var command=bad with {PresetId=version.PresetId,PresetVersionId=version.Id};
        await Assert.ThrowsAsync<SalesPresentationPresetConflictException>(()=>service.ApplyToInvitationAsync(f.CompanyId,f.UserId,f.InvitationId,command with {PresenterAgentId=Guid.NewGuid()},null,default));
        Assert.Empty(await f.Db.SalesMeetingSessions.ToListAsync());
        var run=await service.ApplyToInvitationAsync(f.CompanyId,f.UserId,f.InvitationId,command,null,default);
        var session=await f.Db.SalesMeetingSessions.SingleAsync();
        Assert.Equal(run!.MeetingSessionId,session.Id);
        Assert.Equal(SalesMeetingConsentStatus.NotRequested,session.ConsentStatus);
        Assert.Equal("Reusable goal",session.MeetingGoal);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ApplyToInvitationAsync(Guid.NewGuid(),f.UserId,f.InvitationId,command,null,default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Started_meeting_allows_pinned_preset_replay_but_rejects_new_attachment(bool attachBeforeStart)
    {
        await using var f=await Fixture.CreateAsync();
        var session=await f.CreateSessionAsync();
        var version=await SeedPreset(f);
        var service=new SalesPresentationRunService(f.Db,null!,TimeProvider.System,f.Service);
        var command=new ApplySalesPresentationPresetCommand(version.PresetId,version.Id,null,null,null,null,null,null,null,false,null);
        var original=attachBeforeStart?await service.ApplyAsync(f.CompanyId,f.UserId,session.Id,command,null,default):null;
        var entity=await f.Db.SalesMeetingSessions.SingleAsync(x=>x.Id==session.Id);
        entity.TransitionTo(SalesMeetingSessionStatus.Presenting,null,null,null,null,f.UserId,DateTime.UtcNow);
        await f.Db.SaveChangesAsync();
        var revision=entity.ConcurrencyVersion;
        if(attachBeforeStart)
        {
            var replay=await service.ApplyToInvitationAsync(f.CompanyId,f.UserId,f.InvitationId,command,null,default);
            Assert.Equal(original!.Id,replay!.Id);
            Assert.Equal(revision,entity.ConcurrencyVersion);
            Assert.Equal(1,await f.Db.SalesPresentationRuns.CountAsync());
            var other=await SeedPreset(f);
            var failure=await Assert.ThrowsAsync<SalesPresentationPresetConflictException>(()=>service.ApplyAsync(f.CompanyId,f.UserId,session.Id,
                command with {PresetId=other.PresetId,PresetVersionId=other.Id,ReplaceActive=true,ExpectedActiveRunVersion=replay.ConcurrencyVersion},null,default));
            Assert.Contains("already started",failure.Message);
        }
        else
        {
            var failure=await Assert.ThrowsAsync<SalesPresentationPresetConflictException>(()=>service.ApplyAsync(f.CompanyId,f.UserId,session.Id,command,null,default));
            Assert.Contains("already started",failure.Message);
            Assert.Empty(await f.Db.SalesPresentationRuns.ToListAsync());
        }
        Assert.Equal(SalesMeetingSessionStatus.Presenting,(await f.Db.SalesMeetingSessions.SingleAsync(x=>x.Id==session.Id)).Status);
    }

    private static async Task<SalesPresentationPresetVersion> SeedPreset(Fixture f)
    {
        var now=DateTime.UtcNow;var agent=Guid.NewGuid();
        var preset=new SalesPresentationPreset(Guid.NewGuid(),f.CompanyId,"Reusable demo",null,f.UserId,now);
        var version=new SalesPresentationPresetVersion(Guid.NewGuid(),f.CompanyId,preset.Id,1,agent,null,"Reusable goal","Business leaders",30,"Generic demo","assisted","en",true,false,true,null,now);
        var asset=new SalesPresentationPresetAsset(Guid.NewGuid(),f.CompanyId,version.Id,"preset.pptx","application/octet-stream",100,new string('a',64),"preset/deck.pptx",null,f.UserId,now);
        asset.BeginProcessing(now,TimeSpan.Zero);asset.MarkProcessed(1,"test","1","flattened",now);
        var slide=new SalesPresentationPresetSlide(Guid.NewGuid(),f.CompanyId,asset.Id,1,1,"Overview","Product facts",null,"slide.png",null,1600,900,1600,900,new string('b',64),"Introduce","Explain the product",60,"Next",now);
        f.Db.AddRange(new Agent(agent,f.CompanyId,"sales","Alex","Presenter","Sales",null,AgentSeniority.Senior,AgentStatus.Active),preset,version,asset,slide);
        await f.Db.SaveChangesAsync();
        version.Publish(f.UserId,now);preset.Publish(version.Id,now);
        await f.Db.SaveChangesAsync();return version;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private Fixture(
            SqliteConnection connection,
            VirtualCompanyDbContext db,
            SalesMeetingSessionService service,
            Guid companyId,
            Guid userId,
            Guid invitationId,
            Guid otherInvitationId,
            Guid customerCompanyId,
            DateTime meetingEndsUtc)
        {
            this.connection = connection;
            Db = db;
            Service = service;
            CompanyId = companyId;
            UserId = userId;
            InvitationId = invitationId;
            OtherInvitationId = otherInvitationId;
            CustomerCompanyId = customerCompanyId;
            MeetingEndsUtc = meetingEndsUtc;
        }

        public VirtualCompanyDbContext Db { get; }
        public SalesMeetingSessionService Service { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid InvitationId { get; }
        public Guid OtherInvitationId { get; }
        public Guid CustomerCompanyId { get; }
        public DateTime MeetingEndsUtc { get; }

        public CreateOrUpdateSalesMeetingSessionRequest Request(
            string goal = "Confirm product fit",
            string consentStatus = "pending",
            string retentionPolicy = "standard",
            int retentionDays = 365,
            long? expectedVersion = null) =>
            new(goal, "Finance leadership", 45, "Reconciliation demo",
                consentStatus, retentionPolicy, retentionDays, expectedVersion);

        public Task<SalesMeetingSessionResponse> CreateSessionAsync() =>
            Service.CreateOrUpdateAsync(
                CompanyId, UserId, InvitationId, Request(),
                "create-correlation", CancellationToken.None);

        public static async Task<Fixture> CreateAsync(bool inconsistentContactCustomer = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var otherCompanyId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var context = new TestCompanyContextAccessor(null, userId);
            var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new VirtualCompanyDbContext(options, context);
            await db.Database.EnsureCreatedAsync();

            var invitationId = Guid.NewGuid();
            var otherInvitationId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var alternateCustomerId = Guid.NewGuid();
            var contactId = Guid.NewGuid();
            var leadId = Guid.NewGuid();
            var dealId = Guid.NewGuid();
            var connectionId = Guid.NewGuid();
            var otherConnectionId = Guid.NewGuid();
            var meetingStarts = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
            var meetingEnds = meetingStarts.AddMinutes(45);

            db.Companies.AddRange(
                new Company(companyId, "Meeting Company"),
                new Company(otherCompanyId, "Other Company"));
            db.Users.Add(new User(userId, "owner@example.com", "Owner", "test", userId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(), companyId, userId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.CustomerCompanies.AddRange(
                new CustomerCompany(customerId, companyId, "Customer"),
                new CustomerCompany(alternateCustomerId, companyId, "Alternate Customer"));
            db.Contacts.Add(new Contact(
                contactId, companyId, "Buyer", "buyer@example.com",
                inconsistentContactCustomer ? alternateCustomerId : customerId));
            var lead = new Lead(
                leadId, companyId, "Qualified lead", SalesPipelineStage.QualifiedStageId,
                SalesStatuses.Qualified, contactId, customerId);
            db.Leads.Add(lead);
            db.Deals.Add(new Deal(
                dealId, companyId, "Opportunity", SalesPipelineStage.QualifiedStageId,
                10_000m, "SEK", sourceLeadId: leadId,
                primaryContactId: contactId, customerCompanyId: customerId));
            AddCalendarConnection(db, companyId, userId, connectionId, "owner@example.com");
            var invitation = ScheduledInvitation(
                invitationId, companyId, leadId, dealId, contactId,
                connectionId, userId, meetingStarts, meetingEnds, "provider-event");
            db.SalesMeetingInvitations.Add(invitation);

            var otherUserId = Guid.NewGuid();
            var otherLeadId = Guid.NewGuid();
            var otherCustomerId = Guid.NewGuid();
            var otherContactId = Guid.NewGuid();
            db.Users.Add(new User(otherUserId, "other@example.com", "Other", "test", otherUserId.ToString("N")));
            db.CustomerCompanies.Add(new CustomerCompany(otherCustomerId, otherCompanyId, "Other Customer"));
            db.Contacts.Add(new Contact(otherContactId, otherCompanyId, "Other Buyer", "other-buyer@example.com", otherCustomerId));
            db.Leads.Add(new Lead(otherLeadId, otherCompanyId, "Other lead", SalesPipelineStage.QualifiedStageId,
                SalesStatuses.Qualified, otherContactId, otherCustomerId));
            AddCalendarConnection(db, otherCompanyId, otherUserId, otherConnectionId, "other@example.com");
            db.SalesMeetingInvitations.Add(ScheduledInvitation(
                otherInvitationId, otherCompanyId, otherLeadId, null, otherContactId,
                otherConnectionId, otherUserId, meetingStarts, meetingEnds, "other-provider-event"));
            await db.SaveChangesAsync();
            context.SetCompanyId(companyId);

            return new Fixture(
                connection, db, new SalesMeetingSessionService(db, TimeProvider.System),
                companyId, userId, invitationId, otherInvitationId, customerId, meetingEnds);
        }

        private static void AddCalendarConnection(
            VirtualCompanyDbContext db,
            Guid companyId,
            Guid userId,
            Guid connectionId,
            string email)
        {
            var external = new ExternalAccountConnection(
                connectionId, companyId, userId, ExternalAccountProvider.Google,
                email, email, $"provider-{connectionId:N}", $"external:{connectionId:N}");
            external.SetStatus(ExternalConnectionStatus.Active);
            var calendar = new CalendarConnection(
                connectionId, companyId, userId, connectionId,
                ExternalAccountProvider.Google, email, email);
            calendar.SetStatus(ExternalConnectionStatus.Active);
            db.ExternalAccountConnections.Add(external);
            db.CalendarConnections.Add(calendar);
        }

        private static SalesMeetingInvitation ScheduledInvitation(
            Guid invitationId,
            Guid companyId,
            Guid leadId,
            Guid? dealId,
            Guid? contactId,
            Guid connectionId,
            Guid userId,
            DateTime startsUtc,
            DateTime endsUtc,
            string providerEventId)
        {
            var invitation = new SalesMeetingInvitation(
                invitationId, companyId, leadId, dealId, contactId,
                connectionId, ExternalAccountProvider.Google, "owner@example.com",
                "buyer@example.com", "Buyer", "Product meeting", "Product fit",
                startsUtc, endsUtc, "Europe/Stockholm", null, true, userId);
            invitation.SubmitForApproval(Guid.NewGuid());
            invitation.MarkApproved(userId, startsUtc.AddDays(-1));
            invitation.BeginScheduling();
            invitation.MarkScheduled(providerEventId, null, null, null, startsUtc.AddDays(-1));
            return invitation;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestCompanyContextAccessor(Guid? companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; private set; } = userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }

        public void SetCompanyId(Guid? value) => CompanyId = value;

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}
