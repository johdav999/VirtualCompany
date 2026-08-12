namespace VirtualCompany.Domain.Entities;

public sealed class MarketingJourneyInboundEvent : ICompanyOwnedEntity
{
    private MarketingJourneyInboundEvent() { }
    public MarketingJourneyInboundEvent(Guid id, Guid companyId, Guid journeyId, int journeyVersion,
        Guid contactId, string eventType, string eventReference, int occurrenceVersion,
        DateTime occurredUtc, string evidenceJson, string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (journeyId == Guid.Empty || contactId == Guid.Empty || journeyVersion < 1 || occurrenceVersion < 1)
            throw new ArgumentException("Journey inbound-event identity is invalid.");
        System.Text.Json.JsonDocument.Parse(evidenceJson);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingLifecycleJourneyId = journeyId;
        JourneyVersion = journeyVersion; ContactId = contactId; EventType = SalesEntityText.NormalizeRequired(eventType, nameof(eventType), 80).ToLowerInvariant();
        EventReference = SalesEntityText.NormalizeRequired(eventReference, nameof(eventReference), 300);
        OccurrenceVersion = occurrenceVersion; OccurredUtc = occurredUtc.ToUniversalTime(); EvidenceJson = evidenceJson;
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 240); Outcome = "recorded";
        ProcessedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public Guid MarketingLifecycleJourneyId { get; private set; } public int JourneyVersion { get; private set; }
    public Guid ContactId { get; private set; } public string EventType { get; private set; } = null!;
    public string EventReference { get; private set; } = null!; public int OccurrenceVersion { get; private set; }
    public DateTime OccurredUtc { get; private set; } public string EvidenceJson { get; private set; } = "{}";
    public string IdempotencyKey { get; private set; } = null!; public string Outcome { get; private set; } = null!;
    public DateTime ProcessedUtc { get; private set; }
    public void SetOutcome(string outcome) { Outcome = SalesEntityText.NormalizeRequired(outcome, nameof(outcome), 40); ProcessedUtc = DateTime.UtcNow; }
}

public sealed class MarketingJourneyStepAttempt : ICompanyOwnedEntity
{
    private MarketingJourneyStepAttempt() { }
    public MarketingJourneyStepAttempt(Guid id, Guid companyId, Guid enrollmentId, int journeyVersion, int stepIndex,
        int attempt, string outcome, string policyEvidenceJson, Guid? channelActionId, string correlationId)
    {
        SalesEntityText.EnsureCompany(companyId); if (enrollmentId == Guid.Empty || journeyVersion < 1 || stepIndex < 0 || attempt < 1) throw new ArgumentException("Journey step attempt is invalid.");
        System.Text.Json.JsonDocument.Parse(policyEvidenceJson); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        MarketingJourneyEnrollmentId = enrollmentId; JourneyVersion = journeyVersion; StepIndex = stepIndex; Attempt = attempt;
        Outcome = SalesEntityText.NormalizeRequired(outcome, nameof(outcome), 40); PolicyEvidenceJson = policyEvidenceJson;
        MarketingChannelActionId = channelActionId; CorrelationId = SalesEntityText.NormalizeRequired(correlationId, nameof(correlationId), 128); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingJourneyEnrollmentId { get; private set; }
    public int JourneyVersion { get; private set; } public int StepIndex { get; private set; } public int Attempt { get; private set; }
    public string Outcome { get; private set; } = null!; public string PolicyEvidenceJson { get; private set; } = "{}";
    public Guid? MarketingChannelActionId { get; private set; } public string CorrelationId { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
}
