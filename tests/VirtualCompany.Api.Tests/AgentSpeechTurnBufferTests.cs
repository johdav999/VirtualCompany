using VirtualCompany.Domain.Agents;

namespace VirtualCompany.Api.Tests;

public sealed class AgentSpeechTurnBufferTests
{
    private static AgentSpeechTurnScope Scope() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, "mic", 1, 1, 1);

    [Fact]
    public void Unfinished_expiry_can_recover_once_as_scoped_clarification_not_an_answer()
    {
        var b = new AgentSpeechTurnBuffer(); var s = Scope(); var now = DateTime.UtcNow;
        b.Hold(s, "A question how", now);
        Assert.Null(b.TakeExpiredIncomplete(now.AddSeconds(11)));
        var expired = b.TakeExpiredIncomplete(now.AddSeconds(12));
        Assert.NotNull(expired); Assert.Equal(s, expired.Value.Scope);
        Assert.Equal("A question how", expired.Value.Text);
        Assert.Null(b.TakeExpiredIncomplete(now.AddSeconds(13)));
        Assert.Null(b.ReleaseComplete(now.AddSeconds(13), false));
        b.Hold(s, "Continue please", now, complete: true);
        Assert.Null(b.TakeExpiredIncomplete(now.AddSeconds(61)));
        Assert.True(b.Expire(now.AddSeconds(61)));
        b.Hold(s, "Private fragment", now); b.Clear();
        Assert.Null(b.TakeExpiredIncomplete(now.AddSeconds(13)));
    }

    [Fact]
    public void Pause_keeps_the_fragment_for_the_same_speakers_continuation_only()
    {
        var b = new AgentSpeechTurnBuffer(); var s = Scope(); var now = DateTime.UtcNow;
        var first = b.Take(s, "A question how", now)!;
        b.Hold(s, first, now);
        Assert.Equal("A question how do you do onboarding of companies to the solution?",
            b.Take(s, "do you do onboarding of companies to the solution?", now.AddSeconds(3)));
        Assert.Equal("Another question", b.Take(s, "Another question", now.AddSeconds(4)));
    }

    [Theory]
    [InlineData("company")][InlineData("room")][InlineData("speaker")][InlineData("consent")]
    [InlineData("participant")][InlineData("track")][InlineData("owner")][InlineData("turn")][InlineData("expiry")]
    public void Stale_or_foreign_fragments_are_never_combined(string change)
    {
        var b = new AgentSpeechTurnBuffer(); var s = Scope(); var now = DateTime.UtcNow;
        b.Hold(s, "Old private words", now);
        var next = change switch {
            "company" => s with { CompanyId = Guid.NewGuid() }, "room" => s with { ConversationId = Guid.NewGuid() },
            "speaker" => s with { ParticipantId = Guid.NewGuid() }, "consent" => s with { ConsentVersion = 2 },
            "participant" => s with { ParticipantGeneration = 2 }, "track" => s with { TrackGeneration = 2 },
            "owner" => s with { OwnerGeneration = 2 }, "turn" => s with { TurnGeneration = 2 }, _ => s };
        Assert.Equal("New words", b.Take(next, "New words", now.AddSeconds(change == "expiry" ? 13 : 1)));
    }

    [Fact]
    public void Complete_turn_releases_once_when_input_drains_without_another_spoken_word()
    {
        var b = new AgentSpeechTurnBuffer(); var now = DateTime.UtcNow;
        b.Hold(Scope(), "Can you continue the presentation?", now, complete: true);
        Assert.Null(b.ReleaseComplete(now.AddSeconds(1), inputBusy: true));
        Assert.Equal("Can you continue the presentation?", b.ReleaseComplete(now.AddSeconds(2), inputBusy: false));
        Assert.Null(b.ReleaseComplete(now.AddSeconds(3), inputBusy: false));
    }

    [Fact]
    public void Drain_never_releases_incomplete_expired_or_replaced_turns()
    {
        var b = new AgentSpeechTurnBuffer(); var s = Scope(); var now = DateTime.UtcNow;
        b.Hold(s, "A question how", now);
        Assert.Null(b.ReleaseComplete(now.AddSeconds(1), false));
        b.Hold(s, "Continue please", now, complete: true);
        Assert.Null(b.ReleaseComplete(now.AddSeconds(61), false));
        b.Hold(s, "Continue please", now, complete: true);
        Assert.Equal("Continue please actually wait", b.Take(s, "actually wait", now));
        Assert.Null(b.ReleaseComplete(now, false));
        b.Hold(s, "Continue please", now, complete: true); b.Clear();
        Assert.Null(b.ReleaseComplete(now, false));
    }

    [Fact]
    public void Complete_command_survives_fragment_timeout_and_expiry_is_observable()
    {
        var b = new AgentSpeechTurnBuffer(); var now = DateTime.UtcNow;
        b.Hold(Scope(), "Continue the presentation", now, complete: true);
        Assert.False(b.Expire(now.AddSeconds(13)));
        Assert.Equal("Continue the presentation", b.ReleaseComplete(now.AddSeconds(14), false));
        b.Hold(Scope(), "Continue", now, complete: true);
        Assert.True(b.Expire(now.AddSeconds(61)));
        Assert.False(b.Expire(now.AddSeconds(62)));
    }

    [Fact]
    public void Size_expiry_and_clear_never_release_unfinished_text_as_an_answer()
    {
        var b = new AgentSpeechTurnBuffer(); var s = Scope(); var now = DateTime.UtcNow;
        b.Hold(s, new string('x', 2000), now);
        Assert.Null(b.Take(s, "more", now));
        b.Hold(s, "unfinished", now); b.Expire(now.AddSeconds(13));
        Assert.Equal("new", b.Take(s, "new", now.AddSeconds(13)));
        b.Hold(s, "unfinished", now); b.Clear();
        Assert.Equal("new", b.Take(s, "new", now));
    }
}
