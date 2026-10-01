using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesMeetingCaptureServiceTests
{
    [Fact]
    public async Task Independent_claim_review_keeps_relevant_supported_claim_and_rejects_generic_overview()
    {
        await using var f = await Fixture.CreateAsync();
        f.Reasoning.AcceptedClaimOrders = [1];
        f.Reasoning.ResultFactory = r => new(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "Ignored summary",
            [new("Virtual Company offers finance and sales agents.", "fact", .9m, [r.Sources.First().Id]),
             new("Company setup requires administrator approval.", "fact", .9m, [r.Sources.First().Id])],
            .9m, [], [], [], []);
        var answer = await f.Questions.AskAsync(f.CompanyId, f.UserId, f.SessionId,
            new(Guid.NewGuid(), 1, f.AgentId, "How is onboarding done?"), null, default);
        Assert.Equal("partially_supported", answer!.Status);
        Assert.Contains("administrator approval", answer.Answer);
        Assert.DoesNotContain("sales agents", answer.Answer);
        Assert.Single(answer.Evidence);
        Assert.Contains(JsonSerializer.Serialize(answer.Question), f.Reasoning.ValidationRequest!.Instruction);
        Assert.True(answer.Answer!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 60);
        f.Reasoning.AcceptedClaimOrders = [];
        var none = await f.Questions.AskAsync(f.CompanyId, f.UserId, f.SessionId,
            new(Guid.NewGuid(), 2, f.AgentId, "What is the onboarding price?"), null, default);
        Assert.Equal("unverified", none!.Status);
        Assert.Equal(SalesMeetingQuestion.SafeNoEvidenceLimitation, none.Answer);
        Assert.Empty(none.Evidence);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("changed_source")]
    [InlineData("revoked_source")]
    [InlineData("changed_answer")]
    [InlineData("version")]
    [InlineData("company")]
    public async Task Exact_source_content_and_answer_are_rechecked_before_release(string change)
    {
        await using var f = await Fixture.CreateAsync();
        var agent = await f.Db.Agents.SingleAsync();
        f.Reasoning.ResultFactory = r => new(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "",
            [new("Setup requires administrator approval.", "fact", .9m,
                [r.Sources.Single(x => x.Type == "approved_agent_brief" && x.Title == "Company policies").Id])], .9m, [], [], [], []);
        var answer = await f.Questions.AskAsync(f.CompanyId, f.UserId, f.SessionId,
            new(Guid.NewGuid(), 1, f.AgentId, "How does setup work?"), null, default);
        if (change is "changed_source" or "revoked_source")
        {
            // Same stable source ID; content changes must not pass an ID-only check.
            agent.CommunicationProfile["briefing"]![AgentBriefingCategories.Policies] =
                change == "revoked_source" ? "" : "Only invited companies may set up.";
            f.Db.Entry(agent).Property(x => x.CommunicationProfile).IsModified = true;
        }
        if (change == "changed_answer")
            (await f.Db.SalesMeetingQuestions.SingleAsync()).Complete("Different answer.", .9m, false,
                Guid.NewGuid(), true, f.Now);
        await f.Db.SaveChangesAsync();
        if (change == "company")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Questions.ValidateEvidenceAsync(Guid.NewGuid(),
                f.UserId, f.SessionId, answer!.Id, answer.Version, default));
        else Assert.Equal(change == "valid", await f.Questions.ValidateEvidenceAsync(f.CompanyId, f.UserId,
            f.SessionId, answer!.Id, answer.Version + (change == "version" ? 1 : 0), default));
    }

    [Theory]
    [InlineData("spoken", true)]
    [InlineData("general_spoken", true)]
    [InlineData("withheld", false)]
    [InlineData("different_owner", false)]
    [InlineData("withdrawn_retention", false)]
    [InlineData("different_participant", false)]
    public async Task Follow_up_retrieval_preserves_actual_question_and_only_authorized_delivered_context(string scenario, bool included)
    {
        await using var f = await Fixture.CreateAsync();
        var now = f.Now;
        var room = new SalesBrowserRoom(f.CompanyId, f.SessionId, f.UserId, now.AddHours(1), now);
        room.Provisioned("test"); room.Start(now, 60);
        room.StartAgent(f.AgentId, f.UserId, Guid.NewGuid(), now.AddMinutes(1), now);
        var participant = new SalesRoomParticipant(f.CompanyId, room.Id, "Host", null, room.ExpiresUtc, f.UserId);
        participant.Consent("ai_processing", true); participant.Consent("retained_transcript", true);
        f.Db.SalesBrowserRooms.Add(room); f.Db.SalesRoomParticipants.Add(participant);
        var priorId = Guid.NewGuid(); var currentId = Guid.NewGuid();
        AddRaw(priorId, "How do you onboard a company?", now.AddSeconds(-10));
        AddRaw(currentId, "How long does that take, including approvals?", now);
        var prior = new SalesMeetingQuestion(Guid.NewGuid(), f.CompanyId, f.SessionId, priorId, 1, f.AgentId,
            "How do you onboard a company?", SalesMeetingSpeakerType.Customer, "Host",
            SalesMeetingInputSource.BrowserRoom, null, 1, f.UserId, now.AddSeconds(-10));
        prior.Complete("Connect the approved sources after administrator approval.", .9m, false, Guid.NewGuid(), true, now);
        f.Db.SalesMeetingQuestions.Add(prior);
        var played = new SalesRoomAgentSpeech(Guid.NewGuid(), f.CompanyId, room.Id, f.SessionId,
            scenario == "general_spoken" ? priorId : Guid.NewGuid(),
            f.AgentId, room.AgentGeneration + (scenario == "different_owner" ? 1 : 0), room.AgentTurnGeneration,
            scenario == "general_spoken" ? SalesRoomAgentSpeechKinds.Conversation : SalesRoomAgentSpeechKinds.Answer,
            f.UserId, now.AddSeconds(-5), questionId: scenario == "general_spoken" ? null : prior.Id);
        played.Claim(now.AddSeconds(-5));
        if (scenario == "withheld") played.Fail(SalesRoomAgentSpeechStates.Withheld, "test", "Not heard", now.AddSeconds(-1));
        else played.Complete(prior.AnswerText, null, "test", 100, now.AddSeconds(-1));
        f.Db.SalesRoomAgentSpeech.Add(played);
        if (scenario == "withdrawn_retention") participant.Consent("retained_transcript", false);
        await f.Db.SaveChangesAsync();
        f.Reasoning.ResultFactory = _ => new(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0", "", [], 0, [], [], [], []);
        var answer = await f.Questions.AskAsync(f.CompanyId, f.UserId, f.SessionId,
            new(currentId, 2, f.AgentId, "How long does that take, including approvals?", InputSource: "browser_room"), null, default);
        Assert.Equal("How long does that take, including approvals?", answer!.Question);
        Assert.StartsWith(answer.Question, Assert.Single(f.Knowledge.Queries).QueryText);
        Assert.Equal(included, f.Reasoning.Request!.Instruction.Contains(played.Id.ToString(), StringComparison.Ordinal));
        Assert.Equal(included, f.Knowledge.Queries[0].QueryText.Contains("administrator approval", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Reasoning.Request.Sources, x => x.Snippet == played.ReleasedText);

        void AddRaw(Guid id, string text, DateTime started)
        {
            f.Db.SalesMeetingTranscriptSegments.Add(new(id, f.CompanyId, f.SessionId, id,
                id == priorId ? 1 : 2, SalesMeetingSpeakerType.Customer, "Host", SalesMeetingInputSource.BrowserRoom,
                text, started, started, null, SalesMeetingReviewState.Unreviewed, f.UserId, id, now));
            f.Db.SalesRoomAgentTranscripts.Add(new(f.CompanyId, room.Id,
                scenario == "different_participant" && id == priorId ? Guid.NewGuid() : participant.Id,
                participant.Version, "track", 1, started, started, false, text, now, id, id,
                room.AgentGeneration, participant.Generation));
        }
    }
}
