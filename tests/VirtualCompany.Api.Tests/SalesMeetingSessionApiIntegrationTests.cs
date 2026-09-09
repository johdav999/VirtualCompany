using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingSessionApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Authenticated_create_is_idempotent_scoped_and_audited()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);
        var request = Request();

        using var firstResponse = await client.PutAsJsonAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/session", request);
        using var secondResponse = await client.PutAsJsonAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/session", request);
        using var crossCompany = await client.GetAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationBId:D}/session");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossCompany.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<SalesMeetingSessionResponse>();
        var second = await secondResponse.Content.ReadFromJsonAsync<SalesMeetingSessionResponse>();
        Assert.NotNull(first);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(seed.CustomerAId, first.CustomerCompanyId);

        var state = await factory.ExecuteDbContextAsync(async db => new
        {
            SessionCount = await db.SalesMeetingSessions.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == seed.CompanyAId),
            AuditCount = await db.AuditEvents.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == seed.CompanyAId && x.Action == "sales.meeting_session.created"),
            CorrelationIds = await db.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyAId && x.Action == "sales.meeting_session.created")
                .Select(x => x.CorrelationId)
                .ToListAsync()
        });
        Assert.Equal(1, state.SessionCount);
        Assert.Equal(1, state.AuditCount);
        Assert.All(state.CorrelationIds, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public async Task Invalid_and_stale_transitions_return_stable_conflicts_without_overwrite()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);
        var createdResponse = await client.PutAsJsonAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/session", Request());
        var created = await createdResponse.Content.ReadFromJsonAsync<SalesMeetingSessionResponse>();
        Assert.NotNull(created);

        using var invalid = await client.PostAsJsonAsync(
            $"/api/sales/meeting-sessions/{created!.Id:D}/transitions",
            new TransitionSalesMeetingSessionRequest("completed", created.ConcurrencyVersion));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        using var invalidJson = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.Equal(
            SalesMeetingSessionProblemCodes.InvalidTransition,
            invalidJson.RootElement.GetProperty("code").GetString());

        using var presentingResponse = await client.PostAsJsonAsync(
            $"/api/sales/meeting-sessions/{created.Id:D}/transitions",
            new TransitionSalesMeetingSessionRequest("presenting", created.ConcurrencyVersion, 1, 0));
        var presenting = await presentingResponse.Content.ReadFromJsonAsync<SalesMeetingSessionResponse>();
        Assert.NotNull(presenting);

        using var stale = await client.PostAsJsonAsync(
            $"/api/sales/meeting-sessions/{created.Id:D}/transitions",
            new TransitionSalesMeetingSessionRequest("discussion", created.ConcurrencyVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var staleJson = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        Assert.Equal(
            SalesMeetingSessionProblemCodes.Conflict,
            staleJson.RootElement.GetProperty("code").GetString());

        var stored = await factory.ExecuteDbContextAsync(db =>
            db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.Id == created.Id));
        Assert.Equal(SalesMeetingSessionStatus.Presenting, stored.Status);
        Assert.Equal(presenting!.ConcurrencyVersion, stored.ConcurrencyVersion);
    }

    [Fact]
    public async Task Missing_authentication_cannot_read_a_session()
    {
        var seed = await SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            CompanyContextResolutionMiddleware.CompanyHeaderName,
            seed.CompanyAId.ToString("D"));

        using var response = await client.GetAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Presentation_preparation_is_authorized_company_scoped_and_safe()
    {
        var seed = await SeedAsync();
        var agentId = Guid.NewGuid();
        await factory.SeedAsync(async db =>
        {
            db.Agents.Add(new Agent(
                agentId, seed.CompanyAId, "alex-preparation", "Alex",
                "Sales representative", "Sales", null,
                AgentSeniority.Senior, AgentStatus.Active));
            await db.SaveChangesAsync();
        });

        using var companyA = Client(seed.CompanyAId);
        using var success = await companyA.GetAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/presentation-preparation");
        var json = await success.Content.ReadAsStringAsync();

        using var companyB = Client(seed.CompanyBId);
        using var crossCompany = await companyB.GetAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/presentation-preparation");

        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add(
            CompanyContextResolutionMiddleware.CompanyHeaderName,
            seed.CompanyAId.ToString("D"));
        using var unauthenticated = await anonymous.GetAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/presentation-preparation");

        using var outsider = factory.CreateClient();
        outsider.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "outsider");
        outsider.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "outsider@example.com");
        outsider.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Outsider");
        outsider.DefaultRequestHeaders.Add(
            CompanyContextResolutionMiddleware.CompanyHeaderName,
            seed.CompanyAId.ToString("D"));
        using var forbidden = await outsider.GetAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/presentation-preparation");

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Contains("create_session", json, StringComparison.Ordinal);
        Assert.Contains(agentId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("organizerEmail", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attendeeEmail", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, crossCompany.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Presentation_deck_endpoint_requires_authentication_and_hides_other_company_data()
    {
        var seed = await SeedAsync();
        using var companyAClient = Client(seed.CompanyAId);
        using var sessionResponse = await companyAClient.PutAsJsonAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/session", Request());
        var session = await sessionResponse.Content.ReadFromJsonAsync<SalesMeetingSessionResponse>();
        Assert.NotNull(session);
        var deckId = Guid.NewGuid();
        await factory.SeedAsync(async db =>
        {
            var agentId = Guid.NewGuid();
            db.Agents.Add(new Agent(agentId, seed.CompanyAId, "alex-deck", "Alex", "Sales", "Sales", null,
                AgentSeniority.Senior, AgentStatus.Active));
            db.SalesPresentationDecks.Add(new SalesPresentationDeck(
                deckId, seed.CompanyAId, session!.Id, agentId, 1, "Private deck", "private.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation", 100,
                new string('a', 64), "safe/private.pptx", null, Guid.NewGuid(), DateTime.UtcNow));
            await db.SaveChangesAsync();
        });

        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add(
            CompanyContextResolutionMiddleware.CompanyHeaderName, seed.CompanyAId.ToString("D"));
        using var unauthenticated = await anonymous.GetAsync(
            $"/api/sales/meeting-sessions/{session!.Id:D}/decks/{deckId:D}");

        using var companyBClient = Client(seed.CompanyBId);
        using var crossCompany = await companyBClient.GetAsync(
            $"/api/sales/meeting-sessions/{session.Id:D}/decks/{deckId:D}");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossCompany.StatusCode);
    }

    [Fact]
    public async Task Capture_autosave_is_idempotent_and_hidden_from_anonymous_or_other_company_callers()
    {
        var seed = await SeedAsync();
        using var companyAClient = Client(seed.CompanyAId);
        using var sessionResponse = await companyAClient.PutAsJsonAsync(
            $"/api/sales/meeting-invitations/{seed.InvitationAId:D}/session", Request());
        var session = await sessionResponse.Content.ReadFromJsonAsync<SalesMeetingSessionResponse>();
        Assert.NotNull(session);

        var batchId = Guid.NewGuid();
        var observationId = Guid.NewGuid();
        var autosave = new AutosaveSalesMeetingCaptureRequest(batchId, 0, Observations:
        [
            new SalesMeetingObservationDraft(observationId, 1, "pain_point", "Monthly close is too slow.")
        ]);
        using var firstResponse = await companyAClient.PostAsJsonAsync(
            $"/api/sales/meeting-sessions/{session!.Id:D}/capture/autosave", autosave);
        using var retryResponse = await companyAClient.PostAsJsonAsync(
            $"/api/sales/meeting-sessions/{session.Id:D}/capture/autosave", autosave);
        var first = await firstResponse.Content.ReadFromJsonAsync<SalesMeetingCaptureSaveResultDto>();
        var retry = await retryResponse.Content.ReadFromJsonAsync<SalesMeetingCaptureSaveResultDto>();

        using var companyBClient = Client(seed.CompanyBId);
        using var crossCompany = await companyBClient.GetAsync(
            $"/api/sales/meeting-sessions/{session.Id:D}/capture");
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add(
            CompanyContextResolutionMiddleware.CompanyHeaderName, seed.CompanyAId.ToString("D"));
        using var unauthenticated = await anonymous.GetAsync(
            $"/api/sales/meeting-sessions/{session.Id:D}/capture");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal("accepted", first!.Disposition);
        Assert.Equal("duplicate", retry!.Disposition);
        Assert.Single(retry.Snapshot.Observations);
        Assert.Equal(HttpStatusCode.NotFound, crossCompany.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task Closing_customer_preview_and_internal_intelligence_are_separate_and_tenant_scoped()
    {
        var seed = await SeedAsync(); using var companyAClient = Client(seed.CompanyAId);
        using var sessionResponse = await companyAClient.PutAsJsonAsync($"/api/sales/meeting-invitations/{seed.InvitationAId:D}/session", Request());
        var session = await sessionResponse.Content.ReadFromJsonAsync<SalesMeetingSessionResponse>(); Assert.NotNull(session);
        var minutesId = Guid.NewGuid(); var intelligenceId = Guid.NewGuid();
        await factory.SeedAsync(async db =>
        {
            var agentId = Guid.NewGuid(); var now = DateTime.UtcNow;
            db.Agents.Add(new Agent(agentId, seed.CompanyAId, "alex-closing-api", "Alex", "Sales", "Sales", null, AgentSeniority.Senior, AgentStatus.Active));
            var minutes = new SalesMeetingMinutes(minutesId, seed.CompanyAId, session!.Id, Guid.NewGuid(), null, 1, 0, now, agentId, null, "test", "test", session.RetentionUntilUtc, Guid.NewGuid(), now);
            minutes.Items.Add(new SalesMeetingMinutesItem(Guid.NewGuid(), seed.CompanyAId, minutesId, 0, SalesMeetingMinutesItemType.Action, "Customer-safe action", "Alex", null, "action-item:test", null, false, now));
            var intelligence = new SalesMeetingInternalIntelligence(intelligenceId, seed.CompanyAId, session.Id, minutesId, 1, 0, now, agentId, null, "test", "test", session.RetentionUntilUtc, Guid.NewGuid(), now);
            intelligence.Items.Add(new SalesMeetingInternalIntelligenceItem(Guid.NewGuid(), seed.CompanyAId, intelligenceId, 0, SalesMeetingInternalIntelligenceItemType.Objection, "Private objection", .8m, "observation:test", null, false, now));
            db.SalesMeetingMinutes.Add(minutes); db.SalesMeetingInternalIntelligence.Add(intelligence); await db.SaveChangesAsync();
        });

        using var customerResponse = await companyAClient.GetAsync($"/api/sales/meeting-sessions/{session!.Id:D}/closing/customer-preview/{minutesId:D}");
        using var internalResponse = await companyAClient.GetAsync($"/api/sales/meeting-sessions/{session.Id:D}/closing/internal-intelligence/{intelligenceId:D}");
        var customerJson = await customerResponse.Content.ReadAsStringAsync(); var internalJson = await internalResponse.Content.ReadAsStringAsync();
        using var companyBClient = Client(seed.CompanyBId);
        using var crossCustomer = await companyBClient.GetAsync($"/api/sales/meeting-sessions/{session.Id:D}/closing/customer-preview/{minutesId:D}");
        using var crossInternal = await companyBClient.GetAsync($"/api/sales/meeting-sessions/{session.Id:D}/closing/internal-intelligence/{intelligenceId:D}");

        Assert.Equal(HttpStatusCode.OK, customerResponse.StatusCode); Assert.Equal(HttpStatusCode.OK, internalResponse.StatusCode);
        Assert.Contains("Customer-safe action", customerJson, StringComparison.Ordinal); Assert.DoesNotContain("Private objection", customerJson, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceId", customerJson, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("generatorAgentId", customerJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Private objection", internalJson, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, crossCustomer.StatusCode); Assert.Equal(HttpStatusCode.NotFound, crossInternal.StatusCode);
    }

    private HttpClient Client(Guid companyId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "meeting-user");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "meeting-user@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Meeting User");
        client.DefaultRequestHeaders.Add(
            CompanyContextResolutionMiddleware.CompanyHeaderName,
            companyId.ToString("D"));
        return client;
    }

    private async Task<SeedIds> SeedAsync()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var invitationAId = Guid.NewGuid();
        var invitationBId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Users.Add(new User(userId, "meeting-user@example.com", "Meeting User", "dev-header", "meeting-user"));
            db.Companies.AddRange(
                new Company(companyAId, "Meeting Tenant A"),
                new Company(companyBId, "Meeting Tenant B"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyAId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyBId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            AddMeetingRecords(db, companyAId, userId, invitationAId, customerAId, "A");
            AddMeetingRecords(db, companyBId, userId, invitationBId, Guid.NewGuid(), "B");
            return Task.CompletedTask;
        });

        return new SeedIds(companyAId, companyBId, invitationAId, invitationBId, customerAId);
    }

    private static void AddMeetingRecords(
        VirtualCompanyDbContext db,
        Guid companyId,
        Guid userId,
        Guid invitationId,
        Guid customerId,
        string suffix)
    {
        var contactId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var starts = new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);
        db.CustomerCompanies.Add(new CustomerCompany(customerId, companyId, $"Customer {suffix}"));
        db.Contacts.Add(new Contact(contactId, companyId, $"Buyer {suffix}", $"buyer-{suffix}@example.com", customerId));
        var lead = new Lead(
            leadId, companyId, $"Lead {suffix}", SalesPipelineStage.QualifiedStageId,
            SalesStatuses.Qualified, contactId, customerId);
        db.Leads.Add(lead);
        db.Deals.Add(new Deal(
            dealId, companyId, $"Deal {suffix}", SalesPipelineStage.QualifiedStageId,
            20_000m, "SEK", sourceLeadId: leadId,
            primaryContactId: contactId, customerCompanyId: customerId));

        var external = new ExternalAccountConnection(
            connectionId, companyId, userId, ExternalAccountProvider.Microsoft365,
            "meeting-user@example.com", "Meeting User", $"account-{suffix}", $"external:{suffix}");
        external.SetStatus(ExternalConnectionStatus.Active);
        var calendar = new CalendarConnection(
            connectionId, companyId, userId, connectionId,
            ExternalAccountProvider.Microsoft365, "meeting-user@example.com", "Meeting User");
        calendar.SetStatus(ExternalConnectionStatus.Active);
        db.ExternalAccountConnections.Add(external);
        db.CalendarConnections.Add(calendar);

        var invitation = new SalesMeetingInvitation(
            invitationId, companyId, leadId, dealId, contactId,
            connectionId, ExternalAccountProvider.Microsoft365,
            "meeting-user@example.com", $"buyer-{suffix}@example.com", $"Buyer {suffix}",
            $"Product meeting {suffix}", "Product fit", starts, starts.AddMinutes(45),
            "Europe/Stockholm", null, true, userId);
        invitation.SubmitForApproval(Guid.NewGuid());
        invitation.MarkApproved(userId, starts.AddDays(-1));
        invitation.BeginScheduling();
        invitation.MarkScheduled($"provider-event-{suffix}", null, null, null, starts.AddDays(-1));
        db.SalesMeetingInvitations.Add(invitation);
    }

    private static CreateOrUpdateSalesMeetingSessionRequest Request() =>
        new(
            "Confirm product fit",
            "Finance leadership",
            45,
            "Reconciliation demo",
            "pending",
            "standard",
            365);

    private sealed record SeedIds(
        Guid CompanyAId,
        Guid CompanyBId,
        Guid InvitationAId,
        Guid InvitationBId,
        Guid CustomerAId);
}
