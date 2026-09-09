using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsMeetingCallDomainTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Callback_lifecycle_is_monotonic_and_deduplicates_sequence()
    {
        var call = Create();
        call.BeginJoin(Now.AddSeconds(1));
        call.RecordProviderCall("provider-call", "graph-call:abc", "establishing", Now.AddSeconds(2));

        Assert.Equal(TeamsMeetingCallStates.WaitingInLobby, call.State);
        Assert.True(call.ApplyProviderState("established", 12, "W/\"12\"", Now.AddSeconds(3)));
        Assert.Equal(TeamsMeetingCallStates.Connected, call.State);
        Assert.False(call.ApplyProviderState("establishing", 11, "W/\"11\"", Now.AddSeconds(4)));
        Assert.False(call.ApplyProviderState("establishing", 13, "W/\"13\"", Now.AddSeconds(5)));
        Assert.Equal(TeamsMeetingCallStates.Connected, call.State);
    }

    [Fact]
    public void Leave_is_versioned_and_terminal_callback_wins()
    {
        var call = Create(); call.BeginJoin(Now); call.RecordProviderCall("provider-call", null, "established", Now);
        var key = call.RequestLeave(Now.AddMinutes(1));
        Assert.Contains(":leave:v2", key, StringComparison.Ordinal);
        Assert.Equal(TeamsMeetingCallStates.LeaveRequested, call.State);
        Assert.True(call.ApplyProviderState("terminated", 20, "20", Now.AddMinutes(2)));
        Assert.Equal(TeamsMeetingCallStates.Ended, call.State);
        Assert.False(TeamsMeetingCallStates.IsActive(call.State));
    }

    [Fact]
    public void Provider_binding_cannot_be_reassigned()
    {
        var call = Create(); call.BindProviderCall("one", null, Now);
        Assert.Throws<InvalidOperationException>(() => call.BindProviderCall("two", null, Now));
    }

    [Fact]
    public void Pending_call_can_be_pinned_once_before_provider_join()
    {
        var call = new TeamsMeetingCall(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new string('A', 64), 1, 1, "pending", Now);

        call.AssignMediaHost("media-17", Now.AddSeconds(1));
        call.BeginJoin(Now.AddSeconds(2));

        Assert.Equal("media-17", call.MediaHostInstanceId);
        Assert.Throws<InvalidOperationException>(() => call.AssignMediaHost("media-18", Now.AddSeconds(3)));
    }

    [Fact]
    public void Media_requires_connected_call_exact_version_and_organizer_then_terminal_state_clears_authority()
    {
        var call = Create();
        call.BeginJoin(Now);
        call.RecordProviderCall("provider-call", null, "established", Now.AddSeconds(1));
        var organizer = call.OrganizerUserId;

        Assert.Throws<UnauthorizedAccessException>(() => call.AuthorizeMediaStart(Guid.NewGuid(), call.ConcurrencyVersion, Now));
        Assert.Throws<InvalidOperationException>(() => call.AuthorizeMediaStart(organizer, call.ConcurrencyVersion - 1, Now));

        call.AuthorizeMediaStart(organizer, call.ConcurrencyVersion, Now.AddSeconds(2));
        Assert.Equal(organizer, call.MediaStartAuthorizedByUserId);
        Assert.NotNull(call.MediaStartAuthorizedUtc);

        call.MarkEnded("terminated", Now.AddSeconds(3));
        Assert.Null(call.MediaStartAuthorizedByUserId);
        Assert.Null(call.MediaStartAuthorizedUtc);
    }

    [Fact]
    public void Rejoin_resets_prior_media_authorization()
    {
        var call = Create(); call.BeginJoin(Now); call.RecordProviderCall("provider-call", null, "established", Now);
        call.AuthorizeMediaStart(call.OrganizerUserId, call.ConcurrencyVersion, Now);
        call.MarkEnded("terminated", Now);

        call.RequestJoin(new string('B', 64), 2, 2, "host-2", Now.AddMinutes(1));

        Assert.Null(call.MediaStartAuthorizedUtc);
        Assert.Equal(TeamsMeetingCallStates.Requested, call.State);
    }

    private static TeamsMeetingCall Create() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), new string('A', 64), 1, 1, "host-1", Now);
}
