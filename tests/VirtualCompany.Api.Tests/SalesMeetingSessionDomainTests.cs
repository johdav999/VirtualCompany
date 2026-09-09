using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingSessionDomainTests
{
    [Fact]
    public void Session_preserves_resume_position_through_interruption_and_resume()
    {
        var session = CreateSession();
        var actor = Guid.NewGuid();
        var now = DateTime.UtcNow;

        session.TransitionTo(SalesMeetingSessionStatus.Presenting, 2, 1, null, null, actor, now);
        session.TransitionTo(SalesMeetingSessionStatus.Interrupted, 2, 3, "slide:2:point:3", null, actor, now.AddSeconds(1));
        session.TransitionTo(SalesMeetingSessionStatus.Answering, null, null, null, null, actor, now.AddSeconds(2));
        session.TransitionTo(SalesMeetingSessionStatus.Resuming, null, null, null, null, actor, now.AddSeconds(3));
        session.TransitionTo(SalesMeetingSessionStatus.Presenting, null, null, null, null, actor, now.AddSeconds(4));

        Assert.Equal(SalesMeetingSessionStatus.Presenting, session.Status);
        Assert.Equal(2, session.CurrentSlideIndex);
        Assert.Equal(3, session.CurrentTalkingPointIndex);
        Assert.Equal("slide:2:point:3", session.ResumeMarker);
        Assert.Equal(6, session.ConcurrencyVersion);
    }

    [Fact]
    public void Invalid_transition_leaves_session_unchanged()
    {
        var session = CreateSession();

        var exception = Assert.Throws<InvalidOperationException>(() => session.TransitionTo(
            SalesMeetingSessionStatus.Completed, null, null, null, null,
            Guid.NewGuid(), DateTime.UtcNow));

        Assert.Contains("ready to completed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(SalesMeetingSessionStatus.Ready, session.Status);
        Assert.Equal(1, session.ConcurrencyVersion);
    }

    [Fact]
    public void Interrupted_session_requires_an_exact_resume_marker()
    {
        var session = CreateSession();
        session.TransitionTo(
            SalesMeetingSessionStatus.Presenting, 4, 2, null, null,
            Guid.NewGuid(), DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => session.TransitionTo(
            SalesMeetingSessionStatus.Interrupted, 4, 2, null, null,
            Guid.NewGuid(), DateTime.UtcNow));
        Assert.Equal(SalesMeetingSessionStatus.Presenting, session.Status);
        Assert.Equal(2, session.ConcurrencyVersion);
    }

    [Fact]
    public void Terminal_sessions_reject_further_transitions()
    {
        var session = CreateSession();
        var actor = Guid.NewGuid();
        session.TransitionTo(SalesMeetingSessionStatus.Closing, null, null, null, null, actor, DateTime.UtcNow);
        session.TransitionTo(SalesMeetingSessionStatus.Completed, null, null, null, null, actor, DateTime.UtcNow);

        Assert.NotNull(session.EndedUtc);
        Assert.Throws<InvalidOperationException>(() => session.TransitionTo(
            SalesMeetingSessionStatus.Discussion, null, null, null, null,
            actor, DateTime.UtcNow));
    }

    [Fact]
    public void Standard_retention_is_fixed_to_365_days()
    {
        Assert.Throws<ArgumentException>(() => CreateSession(retentionDays: 30));
    }

    [Fact]
    public void Presentation_commands_are_ordered_and_resume_the_exact_saved_position()
    {
        var session = CreateSession();
        var actor = Guid.NewGuid();
        var first = Guid.NewGuid();

        session.ApplyPresentationCommand(
            SalesPresentationCommandType.Goto, first, 1, 1, 3, 2, null, actor, DateTime.UtcNow);
        session.ApplyPresentationCommand(
            SalesPresentationCommandType.Pause, Guid.NewGuid(), 2, 2, null, null,
            "slide:3:talking-point:2", actor, DateTime.UtcNow.AddSeconds(1));
        session.ApplyPresentationCommand(
            SalesPresentationCommandType.Resume, Guid.NewGuid(), 3, 3, null, null,
            null, actor, DateTime.UtcNow.AddSeconds(2));

        Assert.Equal(SalesMeetingSessionStatus.Presenting, session.Status);
        Assert.Equal(3, session.CurrentSlideIndex);
        Assert.Equal(2, session.CurrentTalkingPointIndex);
        Assert.Equal("slide:3:talking-point:2", session.ResumeMarker);
        Assert.Equal(3, session.LastPresentationSequence);
        Assert.Equal(4, session.ConcurrencyVersion);
    }

    [Fact]
    public void Presentation_command_rejects_stale_version_and_out_of_order_sequence_without_mutation()
    {
        var session = CreateSession();
        var actor = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => session.ApplyPresentationCommand(
            SalesPresentationCommandType.Next, Guid.NewGuid(), 2, 1, 1, 0, null, actor, DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => session.ApplyPresentationCommand(
            SalesPresentationCommandType.Next, Guid.NewGuid(), 1, 99, 1, 0, null, actor, DateTime.UtcNow));

        Assert.Equal(0, session.LastPresentationSequence);
        Assert.Equal(0, session.CurrentSlideIndex);
        Assert.Equal(1, session.ConcurrencyVersion);
    }

    private static SalesMeetingSession CreateSession(int retentionDays = 365)
    {
        var now = DateTime.UtcNow;
        return new SalesMeetingSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Confirm product fit", "Finance leadership", 45, "Reconciliation demo",
            "provider-meeting", SalesMeetingConsentStatus.Pending,
            SalesMeetingRetentionPolicy.Standard, retentionDays,
            now.AddHours(1), Guid.NewGuid(), now);
    }
}
