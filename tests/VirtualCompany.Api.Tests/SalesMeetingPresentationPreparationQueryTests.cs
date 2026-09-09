using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingPresentationPreparationQueryTests
{
    [Fact]
    public async Task Scheduled_invitation_with_provider_event_and_active_sales_agent_allows_session_creation()
    {
        await using var fixture = await Fixture.CreateAsync(scheduled: true);

        var result = await fixture.Query.GetAsync(
            fixture.CompanyId, fixture.InvitationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Invitation.HasProviderEvent);
        Assert.Equal(25 * 1024 * 1024, result.MaximumUploadBytes);
        Assert.True(result.Invitation.IsEligibleForSessionCreation);
        Assert.Equal(SalesMeetingPreparationReadinessStates.SessionRequired, result.ReadinessState);
        Assert.Contains(SalesMeetingPreparationActionValues.CreateSession, result.AllowedActions);
        Assert.Contains(result.BlockingReasons,
            x => x.Code == SalesMeetingPreparationReasonCodes.SessionMissing);
        var agent = Assert.Single(result.EligibleAgents);
        Assert.Equal(fixture.AgentId, agent.Id);
        Assert.Equal("alex-sales", agent.TemplateId);
    }

    [Fact]
    public async Task Missing_customer_company_blocks_session_creation_with_actionable_reason()
    {
        await using var fixture = await Fixture.CreateAsync(
            scheduled: true, linkedCustomerCompany: false);

        var result = await fixture.Query.GetAsync(
            fixture.CompanyId, fixture.InvitationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(SalesMeetingPreparationReadinessStates.Blocked, result!.ReadinessState);
        Assert.DoesNotContain(SalesMeetingPreparationActionValues.CreateSession, result.AllowedActions);
        Assert.Contains(result.BlockingReasons,
            x => x.Code == SalesMeetingPreparationReasonCodes.CustomerCompanyMissing &&
                 x.Explanation.Contains("Associate this lead", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ineligible_invitation_and_agents_return_stable_blockers()
    {
        await using var fixture = await Fixture.CreateAsync(
            scheduled: false, activeSalesAgent: false);

        var result = await fixture.Query.GetAsync(
            fixture.CompanyId, fixture.InvitationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(SalesMeetingPreparationReadinessStates.Blocked, result!.ReadinessState);
        Assert.Empty(result.AllowedActions);
        Assert.Empty(result.EligibleAgents);
        Assert.Contains(result.BlockingReasons,
            x => x.Code == SalesMeetingPreparationReasonCodes.InvitationNotScheduled);
        Assert.DoesNotContain(result.BlockingReasons,
            x => x.Explanation.Contains("waiting_for_approval", StringComparison.Ordinal));
        Assert.Contains(result.BlockingReasons,
            x => x.Code == SalesMeetingPreparationReasonCodes.EligibleSalesAgentMissing);
    }

    [Fact]
    public async Task Existing_session_progresses_from_upload_to_processing_activation_and_presenter()
    {
        await using var fixture = await Fixture.CreateAsync(scheduled: true);
        var session = fixture.AddSession();
        await fixture.Db.SaveChangesAsync();

        var withoutDeck = await fixture.Query.GetAsync(
            fixture.CompanyId, fixture.InvitationId, CancellationToken.None);
        Assert.NotNull(withoutDeck);
        Assert.Equal(SalesMeetingPreparationReadinessStates.DeckRequired, withoutDeck!.ReadinessState);
        Assert.Contains(SalesMeetingPreparationActionValues.UpdateSession, withoutDeck.AllowedActions);
        Assert.Contains(SalesMeetingPreparationActionValues.UploadDeck, withoutDeck.AllowedActions);
        Assert.DoesNotContain(SalesMeetingPreparationActionValues.ActivateDeck, withoutDeck.AllowedActions);
        Assert.False(withoutDeck.CanOpenPresenter);

        var deck = fixture.AddDeck(session.Id);
        deck.BeginProcessing(fixture.Now, TimeSpan.FromMinutes(10));
        await fixture.Db.SaveChangesAsync();
        var processing = await fixture.Query.GetAsync(
            fixture.CompanyId, fixture.InvitationId, CancellationToken.None);
        Assert.Equal(SalesMeetingPreparationReadinessStates.Processing, processing!.ReadinessState);
        Assert.DoesNotContain(SalesMeetingPreparationActionValues.OpenPresenter, processing.AllowedActions);

        deck.MarkProcessed(3, "test", "1", "flattened", 1, fixture.Now.AddMinutes(1));
        await fixture.Db.SaveChangesAsync();
        var awaitingActivation = await fixture.Query.GetAsync(
            fixture.CompanyId, fixture.InvitationId, CancellationToken.None);
        Assert.Equal(
            SalesMeetingPreparationReadinessStates.ActivationRequired,
            awaitingActivation!.ReadinessState);
        Assert.Contains(SalesMeetingPreparationActionValues.ActivateDeck, awaitingActivation.AllowedActions);

        deck.Activate(fixture.Now.AddMinutes(2));
        await fixture.Db.SaveChangesAsync();
        var ready = await fixture.Query.GetAsync(
            fixture.CompanyId, fixture.InvitationId, CancellationToken.None);
        Assert.Equal(SalesMeetingPreparationReadinessStates.Ready, ready!.ReadinessState);
        Assert.True(ready.CanOpenPresenter);
        Assert.Equal(deck.Id, ready.ActiveDeckId);
        Assert.Equal(3, ready.ActiveDeckSlideCount);
        Assert.Contains(SalesMeetingPreparationActionValues.OpenPresenter, ready.AllowedActions);
    }

    [Fact]
    public async Task Retryable_failure_allows_retry_and_wrong_company_returns_no_data()
    {
        await using var fixture = await Fixture.CreateAsync(scheduled: true);
        var session = fixture.AddSession();
        var deck = fixture.AddDeck(session.Id);
        deck.MarkFailed(
            "renderer_unavailable", "Rendering could not start.", canRetry: true,
            blocked: false, fixture.Now);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Query.GetAsync(
            fixture.CompanyId, fixture.InvitationId, CancellationToken.None);
        var crossCompany = await fixture.Query.GetAsync(
            Guid.NewGuid(), fixture.InvitationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(SalesMeetingPreparationActionValues.RetryProcessing, result!.AllowedActions);
        Assert.Contains(result.BlockingReasons,
            x => x.Code == SalesMeetingPreparationReasonCodes.DeckProcessingFailed);
        Assert.Null(crossCompany);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            VirtualCompanyDbContext db,
            SalesMeetingPresentationPreparationQuery query,
            Guid companyId,
            Guid userId,
            Guid invitationId,
            Guid agentId,
            DateTime now)
        {
            Db = db;
            Query = query;
            CompanyId = companyId;
            UserId = userId;
            InvitationId = invitationId;
            AgentId = agentId;
            Now = now;
        }

        public VirtualCompanyDbContext Db { get; }
        public SalesMeetingPresentationPreparationQuery Query { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid InvitationId { get; }
        public Guid AgentId { get; }
        public DateTime Now { get; }

        public static async Task<Fixture> CreateAsync(
            bool scheduled,
            bool activeSalesAgent = true,
            bool linkedCustomerCompany = true)
        {
            var companyId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var invitationId = Guid.NewGuid();
            var leadId = Guid.NewGuid();
            var customerCompanyId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var now = new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc);
            var context = new TestCompanyContextAccessor(companyId, userId);
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseInMemoryDatabase($"sales-preparation-{Guid.NewGuid():N}")
                    .Options,
                context);
            var invitation = new SalesMeetingInvitation(
                invitationId, companyId, leadId, null, null, Guid.NewGuid(),
                ExternalAccountProvider.Microsoft365, "owner@example.com", "buyer@example.com",
                "Buyer", "Product meeting", "Confirm fit", now.AddDays(1),
                now.AddDays(1).AddMinutes(45), "Europe/Stockholm", null, true, userId);
            if (scheduled)
            {
                invitation.SubmitForApproval(Guid.NewGuid());
                invitation.MarkApproved(userId, now);
                invitation.BeginScheduling();
                invitation.MarkScheduled("provider-event", null, null, null, now);
            }

            db.SalesMeetingInvitations.Add(invitation);
            if (linkedCustomerCompany)
                db.CustomerCompanies.Add(new CustomerCompany(customerCompanyId, companyId, "Northstar AB"));
            db.Leads.Add(new Lead(
                leadId, companyId, "Welheld opportunity", SalesPipelineStage.QualifiedStageId,
                customerCompanyId: linkedCustomerCompany ? customerCompanyId : null));
            db.Agents.Add(new Agent(
                agentId, companyId, "alex-sales", "Alex", "Sales representative", "Sales",
                null, AgentSeniority.Senior,
                activeSalesAgent ? AgentStatus.Active : AgentStatus.Paused));
            db.Agents.Add(new Agent(
                Guid.NewGuid(), companyId, "finance-agent", "Frank", "Finance specialist",
                "Finance", null, AgentSeniority.Senior, AgentStatus.Active));
            await db.SaveChangesAsync();
            return new Fixture(
                db, new SalesMeetingPresentationPreparationQuery(
                    db, Microsoft.Extensions.Options.Options.Create(new SalesPresentationOptions())),
                companyId, userId, invitationId, agentId, now);
        }

        public SalesMeetingSession AddSession()
        {
            var invitation = Db.SalesMeetingInvitations.Single(x => x.Id == InvitationId);
            var session = new SalesMeetingSession(
                Guid.NewGuid(), CompanyId, InvitationId, invitation.LeadId,
                invitation.DealId, invitation.ContactId, Guid.NewGuid(),
                "Confirm fit", "Finance leadership", 45, null,
                invitation.ExternalEventId!, SalesMeetingConsentStatus.NotRequested,
                SalesMeetingRetentionPolicy.Standard, 365, Now.AddDays(1),
                UserId, Now);
            Db.SalesMeetingSessions.Add(session);
            return session;
        }

        public SalesPresentationDeck AddDeck(Guid sessionId)
        {
            var deck = new SalesPresentationDeck(
                Guid.NewGuid(), CompanyId, sessionId, AgentId, 1,
                "Welheld", "welheld.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                1024, new string('a', 64), "secret-storage-key", null, UserId, Now);
            Db.SalesPresentationDecks.Add(deck);
            return deck;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestCompanyContextAccessor(Guid? companyId, Guid userId)
        : ICompanyContextAccessor
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
