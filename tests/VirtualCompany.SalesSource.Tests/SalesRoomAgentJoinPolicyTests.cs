using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using Xunit;

namespace VirtualCompany.SalesSource.Tests;

public sealed class SalesRoomAgentJoinPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Initial_join_is_pending_only_in_an_accessible_room()
    {
        var room = Room();
        Assert.False(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now));
        room.Provisioned("test-room");
        Assert.True(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now));
        room.Start(Now, 60);
        Assert.True(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now));
        Assert.False(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now.AddHours(1)));
        room.Ended();
        Assert.False(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now));
    }

    [Theory]
    [InlineData("host_stopped")]
    [InlineData("quota_exceeded")]
    [InlineData("consent_withdrawn")]
    [InlineData("worker_lease_expired")]
    [InlineData("provider_failed")]
    public void Automatic_join_never_overrides_a_stop_or_recovery_requirement(string reason)
    {
        var room = Room(); room.Provisioned("test-room"); room.Start(Now, 60);
        room.StopAgent(reason, "Requires an explicit restart", Now);
        Assert.False(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now));
    }

    [Fact]
    public void Existing_worker_including_paused_or_expired_lease_is_not_restarted()
    {
        var room = Room(); room.Provisioned("test-room"); room.Start(Now, 60);
        var owner = Guid.NewGuid();
        room.StartAgent(Guid.NewGuid(), room.OrganizerUserId, owner, Now.AddSeconds(30), Now);
        Assert.False(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now));
        room.AgentReady(owner, room.AgentGeneration);
        Assert.False(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now));
        room.PauseAgent(owner, room.AgentGeneration, "human_speaking", "Listening");
        Assert.False(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now));
        Assert.False(SalesRoomAgentJoinPolicy.CanStartAutomatically(room, Now.AddMinutes(1)));
    }

    private static SalesBrowserRoom Room() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now);
}
