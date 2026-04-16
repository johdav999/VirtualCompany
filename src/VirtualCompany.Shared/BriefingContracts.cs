using System.Text.Json.Nodes;

namespace VirtualCompany.Shared;

public sealed record SharedBriefingSourceReference(
    string EntityType,
    Guid EntityId,
    string Label,
    string? Status = null,
    string? Route = null);

public sealed record SharedBriefingLinkedEntityReference(
    string EntityType,
    Guid EntityId,
    string DisplayLabel,
    string State,
    string? EntityStatus,
    bool IsAccessible,
    string? PlaceholderReason,
    string? Route = null);

public sealed record SharedBriefingPriority(
    string Category,
    int Score,
    string? RuleCode);

public sealed record SharedBriefingSummaryCounts(
    int CriticalAlertsCount,
    int OpenApprovalsCount,
    int BlockedWorkflowsCount,
    int OverdueTasksCount);

public sealed record SharedBriefingInsightContribution(
    Guid CompanyId,
    Guid? TenantId,
    Guid AgentId,
    SharedBriefingSourceReference SourceReference,
    DateTime TimestampUtc,
    decimal? Confidence,
    Guid? CompanyEntityId,
    Guid? WorkflowInstanceId,
    Guid? TaskId,
    string? EventCorrelationId,
    string Topic,
    string Narrative,
    string? Assessment)
{
    public Dictionary<string, JsonNode?> ConfidenceMetadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record SharedBriefingConflictViewpoint(
    string Assessment,
    IReadOnlyList<Guid> AgentIds,
    IReadOnlyList<string> Narratives,
    IReadOnlyList<SharedBriefingSourceReference> SourceReferences);

public sealed partial record SharedAggregatedBriefingSection(
    string SectionKey,
    string Title,
    string GroupingType,
    string GroupingKey,
    string Narrative,
    bool IsConflicting,
    IReadOnlyList<SharedBriefingInsightContribution> Contributions,
    IReadOnlyList<SharedBriefingConflictViewpoint> ConflictViewpoints,
    IReadOnlyList<SharedBriefingSourceReference> RelatedReferences);

public sealed partial record SharedAggregatedBriefingSection
{
    public string SectionType { get; init; } = GroupingType;
    public string PriorityCategory { get; init; } = "informational";
    public int PriorityScore { get; init; }
    public string? PriorityRuleCode { get; init; }
    public IReadOnlyList<SharedBriefingLinkedEntityReference> LinkedEntities { get; init; } = [];
    public string Priority => PriorityCategory;
    public int PriorityRank => PriorityScore;
    public string? SeverityRuleId => PriorityRuleCode;
    public IReadOnlyList<SharedBriefingLinkedEntityReference> References => LinkedEntities;
}

public sealed record SharedCompanyBriefing(
    Guid Id,
    Guid CompanyId,
    string BriefingType,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string Title,
    string SummaryBody,
    IReadOnlyList<SharedBriefingSourceReference> SourceReferences,
    Guid? MessageId,
    DateTime GeneratedUtc)
{
    public string NarrativeText { get; init; } = SummaryBody;
    public IReadOnlyList<SharedAggregatedBriefingSection> StructuredSections { get; init; } = [];
    public SharedBriefingSummaryCounts SummaryCounts { get; init; } = new(0, 0, 0, 0);
}

public sealed record SharedCompanyBriefingDeliveryPreference(
    bool InAppEnabled,
    bool MobileEnabled,
    bool DailyEnabled,
    bool WeeklyEnabled,
    TimeOnly PreferredDeliveryTime,
    string? PreferredTimezone);
