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

    private SalesRoomFloor Floor(string mode) => new(company, room, host, 4, 10, 2, 0,
        "slide:2:talking-point:0", mode, now);
}
