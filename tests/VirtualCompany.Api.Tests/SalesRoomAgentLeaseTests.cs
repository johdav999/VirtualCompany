using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomAgentLeaseTests
{
    [Fact]
    public void One_active_owner_is_enforced_and_preemption_fences_the_published_turn()
    {
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var organizer = Guid.NewGuid();
        var room = LiveRoom(organizer, now);
        var owner = Guid.NewGuid();
        var agent = Guid.NewGuid();

        room.StartAgent(agent, organizer, owner, now.AddSeconds(30), now);
        var generation = room.AgentGeneration;
        var turn = room.AgentTurnGeneration;

        Assert.Throws<InvalidOperationException>(() =>
            room.StartAgent(agent, organizer, Guid.NewGuid(), now.AddSeconds(30), now.AddSeconds(1)));
        Assert.True(room.IsAgentOwner(owner, generation, now.AddSeconds(1)));

        room.AgentReady(owner, generation);
        room.AgentSpeaking(owner, generation);
        room.PreemptAgent(owner, generation, "Human speech detected.");

        Assert.Equal(SalesRoomAgentHealthStates.Paused, room.AgentHealth);
        Assert.Equal(turn + 1, room.AgentTurnGeneration);
        Assert.Equal("human_speaking", room.AgentLastErrorCode);
    }

    [Fact]
    public void Non_organizer_cannot_start_the_agent()
    {
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var room = LiveRoom(Guid.NewGuid(), now);

        Assert.Throws<UnauthorizedAccessException>(() => room.StartAgent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            now.AddSeconds(30), now));
        Assert.Equal(SalesRoomAgentHealthStates.NotStarted, room.AgentHealth);
    }

    [Fact]
    public void Restart_uses_a_new_generation_and_stale_workers_cannot_renew_or_mutate()
    {
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var organizer = Guid.NewGuid();
        var room = LiveRoom(organizer, now);
        var oldOwner = Guid.NewGuid();
        room.StartAgent(Guid.NewGuid(), organizer, oldOwner, now.AddSeconds(30), now);
        var oldGeneration = room.AgentGeneration;
        room.StopAgent("host_stopped", null, now.AddSeconds(2));

        var newOwner = Guid.NewGuid();
        room.StartAgent(Guid.NewGuid(), organizer, newOwner, now.AddSeconds(40), now.AddSeconds(3));

        Assert.True(room.AgentGeneration > oldGeneration);
        Assert.False(room.RenewAgentLease(oldOwner, oldGeneration, now.AddMinutes(1), now.AddSeconds(4)));
        Assert.Throws<InvalidOperationException>(() => room.AgentReady(oldOwner, oldGeneration));
        Assert.True(room.IsAgentOwner(newOwner, room.AgentGeneration, now.AddSeconds(4)));
    }

    [Fact]
    public void Reconciliation_preserves_another_instances_valid_lease_and_selects_only_stale_or_disabled_work()
    {
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var organizer = Guid.NewGuid();
        var room = LiveRoom(organizer, now);
        room.StartAgent(Guid.NewGuid(), organizer, Guid.NewGuid(), now.AddSeconds(30), now);

        Assert.False(SalesRoomAgentCoordinator.ShouldStopForReconciliation(room, false, now.AddSeconds(5)));
        Assert.True(SalesRoomAgentCoordinator.ShouldStopForReconciliation(room, false, now.AddSeconds(31)));
        Assert.True(SalesRoomAgentCoordinator.ShouldStopForReconciliation(room, true, now.AddSeconds(5)));
    }

    private static SalesBrowserRoom LiveRoom(Guid organizer, DateTime now)
    {
        var room = new SalesBrowserRoom(Guid.NewGuid(), Guid.NewGuid(), organizer, now.AddHours(1), now);
        room.Provisioned("provider-room");
        room.Start(now.AddSeconds(1), 45);
        return room;
    }
}
