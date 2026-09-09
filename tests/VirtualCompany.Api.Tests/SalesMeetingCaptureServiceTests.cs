using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingCaptureServiceTests
{
    [Fact]
    public async Task Autosave_is_idempotent_ordered_and_uses_a_separate_capture_version()
    {
        await using var fixture = await Fixture.CreateAsync();
        var batchId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var request = new AutosaveSalesMeetingCaptureRequest(batchId, 0,
            [new(itemId, 1, "customer", "Jordan", "host_mediated", "We need a faster close.", fixture.Now, fixture.Now.AddSeconds(3), .9m)],
            [new(Guid.NewGuid(), 1, "pain_point", "Monthly close is too slow.", .8m, $"transcript:{itemId:N}")],
            [new(Guid.NewGuid(), 1, "Send security overview", OwnerLabel: "Alex")]);

        var first = await fixture.Capture.AutosaveAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, request, "corr", CancellationToken.None);
        var duplicate = await fixture.Capture.AutosaveAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, request, "corr", CancellationToken.None);

        Assert.Equal("accepted", first!.Disposition);
        Assert.Equal("duplicate", duplicate!.Disposition);
        Assert.Equal(1, duplicate.Snapshot.CaptureVersion);
        Assert.Single(duplicate.Snapshot.TranscriptSegments);
        Assert.Single(duplicate.Snapshot.Observations);
        Assert.Single(duplicate.Snapshot.ActionItems);
        var session = await fixture.Db.SalesMeetingSessions.SingleAsync();
        Assert.Equal(1, session.ConcurrencyVersion);
        Assert.Equal(1, session.CaptureVersion);
        Assert.Equal(1, await fixture.Db.SalesMeetingTranscriptSegments.CountAsync());
    }

    [Fact]
    public async Task Stale_capture_version_is_rejected_without_partial_records()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Capture.AutosaveAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(Guid.NewGuid(), 0, Observations: [new(Guid.NewGuid(), 1, "buying_signal", "Asked for rollout timing.")]), null, CancellationToken.None);

        await Assert.ThrowsAsync<SalesMeetingCaptureConflictException>(() => fixture.Capture.AutosaveAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(Guid.NewGuid(), 0, Observations: [new(Guid.NewGuid(), 2, "commitment", "Will invite procurement.")]), null, CancellationToken.None));

        Assert.Single(await fixture.Db.SalesMeetingObservations.ToListAsync());
    }

    [Fact]
    public async Task Grounded_answer_persists_only_allowlisted_claim_sources_and_requires_explicit_stage_approval()
    {
        await using var fixture = await Fixture.CreateAsync(withDeck: true);
        fixture.Reasoning.ResultFactory = request =>
        {
            var sourceId = request.Sources.Single(x => x.Type == "visible_slide").Id;
            return new(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "The slide confirms unified operations.",
                [new("The product connects operating teams.", "confirmed_fact", .92m, [sourceId])], .92m, [], [], [], [sourceId]);
        };

        var answer = await fixture.Questions.AskAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(Guid.NewGuid(), 1, fixture.AgentId, "Does this connect our operating teams?", "customer", "Jordan"), "corr", CancellationToken.None);

        Assert.Equal("completed", answer!.Status);
        Assert.False(answer.FollowUpRequired);
        Assert.Single(answer.Evidence);
        Assert.Equal("private", answer.Visibility);
        Assert.Contains(fixture.Reasoning.Request!.AllowedTools, x => x == SalesMeetingCaptureToolNames.AnswerQuestion);

        var approved = await fixture.Questions.ApproveForStageAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            answer.Id, answer.Version, "corr", CancellationToken.None);
        var stage = await fixture.Questions.ListStageAnswersAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, CancellationToken.None);
        Assert.Equal("approved_for_stage", approved!.Visibility);
        Assert.Single(stage);
        Assert.DoesNotContain("Evidence", System.Text.Json.JsonSerializer.Serialize(stage), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unsupported_or_failed_answer_is_truthful_and_recoverable()
    {
        await using var fixture = await Fixture.CreateAsync(withDeck: true);
        fixture.Reasoning.ResultFactory = _ => new(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "Invented answer",
            [new("Unsupported", "fact", .99m, ["other-company-source"])], .99m, [], [], [], ["other-company-source"]);
        var unsupported = await fixture.Questions.AskAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(Guid.NewGuid(), 1, fixture.AgentId, "What discount can we promise?"), null, CancellationToken.None);
        Assert.Equal("unverified", unsupported!.Status);
        Assert.True(unsupported.FollowUpRequired);
        Assert.Empty(unsupported.Evidence);
        Assert.DoesNotContain("Invented", unsupported.Answer);

        fixture.Reasoning.ResultFactory = _ => throw new HttpRequestException("secret provider detail");
        var failed = await fixture.Questions.AskAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(Guid.NewGuid(), 2, fixture.AgentId, "Can you verify the policy?"), null, CancellationToken.None);
        Assert.Equal("failed", failed!.Status);
        Assert.Equal("provider_unavailable", failed.FailureCode);
        Assert.DoesNotContain("secret", failed.FailureSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capture_rejects_a_user_without_membership_and_does_not_disclose_the_session()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Capture.GetAsync(
            fixture.CompanyId, Guid.NewGuid(), fixture.SessionId, CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_answer_is_persisted_as_recoverable_before_cancellation_propagates()
    {
        await using var fixture = await Fixture.CreateAsync(withDeck: true);
        fixture.Reasoning.ResultFactory = _ => throw new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Questions.AskAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(Guid.NewGuid(), 1, fixture.AgentId, "Can you verify the rollout date?"), null,
            CancellationToken.None));

        var stored = await fixture.Db.SalesMeetingQuestions.AsNoTracking().SingleAsync();
        Assert.Equal(SalesMeetingQuestionStatus.Cancelled, stored.Status);
        Assert.True(stored.FollowUpRequired);
        Assert.Equal("cancelled", stored.FailureCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, Guid companyId, Guid userId, Guid sessionId, Guid agentId,
            DateTime now, RecordingReasoning reasoning)
        {
            Db = db; CompanyId = companyId; UserId = userId; SessionId = sessionId; AgentId = agentId; Now = now; Reasoning = reasoning;
            Capture = new SalesMeetingCaptureService(db, TimeProvider.System);
            Questions = new SalesMeetingQuestionAnsweringService(db, new EmptyKnowledge(), reasoning,
                new AllowingAuthority(companyId, agentId), TimeProvider.System, NullLogger<SalesMeetingQuestionAnsweringService>.Instance);
        }
        public VirtualCompanyDbContext Db { get; }
        public SalesMeetingCaptureService Capture { get; }
        public SalesMeetingQuestionAnsweringService Questions { get; }
        public RecordingReasoning Reasoning { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid SessionId { get; }
        public Guid AgentId { get; }
        public DateTime Now { get; }

        public static async Task<Fixture> CreateAsync(bool withDeck = false)
        {
            var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var sessionId = Guid.NewGuid(); var agentId = Guid.NewGuid(); var customerId = Guid.NewGuid(); var now = DateTime.UtcNow;
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase($"meeting-capture-{Guid.NewGuid():N}").Options, new TestContext(companyId, userId));
            db.Companies.Add(new Company(companyId, "Capture Company")); db.Users.Add(new User(userId, "owner@example.com", "Owner", "test", userId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.CustomerCompanies.Add(new CustomerCompany(customerId, companyId, "Customer"));
            db.Agents.Add(new Agent(agentId, companyId, "alex", "Alex", "Sales Manager", "Sales", null, AgentSeniority.Senior, AgentStatus.Active));
            var session = new SalesMeetingSession(sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, null, customerId,
                "Confirm fit", "Operations leaders", 30, null, "provider", SalesMeetingConsentStatus.Pending,
                SalesMeetingRetentionPolicy.Standard, 365, now, userId, now);
            db.SalesMeetingSessions.Add(session);
            if (withDeck)
            {
                session.TransitionTo(SalesMeetingSessionStatus.Presenting, 1, 0, null, null, userId, now);
                var deckId = Guid.NewGuid(); var deck = new SalesPresentationDeck(deckId, companyId, sessionId, agentId, 1, "Deck", "deck.pptx",
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation", 100, new string('a', 64), "safe/deck.pptx", null, userId, now);
                deck.BeginProcessing(now, TimeSpan.FromMinutes(1)); deck.MarkProcessed(1, "test", "1", "static", 1, now); deck.Activate(now); db.SalesPresentationDecks.Add(deck);
                db.SalesPresentationSlides.Add(new SalesPresentationSlide(Guid.NewGuid(), companyId, deckId, 1, 1, "Operations", "Unified operations across finance, sales, and support.",
                    "Approved presenter notes.", "safe/1.svg", null, 1600, 900, 1, 1, new string('b', 64), "Explain unified operations", 45, "Continue", now));
            }
            await db.SaveChangesAsync();
            return new(db, companyId, userId, sessionId, agentId, now, new RecordingReasoning());
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class EmptyKnowledge : ICompanyKnowledgeSearchService
    {
        public Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchAsync(CompanyKnowledgeSemanticSearchQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CompanyKnowledgeSearchResultDto>>([]);
    }
    private sealed class RecordingReasoning : IAgentReasoningGateway
    {
        public Func<AgentReasoningRequest, AgentReasoningResult> ResultFactory { get; set; } = _ => throw new InvalidOperationException("Configure the result.");
        public AgentReasoningRequest? Request { get; private set; }
        public Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken cancellationToken) { Request = request; return Task.FromResult(ResultFactory(request)); }
        public Task<AgentReasoningResult?> GetRunAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken) => Task.FromResult<AgentReasoningResult?>(null);
    }
    private sealed class AllowingAuthority(Guid companyId, Guid agentId) : IAgentEffectiveAuthorityResolver
    {
        public Task<AgentEffectiveAuthorityDto> ResolveAsync(Guid requestedCompanyId, Guid requestedAgentId, CancellationToken cancellationToken)
        {
            var tools = new[]
            {
                Tool(SalesMeetingCaptureToolNames.ReadContext, "read"), Tool(SalesMeetingCaptureToolNames.SearchApprovedKnowledge, "read"), Tool(SalesMeetingCaptureToolNames.AnswerQuestion, "recommend")
            };
            return Task.FromResult(new AgentEffectiveAuthorityDto(companyId, agentId, "Alex", "Sales", "active", true, "level_0", "v1", "hash", [], [], tools, DateTime.UtcNow));
        }
        private static EffectiveAgentToolAuthorityDto Tool(string name, string action) => new(name, "1.0.0", action, "sales", AgentCapabilityStates.Available, AgentAuthorityReasonCodes.Available, "Allowed", "configured", "v1", [], []);
    }
    private sealed class TestContext(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId; public Guid? UserId { get; private set; } = userId; public bool IsResolved => true; public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { Membership = value; CompanyId = value?.CompanyId; UserId = value?.UserId; }
    }
}
