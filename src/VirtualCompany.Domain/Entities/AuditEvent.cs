namespace VirtualCompany.Domain.Entities;

public sealed record AuditDataSourceUsed(
    string SourceType,
    string? SourceId = null,
    string? DisplayName = null,
    string? Reference = null);

public sealed class AuditEvent : ICompanyOwnedEntity
{
    private AuditEvent()
    {
        Metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    private const string AgentActorType = "agent";
    private const string AgentTargetType = "agent";
    private const string AgentToolExecutionTargetType = "agent_tool_execution";
    private const string ApprovalRequestTargetType = "approval_request";
    private const string WorkflowInstanceTargetType = "workflow_instance";
    private const string WorkTaskTargetType = "work_task";

    public AuditEvent(
        Guid id,
        Guid companyId,
        string actorType,
        Guid? actorId,
        string action,
        string targetType,
        string targetId,
        string outcome,
        string? rationaleSummary = null,
        IEnumerable<string>? dataSources = null,
        IReadOnlyDictionary<string, string?>? metadata = null,
        string? correlationId = null,
        DateTime? occurredUtc = null,
        IEnumerable<AuditDataSourceUsed>? dataSourcesUsed = null,
        string? payloadDiffJson = null,
        string? agentName = null,
        string? agentRole = null,
        string? responsibilityDomain = null,
        string? promptProfileVersion = null,
        string? boundaryDecisionOutcome = null,
        string? identityReasonCode = null,
        string? boundaryReasonCode = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (actorId.HasValue && actorId.Value == Guid.Empty)
        {
            throw new ArgumentException("ActorId cannot be empty.", nameof(actorId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ActorType = NormalizeRequired(actorType, nameof(actorType), 64);
        ActorId = actorId;
        Action = NormalizeRequired(action, nameof(action), 128);
        TargetType = NormalizeRequired(targetType, nameof(targetType), 128);
        TargetId = NormalizeRequired(targetId, nameof(targetId), 128);
        Outcome = NormalizeRequired(outcome, nameof(outcome), 64);
        RationaleSummary = NormalizeOptional(rationaleSummary, nameof(rationaleSummary), 512);
        DataSources = NormalizeDataSources(dataSources);
        DataSourcesUsed = NormalizeDataSourcesUsed(dataSourcesUsed);
        Metadata = NormalizeMetadata(metadata);
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        RelatedAgentId = ResolveActorAgentId() ?? ResolveRelatedId(AgentTargetType, "agentId");
        RelatedTaskId = ResolveRelatedId(WorkTaskTargetType, "taskId", "workTaskId");
        RelatedWorkflowInstanceId = ResolveRelatedId(WorkflowInstanceTargetType, "workflowInstanceId");
        RelatedApprovalRequestId = ResolveRelatedId(ApprovalRequestTargetType, "approvalRequestId");
        RelatedToolExecutionAttemptId = ResolveRelatedId(AgentToolExecutionTargetType, "toolExecutionId", "toolExecutionAttemptId");
        OccurredUtc = occurredUtc ?? DateTime.UtcNow;
        PayloadDiffJson = NormalizeOptional(payloadDiffJson, nameof(payloadDiffJson), 16000);
        AgentName = NormalizeOptional(agentName ?? ResolveMetadataValue("agentName", "agentDisplayName"), nameof(agentName), 200);
        AgentRole = NormalizeOptional(agentRole ?? ResolveMetadataValue("agentRole", "agentRoleName"), nameof(agentRole), 128);
        ResponsibilityDomain = NormalizeOptional(responsibilityDomain ?? ResolveMetadataValue("responsibilityDomain", "requestedDomain"), nameof(responsibilityDomain), 128);
        PromptProfileVersion = NormalizeOptional(promptProfileVersion ?? ResolveMetadataValue("promptProfileVersion"), nameof(promptProfileVersion), 64);
        BoundaryDecisionOutcome = NormalizeOptional(boundaryDecisionOutcome ?? ResolveMetadataValue("boundaryDecisionOutcome"), nameof(boundaryDecisionOutcome), 64);
        IdentityReasonCode = NormalizeOptional(identityReasonCode ?? ResolveMetadataValue("identityReasonCode", "reasonCode"), nameof(identityReasonCode), 128);
        BoundaryReasonCode = NormalizeOptional(boundaryReasonCode ?? ResolveMetadataValue("boundaryReasonCode"), nameof(boundaryReasonCode), 128);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ActorType { get; private set; } = null!;
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = null!;
    public string TargetType { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public string? RationaleSummary { get; private set; }
    public List<string> DataSources { get; private set; } = [];
    public List<AuditDataSourceUsed> DataSourcesUsed { get; private set; } = [];
    public Dictionary<string, string?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? CorrelationId { get; private set; }
    public Guid? RelatedAgentId { get; private set; }
    public Guid? RelatedTaskId { get; private set; }
    public Guid? RelatedWorkflowInstanceId { get; private set; }
    public Guid? RelatedApprovalRequestId { get; private set; }
    public Guid? RelatedToolExecutionAttemptId { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public string? PayloadDiffJson { get; private set; }
    public string? AgentName { get; private set; }
    public string? AgentRole { get; private set; }
    public string? ResponsibilityDomain { get; private set; }
    public string? PromptProfileVersion { get; private set; }
    public string? BoundaryDecisionOutcome { get; private set; }
    public string? IdentityReasonCode { get; private set; }
    public string? BoundaryReasonCode { get; private set; }
    public Company Company { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static List<string> NormalizeDataSources(IEnumerable<string>? dataSources)
    {
        if (dataSources is null)
        {
            return [];
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataSource in dataSources)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                continue;
            }

            normalized.Add(NormalizeRequired(dataSource, nameof(dataSources), 64));
        }

        return normalized.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<AuditDataSourceUsed> NormalizeDataSourcesUsed(IEnumerable<AuditDataSourceUsed>? dataSourcesUsed)
    {
        if (dataSourcesUsed is null)
        {
            return [];
        }

        var normalized = new Dictionary<string, AuditDataSourceUsed>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataSource in dataSourcesUsed)
        {
            if (dataSource is null || string.IsNullOrWhiteSpace(dataSource.SourceType))
            {
                continue;
            }

            var source = new AuditDataSourceUsed(
                NormalizeRequired(dataSource.SourceType, nameof(dataSourcesUsed), 64),
                NormalizeOptional(dataSource.SourceId, nameof(dataSourcesUsed), 128),
                NormalizeOptional(dataSource.DisplayName, nameof(dataSourcesUsed), 200),
                NormalizeOptional(dataSource.Reference, nameof(dataSourcesUsed), 512));

            var key = string.Join(
                ":",
                source.SourceType,
                source.SourceId ?? string.Empty,
                source.DisplayName ?? string.Empty,
                source.Reference ?? string.Empty);
            normalized[key] = source;
        }

        return normalized.Values
            .OrderBy(value => value.SourceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string?> NormalizeMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (metadata is null)
        {
            return normalized;
        }

        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[NormalizeRequired(pair.Key, nameof(metadata), 100)] =
                NormalizeOptional(pair.Value, nameof(metadata), 512);
        }

        return normalized;
    }

    private Guid? ResolveActorAgentId() =>
        ActorId.HasValue && string.Equals(ActorType, AgentActorType, StringComparison.OrdinalIgnoreCase)
            ? ActorId.Value
            : null;

    private string? ResolveMetadataValue(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private Guid? ResolveRelatedId(string targetType, params string[] metadataKeys)
    {
        if (string.Equals(TargetType, targetType, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(TargetId, out var targetId))
        {
            return targetId;
        }

        foreach (var metadataKey in metadataKeys)
        {
            if (Metadata.TryGetValue(metadataKey, out var value) &&
                Guid.TryParse(value, out var metadataId))
            {
                return metadataId;
            }
        }

        return null;
    }
}
