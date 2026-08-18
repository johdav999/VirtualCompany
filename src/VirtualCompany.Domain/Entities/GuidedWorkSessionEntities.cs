using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public static class GuidedWorkSessionStatuses
{
    public const string Active = "active";
    public const string ReviewReady = "review_ready";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

public static class GuidedDraftFieldStatuses
{
    public const string Missing = "missing";
    public const string Proposed = "proposed";
    public const string NeedsWork = "needs_work";
    public const string Confirmed = "confirmed";
    public const string Conflicting = "conflicting";
    public const string Unknown = "unknown";
}

public sealed class GuidedWorkSession : ICompanyOwnedEntity
{
    private GuidedWorkSession() { }

    public GuidedWorkSession(Guid id, Guid companyId, Guid conversationId, Guid agentId, Guid createdByUserId,
        string artifactType, string schemaVersion, Guid? targetArtifactId, string correlationId)
    {
        if (companyId == Guid.Empty || conversationId == Guid.Empty || agentId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("Company, conversation, agent, and user are required.");

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ConversationId = conversationId;
        AgentId = agentId;
        CreatedByUserId = createdByUserId;
        ArtifactType = NormalizeRequired(artifactType, nameof(artifactType), 96);
        SchemaVersion = NormalizeRequired(schemaVersion, nameof(schemaVersion), 32);
        TargetArtifactId = targetArtifactId;
        CorrelationId = NormalizeRequired(correlationId, nameof(correlationId), 128);
        Status = GuidedWorkSessionStatuses.Active;
        SafeSummary = "The workshop is ready to begin.";
        NextQuestion = "What would you like the agent to know first?";
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string ArtifactType { get; private set; } = null!;
    public string SchemaVersion { get; private set; } = null!;
    public Guid? TargetArtifactId { get; private set; }
    public string? TargetArtifactVersion { get; private set; }
    public string Status { get; private set; } = null!;
    public int Sequence { get; private set; }
    public int Version { get; private set; }
    public int RequiredFieldCount { get; private set; }
    public int ReadyFieldCount { get; private set; }
    public string SafeSummary { get; private set; } = null!;
    public string? NextQuestion { get; private set; }
    public string? ReviewTokenHash { get; private set; }
    public DateTime? ReviewTokenExpiresUtc { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Conversation Conversation { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;
    public ICollection<GuidedDraftField> Fields { get; } = new List<GuidedDraftField>();
    public ICollection<GuidedSessionOperation> Operations { get; } = new List<GuidedSessionOperation>();
    public ICollection<GuidedVoiceBinding> VoiceBindings { get; } = new List<GuidedVoiceBinding>();

    public void Advance(string summary, string? nextQuestion, int requiredCount, int readyCount)
    {
        EnsureMutable();
        SafeSummary = NormalizeRequired(summary, nameof(summary), 2000);
        NextQuestion = NormalizeOptional(nextQuestion, 1000);
        RequiredFieldCount = Math.Max(0, requiredCount);
        ReadyFieldCount = Math.Clamp(readyCount, 0, RequiredFieldCount);
        Sequence++;
        Touch();
    }

    public void SetInitialTargetVersion(string? targetVersion)
    {
        if (Sequence != 0) throw new InvalidOperationException("Target version can only be initialized before the session starts.");
        TargetArtifactVersion = NormalizeOptional(targetVersion, 128);
    }

    public void PrepareReview(string tokenHash, DateTime expiresUtc, int requiredCount, int readyCount, string summary)
    {
        EnsureMutable();
        if (readyCount < requiredCount) throw new InvalidOperationException("Required fields are incomplete.");
        Status = GuidedWorkSessionStatuses.ReviewReady;
        ReviewTokenHash = NormalizeRequired(tokenHash, nameof(tokenHash), 128);
        ReviewTokenExpiresUtc = expiresUtc;
        RequiredFieldCount = requiredCount;
        ReadyFieldCount = readyCount;
        SafeSummary = NormalizeRequired(summary, nameof(summary), 2000);
        NextQuestion = null;
        Touch();
    }

    public void ReturnToActive()
    {
        EnsureMutable();
        Status = GuidedWorkSessionStatuses.Active;
        ReviewTokenHash = null;
        ReviewTokenExpiresUtc = null;
        Touch();
    }

    public void Complete(string? targetVersion)
    {
        EnsureMutable();
        Status = GuidedWorkSessionStatuses.Completed;
        TargetArtifactVersion = NormalizeOptional(targetVersion, 128);
        ReviewTokenHash = null;
        ReviewTokenExpiresUtc = null;
        CompletedUtc = UpdatedUtc = DateTime.UtcNow;
        Version++;
    }

    public void Cancel()
    {
        EnsureMutable();
        Status = GuidedWorkSessionStatuses.Cancelled;
        ReviewTokenHash = null;
        ReviewTokenExpiresUtc = null;
        CancelledUtc = UpdatedUtc = DateTime.UtcNow;
        Version++;
    }

    private void EnsureMutable()
    {
        if (Status is GuidedWorkSessionStatuses.Completed or GuidedWorkSessionStatuses.Cancelled)
            throw new InvalidOperationException("The guided session is no longer editable.");
    }

    private void Touch() { UpdatedUtc = DateTime.UtcNow; Version++; }
    private static string NormalizeRequired(string value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) :
        value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name) : value.Trim();
    private static string? NormalizeOptional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null :
        value.Trim().Length > max ? throw new ArgumentOutOfRangeException(nameof(value)) : value.Trim();
}

public sealed class GuidedDraftField : ICompanyOwnedEntity
{
    private GuidedDraftField() { }
    public GuidedDraftField(Guid id, Guid companyId, Guid sessionId, string path, string label, string valueType, bool isRequired)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SessionId = sessionId;
        Path = Required(path, 160);
        Label = Required(label, 160);
        ValueType = Required(valueType, 32);
        IsRequired = isRequired;
        Status = GuidedDraftFieldStatuses.Missing;
        SourceType = "none";
        UpdatedUtc = DateTime.UtcNow;
        Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public string Path { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public string ValueType { get; private set; } = null!;
    public bool IsRequired { get; private set; }
    public string? ValueJson { get; private set; }
    public string Status { get; private set; } = null!;
    public string SourceType { get; private set; } = null!;
    public Guid? SourceMessageId { get; private set; }
    public Dictionary<string, JsonNode?> SourceMetadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Explanation { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public int Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public GuidedWorkSession Session { get; private set; } = null!;

    public void Set(string? valueJson, string status, string sourceType, Guid? sourceMessageId,
        IDictionary<string, JsonNode?>? sourceMetadata, string? explanation)
    {
        var originalValue = SourceMetadata.TryGetValue("original_value_json", out var original) ? original?.DeepClone() : null;
        ValueJson = string.IsNullOrWhiteSpace(valueJson) ? null : valueJson;
        Status = Required(status, 32);
        SourceType = Required(sourceType, 32);
        SourceMessageId = sourceMessageId;
        SourceMetadata = sourceMetadata?.ToDictionary(x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase)
            ?? new(StringComparer.OrdinalIgnoreCase);
        if (originalValue is not null && !SourceMetadata.ContainsKey("original_value_json")) SourceMetadata["original_value_json"] = originalValue;
        Explanation = string.IsNullOrWhiteSpace(explanation) ? null : explanation.Trim()[..Math.Min(explanation.Trim().Length, 1000)];
        UpdatedUtc = DateTime.UtcNow;
        Version++;
    }
    private static string Required(string value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max
        ? throw new ArgumentException("A bounded value is required.") : value.Trim();
}

public sealed class GuidedSessionOperation : ICompanyOwnedEntity
{
    private GuidedSessionOperation() { }
    public GuidedSessionOperation(Guid id, Guid companyId, Guid sessionId, Guid clientRequestId, string operationType, string responseJson)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId; SessionId = sessionId; ClientRequestId = clientRequestId;
        OperationType = operationType; ResponseJson = responseJson; CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ClientRequestId { get; private set; }
    public string OperationType { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public GuidedWorkSession Session { get; private set; } = null!;
}

public sealed class GuidedVoiceBinding : ICompanyOwnedEntity
{
    private GuidedVoiceBinding() { }
    public GuidedVoiceBinding(Guid id, Guid companyId, Guid sessionId, Guid userId, string providerCallId, DateTime expiresUtc)
    {
        Id=id==Guid.Empty?Guid.NewGuid():id; CompanyId=companyId; SessionId=sessionId; UserId=userId;
        ProviderCallId=string.IsNullOrWhiteSpace(providerCallId)?throw new ArgumentException("Provider call id is required."):providerCallId.Trim();
        Status="connecting"; ExpiresUtc=expiresUtc; CreatedUtc=UpdatedUtc=DateTime.UtcNow;
    }
    public Guid Id{get;private set;} public Guid CompanyId{get;private set;} public Guid SessionId{get;private set;} public Guid UserId{get;private set;}
    public string ProviderCallId{get;private set;}=null!; public string Status{get;private set;}=null!; public int ReconnectCount{get;private set;}
    public string? LastProviderEventId{get;private set;} public DateTime ExpiresUtc{get;private set;} public DateTime CreatedUtc{get;private set;}
    public DateTime UpdatedUtc{get;private set;} public DateTime? EndedUtc{get;private set;} public Company Company{get;private set;}=null!; public GuidedWorkSession Session{get;private set;}=null!; public User User{get;private set;}=null!;
    public void Connected(){Status="active";UpdatedUtc=DateTime.UtcNow;} public void Reconnecting(){Status="reconnecting";ReconnectCount++;UpdatedUtc=DateTime.UtcNow;}
    public void RecordEvent(string? eventId){if(!string.IsNullOrWhiteSpace(eventId))LastProviderEventId=eventId.Trim()[..Math.Min(eventId.Trim().Length,128)];UpdatedUtc=DateTime.UtcNow;}
    public void End(string status){Status=string.IsNullOrWhiteSpace(status)?"ended":status.Trim()[..Math.Min(status.Trim().Length,32)];EndedUtc=UpdatedUtc=DateTime.UtcNow;}
}
