namespace VirtualCompany.Domain.Agents;

// Ephemeral transcript assembly, not conversation memory. Time/size limits only bound
// retention; they never decide that an unfinished sentence is ready to answer.
public sealed record AgentSpeechTurnScope(Guid CompanyId, Guid ConversationId, Guid ParticipantId,
    long ParticipantGeneration, long ConsentVersion, string TrackId, long TrackGeneration,
    long OwnerGeneration, long TurnGeneration);

public sealed class AgentSpeechTurnBuffer
{
    private (AgentSpeechTurnScope Scope, string Text, DateTime Expires, bool Complete)? pending;
    public string? Take(AgentSpeechTurnScope scope, string next, DateTime now)
    {
        var previous = pending;
        pending = null;
        var text = previous is { } p && p.Scope == scope && p.Expires > now
            ? p.Text + " " + next.Trim() : next.Trim();
        return text.Length is > 0 and <= 2000 ? text : null;
    }
    public void Hold(AgentSpeechTurnScope scope, string text, DateTime now, bool complete = false)
    {
        if (text.Length is < 1 or > 2000) throw new ArgumentException("A bounded transcript is required.");
        pending = (scope, text, now.AddSeconds(complete ? 60 : 12), complete);
    }
    // Only a semantic verdict can release a turn. Silence merely removes a transport fence.
    public string? ReleaseComplete(DateTime now, bool inputBusy)
    {
        Expire(now);
        if (inputBusy || pending is not { Complete: true } p) return null;
        pending = null;
        return p.Text;
    }
    public bool Expire(DateTime now)
    {
        if (pending is not { } p || p.Expires > now) return false;
        pending = null;
        return p.Complete;
    }
    // Expiry can request clarification, never establish a factual question. The caller
    // must recheck this exact scope before releasing any recovery speech.
    public (AgentSpeechTurnScope Scope, string Text)? TakeExpiredIncomplete(DateTime now)
    {
        if (pending is not { Complete: false } p || p.Expires > now) return null;
        pending = null;
        return (p.Scope, p.Text);
    }
    public void Clear() => pending = null;
}
