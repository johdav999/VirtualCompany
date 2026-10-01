namespace VirtualCompany.Domain.Agents;

public enum AgentConversationPhase
{
    Presenting, Interpreting, Retrieving, AwaitingApproval, SpeakingAnswer,
    SpeakingBridge, WaitingForReply, Paused, Stopped, Listening
}
public enum AgentConversationAction { Interpret, Retrieve, SpeakAnswer, SpeakBridge, Resume, DispatchTool }
public enum AgentConversationIntent { Unknown, Question, Continue, Wait, Acknowledgement }

// Server-created binding, never deserialized from model arguments. Presentation/floor remain
// the durable authority. A new owner or changed checkpoint invalidates every old proposal.
public sealed record AgentConversationBinding(Guid CompanyId, Guid AgentId, Guid ConversationId,
    Guid SessionId, Guid ParticipantId, long ParticipantGeneration, Guid OwnerId, long OwnerGeneration,
    long TurnGeneration, long ResponseGeneration, string Mode, long PolicyVersion,
    long PresentationVersion, int Slide, int Point, int OffsetMilliseconds);

public sealed record AgentConversationAuthority(AgentConversationBinding Binding, bool Enabled,
    bool ParticipantAllowed, bool ConsentAllowed, bool SessionActive, bool BudgetAvailable,
    bool ControllerAllowed, DateTime ExpiresUtc);

public sealed record AgentConversationDecision(bool Allowed, string Code, bool ApprovalRequired = false)
{
    public static AgentConversationDecision Permit { get; } = new(true, "allowed");
}

public sealed record AgentConversationProposal(Guid Id, long Version, AgentConversationAction Action,
    AgentConversationIntent Intent, Guid? HeardTurnId, Guid? PlayedResponseId, DateTime ExpiresUtc);

/// <summary>
/// An operation-local conversation controller. No raw audio/text is kept here. References
/// point to separately consented input and actually played output. Do not reconstruct pending
/// proposals after restart: start a new controller and require a fresh participant turn.
/// The caller serializes transitions; version checks reject duplicate/reordered completions.
/// </summary>
public sealed class AgentConversation
{
    public const long PolicyVersion = 1;
    public AgentConversationBinding Binding { get; }
    public AgentConversationPhase Phase { get; private set; }
    public long Version { get; private set; } = 1;
    public Guid? HeardTurnId { get; private set; }
    public Guid? PlayedResponseId { get; private set; }
    public AgentConversationProposal? Pending { get; private set; }
    public DateTime? ReplyDeadlineUtc { get; private set; }

    public AgentConversation(AgentConversationBinding binding, AgentConversationPhase phase)
    {
        if (binding.CompanyId == Guid.Empty || binding.AgentId == Guid.Empty || binding.ConversationId == Guid.Empty ||
            binding.SessionId == Guid.Empty || binding.ParticipantId == Guid.Empty || binding.OwnerId == Guid.Empty ||
            binding.ParticipantGeneration < 1 || binding.OwnerGeneration < 1 || binding.TurnGeneration < 1 ||
            binding.ResponseGeneration < 1 || binding.PresentationVersion < 1 || binding.Slide < 1 || binding.Point < 0 ||
            binding.OffsetMilliseconds < 0 || !Enum.IsDefined(phase)) throw new ArgumentException("Invalid conversation binding.");
        Binding = binding; Phase = phase;
    }

    public AgentConversationDecision Check(AgentConversationAuthority current, DateTime now)
    {
        if (!current.Enabled) return new(false, "conversation_disabled");
        if (Binding != current.Binding || Binding.PolicyVersion != PolicyVersion) return new(false, "conversation_stale");
        if (Binding.Mode is not ("autonomous" or "assisted" or "manual")) return new(false, "mode_unknown");
        if (!current.ParticipantAllowed) return new(false, "participant_denied");
        if (!current.ConsentAllowed) return new(false, "consent_required");
        if (!current.SessionActive || current.ExpiresUtc <= now || Phase == AgentConversationPhase.Stopped)
            return new(false, "session_inactive");
        if (!current.BudgetAvailable) return new(false, "budget_exhausted");
        return AgentConversationDecision.Permit;
    }

    public AgentConversationDecision Authorize(AgentConversationAuthority current, AgentConversationAction action,
        DateTime now, bool supportedAnswer = false, bool exactHostApproval = false, bool bridgeValidated = false,
        bool toolRegistered = false)
    {
        var check = Check(current, now);
        if (!check.Allowed) return check;
        return action switch
        {
            AgentConversationAction.Interpret when Phase is not AgentConversationPhase.Stopped => AgentConversationDecision.Permit,
            AgentConversationAction.Retrieve when Phase == AgentConversationPhase.Interpreting => AgentConversationDecision.Permit,
            AgentConversationAction.SpeakAnswer when !supportedAnswer => new(false, "evidence_required"),
            AgentConversationAction.SpeakAnswer when Binding.Mode != "autonomous" && !exactHostApproval => new(false, "host_approval_required", true),
            AgentConversationAction.SpeakAnswer when Phase is AgentConversationPhase.Retrieving or AgentConversationPhase.AwaitingApproval or AgentConversationPhase.SpeakingAnswer => AgentConversationDecision.Permit,
            AgentConversationAction.SpeakBridge when Binding.Mode == "autonomous" && bridgeValidated &&
                Phase is AgentConversationPhase.WaitingForReply or AgentConversationPhase.SpeakingBridge => AgentConversationDecision.Permit,
            AgentConversationAction.Resume when Binding.Mode == "autonomous" && current.ControllerAllowed &&
                Phase == AgentConversationPhase.WaitingForReply && ReplyDeadlineUtc > now &&
                Pending is { Action: AgentConversationAction.Resume, Intent: AgentConversationIntent.Continue } p &&
                p.HeardTurnId == HeardTurnId && HeardTurnId.HasValue && p.PlayedResponseId == PlayedResponseId &&
                PlayedResponseId.HasValue && p.ExpiresUtc > now => AgentConversationDecision.Permit,
            AgentConversationAction.DispatchTool when toolRegistered && Phase is AgentConversationPhase.Interpreting or AgentConversationPhase.Retrieving => AgentConversationDecision.Permit,
            _ => new(false, "conversation_action_denied")
        };
    }

    public void Heard(Guid turnId, long expectedVersion, AgentConversationAuthority current, DateTime now)
    {
        RequireVersion(expectedVersion); Require(Authorize(current, AgentConversationAction.Interpret, now));
        if (turnId == Guid.Empty || turnId == HeardTurnId) throw new InvalidOperationException("Duplicate or empty input turn.");
        HeardTurnId = turnId; Pending = null; Phase = AgentConversationPhase.Interpreting; Version++;
    }

    public void Retrieve(long expectedVersion, AgentConversationAuthority current, DateTime now)
    {
        RequireVersion(expectedVersion); Require(Authorize(current, AgentConversationAction.Retrieve, now));
        // A new question supersedes the previous invitation to continue, even if retrieval fails.
        PlayedResponseId = null; ReplyDeadlineUtc = null; Pending = null;
        Phase = AgentConversationPhase.Retrieving; Version++;
    }

    public AgentConversationDecision AnswerReady(long expectedVersion, AgentConversationAuthority current,
        DateTime now, bool supported, bool exactHostApproval)
    {
        RequireVersion(expectedVersion);
        var decision = Authorize(current, AgentConversationAction.SpeakAnswer, now, supported, exactHostApproval);
        Phase = decision.Allowed ? AgentConversationPhase.SpeakingAnswer :
            decision.ApprovalRequired ? AgentConversationPhase.AwaitingApproval : AgentConversationPhase.Paused;
        Pending = null; Version++; return decision;
    }

    public void Played(Guid responseId, long expectedVersion, AgentConversationAuthority current, DateTime now)
    {
        RequireVersion(expectedVersion); Require(Check(current, now));
        if (responseId == Guid.Empty || Phase is not (AgentConversationPhase.SpeakingAnswer or AgentConversationPhase.SpeakingBridge))
            throw new InvalidOperationException("Only completed speech can establish reply context.");
        PlayedResponseId = responseId; Phase = AgentConversationPhase.WaitingForReply;
        ReplyDeadlineUtc = new[] { now.AddSeconds(45), current.ExpiresUtc }.Min(); Pending = null; Version++;
    }

    public void Bridge(long expectedVersion, AgentConversationAuthority current, DateTime now, bool validated)
    { RequireVersion(expectedVersion); Require(Authorize(current, AgentConversationAction.SpeakBridge, now, bridgeValidated: validated)); Phase = AgentConversationPhase.SpeakingBridge; Version++; }

    public AgentConversationDecision Propose(AgentConversationAction action, AgentConversationIntent intent,
        long expectedVersion, AgentConversationAuthority current, DateTime now)
    {
        RequireVersion(expectedVersion); Require(Check(current, now));
        if (Phase != AgentConversationPhase.Interpreting || !HeardTurnId.HasValue)
            return new(false, "confirmed_turn_required");
        // A proposal is not execution. Unknown intent and a new question cannot authorize resume.
        Pending = new(Guid.NewGuid(), ++Version, action, intent, HeardTurnId, PlayedResponseId, now.AddSeconds(15));
        Phase = AgentConversationPhase.WaitingForReply;
        return Authorize(current, action, now);
    }

    public AgentConversationDecision Consume(Guid proposalId, long expectedVersion, AgentConversationAuthority current, DateTime now)
    {
        RequireVersion(expectedVersion);
        if (Pending is not { } proposal || proposal.Id != proposalId) return new(false, "proposal_missing");
        var decision = Authorize(current, proposal.Action, now);
        Pending = null; Version++;
        Phase = decision.Allowed ? AgentConversationPhase.Presenting : AgentConversationPhase.Paused;
        return decision;
    }

    public void Pause(bool stopped = false)
    { Pending = null; ReplyDeadlineUtc = null; Phase = stopped ? AgentConversationPhase.Stopped : AgentConversationPhase.Paused; Version++; }
    private void RequireVersion(long expected) { if (Version != expected) throw new InvalidOperationException("Conversation version changed."); }
    private static void Require(AgentConversationDecision decision) { if (!decision.Allowed) throw new InvalidOperationException(decision.Code); }
}
