using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesMeetingCaptureServiceTests
{
    [Fact]
    public void Sales_answer_is_bounded_without_cutting_claim_qualifiers_or_using_summary()
    {
        var longClaim = string.Join(" ", Enumerable.Repeat("detail", 90));
        var result = new AgentReasoningResult(Guid.NewGuid(), AgentAiRunStatuses.NeedsReview, "1",
            "Unsupported summary.",
            [new("Onboarding starts with company setup, subject to administrator approval.", "fact", .9m, ["source"]),
             new(longClaim, "fact", .9m, ["source"]),
             new("Then connect your approved knowledge sources.", "fact", .9m, ["source"])],
            .9m, [], ["Exact timeline.", longClaim], [], []);
        var answer = SalesMeetingAnswerGrounding.Compose(result, new HashSet<string> { "source" });
        Assert.True(answer.Partial);
        Assert.Equal(2, answer.Claims.Count);
        Assert.Contains("subject to administrator approval", answer.Text);
        Assert.Contains("Exact timeline", answer.Text);
        Assert.DoesNotContain("Unsupported summary", answer.Text);
        Assert.DoesNotContain("detail detail", answer.Text);
        Assert.True(answer.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 100);
    }

    [Fact]
    public async Task Partial_answer_keeps_supported_claims_and_limitations_private_until_versioned_host_approval()
    {
        await using var f = await Fixture.CreateAsync(withDeck: true);
        f.Reasoning.ResultFactory = request => new(Guid.NewGuid(), AgentAiRunStatuses.NeedsReview, "1.0.0",
            "Unsupported summary promises onboarding tomorrow.",
            [new("Onboarding includes company setup.", "confirmed_fact", .95m, [request.Sources.First().Id]),
             new("Onboarding is free.", "confirmed_fact", .9m, ["foreign-document"])],
            .95m, [], ["Exact onboarding timeline."], [], []);
        var answer = await f.Questions.AskAsync(f.CompanyId, f.UserId, f.SessionId,
            new(Guid.NewGuid(), 1, f.AgentId, "How does onboarding work?"), null, default);
        Assert.Equal("partially_supported", answer!.Status);
        Assert.True(answer.FollowUpRequired);
        Assert.Equal("private", answer.Visibility);
        Assert.Contains("company setup", answer.Answer);
        Assert.Contains("Exact onboarding timeline", answer.Answer);
        Assert.DoesNotContain("tomorrow", answer.Answer);
        Assert.DoesNotContain("free", answer.Answer);
        Assert.Single(answer.Evidence);
        Assert.Empty(await f.Questions.ListStageAnswersAsync(f.CompanyId, f.UserId, f.SessionId, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Questions.ApproveForStageAsync(Guid.NewGuid(), f.UserId, f.SessionId, answer.Id, answer.Version, null, default));
        await Assert.ThrowsAsync<SalesMeetingCaptureConflictException>(() => f.Questions.ApproveForStageAsync(f.CompanyId, f.UserId, f.SessionId, answer.Id, answer.Version - 1, null, default));
        await f.Questions.ApproveForStageAsync(f.CompanyId, f.UserId, f.SessionId, answer.Id, answer.Version, null, default);
        Assert.Single(await f.Questions.ListStageAnswersAsync(f.CompanyId, f.UserId, f.SessionId, default));
        var entity = await f.Db.SalesMeetingQuestions.SingleAsync();
        entity.Complete("Revised supported answer. Timing requires follow-up.", .9m, true, Guid.Empty, true, f.Now, true);
        Assert.Equal(SalesMeetingAnswerVisibility.Private, entity.Visibility);
        Assert.Null(entity.StageApprovedUtc);
        await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Questions.ListStageAnswersAsync(f.CompanyId, f.UserId, f.SessionId, default));
    }

    [Fact]
    public async Task Failed_reasoning_is_not_reported_as_missing_evidence()
    {
        await using var f = await Fixture.CreateAsync(withDeck: true);
        f.Reasoning.ResultFactory = _ => new(Guid.NewGuid(), AgentAiRunStatuses.Failed, "1", "", [], 0, [], [], [], []);
        var answer = await f.Questions.AskAsync(f.CompanyId, f.UserId, f.SessionId,
            new(Guid.NewGuid(), 1, f.AgentId, "How does onboarding work?"), null, default);
        Assert.Equal("failed", answer!.Status);
        Assert.Null(answer.Answer);
        Assert.Equal("provider_unavailable", answer.FailureCode);
    }

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
        Assert.Contains(System.Text.Json.JsonSerializer.Serialize("Does this connect our operating teams?"),
            fixture.Reasoning.Request.Instruction);
        Assert.DoesNotContain(fixture.Reasoning.Request.Sources, x => x.Snippet == "Does this connect our operating teams?");

        var approved = await fixture.Questions.ApproveForStageAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            answer.Id, answer.Version, "corr", CancellationToken.None);
        var stage = await fixture.Questions.ListStageAnswersAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId, CancellationToken.None);
        Assert.Equal("approved_for_stage", approved!.Visibility);
        Assert.Single(stage);
        Assert.DoesNotContain("Evidence", System.Text.Json.JsonSerializer.Serialize(stage), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Grounded_answer_receives_agent_brief_and_confirmed_artifacts_from_the_whole_meeting()
    {
        await using var fixture = await Fixture.CreateAsync(withDeck: true);
        var deck = await fixture.Db.SalesPresentationDecks.SingleAsync();
        fixture.Db.SalesMeetingArtifacts.Add(new SalesMeetingArtifact(Guid.NewGuid(), fixture.CompanyId,
            fixture.SessionId, deck.Id, null, 1, SalesMeetingArtifactType.BriefPositioning,
            "product_positioning", 0, "The governed product description applies across the meeting.",
            SalesMeetingArtifactClassification.ConfirmedFact, "knowledge:product-catalog", null, fixture.Now));
        await fixture.Db.SaveChangesAsync();
        fixture.Reasoning.ResultFactory = request =>
        {
            var source = request.Sources.Single(x => x.Type == "approved_agent_brief" && x.Title == "Products and services");
            Assert.Contains(request.Sources, x => x.Type == "approved_agent_role_brief");
            Assert.Contains(request.Sources, x => x.Type == "approved_meeting_artifact" &&
                x.Title.Contains("product_positioning", StringComparison.Ordinal));
            return new(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "The product brief is available.",
                [new("The product brief is available.", "confirmed_fact", .9m, [source.Id])], .9m, [], [], [], [source.Id]);
        };

        var answer = await fixture.Questions.AskAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(Guid.NewGuid(), 1, fixture.AgentId, "What does the company sell?"), null, CancellationToken.None);

        Assert.Equal("completed", answer!.Status);
        Assert.Single(answer.Evidence);
        Assert.Equal("approved_agent_brief", answer.Evidence[0].SourceType);
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

    [Fact]
    public async Task Meeting_answers_search_agent_scoped_repository_evidence_again_for_each_question()
    {
        await using var fixture = await Fixture.CreateAsync();
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        fixture.Knowledge.Results = [new(chunkId, "Standard onboarding takes ten working days.", .95,
            documentId, "OneDrive onboarding policy", 0, "policy.md#chunk-1", new Dictionary<string, JsonNode?>(),
            new(documentId, "OneDrive onboarding policy", "reference", "microsoft365_repository", null, chunkId, 0, "policy.md#chunk-1"),
            new(documentId, "OneDrive onboarding policy", "reference", "microsoft365_repository", null))];
        fixture.Reasoning.ResultFactory = request =>
        {
            var source = Assert.Single(request.Sources.Where(x => x.Type == "approved_company_knowledge"));
            return new(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "Onboarding takes ten working days.",
                [new("Onboarding takes ten working days.", "confirmed_fact", .95m, [source.Id])], .95m, [], [], [], [source.Id]);
        };

        var answer = await fixture.Questions.AskAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(Guid.NewGuid(), 1, fixture.AgentId, "How long does onboarding take?"), null, default);

        Assert.Equal("completed", answer!.Status);
        Assert.Equal("OneDrive onboarding policy", Assert.Single(answer.Evidence).SourceTitle);
        var query = Assert.Single(fixture.Knowledge.Queries);
        Assert.Equal(fixture.CompanyId, query.CompanyId);
        Assert.Equal(fixture.AgentId, query.AccessContext!.AgentId);
        Assert.Equal(fixture.UserId, query.AccessContext.UserId);
        Assert.Null(query.AllowedDocumentIds); // Search all authorized sources, not a fixed deck/document list.

        // The shared retrieval boundary may withhold revoked, stale or unavailable sources.
        fixture.Knowledge.Results = [];
        await Assert.ThrowsAsync<SalesMeetingCaptureConflictException>(() => fixture.Questions.ApproveForStageAsync(
            fixture.CompanyId, fixture.UserId, fixture.SessionId, answer.Id, answer.Version, null, default));
        fixture.Knowledge.Queries.Clear();
        fixture.Reasoning.ResultFactory = request =>
        {
            Assert.DoesNotContain(request.Sources, x => x.Type == "approved_company_knowledge");
            return new(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "Not verified", [], 0m, [], [], [], []);
        };
        var unavailable = await fixture.Questions.AskAsync(fixture.CompanyId, fixture.UserId, fixture.SessionId,
            new(Guid.NewGuid(), 2, fixture.AgentId, "Can you confirm the onboarding policy again?"), null, default);
        Assert.Single(fixture.Knowledge.Queries);
        Assert.True(unavailable!.FollowUpRequired);
        Assert.Empty(unavailable.Evidence);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Questions.AskAsync(Guid.NewGuid(),
            fixture.UserId, fixture.SessionId, new(Guid.NewGuid(), 3, fixture.AgentId, "Read the policy"), null, default));
        Assert.Single(fixture.Knowledge.Queries);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, Guid companyId, Guid userId, Guid sessionId, Guid agentId,
            DateTime now, RecordingReasoning reasoning)
        {
            Db = db; CompanyId = companyId; UserId = userId; SessionId = sessionId; AgentId = agentId; Now = now; Reasoning = reasoning;
            Capture = new SalesMeetingCaptureService(db, TimeProvider.System);
            Questions = new SalesMeetingQuestionAnsweringService(db, Knowledge, reasoning,
                new AllowingAuthority(companyId, agentId), TimeProvider.System, NullLogger<SalesMeetingQuestionAnsweringService>.Instance);
        }
        public VirtualCompanyDbContext Db { get; }
        public SalesMeetingCaptureService Capture { get; }
        public SalesMeetingQuestionAnsweringService Questions { get; }
        public RecordingReasoning Reasoning { get; }
        public RecordingKnowledge Knowledge { get; } = new();
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
            var communicationProfile = new Dictionary<string, JsonNode?>
            {
                ["briefing"] = new JsonObject
                {
                    [AgentBriefingCategories.CompanyInformation] = "Capture Company provides governed business operations.",
                    [AgentBriefingCategories.ProductsAndServices] = "The product coordinates finance, sales, and support workflows.",
                    [AgentBriefingCategories.Policies] = "Use only approved evidence for customer-visible claims.",
                    [AgentBriefingCategories.CustomerSupport] = "Escalate claims that cannot be verified.",
                    [AgentBriefingCategories.OtherInstructions] = "Keep answers concise."
                }
            };
            db.Agents.Add(new Agent(agentId, companyId, "alex", "Alex", "Sales Manager", "Sales", null,
                AgentSeniority.Senior, AgentStatus.Active, roleBrief: "Explain the approved company and product context.",
                communicationProfile: communicationProfile));
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

    private sealed class RecordingKnowledge : ICompanyKnowledgeSearchService
    {
        public IReadOnlyList<CompanyKnowledgeSearchResultDto> Results { get; set; } = [];
        public List<CompanyKnowledgeSemanticSearchQuery> Queries { get; } = [];
        public Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchAsync(CompanyKnowledgeSemanticSearchQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(Results);
        }
    }
    private sealed class RecordingReasoning : IAgentReasoningGateway
    {
        public Func<AgentReasoningRequest, AgentReasoningResult> ResultFactory { get; set; } = _ => throw new InvalidOperationException("Configure the result.");
        public AgentReasoningRequest? Request { get; private set; }
        public int[]? AcceptedClaimOrders;
        public AgentReasoningRequest? ValidationRequest;
        private AgentReasoningResult? proposal;
        public Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken cancellationToken)
        {
            if (request.PromptVersion == "sales-meeting-claim-validation-v1")
            {
                ValidationRequest = request;
                var accepted = AcceptedClaimOrders ?? Enumerable.Range(0, proposal!.Claims.Count).ToArray();
                return Task.FromResult(new AgentReasoningResult(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0",
                    "", [], 1, [], [], [], [], StructuredResult: new JsonObject
                    { ["resultVersion"] = "1.0.0", ["state"] = "ready",
                        ["acceptedClaimOrders"] = new JsonArray(accepted.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) }));
            }
            Request = request; proposal = ResultFactory(request); return Task.FromResult(proposal);
        }
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
