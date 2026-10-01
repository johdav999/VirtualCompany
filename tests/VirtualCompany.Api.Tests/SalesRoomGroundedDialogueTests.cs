using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Fact]
    public async Task Retrieval_provider_failure_is_not_missing_evidence_and_next_question_remains_available()
    {
        await using var f = await Fixture.Create(true, true);
        await DialogueTurn(f);
        f.Answerer.ProviderFailed = true;
        await f.Ask("How is onboarding done?", f.Answerer);
        f.Db.ChangeTracker.Clear();
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        Assert.Equal("provider_unavailable", room.AgentLastErrorCode);
        Assert.Equal(SalesRoomAgentHealthStates.Ready, room.AgentHealth);
        Assert.False(await f.Db.SalesRoomAgentSpeech.AnyAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Answer));
        Assert.False(f.Media.Disposed);
        f.Answerer.ProviderFailed = false;
        f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(1);
        await f.Ask("What can the finance agent do?", f.Answerer);
        var speech = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Answer);
        await f.Play(speech.Id);
        Assert.Equal(1, f.Media.Completions);
    }
    [Theory]
    [InlineData("autonomous")]
    [InlineData("manual")]
    [InlineData("assisted")]
    public async Task Indexed_document_question_uses_real_retrieval_evidence_and_only_autonomous_mode_auto_speaks(string mode)
    {
        await using var f = await Fixture.Create(true, true);
        var service = await IndexedAnswerService(f);
        await RetainQuestion(f, "How is onboarding done?");
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        if (mode != "autonomous") floor.SetMode(mode, floor.PresentationVersion, f.Source.Clock.Now, true);
        await f.Db.SaveChangesAsync();
        await f.Ask("How is onboarding done?", service);
        f.Db.ChangeTracker.Clear();
        var answer = await f.Db.SalesMeetingQuestions.Include(x => x.Evidence).SingleAsync();
        Assert.Equal("How is onboarding done?", answer.QuestionText);
        Assert.Equal(SalesMeetingQuestionStatus.PartiallySupported, answer.Status);
        Assert.Contains("administrator approval", answer.AnswerText);
        Assert.Contains("timeline", answer.AnswerText);
        Assert.DoesNotContain("knowledge-chunk:", answer.AnswerText);
        Assert.True(answer.AnswerText!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 60);
        Assert.Equal("Indexed onboarding policy", Assert.Single(answer.Evidence).SourceTitle);
        Assert.Equal("approved_company_knowledge", answer.Evidence.First().SourceType);
        var queued = await f.Db.SalesRoomAgentSpeech.SingleOrDefaultAsync(x => x.QuestionId == answer.Id);
        if (mode == "autonomous")
        {
            Assert.Equal(SalesMeetingAnswerVisibility.ApprovedForStage, answer.Visibility);
            Assert.NotNull(queued);
            await f.Play(queued.Id);
            f.Db.ChangeTracker.Clear();
            var receipt = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == queued.Id);
            Assert.Equal(SalesRoomAgentSpeechStates.Spoken, receipt.Status);
            Assert.Equal(answer.AnswerText, receipt.ReleasedText);
            Assert.Equal(100, receipt.DurationMilliseconds);
            Assert.Equal(100, f.Media.DeliveredMilliseconds(receipt.TurnGeneration));
            Assert.Contains(answer.Evidence.First().SourceId, receipt.EvidenceJson);
            Assert.Equal(SalesRoomAgentHealthStates.Ready, (await f.Db.SalesBrowserRooms.SingleAsync()).AgentHealth);
            Assert.False(f.Media.Disposed);
            var owner = await f.Db.SalesBrowserRooms.SingleAsync();
            Assert.True(owner.RenewAgentLease(f.Work.LeaseOwnerId, f.Work.Generation,
                f.Source.Clock.Now.AddMinutes(2), f.Source.Clock.Now));
            await f.Db.SaveChangesAsync();
            f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(50);
            // Availability does not expire 45 seconds after an answer, nor require a bridge.
            Assert.NotNull(await f.FreshInput("Can you continue the presentation?"));
        }
        else
        {
            Assert.Null(queued);
            Assert.Equal(SalesMeetingAnswerVisibility.Private, answer.Visibility);
            Assert.Equal(0, f.Media.Completions);
            await service.ApproveForStageAsync(f.Source.Company, f.Source.Actor, f.Source.Session,
                answer.Id, answer.ConcurrencyVersion, null, default);
            f.Db.ChangeTracker.Clear();
            Assert.Equal(SalesMeetingAnswerVisibility.ApprovedForStage, (await f.Db.SalesMeetingQuestions.SingleAsync()).Visibility);
            Assert.Equal(0, f.Media.Completions); // Approval alone does not bypass the explicit speech action.
            var currentRoom = await f.Db.SalesBrowserRooms.SingleAsync();
            var approved = await f.Db.SalesMeetingQuestions.SingleAsync();
            var command = Guid.NewGuid();
            await ConversationService(f, f.Db).SpeakAnswerAsync(f.Source.Company, f.Source.Actor, f.Work.RoomId,
                new(command, currentRoom.Version, approved.Id, approved.ConcurrencyVersion), default);
            var speech = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.CommandId == command);
            await f.Play(speech.Id);
            Assert.Equal(1, f.Media.Completions);
        }
    }

    [Theory]
    [InlineData("mismatch")]
    [InlineData("provider")]
    [InlineData("source")]
    [InlineData("revoked_after_generation")]
    public async Task Withheld_answer_keeps_session_available_and_fresh_question_never_replays_old_audio(string failure)
    {
        await using var f = await Fixture.Create(true, true);
        var service = await IndexedAnswerService(f);
        await RetainQuestion(f, "How is onboarding done?");
        await f.Ask("How is onboarding done?", service);
        var first = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Answer);
        if (failure == "mismatch") f.Source.Speech.Match = false;
        if (failure == "provider") f.Source.Speech.Fail = true;
        if (failure == "source") f.Answerer.EvidenceValid = false;
        if (failure == "source") f.Answerer.RealService = null;
        if (failure == "revoked_after_generation") f.Source.Speech.BeforeReturn = async () =>
        {
            await using var db = f.OtherDb();
            (await db.CompanyKnowledgeChunks.SingleAsync()).Deactivate();
            await db.SaveChangesAsync();
        };
        await f.Play(first.Id);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRoomAgentSpeechStates.Withheld, (await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == first.Id)).Status);
        Assert.Equal(0, f.Media.SentFrames);
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        Assert.Equal(SalesRoomAgentHealthStates.Ready, room.AgentHealth);
        Assert.True(room.IsAgentOwner(f.Work.LeaseOwnerId, f.Work.Generation, f.Source.Clock.Now));
        Assert.False(f.Media.Disposed);
        f.Source.Speech.Match = true; f.Source.Speech.Fail = false; f.Source.Speech.BeforeReturn = null;
        f.Answerer.EvidenceValid = true; f.Answerer.RealService = null;
        f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(1);
        await f.Ask("What can the finance agent do?", f.Answerer);
        f.Db.ChangeTracker.Clear();
        var second = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Answer && x.Id != first.Id);
        await f.Play(second.Id);
        await f.Play(first.Id);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(1, f.Media.Completions);
        Assert.Equal("The finance agent prepares work for human review.",
            (await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == second.Id)).ReleasedText);
    }

    private static async Task RetainQuestion(Fixture f, string question)
    {
        await DialogueTurn(f);
        f.Db.SalesRoomConsents.Add(new(await f.Db.SalesRoomParticipants.SingleAsync(), "retained_transcript", true,
            "test", f.Source.Clock.Now.AddSeconds(-1)));
        await f.Db.SaveChangesAsync();
        await using var db = f.OtherDb();
        var participant = await db.SalesRoomParticipants.SingleAsync();
        Assert.NotNull(await new SalesRoomCaptureService(db, f.Source.Clock).RetainAsync(new(f.Source.Company,
            f.Work.RoomId, participant.Id, participant.Version, true, f.Work.Generation, f.Work.LeaseOwnerId,
            "test-mic", 1, f.Source.Clock.Now, f.Source.Clock.Now, false, question), default));
    }

    // Real SQL-backed indexed chunk retrieval/access policy and production answering/worker.
    // Embeddings, semantic claim review and speech/media are deterministic provider seams.
    private static async Task<SalesMeetingQuestionAnsweringService> IndexedAnswerService(Fixture f)
    {
        const string content = "Onboarding starts with company setup and administrator approval, followed by connecting approved knowledge sources. The company provides access to third-party systems. No fixed rollout timeline is recorded.";
        var document = new CompanyKnowledgeDocument(Guid.NewGuid(), f.Source.Company, "Indexed onboarding policy",
            CompanyKnowledgeDocumentType.Policy, "test/policy.txt", null, "policy.txt", "text/plain", ".txt", 100,
            accessScope: new(f.Source.Company, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
        document.MarkScanClean(); document.MarkProcessing(); document.MarkProcessed();
        document.MarkIndexed(content, 1, 1, "test", "test", "1", 2, "fingerprint");
        f.Db.CompanyKnowledgeDocuments.Add(document);
        f.Db.CompanyKnowledgeChunks.Add(new(Guid.NewGuid(), f.Source.Company, document.Id, 1, 0, content,
            KnowledgeEmbeddingSerializer.Serialize([1f, 0f]), "test", "test", "1", 2,
            new Dictionary<string, JsonNode?>(), "policy#1"));
        await f.Db.SaveChangesAsync();
        var search = new CompanyKnowledgeSearchService(f.Db, new FixedEmbedding(), new NoAmbientMember(),
            new KnowledgeAccessPolicyEvaluator(), new RemoteKnowledgeSourceAvailabilityGate(f.Db, null!,
                Options.Create(new DocumentRepositoryOperationsOptions { Enabled = true }), NullLogger<RemoteKnowledgeSourceAvailabilityGate>.Instance));
        var service = new SalesMeetingQuestionAnsweringService(f.Db, search, new IndexedReasoning(), new DialogueAuthority(),
            f.Source.Clock, NullLogger<SalesMeetingQuestionAnsweringService>.Instance);
        f.Answerer.RealService = service;
        return service;
    }
    private sealed class FixedEmbedding : IEmbeddingGenerator
    {
        public Task<EmbeddingBatchResult> GenerateAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            Task.FromResult(new EmbeddingBatchResult("test", "test", "1", 2, texts.Select(_ => new EmbeddingVectorResult([1f, 0f])).ToArray()));
    }
    private sealed class NoAmbientMember : ICompanyMembershipContextResolver
    {
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken ct) => Task.FromResult<ResolvedCompanyMembershipContext?>(null);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid c, CancellationToken ct) => ResolveAsync(ct);
    }
    private sealed class IndexedReasoning : IAgentReasoningGateway
    {
        public Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken ct)
        {
            if (request.StructuredResultSchema is not null)
                return Task.FromResult(new AgentReasoningResult(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "",
                    [], 1, [], [], [], [], StructuredResult: new JsonObject { ["resultVersion"] = "1.0.0",
                        ["state"] = "ready", ["acceptedClaimOrders"] = new JsonArray(0) }));
            var source = Assert.Single(request.Sources.Where(x => x.Type == "approved_company_knowledge"));
            Assert.Contains("administrator approval", source.Snippet);
            return Task.FromResult(new AgentReasoningResult(Guid.NewGuid(), AgentAiRunStatuses.NeedsReview, "1.0.0", "Never speak this summary",
                [new("Onboarding starts with company setup and administrator approval, followed by connecting approved knowledge sources.",
                    "fact", .9m, [source.Id])], .9m, [], ["The rollout timeline"], [], []));
        }
        public Task<AgentReasoningResult?> GetRunAsync(Guid c, Guid a, Guid r, CancellationToken ct) => Task.FromResult<AgentReasoningResult?>(null);
    }
    private sealed class DialogueAuthority : IAgentEffectiveAuthorityResolver
    {
        public Task<AgentEffectiveAuthorityDto> ResolveAsync(Guid c, Guid a, CancellationToken ct) =>
            Task.FromResult(new AgentEffectiveAuthorityDto(c, a, "Alex", "Sales", "active", true, "level_0", "v1", "hash", [], [],
                new[] { SalesMeetingCaptureToolNames.ReadContext, SalesMeetingCaptureToolNames.SearchApprovedKnowledge, SalesMeetingCaptureToolNames.AnswerQuestion }
                    .Select(t => new EffectiveAgentToolAuthorityDto(t, "1.0.0", t == SalesMeetingCaptureToolNames.AnswerQuestion ? "recommend" : "read",
                        "sales", AgentCapabilityStates.Available, AgentAuthorityReasonCodes.Available, "Allowed", "configured", "v1", [], [])).ToArray(), DateTime.UtcNow));
    }
}
