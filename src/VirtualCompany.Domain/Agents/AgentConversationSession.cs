namespace VirtualCompany.Domain.Agents;

/// <summary>
/// Availability of an established conversation, independent of any output or presentation
/// turn. The existing host owns the lease and media connection. Fresh input takes a fresh
/// authority snapshot; an old action must still use its original AgentConversation binding.
/// Only bounded reference IDs are kept here, never audio, text or executable proposals.
/// </summary>
public sealed class AgentConversationSession(Guid companyId, Guid agentId, Guid conversationId,
    Guid sessionId, Guid ownerId, long ownerGeneration)
{
    private const int HistoryLimit = 128;
    private readonly HashSet<Guid> heard = [];
    private readonly Queue<Guid> heardOrder = new();
    private readonly HashSet<Guid> played = [];
    private readonly Queue<Guid> playedOrder = new();
    public bool Stopped { get; private set; }

    public AgentConversation? BeginTurn(AgentConversationAuthority authority, Guid turnId, DateTime now)
    {
        var b = authority.Binding;
        if (Stopped || turnId == Guid.Empty || heard.Contains(turnId) || b.CompanyId != companyId ||
            b.AgentId != agentId || b.ConversationId != conversationId || b.SessionId != sessionId ||
            b.OwnerId != ownerId || b.OwnerGeneration != ownerGeneration) return null;
        var turn = new AgentConversation(b, AgentConversationPhase.Listening);
        if (!turn.Check(authority, now).Allowed) return null;
        turn.Heard(turnId, turn.Version, authority, now);
        Remember(heard, heardOrder, turnId);
        return turn;
    }

    public bool HasHeard(Guid id) => heard.Contains(id);
    public bool HasPlayed(Guid id) => played.Contains(id);
    public void RecordPlayed(Guid id) { if (!Stopped) Remember(played, playedOrder, id); }
    public void Stop()
    {
        Stopped = true;
        heard.Clear(); heardOrder.Clear(); played.Clear(); playedOrder.Clear();
    }

    private static void Remember(HashSet<Guid> ids, Queue<Guid> order, Guid id)
    {
        if (!ids.Add(id)) return;
        order.Enqueue(id);
        while (order.Count > HistoryLimit) ids.Remove(order.Dequeue());
    }
}
