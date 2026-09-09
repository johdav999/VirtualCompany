using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingVoiceSession : ICompanyOwnedEntity
{
    private SalesMeetingVoiceSession() { }

    public SalesMeetingVoiceSession(Guid id, Guid companyId, Guid meetingSessionId, Guid agentId, Guid startedByUserId,
        string mediaRoute, DateTime expiresUtc, DateTime nowUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, meetingSessionId, agentId, startedByUserId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MeetingSessionId = meetingSessionId;
        AgentId = agentId;
        StartedByUserId = startedByUserId;
        MediaRoute = Required(mediaRoute, nameof(mediaRoute), 64);
        ExpiresUtc = Utc(expiresUtc);
        CreatedUtc = UpdatedUtc = Utc(nowUtc);
        if (ExpiresUtc <= CreatedUtc) throw new ArgumentOutOfRangeException(nameof(expiresUtc));
        Status = SalesMeetingVoiceSessionStatus.Connecting;
        ConcurrencyVersion = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MeetingSessionId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid StartedByUserId { get; private set; }
    public string MediaRoute { get; private set; } = null!;
    public string? Provider { get; private set; }
    public string? ProviderSessionId { get; private set; }
    public string? Model { get; private set; }
    public string? MediaTransport { get; private set; }
    public SalesMeetingVoiceSessionStatus Status { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? ConnectedUtc { get; private set; }
    public DateTime? EndedUtc { get; private set; }
    public long LastProviderSequence { get; private set; }
    public int ReconnectCount { get; private set; }
    public int AudioDurationMilliseconds { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public SalesMeetingSession MeetingSession { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;
    public ICollection<SalesMeetingVoiceEventReceipt> Events { get; } = new List<SalesMeetingVoiceEventReceipt>();

    public void Activate(string provider, string providerSessionId, string model, string mediaTransport, DateTime expiresUtc, DateTime nowUtc)
    {
        if (Status != SalesMeetingVoiceSessionStatus.Connecting) throw new InvalidOperationException("Only a connecting voice session can become active.");
        Provider = Required(provider, nameof(provider), 64);
        ProviderSessionId = Required(providerSessionId, nameof(providerSessionId), 200);
        Model = Required(model, nameof(model), 120);
        MediaTransport = Required(mediaTransport, nameof(mediaTransport), 64);
        ExpiresUtc = Utc(expiresUtc);
        Status = SalesMeetingVoiceSessionStatus.Active;
        ConnectedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    public void RecordEvent(long sequence, int audioMilliseconds, int inputTokens, int outputTokens, DateTime nowUtc)
    {
        if (Status is not (SalesMeetingVoiceSessionStatus.Active or SalesMeetingVoiceSessionStatus.Reconnecting or SalesMeetingVoiceSessionStatus.Degraded))
            throw new InvalidOperationException("Voice events require an active or degraded session.");
        if (sequence <= LastProviderSequence) throw new InvalidOperationException("Realtime event sequence is not newer.");
        if (audioMilliseconds < 0 || inputTokens < 0 || outputTokens < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        LastProviderSequence = sequence;
        AudioDurationMilliseconds = checked(AudioDurationMilliseconds + audioMilliseconds);
        InputTokens = checked(InputTokens + inputTokens);
        OutputTokens = checked(OutputTokens + outputTokens);
        Touch(nowUtc);
    }

    public void MarkReconnecting(int maximumReconnects, string summary, DateTime nowUtc)
    {
        if (ReconnectCount >= maximumReconnects) { Fail("reconnect_limit_exceeded", summary, SalesMeetingVoiceSessionStatus.Degraded, nowUtc); return; }
        ReconnectCount++;
        Status = SalesMeetingVoiceSessionStatus.Reconnecting;
        LastErrorCode = "reconnecting";
        LastErrorSummary = Required(summary, nameof(summary), 1000);
        Touch(nowUtc);
    }

    public void MarkConnected(DateTime nowUtc)
    {
        Status = SalesMeetingVoiceSessionStatus.Active;
        LastErrorCode = LastErrorSummary = null;
        Touch(nowUtc);
    }

    public void Stop(long expectedVersion, string reason, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        if (EndedUtc.HasValue) return;
        Status = SalesMeetingVoiceSessionStatus.Stopped;
        LastErrorSummary = Required(reason, nameof(reason), 1000);
        EndedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    public void RevokeConsent(long expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        Status = SalesMeetingVoiceSessionStatus.ConsentRevoked;
        LastErrorCode = "consent_revoked";
        LastErrorSummary = "Meeting media consent was revoked. Voice capture stopped.";
        EndedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    public void Fail(string code, string summary, SalesMeetingVoiceSessionStatus status, DateTime nowUtc)
    {
        if (status is not (SalesMeetingVoiceSessionStatus.Degraded or SalesMeetingVoiceSessionStatus.Failed or SalesMeetingVoiceSessionStatus.QuotaExceeded))
            throw new ArgumentOutOfRangeException(nameof(status));
        Status = status;
        LastErrorCode = Required(code, nameof(code), 120);
        LastErrorSummary = Required(summary, nameof(summary), 1000);
        if (status != SalesMeetingVoiceSessionStatus.Degraded) EndedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    private void EnsureVersion(long value) { if (value != ConcurrencyVersion) throw new InvalidOperationException("The voice session changed after it was opened."); }
    private void Touch(DateTime value) { UpdatedUtc = Utc(value); ConcurrencyVersion++; }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim()[..Math.Min(value.Trim().Length, max)];
}

public sealed class SalesMeetingVoiceEventReceipt : ICompanyOwnedEntity
{
    private SalesMeetingVoiceEventReceipt() { }

    public SalesMeetingVoiceEventReceipt(Guid id, Guid companyId, Guid voiceSessionId, string providerEventId,
        long sequence, string eventType, DateTime occurredUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, voiceSessionId);
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        VoiceSessionId = voiceSessionId;
        ProviderEventId = Required(providerEventId, nameof(providerEventId), 200);
        Sequence = sequence;
        EventType = Required(eventType, nameof(eventType), 80);
        Outcome = SalesMeetingVoiceEventOutcome.Processed;
        OccurredUtc = Utc(occurredUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VoiceSessionId { get; private set; }
    public string ProviderEventId { get; private set; } = null!;
    public long Sequence { get; private set; }
    public string EventType { get; private set; } = null!;
    public SalesMeetingVoiceEventOutcome Outcome { get; private set; }
    public Guid? QuestionId { get; private set; }
    public string? ResultJson { get; private set; }
    public string? ReasonCode { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public SalesMeetingVoiceSession VoiceSession { get; private set; } = null!;

    public void Complete(SalesMeetingVoiceEventOutcome outcome, Guid? questionId, string? resultJson, string? reasonCode)
    {
        Outcome = outcome;
        QuestionId = questionId == Guid.Empty ? null : questionId;
        ResultJson = Optional(resultJson, 8000);
        ReasonCode = Optional(reasonCode, 120);
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
