namespace VirtualCompany.Domain.Entities;

public sealed class AgentOrchestrationRun : ICompanyOwnedEntity
{
    private AgentOrchestrationRun() { }

    public AgentOrchestrationRun(Guid companyId, Guid agentId, Guid? actorUserId, string capabilityId,
        string capabilityVersion, string promptVersion, string schemaVersion, string correlationId,
        Guid? taskId = null, Guid? conversationId = null)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
        Id = Guid.NewGuid(); CompanyId = companyId; AgentId = agentId; ActorUserId = actorUserId;
        CapabilityId = Required(capabilityId, 100); CapabilityVersion = Required(capabilityVersion, 32);
        PromptVersion = Required(promptVersion, 32); SchemaVersion = Required(schemaVersion, 32);
        CorrelationId = Required(correlationId, 128); TaskId = taskId; ConversationId = conversationId;
        Status = "running"; CreatedUtc = UpdatedUtc = DateTime.UtcNow; StartedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? ConversationId { get; private set; }
    public string CapabilityId { get; private set; } = null!;
    public string CapabilityVersion { get; private set; } = null!;
    public string PromptVersion { get; private set; } = null!;
    public string SchemaVersion { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? Provider { get; private set; }
    public string? Model { get; private set; }
    public decimal? Confidence { get; private set; }
    public string? Summary { get; private set; }
    public string? ResultJson { get; private set; }
    public string? SourceIdsJson { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public long? LatencyMilliseconds { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; } = 1;

    public void Complete(string status, string provider, string model, decimal confidence, string summary,
        string resultJson, string sourceIdsJson, int? inputTokens, int? outputTokens, long latencyMilliseconds)
    {
        EnsureRunning();
        if (status is not ("completed" or "needs_review" or "blocked")) throw new ArgumentException("Invalid completion status.");
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        Status = status; Provider = Optional(provider, 100); Model = Optional(model, 200); Confidence = confidence;
        Summary = Optional(summary, 2000); ResultJson = resultJson; SourceIdsJson = sourceIdsJson;
        InputTokens = inputTokens; OutputTokens = outputTokens; LatencyMilliseconds = latencyMilliseconds;
        CompletedUtc = UpdatedUtc = DateTime.UtcNow;
        Version++;
    }

    public void Fail(string status, string code, string message, long latencyMilliseconds)
    {
        EnsureRunning();
        if (status is not ("failed" or "cancelled" or "blocked")) throw new ArgumentException("Invalid failure status.");
        Status = status; FailureCode = Required(code, 100); FailureMessage = Optional(message, 1000);
        LatencyMilliseconds = latencyMilliseconds; CompletedUtc = UpdatedUtc = DateTime.UtcNow;
        Version++;
    }

    private void EnsureRunning() { if (Status != "running") throw new InvalidOperationException("The run is already terminal."); }
    internal static string Required(string? value, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A required value is missing.") : value.Trim()[..Math.Min(value.Trim().Length, max)];
    internal static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}

public sealed class AgentHandoff : ICompanyOwnedEntity
{
    private AgentHandoff() { }
    public AgentHandoff(Guid companyId, string type, Guid requestingAgentId, Guid receivingAgentId, string objective,
        string requestedOutcome, DateTime? dueUtc, string evidenceJson, string correlationId, Guid? relatedTaskId)
    {
        if (companyId == Guid.Empty || requestingAgentId == Guid.Empty || receivingAgentId == Guid.Empty) throw new ArgumentException("Company and agents are required.");
        if (requestingAgentId == receivingAgentId) throw new ArgumentException("A handoff requires different agents.");
        Id = Guid.NewGuid(); CompanyId = companyId; Type = AgentOrchestrationRun.Required(type, 100); Version = "1.0";
        RequestingAgentId = requestingAgentId; ReceivingAgentId = receivingAgentId;
        Objective = AgentOrchestrationRun.Required(objective, 1000); RequestedOutcome = AgentOrchestrationRun.Required(requestedOutcome, 1000);
        DueUtc = dueUtc; EvidenceJson = evidenceJson; CorrelationId = AgentOrchestrationRun.Required(correlationId, 128);
        RelatedTaskId = relatedTaskId; Status = "proposed"; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public string Type { get; private set; } = null!; public string Version { get; private set; } = null!;
    public Guid RequestingAgentId { get; private set; } public Guid ReceivingAgentId { get; private set; }
    public string Objective { get; private set; } = null!; public string RequestedOutcome { get; private set; } = null!;
    public string Status { get; private set; } = null!; public DateTime? DueUtc { get; private set; }
    public string EvidenceJson { get; private set; } = "[]"; public Guid? RelatedTaskId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; } public string? CompletionSummary { get; private set; }
    public decimal? Confidence { get; private set; } public string? FailureReason { get; private set; }
    public string CorrelationId { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; } public DateTime? CompletedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; } = 1;
    public void Transition(string next, string? summary = null, decimal? confidence = null)
    {
        var allowed = Status switch { "proposed" => new[] { "accepted", "rejected", "cancelled" }, "accepted" => new[] { "in_progress", "cancelled" },
            "in_progress" => new[] { "awaiting_information", "awaiting_approval", "completed", "failed", "escalated", "cancelled" },
            "awaiting_information" or "awaiting_approval" => new[] { "in_progress", "failed", "cancelled", "escalated" }, _ => [] };
        if (!allowed.Contains(next)) throw new InvalidOperationException($"Cannot move handoff from {Status} to {next}.");
        Status = next; CompletionSummary = AgentOrchestrationRun.Optional(summary, 2000); Confidence = confidence;
        UpdatedUtc = DateTime.UtcNow; if (next is "completed" or "rejected" or "cancelled" or "failed") CompletedUtc = UpdatedUtc;
        ConcurrencyVersion++;
    }
}

public sealed class AgentMemoryCandidate : ICompanyOwnedEntity
{
    private AgentMemoryCandidate() { }
    public AgentMemoryCandidate(Guid companyId, Guid proposingAgentId, string memoryType, string scope, string content,
        string evidenceJson, decimal confidence, string sensitivity, DateTime expiresUtc, string fingerprint, Guid? runId)
    {
        if (companyId == Guid.Empty || proposingAgentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        Id = Guid.NewGuid(); CompanyId = companyId; ProposingAgentId = proposingAgentId;
        MemoryType = AgentOrchestrationRun.Required(memoryType, 64); Scope = AgentOrchestrationRun.Required(scope, 64);
        Content = AgentOrchestrationRun.Required(content, 4000); EvidenceJson = evidenceJson; Confidence = confidence;
        Sensitivity = AgentOrchestrationRun.Required(sensitivity, 32); ExpiresUtc = expiresUtc; Fingerprint = AgentOrchestrationRun.Required(fingerprint, 128);
        OrchestrationRunId = runId; Status = "needs_review"; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ProposingAgentId { get; private set; }
    public string MemoryType { get; private set; } = null!; public string Scope { get; private set; } = null!;
    public string Content { get; private set; } = null!; public string EvidenceJson { get; private set; } = "[]";
    public decimal Confidence { get; private set; } public string Sensitivity { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!; public string Status { get; private set; } = null!;
    public Guid? OrchestrationRunId { get; private set; } public Guid? ActivatedMemoryItemId { get; private set; }
    public Guid? ReviewerUserId { get; private set; } public string? ReviewReason { get; private set; }
    public DateTime ExpiresUtc { get; private set; } public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; } public DateTime? ReviewedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; } = 1;
    public void Approve(Guid reviewer) { EnsureReviewable(); ReviewerUserId = reviewer; Status = "approved"; ReviewedUtc = UpdatedUtc = DateTime.UtcNow; ConcurrencyVersion++; }
    public void Reject(Guid reviewer, string reason) { EnsureReviewable(); ReviewerUserId = reviewer; ReviewReason = AgentOrchestrationRun.Required(reason, 500); Status = "rejected"; ReviewedUtc = UpdatedUtc = DateTime.UtcNow; ConcurrencyVersion++; }
    public void Activate(Guid memoryItemId) { if (Status != "approved") throw new InvalidOperationException("Only approved candidates can be activated."); ActivatedMemoryItemId = memoryItemId; Status = "activated"; UpdatedUtc = DateTime.UtcNow; ConcurrencyVersion++; }
    public void Expire() { if (Status is "activated" or "rejected") return; Status = "expired"; UpdatedUtc = DateTime.UtcNow; ConcurrencyVersion++; }
    private void EnsureReviewable() { if (Status != "needs_review") throw new InvalidOperationException("Candidate is not awaiting review."); }
}

public sealed class AgentAiQualityEvent : ICompanyOwnedEntity
{
    private AgentAiQualityEvent() { }
    public AgentAiQualityEvent(Guid companyId, Guid agentId, string capabilityId, Guid? runId, string eventType,
        string eventIdentity, string? reasonCode, string? comment, decimal? confidence, string correlationId)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        Id = Guid.NewGuid(); CompanyId = companyId; AgentId = agentId; CapabilityId = AgentOrchestrationRun.Required(capabilityId, 100);
        OrchestrationRunId = runId; EventType = AgentOrchestrationRun.Required(eventType, 64); EventIdentity = AgentOrchestrationRun.Required(eventIdentity, 200);
        ReasonCode = AgentOrchestrationRun.Optional(reasonCode, 100); Comment = AgentOrchestrationRun.Optional(comment, 1000);
        Confidence = confidence; CorrelationId = AgentOrchestrationRun.Required(correlationId, 128); OccurredUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid AgentId { get; private set; }
    public string CapabilityId { get; private set; } = null!; public Guid? OrchestrationRunId { get; private set; }
    public string EventType { get; private set; } = null!; public string EventIdentity { get; private set; } = null!;
    public string? ReasonCode { get; private set; } public string? Comment { get; private set; } public decimal? Confidence { get; private set; }
    public string CorrelationId { get; private set; } = null!; public DateTime OccurredUtc { get; private set; }
}
