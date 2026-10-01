using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomConversationStatusProjectionTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly RealtimeAgentHealth Healthy = new(true, true, true, "test", "test", "available");

    [Fact]
    public void Continuous_profile_keeps_paused_deck_listening_without_reply_deadline()
    {
        var (room, floor) = Active("autonomous");
        floor.PauseAt(20, room.AgentTurnGeneration, Now);
        var view = SalesRoomConversationStatusProjection.Project(room, floor, null, null, true, Healthy, null, null,
            Now.AddSeconds(70), continuous: true);
        Assert.Equal("presentation_paused", view.Phase);
        Assert.Equal("available", view.Availability);
        room.AgentReconnecting(room.AgentLeaseOwnerId!.Value, room.AgentGeneration);
        Assert.Equal("reconnecting", SalesRoomConversationStatusProjection.Project(room, floor, null, null, true, Healthy,
            room.AgentLastErrorCode, room.AgentLastErrorSummary, Now, continuous: true).Phase);
        room.StopAgent("host_stopped", null, Now);
        Assert.Equal("stopped", SalesRoomConversationStatusProjection.Project(room, floor, null, null, true, Healthy,
            null, null, Now, continuous: true).Phase);
    }

    [Fact]
    public void Ready_worker_with_stale_agent_floor_does_not_claim_audio_is_playing()
    {
        var (room, floor) = Active("autonomous");
        floor.AgentClaim(floor.ResponseGeneration, room.AgentTurnGeneration, Now);
        Assert.Equal("listening", SalesRoomConversationStatusProjection.Project(room, floor, null, null, true, Healthy,
            null, null, Now, continuous: true).Phase);
        room.AgentSpeaking(room.AgentLeaseOwnerId!.Value, room.AgentGeneration);
        Assert.Equal("presenting", SalesRoomConversationStatusProjection.Project(room, floor, null, null, true, Healthy,
            null, null, Now, continuous: true).Phase);
    }

    [Fact]
    public void Withheld_dialogue_keeps_ready_presence_and_actionable_reason()
    {
        var (room, floor) = Active("autonomous");
        room.ConversationReplyWithheld(room.AgentLeaseOwnerId!.Value, room.AgentGeneration);
        var view = Project(room, floor, code: "conversation_reply_withheld");
        Assert.Equal("ready", view.Phase);
        Assert.Equal("available", view.Availability);
    }

    [Fact]
    public void Rejected_question_projects_connected_ready_not_stopped()
    {
        var (room, floor) = Active("autonomous");
        room.AgentTurnFailed(room.AgentLeaseOwnerId!.Value, room.AgentGeneration, "question_rejected", "Please ask again.");
        floor.PauseAt(20, room.AgentTurnGeneration, Now);
        Assert.Equal("ready", Project(room, floor, code: "question_rejected").Phase);
        Assert.Equal("available", Project(room, floor, code: "question_rejected").Availability);
    }

    [Theory]
    [InlineData("manual", "host_invocation_required")]
    [InlineData("assisted", "confirmation_required")]
    public void Nonautonomous_turn_waits_for_host_approval(string mode, string expectedPending)
    {
        var (room, floor) = Active(mode);
        floor.ProposeTurn(Guid.NewGuid(), 1, true, false, Guid.NewGuid(), Now);
        Assert.Equal(expectedPending, floor.PendingTurnState);
        var view = Project(room, floor);
        Assert.Equal("awaiting_approval", view.Phase);
    }

    [Fact]
    public void Autonomous_turn_checks_sources_then_answers_without_approval_phase()
    {
        var (room, floor) = Active("autonomous");
        var question = Guid.NewGuid();
        floor.ProposeTurn(Guid.NewGuid(), 1, true, false, question, Now);
        var waiting = Project(room, floor, answer: new(question, "How?", null, "pending", "private_host", 1, []));
        Assert.Equal("checking_sources", waiting.Phase);
        var speech = Speech(room, floor, "answer");
        speech.Claim(Now);
        Assert.Equal("answering", Project(room, floor, speech).Phase);
        var completed = new SalesRoomAgentAnswerView(question, "How?", "Grounded answer", "completed", "private_host", 2, []);
        Assert.Equal("awaiting_recovery", Project(room, floor, answer: completed).Phase);
    }

    [Theory]
    [InlineData("bridge")]
    [InlineData("answer")]
    public void Played_answer_or_bridge_listens_only_for_current_lease_generation_and_reply_window(string kind)
    {
        var (room, floor) = Active("autonomous");
        var bridge = Speech(room, floor, kind);
        bridge.Claim(Now);
        bridge.Complete("May I continue?", "validated", "test", 400, Now);
        Assert.Equal("listening_for_reply", Project(room, floor, bridge).Phase);
        Assert.Equal("ready", Project(room, floor, bridge, at: Now.AddSeconds(46)).Phase);
        room.StopAgent("host_stopped", "Stopped", Now);
        Assert.NotEqual("listening_for_reply", Project(room, floor, bridge).Phase);
    }

    [Fact]
    public void Accepted_narration_is_queued_not_claimed_played()
    {
        var (room, floor) = Active("autonomous");
        floor.AgentClaim(floor.ResponseGeneration, room.AgentTurnGeneration, Now);
        Assert.Equal("resuming", Project(room, floor, Speech(room, floor, "narration")).Phase);
    }

    [Fact]
    public void Rollback_configuration_outage_quota_and_expiry_are_distinct()
    {
        var (room, floor) = Active("autonomous");
        Assert.Equal("off", Project(room, floor, enabled: false).Availability);
        Assert.Equal("configuration", Project(room, floor, provider: new(false, false, false, "test", "test", "degraded")).Availability);
        Assert.Equal("provider_unavailable", Project(room, floor, provider: new(true, true, false, "test", "test", "degraded")).Availability);
        Assert.Equal("quota", Project(room, floor, code: "quota_exceeded").Availability);
        room.Ended();
        Assert.Equal("expired", Project(room, floor).Availability);
    }

    [Fact]
    public void Confirmed_interruption_pauses_deck_not_the_healthy_continuous_conversation()
    {
        var (room, floor) = Active("autonomous");
        room.PreemptAgent(room.AgentLeaseOwnerId!.Value, room.AgentGeneration, "Question interrupted narration");
        floor.PauseAt(120, room.AgentTurnGeneration, Now);
        var view = SalesRoomConversationStatusProjection.Project(room, floor, null, null, true, Healthy,
            "human_speaking", room.AgentLastErrorSummary, Now, continuous: true);
        Assert.Equal("presentation_paused", view.Phase);
        Assert.Equal("available", view.Availability);
        var question = Guid.NewGuid();
        floor.ProposeTurn(Guid.NewGuid(), 1, true, false, question, Now);
        view = SalesRoomConversationStatusProjection.Project(room, floor, null,
            new(question, "How?", null, "pending", "private_host", 1, []), true, Healthy,
            "human_speaking", room.AgentLastErrorSummary, Now, continuous: true);
        Assert.Equal("checking_sources", view.Phase);
        room.TakeOverAgent("Host took over");
        Assert.Equal("paused", SalesRoomConversationStatusProjection.Project(room, floor, null, null, true, Healthy,
            "host_takeover", room.AgentLastErrorSummary, Now, continuous: true).Phase);
    }

    private static SalesRoomConversationStatusView Project(SalesBrowserRoom room, SalesRoomFloor floor,
        SalesRoomAgentSpeech? speech = null, SalesRoomAgentAnswerView? answer = null, bool enabled = true,
        RealtimeAgentHealth? provider = null, string? code = null, DateTime? at = null) =>
        SalesRoomConversationStatusProjection.Project(room, floor, speech, answer, enabled,
            provider ?? Healthy, code, code, at ?? Now);

    private static (SalesBrowserRoom Room, SalesRoomFloor Floor) Active(string mode)
    {
        var company = Guid.NewGuid(); var host = Guid.NewGuid(); var meeting = Guid.NewGuid();
        var room = new SalesBrowserRoom(company, meeting, host, Now.AddHours(1), Now);
        room.Provisioned("test"); room.Start(Now, 60);
        room.StartAgent(Guid.NewGuid(), host, Guid.NewGuid(), Now.AddMinutes(2), Now);
        room.AgentReady(room.AgentLeaseOwnerId!.Value, room.AgentGeneration);
        return (room, new SalesRoomFloor(company, room.Id, host, room.AgentTurnGeneration, 1, 1, 1, null, mode, Now));
    }

    private static SalesRoomAgentSpeech Speech(SalesBrowserRoom room, SalesRoomFloor floor, string kind) =>
        new(Guid.NewGuid(), room.CompanyId, room.Id, room.MeetingSessionId!.Value, Guid.NewGuid(),
            room.AgentId!.Value, room.AgentGeneration, floor.TurnGeneration, kind,
            room.OrganizerUserId, Now, kind == "narration" ? Guid.NewGuid() : null,
            kind == "narration" ? Guid.NewGuid() : null, kind == "narration" ? null : Guid.NewGuid(),
            responseGeneration: floor.ResponseGeneration);
}
