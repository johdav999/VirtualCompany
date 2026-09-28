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
        pending = (scope, text, now.AddSeconds(12), complete);
    }
    // Only a semantic verdict can release a turn. Silence merely removes a transport fence.
    public string? ReleaseComplete(DateTime now, bool inputBusy)
    {
        Expire(now);
        if (inputBusy || pending is not { Complete: true } p) return null;
        pending = null;
        return p.Text;
    }
    public void Expire(DateTime now) { if (pending is { } p && p.Expires <= now) pending = null; }
    public void Clear() => pending = null;
}
