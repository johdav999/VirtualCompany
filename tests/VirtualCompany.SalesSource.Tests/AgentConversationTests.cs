using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;
using Xunit;

namespace VirtualCompany.SalesSource.Tests;

public class AgentConversationTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private static AgentConversationAuthority Authority(string mode = "autonomous") => new(
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Guid.NewGuid(), 1, 1, 1, mode, AgentConversation.PolicyVersion, 1, 1, 0, 0),
        true, true, true, true, true, true, Now.AddHours(1));

    [Theory]
    [InlineData("autonomous", false, true)]
    [InlineData("assisted", false, false)]
    [InlineData("manual", false, false)]
    [InlineData("assisted", true, true)]
    [InlineData("manual", true, true)]
    [InlineData("unknown", true, false)]
    public void Supported_answer_obeys_mode_and_exact_approval(string mode, bool approved, bool allowed)
    {
        var a = Authority(mode);
        var c = new AgentConversation(a.Binding, AgentConversationPhase.Retrieving);
        var result = c.AnswerReady(c.Version, a, Now, true, approved);
        Assert.Equal(allowed, result.Allowed);
        if (!approved && mode is "manual" or "assisted")
        {
            Assert.True(result.ApprovalRequired);
            Assert.Equal(AgentConversationPhase.AwaitingApproval, c.Phase);
            Assert.True(c.AnswerReady(c.Version, a, Now, true, true).Allowed);
        }
    }

    [Fact]
    public void Evidence_free_answer_and_unvalidated_bridge_are_never_released()
    {
        var a = Authority(); var c = new AgentConversation(a.Binding, AgentConversationPhase.Retrieving);
        Assert.False(c.AnswerReady(c.Version, a, Now, false, true).Allowed);
        c = CompletedAnswer(a);
        Assert.False(c.Authorize(a, AgentConversationAction.SpeakBridge, Now).Allowed);
        c.Bridge(c.Version, a, Now, true);
        c.Played(Guid.NewGuid(), c.Version, a, Now);
        Assert.Equal(AgentConversationPhase.WaitingForReply, c.Phase);
    }

    [Fact]
    public void Confirmed_input_retrieval_played_output_and_reply_are_versioned()
    {
        var a = Authority(); var c = new AgentConversation(a.Binding, AgentConversationPhase.Presenting);
        var input = Guid.NewGuid(); c.Heard(input, 1, a, Now);
        Assert.Equal(input, c.HeardTurnId);
        Assert.Throws<InvalidOperationException>(() => c.Heard(input, 1, a, Now));
        Assert.Throws<InvalidOperationException>(() => c.Heard(input, c.Version, a, Now));
        c.Retrieve(c.Version, a, Now);
        Assert.True(c.AnswerReady(c.Version, a, Now, true, false).Allowed);
        c.Played(Guid.NewGuid(), c.Version, a, Now);
        Assert.Equal(AgentConversationPhase.WaitingForReply, c.Phase);
    }

    [Theory]
    [InlineData(AgentConversationIntent.Unknown, false)]
    [InlineData(AgentConversationIntent.Question, false)]
    [InlineData(AgentConversationIntent.Wait, false)]
    [InlineData(AgentConversationIntent.Continue, true)]
    public void Only_a_clear_reply_to_current_played_context_can_propose_resume(AgentConversationIntent intent, bool expected)
    {
        var a = Authority(); var c = CompletedAnswer(a);
        Assert.False(c.Authorize(a, AgentConversationAction.Resume, Now).Allowed); // silence
        c.Heard(Guid.NewGuid(), c.Version, a, Now.AddSeconds(1));
        Assert.Equal(expected, c.Propose(AgentConversationAction.Resume, intent, c.Version, a, Now.AddSeconds(1)).Allowed);
        var id = c.Pending!.Id; var version = c.Version;
        Assert.Equal(expected, c.Consume(id, version, a, Now.AddSeconds(2)).Allowed);
        Assert.Throws<InvalidOperationException>(() => c.Consume(id, version, a, Now.AddSeconds(2)));
        Assert.False(c.Consume(id, c.Version, a, Now.AddSeconds(2)).Allowed);
    }

    [Fact]
    public void Expired_reply_or_proposal_and_recreated_controller_cannot_resume()
    {
        var a = Authority(); var c = CompletedAnswer(a);
        c.Heard(Guid.NewGuid(), c.Version, a, Now.AddSeconds(46));
        Assert.False(c.Propose(AgentConversationAction.Resume, AgentConversationIntent.Continue, c.Version, a, Now.AddSeconds(46)).Allowed);
        c = CompletedAnswer(a); c.Heard(Guid.NewGuid(), c.Version, a, Now);
        c.Propose(AgentConversationAction.Resume, AgentConversationIntent.Continue, c.Version, a, Now);
        var id = c.Pending!.Id;
        Assert.False(c.Consume(id, c.Version, a, Now.AddSeconds(16)).Allowed);
        var recovered = new AgentConversation(a.Binding, AgentConversationPhase.WaitingForReply);
        recovered.Heard(Guid.NewGuid(), recovered.Version, a, Now);
        Assert.False(recovered.Propose(AgentConversationAction.Resume, AgentConversationIntent.Continue, recovered.Version, a, Now).Allowed);
    }

    [Fact]
    public void All_security_and_generation_boundaries_deny_old_actions()
    {
        var a = Authority(); var b = a.Binding; var c = CompletedAnswer(a);
        c.Heard(Guid.NewGuid(), c.Version, a, Now);
        c.Propose(AgentConversationAction.Resume, AgentConversationIntent.Continue, c.Version, a, Now);
        AgentConversationAuthority[] changes = [
            a with { Enabled = false }, a with { ParticipantAllowed = false }, a with { ConsentAllowed = false },
            a with { SessionActive = false }, a with { BudgetAvailable = false }, a with { ControllerAllowed = false },
            a with { ExpiresUtc = Now },
            a with { Binding = b with { CompanyId = Guid.NewGuid() } },
            a with { Binding = b with { ConversationId = Guid.NewGuid() } },
            a with { Binding = b with { SessionId = Guid.NewGuid() } },
            a with { Binding = b with { AgentId = Guid.NewGuid() } },
            a with { Binding = b with { ParticipantId = Guid.NewGuid() } },
            a with { Binding = b with { ParticipantGeneration = 2 } },
            a with { Binding = b with { OwnerId = Guid.NewGuid() } },
            a with { Binding = b with { OwnerGeneration = 2 } },
            a with { Binding = b with { TurnGeneration = 2 } },
            a with { Binding = b with { ResponseGeneration = 2 } },
            a with { Binding = b with { Mode = "manual" } },
            a with { Binding = b with { PolicyVersion = 2 } },
            a with { Binding = b with { PresentationVersion = 2 } },
            a with { Binding = b with { Slide = 2 } }, a with { Binding = b with { Point = 1 } },
            a with { Binding = b with { OffsetMilliseconds = 100 } }
        ];
        foreach (var changed in changes) Assert.False(c.Authorize(changed, AgentConversationAction.Resume, Now).Allowed);
        c.Pause(); Assert.Null(c.Pending); Assert.False(c.Authorize(a, AgentConversationAction.Resume, Now).Allowed);
        c.Pause(stopped: true); Assert.False(c.Authorize(a, AgentConversationAction.Interpret, Now).Allowed);
    }

    [Fact]
    public void New_question_invalidates_reply_context_even_when_no_answer_is_available()
    {
        var a = Authority(); var c = CompletedAnswer(a);
        c.Heard(Guid.NewGuid(), c.Version, a, Now);
        c.Retrieve(c.Version, a, Now);
        Assert.Null(c.PlayedResponseId); Assert.Null(c.ReplyDeadlineUtc);
        c.AnswerReady(c.Version, a, Now, false, false);
        c.Heard(Guid.NewGuid(), c.Version, a, Now);
        Assert.False(c.Propose(AgentConversationAction.Resume, AgentConversationIntent.Continue, c.Version, a, Now).Allowed);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("assisted")]
    public void Host_approved_answer_does_not_allow_automatic_bridge_or_resume(string mode)
    {
        var a = Authority(mode); var c = new AgentConversation(a.Binding, AgentConversationPhase.Retrieving);
        Assert.True(c.AnswerReady(c.Version, a, Now, true, true).Allowed);
        c.Played(Guid.NewGuid(), c.Version, a, Now);
        Assert.False(c.Authorize(a, AgentConversationAction.SpeakBridge, Now, bridgeValidated: true).Allowed);
        c.Heard(Guid.NewGuid(), c.Version, a, Now);
        Assert.False(c.Propose(AgentConversationAction.Resume, AgentConversationIntent.Continue, c.Version, a, Now).Allowed);
    }

    [Fact]
    public void Tool_proposal_requires_registration_and_cannot_bypass_consent()
    {
        var a = Authority(); var c = new AgentConversation(a.Binding, AgentConversationPhase.Interpreting);
        Assert.False(c.Authorize(a, AgentConversationAction.DispatchTool, Now).Allowed);
        Assert.True(c.Authorize(a, AgentConversationAction.DispatchTool, Now, toolRegistered: true).Allowed);
        Assert.False(c.Authorize(a with { ConsentAllowed = false }, AgentConversationAction.DispatchTool, Now, toolRegistered: true).Allowed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mode_change_fences_pending_turns_only_when_rollout_enabled(bool enabled)
    {
        var a = Authority(); var b = a.Binding;
        var floor = new SalesRoomFloor(b.CompanyId, b.ConversationId, b.ParticipantId, 1, 1, 1, 0, "checkpoint", "autonomous", Now);
        floor.ProposeTurn(b.ParticipantId, 1, true, false, Guid.NewGuid(), Now);
        var oldResponse = floor.ResponseGeneration;
        floor.SetMode("manual", 2, Now, enabled);
        Assert.Equal(oldResponse + (enabled ? 1 : 0), floor.ResponseGeneration);
        Assert.Equal(enabled, floor.PendingTurnId is null);
        floor.SetMode("autonomous", 3, Now, enabled);
        Assert.Equal(oldResponse + (enabled ? 2 : 0), floor.ResponseGeneration);
    }

    private static AgentConversation CompletedAnswer(AgentConversationAuthority a)
    {
        var c = new AgentConversation(a.Binding, AgentConversationPhase.Retrieving);
        Assert.True(c.AnswerReady(c.Version, a, Now, true, false).Allowed);
        c.Played(Guid.NewGuid(), c.Version, a, Now);
        return c;
    }
}
