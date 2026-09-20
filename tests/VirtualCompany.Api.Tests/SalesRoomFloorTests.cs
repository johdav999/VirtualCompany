using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomFloorTests
{
    private readonly Guid company = Guid.NewGuid();
    private readonly Guid room = Guid.NewGuid();
    private readonly Guid host = Guid.NewGuid();
    private readonly Guid guest = Guid.NewGuid();
    private readonly DateTime now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("manual", SalesRoomPendingTurnStates.HostInvocationRequired)]
    [InlineData("assisted", SalesRoomPendingTurnStates.ConfirmationRequired)]
    [InlineData("autonomous", SalesRoomPendingTurnStates.Authorized)]
    public void Addressed_turns_follow_the_server_side_floor_mode(string mode, string expected)
    {
        var floor = Floor(mode);

        floor.ProposeTurn(guest, 2, true, false, Guid.NewGuid(), now.AddSeconds(1));

        Assert.Equal(expected, floor.PendingTurnState);
        Assert.Equal(SalesRoomFloorStates.Pending, floor.State);
        Assert.True(floor.PendingAddressedAgent);
    }

    [Fact]
    public void Overlap_never_authorizes_an_agent_answer()
    {
        var floor = Floor("autonomous");

        floor.ProposeTurn(guest, 1, true, true, Guid.NewGuid(), now.AddSeconds(1));

        Assert.Equal(SalesRoomPendingTurnStates.OverlapWaiting, floor.PendingTurnState);
        Assert.Equal(SalesRoomFloorStates.Overlap, floor.State);
    }

    [Fact]
    public void Takeover_fences_the_response_and_tracks_client_silence_receipts()
    {
        var floor = Floor("autonomous");
        floor.AgentClaim(floor.ResponseGeneration, 4, now.AddSeconds(1));
        var response = floor.ResponseGeneration;
        var stop = Guid.NewGuid();

        floor.TakeOver(host, floor.Version, stop, 2, now.AddMilliseconds(750), 5, 12, 3, 1,
            "slide:3:talking-point:1", now.AddSeconds(2));
        floor.AcknowledgeStop(stop, now.AddSeconds(2));
        floor.AcknowledgeStop(stop, now.AddSeconds(2));

        Assert.Equal(SalesRoomFloorStates.Host, floor.State);
        Assert.True(floor.ResponseGeneration > response);
        Assert.Equal(SalesRoomPlaybackStopStates.Acknowledged, floor.PlaybackStopState);
        Assert.Equal(2, floor.PlaybackStopAcknowledgedCount);
    }

    [Fact]
    public void Resume_rejects_a_moved_presentation_and_preserves_the_saved_offset()
    {
        var floor = Floor("manual");
        floor.PauseAt(1420, 5, now.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => floor.Resume(host, floor.Version, 11, 2, 0,
            "slide:2:talking-point:0", 6, now.AddSeconds(2)));
        Assert.Equal(1420, floor.ResumeOffsetMilliseconds);

        floor.Resume(host, floor.Version, 10, 2, 0, "slide:2:talking-point:0", 6, now.AddSeconds(3));
        Assert.Equal(SalesRoomFloorStates.Agent, floor.State);
        Assert.Equal(1420, floor.ResumeOffsetMilliseconds);
    }

    [Fact]
    public void Human_slide_change_synchronizes_the_floor_and_fences_stale_narration()
    {
        var floor = Floor("manual");
        var response = floor.ResponseGeneration;

        floor.PresentationMoved(host, 11, 3, 0, null, now.AddSeconds(1));

        Assert.Equal(SalesRoomFloorStates.Host, floor.State);
        Assert.Equal(3, floor.SlideNumber);
        Assert.Equal(0, floor.TalkingPointIndex);
        Assert.Equal(11, floor.PresentationVersion);
        Assert.True(floor.ResponseGeneration > response);
    }
    [Fact]
    public void Mode_change_updates_the_floor_policy_and_presentation_version()
    {
        var floor = Floor("manual");

        floor.SetMode("assisted", 11, now.AddSeconds(1));

        Assert.Equal("assisted", floor.ControlMode);
        Assert.Equal(11, floor.PresentationVersion);
    }

    [Fact]
    public void Assisted_narration_continues_to_the_next_point_without_returning_the_floor()
    {
        var floor = Floor("assisted");
        floor.AgentClaim(floor.ResponseGeneration, 4, now.AddSeconds(1));

        floor.AgentAdvanced(10, 2, 2, "slide:2:talking-point:2", 4, now.AddSeconds(2));

        Assert.Equal(SalesRoomFloorStates.Agent, floor.State);
        Assert.Equal(2, floor.TalkingPointIndex);
        Assert.Equal(2, floor.ResponseGeneration);
    }
    [Fact]
    public void Completed_manual_narration_returns_the_floor_and_advances_to_the_next_point()
    {
        var floor = Floor("manual");
        floor.AgentClaim(floor.ResponseGeneration, 4, now.AddSeconds(1));

        floor.AgentCompleted(host, now.AddSeconds(2), 2);

        Assert.Equal(SalesRoomFloorStates.Host, floor.State);
        Assert.Equal(host, floor.FloorOwnerParticipantId);
        Assert.Equal(2, floor.TalkingPointIndex);
        Assert.Equal("slide:2:talking-point:2", floor.ResumeMarker);
    }
    [Fact]
    public void Only_a_preauthorized_cohost_can_control_the_floor()
    {
        var cohost = Guid.NewGuid();
        var floor = Floor("assisted");
        floor.AuthorizeCoHost(cohost, now.AddSeconds(1));
        floor.ProposeTurn(guest, 1, true, false, Guid.NewGuid(), now.AddSeconds(2));

        Assert.Throws<UnauthorizedAccessException>(() => floor.ApprovePending(guest, floor.Version, now.AddSeconds(3)));
        floor.ApprovePending(cohost, floor.Version, now.AddSeconds(4));

        Assert.Equal(SalesRoomPendingTurnStates.Authorized, floor.PendingTurnState);
    }

    [Theory]
    [InlineData("Alex, what supports that delivery date?", "Alex", true)]
    [InlineData("Nora, hur fungerar implementationen?", "Nora", true)]
    [InlineData("What do you think about the delivery date?", "Alex", false)]
    [InlineData("Alex, thanks for the explanation.", "Alex", false)]
    public void Address_detection_distinguishes_agent_questions_from_human_dialogue(
        string text, string agentName, bool expected) =>
        Assert.Equal(expected, SalesRoomAgentWorker.IsAddressedQuestion(text, agentName));

    [Theory]
    [InlineData("What supports that delivery date?", true, 1, false, true)]
    [InlineData("Hur fungerar implementationen?", true, 1, false, true)]
    [InlineData("Find us agent two", true, 1, false, true)]
    [InlineData("Thanks for the explanation.", true, 1, false, false)]
    [InlineData("Okay", true, 1, false, false)]
    [InlineData("Fan noise", true, 1, false, false)]
    [InlineData("What supports that delivery date?", false, 1, false, true)]
    [InlineData("What supports that delivery date?", true, 2, false, false)]
    [InlineData("What supports that delivery date?", true, 1, true, false)]
    public void A_lone_human_question_or_substantive_interruption_is_implicitly_addressed(
        string text, bool interruptedAgent, int connectedHumans, bool overlapped, bool expected) =>
        Assert.Equal(expected, SalesRoomAgentWorker.ShouldTreatInterruptedSpeechAsAddressedQuestion(
            text, interruptedAgent, connectedHumans, overlapped));

    [Fact]
    public void Active_microphone_speaker_counts_as_connected_when_durable_presence_is_stale()
    {
        var activeSpeaker = Guid.NewGuid();
        var disconnectedGuest = Guid.NewGuid();

        var connected = SalesRoomAgentWorker.CountConnectedHumans(
            [activeSpeaker, disconnectedGuest], activeSpeaker, _ => false);

        Assert.Equal(1, connected);
    }

    [Fact]
    public void Another_live_participant_prevents_implicit_question_addressing()
    {
        var activeSpeaker = Guid.NewGuid();
        var connectedGuest = Guid.NewGuid();

        var connected = SalesRoomAgentWorker.CountConnectedHumans(
            [activeSpeaker, connectedGuest], activeSpeaker, id => id == connectedGuest);

        Assert.Equal(2, connected);
        Assert.False(SalesRoomAgentWorker.ShouldTreatInterruptedSpeechAsAddressedQuestion(
            "What does that mean?", true, connected, false));
    }

    [Fact]
    public void Response_control_reports_the_interruption_before_cancellation_changes_room_state()
    {
        var control = new SalesRoomAgentRunControl();
        var response = control.BeginResponse(default);

        Assert.True(control.CancelResponse());
        Assert.True(response.IsCancellationRequested);
        Assert.False(control.CancelResponse());

        control.EndResponse(response);
        Assert.False(control.CancelResponse());
    }
    [Fact]
    public void Transcript_correlation_completes_when_commit_arrives_first()
    {
        var correlation = new SalesRoomTranscriptCorrelation<string>();
        correlation.Enqueue("organizer utterance");

        Assert.False(correlation.Commit("item-1", out _, out _));
        Assert.True(correlation.Complete("item-1", "What is the price?", out var context, out var text));
        Assert.Equal("organizer utterance", context);
        Assert.Equal("What is the price?", text);
        Assert.Equal(0, correlation.PendingCount);
    }

    [Fact]
    public void Transcript_correlation_completes_when_provider_completion_arrives_first()
    {
        var correlation = new SalesRoomTranscriptCorrelation<string>();
        correlation.Enqueue("organizer utterance");

        Assert.False(correlation.Complete("item-1", "What is the price?", out _, out _));
        Assert.True(correlation.Commit("item-1", out var context, out var text));
        Assert.Equal("organizer utterance", context);
        Assert.Equal("What is the price?", text);
        Assert.False(correlation.Complete("item-1", "duplicate", out _, out _));
        Assert.Equal(0, correlation.PendingCount);
    }
    [Theory]
    [InlineData(41, 42, true)]
    [InlineData(42, 42, true)]
    [InlineData(43, 42, false)]
    [InlineData(0, 42, false)]
    public void Speech_commands_accept_stale_versions_after_current_state_is_revalidated(
        long expectedVersion, long currentVersion, bool expected) =>
        Assert.Equal(expected, SalesRoomAgentService.AcceptsSpeechCommandVersion(expectedVersion, currentVersion));
    private SalesRoomFloor Floor(string mode) => new(company, room, host, 4, 10, 2, 0,
        "slide:2:talking-point:0", mode, now);
}
