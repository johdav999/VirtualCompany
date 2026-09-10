namespace VirtualCompany.Domain.Entities;

public static class SalesRoomAgentSpeechKinds
{ public const string Narration = "narration", Answer = "answer"; }
public static class SalesRoomAgentSpeechStates
{ public const string Queued = "queued", Processing = "processing", Spoken = "spoken", Interrupted = "interrupted", Failed = "failed", Withheld = "withheld"; }

public sealed class SalesRoomAgentSpeech : ICompanyOwnedEntity
{
    private SalesRoomAgentSpeech() { }
    public SalesRoomAgentSpeech(Guid id, Guid companyId, Guid roomId, Guid sessionId, Guid commandId,
        Guid agentId, long agentGeneration, long turnGeneration, string kind, Guid requestedByUserId,
        DateTime createdUtc, Guid? narrationRevisionId = null, Guid? narrationSegmentId = null,
        Guid? questionId = null, int offsetMilliseconds = 0, long responseGeneration = 1)
    {
        if (companyId == Guid.Empty || roomId == Guid.Empty || sessionId == Guid.Empty || commandId == Guid.Empty ||
            agentId == Guid.Empty || requestedByUserId == Guid.Empty || agentGeneration < 1 || turnGeneration < 1 || responseGeneration < 1)
            throw new ArgumentException("A complete room speech binding is required.");
        if (kind == SalesRoomAgentSpeechKinds.Narration && (narrationRevisionId is null || narrationSegmentId is null || questionId is not null) ||
            kind == SalesRoomAgentSpeechKinds.Answer && (questionId is null || narrationRevisionId is not null || narrationSegmentId is not null) ||
            kind is not (SalesRoomAgentSpeechKinds.Narration or SalesRoomAgentSpeechKinds.Answer) || offsetMilliseconds < 0)
            throw new ArgumentException("The room speech source is invalid.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; RoomId = roomId; SessionId = sessionId;
        CommandId = commandId; AgentId = agentId; AgentGeneration = agentGeneration; TurnGeneration = turnGeneration;
        ResponseGeneration = responseGeneration;
        Kind = kind; RequestedByUserId = requestedByUserId; CreatedUtc = createdUtc; UpdatedUtc = createdUtc;
        NarrationRevisionId = narrationRevisionId; NarrationSegmentId = narrationSegmentId; QuestionId = questionId;
        OffsetMilliseconds = offsetMilliseconds; Status = SalesRoomAgentSpeechStates.Queued; Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid CommandId { get; private set; }
    public Guid AgentId { get; private set; }
    public long AgentGeneration { get; private set; }
    public long TurnGeneration { get; private set; }
    public long ResponseGeneration { get; private set; } = 1;
    public string Kind { get; private set; } = null!;
    public Guid? NarrationRevisionId { get; private set; }
    public Guid? NarrationSegmentId { get; private set; }
    public Guid? QuestionId { get; private set; }
    public int OffsetMilliseconds { get; private set; }
    public string Status { get; private set; } = null!;
    public string? ReleasedText { get; private set; }
    public string? EvidenceJson { get; private set; }
    public string? ProviderResponseId { get; private set; }
    public int DurationMilliseconds { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; }
    public void ExpireContent() { ReleasedText = null; EvidenceJson = null; FailureSummary = null; Version++; }
    public void Claim(DateTime now) { if (Status != SalesRoomAgentSpeechStates.Queued) throw new InvalidOperationException("Speech is not queued."); Status = SalesRoomAgentSpeechStates.Processing; UpdatedUtc = now; Version++; }
    public void Complete(string? releasedText, string? evidenceJson, string? providerResponseId, int durationMilliseconds, DateTime now)
    { Status = SalesRoomAgentSpeechStates.Spoken; ReleasedText = Limit(releasedText, 8000); EvidenceJson = Limit(evidenceJson, 16000); ProviderResponseId = Limit(providerResponseId, 200); DurationMilliseconds = Math.Max(0, durationMilliseconds); CompletedUtc = UpdatedUtc = now; Version++; }
    public void Fail(string status, string code, string summary, DateTime now)
    { if (status is not (SalesRoomAgentSpeechStates.Interrupted or SalesRoomAgentSpeechStates.Failed or SalesRoomAgentSpeechStates.Withheld)) throw new ArgumentException("Invalid terminal speech status."); Status = status; FailureCode = Limit(code, 100); FailureSummary = Limit(summary, 1000); CompletedUtc = UpdatedUtc = now; Version++; }
    private static string? Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(max, value.Trim().Length)];
}
