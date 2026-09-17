using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public class SalesMeetingPresetEligibilityTests
{
    [Fact]
    public void Reopening_preparation_is_organizer_only_and_preserves_history_identity()
    {
        var actor = Guid.NewGuid(); var now = DateTime.UtcNow;
        var session = new SalesMeetingSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            null, null, Guid.NewGuid(), "Goal", "Audience", 60, null, "meeting", SalesMeetingConsentStatus.NotRequested,
            SalesMeetingRetentionPolicy.Standard, 365, now, actor, now);
        session.TransitionTo(SalesMeetingSessionStatus.Presenting, 1, 0, null, null, actor, now);
        var version = session.ConcurrencyVersion;
        Assert.Throws<UnauthorizedAccessException>(() => session.ReopenPreparation(Guid.NewGuid(), now));
        Assert.Equal(SalesMeetingSessionStatus.Presenting, session.Status);
        session.ReopenPreparation(actor, now);
        Assert.Equal(SalesMeetingSessionStatus.Ready, session.Status);
        Assert.Equal(0, session.CurrentSlideIndex);
        Assert.Equal(version + 1, session.ConcurrencyVersion);
        session.TransitionTo(SalesMeetingSessionStatus.Cancelled, null, null, null, "Cancelled", actor, now);
        Assert.Throws<InvalidOperationException>(() => session.ReopenPreparation(actor, now));
    }

    [Theory]
    [InlineData(SalesMeetingSessionStatus.Ready, false, "not_started", true)]
    [InlineData(SalesMeetingSessionStatus.Presenting, false, "stopped", true)]
    [InlineData(SalesMeetingSessionStatus.Interrupted, false, "paused", true)]
    [InlineData(SalesMeetingSessionStatus.Presenting, true, "stopped", false)]
    [InlineData(SalesMeetingSessionStatus.Ready, true, "not_started", false)]
    [InlineData(SalesMeetingSessionStatus.Presenting, false, "speaking", false)]
    [InlineData(SalesMeetingSessionStatus.Presenting, false, "starting", false)]
    [InlineData(SalesMeetingSessionStatus.Completed, false, "stopped", false)]
    [InlineData(SalesMeetingSessionStatus.Cancelled, false, "stopped", false)]
    [InlineData(SalesMeetingSessionStatus.Failed, false, "stopped", false)]
    public void Browser_eligibility_depends_on_real_participation_not_the_clock(
        SalesMeetingSessionStatus status, bool connected, string agent, bool expected)
        => Assert.Equal(expected, SalesMeetingPresetEligibility.Allows(status, true, true, connected, agent));

    [Fact]
    public void Ended_room_cannot_be_edited_even_if_session_is_ready()
        => Assert.False(SalesMeetingPresetEligibility.Allows(SalesMeetingSessionStatus.Ready, true, false, false, "stopped"));

    [Theory]
    [InlineData(SalesMeetingSessionStatus.Ready, true)]
    [InlineData(SalesMeetingSessionStatus.Presenting, false)]
    public void Non_browser_meetings_keep_the_existing_lifecycle_rule(SalesMeetingSessionStatus status, bool expected)
        => Assert.Equal(expected, SalesMeetingPresetEligibility.Allows(status, false, false, false, null));
}
