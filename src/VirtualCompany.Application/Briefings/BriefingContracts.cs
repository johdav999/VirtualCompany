using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Application.Briefings;

public sealed record GenerateCompanyBriefingCommand(
    string BriefingType,
    DateTime? NowUtc = null,
    bool Force = false,
    string? CorrelationId = null);

public sealed record GenerateDueBriefingsCommand(
    DateTime NowUtc,
    int BatchSize = 50);

public sealed record BriefingSourceReferenceDto(
    string EntityType,
    Guid EntityId,
    string Label,
    string? Status = null,
    string? Route = null);

public sealed record BriefingLinkedEntityReferenceDto(
    string EntityType,
    Guid EntityId,
    string DisplayLabel,
    string State,
    string? EntityStatus,
    bool IsAccessible,
    string? PlaceholderReason,
    string? Route = null);

public sealed record BriefingPriorityDto(
    string Category,
    int Score,
    string? RuleCode);

public sealed record BriefingSummaryCountsDto(
    int CriticalAlertsCount,
    int OpenApprovalsCount,
    int BlockedWorkflowsCount,
    int OverdueTasksCount);

public sealed record BriefingSectionDto(
    string Name,
    IReadOnlyList<string> Items);

public sealed record BriefingAggregateItemDto(
    string Section,
    string Title,
    string Summary,
    string? Severity,
    string? Status,
    string? SourceEntityType,
    Guid? SourceEntityId,
    DateTime OccurredUtc,
    Dictionary<string, JsonNode?> Metadata,
    IReadOnlyList<BriefingSourceReferenceDto> References)
{
    public Guid? AgentId { get; init; }
    public Guid? CompanyEntityId { get; init; }
    public Guid? WorkflowInstanceId { get; init; }
    public Guid? TaskId { get; init; }
    public string? EventCorrelationId { get; init; }
    public string? Assessment { get; init; }
    public BriefingSectionPriorityCategory PriorityCategory { get; init; } = BriefingSectionPriorityCategory.Informational;
    public int PriorityScore { get; init; }
    public string? PriorityRuleCode { get; init; }
}

public sealed record BriefingAggregateResultDto(
    Guid CompanyId,
    string BriefingType,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    IReadOnlyList<BriefingAggregateItemDto> Alerts,
    IReadOnlyList<BriefingAggregateItemDto> PendingApprovals,
    IReadOnlyList<BriefingAggregateItemDto> KpiHighlights,
    IReadOnlyList<BriefingAggregateItemDto> Anomalies,
    IReadOnlyList<BriefingAggregateItemDto> NotableAgentUpdates)
{
    public string NarrativeText { get; init; } = string.Empty;
    public IReadOnlyList<AggregatedBriefingSectionDto> StructuredSections { get; init; } = [];
    public BriefingSummaryCountsDto SummaryCounts { get; init; } = new(0, 0, 0, 0);
    public FinanceCashPositionDto? CashPosition { get; init; }

    public bool HasItems =>
        Alerts.Count > 0 ||
        PendingApprovals.Count > 0 ||
        KpiHighlights.Count > 0 ||
        Anomalies.Count > 0 ||
        NotableAgentUpdates.Count > 0;
}

public static class BriefingInsightGroupingTypes
{
    public const string CompanyEntity = "company_entity";
    public const string Workflow = "workflow";
    public const string Task = "task";
    public const string EventCorrelation = "event_correlation";
    public const string Topic = "topic";
}

public sealed partial record BriefingInsightContributionDto(
    Guid CompanyId,
    Guid? TenantId,
    Guid AgentId,
    BriefingSourceReferenceDto SourceReference,
    DateTime TimestampUtc,
    decimal? Confidence,
    Guid? CompanyEntityId,
    Guid? WorkflowInstanceId,
    Guid? TaskId,
    string? EventCorrelationId,
    string Topic,
    string Narrative,
    string? Assessment,
    Dictionary<string, JsonNode?> Metadata,
    Guid? ContributionId = null);
 
public sealed partial record BriefingInsightContributionDto
{
    public Dictionary<string, JsonNode?> ConfidenceMetadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record BriefingInsightAggregationRequest(
    Guid CompanyId,
    Guid? TenantId,
    IReadOnlyList<BriefingInsightContributionDto> Contributions);

public sealed record BriefingConflictViewpointDto(
    string Assessment,
    IReadOnlyList<Guid> AgentIds,
    IReadOnlyList<string> Narratives,
    IReadOnlyList<BriefingSourceReferenceDto> SourceReferences);

public sealed record AggregatedBriefingSectionDto(
    string SectionKey,
    string Title,
    string GroupingType,
    string GroupingKey,
    string Narrative,
    bool IsConflicting,
    IReadOnlyList<BriefingInsightContributionDto> Contributions,
    IReadOnlyList<BriefingConflictViewpointDto> ConflictViewpoints,
    IReadOnlyList<BriefingSourceReferenceDto> RelatedReferences)
{
    public string SectionType { get; init; } = GroupingType;
    public string PriorityCategory { get; init; } = BriefingSectionPriorityCategory.Informational.ToStorageValue();
    public int PriorityScore { get; init; }
    public string? PriorityRuleCode { get; init; }
    public IReadOnlyList<BriefingLinkedEntityReferenceDto> LinkedEntities { get; init; } = [];
    public string Priority => PriorityCategory;
    public int PriorityRank => PriorityScore;
    public string? SeverityRuleId => PriorityRuleCode;
    public string Summary => Narrative;
    public IReadOnlyList<BriefingLinkedEntityReferenceDto> References => LinkedEntities;
}

public sealed record AggregatedBriefingPayloadDto(
    Guid CompanyId,
    Guid? TenantId,
    string NarrativeText,
    IReadOnlyList<AggregatedBriefingSectionDto> Sections);

public interface IBriefingInsightAggregationService
{
    AggregatedBriefingPayloadDto Aggregate(BriefingInsightAggregationRequest request);
}

public sealed record CachedExecutiveDashboardAggregateDto(
    Guid CompanyId,
    string BriefingType,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateTime GeneratedUtc,
    BriefingAggregateResultDto Aggregate);

public sealed record CompanyBriefingDto(
    Guid Id,
    Guid CompanyId,
    string BriefingType,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string Title,
    string SummaryBody,
    Dictionary<string, JsonNode?> StructuredPayload,
    IReadOnlyList<BriefingSourceReferenceDto> SourceReferences,
    Guid? MessageId,
    DateTime GeneratedUtc,
    Dictionary<string, JsonNode?> PreferenceSnapshot)
{
    public string NarrativeText { get; init; } = SummaryBody;
    public IReadOnlyList<AggregatedBriefingSectionDto> StructuredSections { get; init; } = [];
    public BriefingSummaryCountsDto SummaryCounts { get; init; } = new(0, 0, 0, 0);
}

public sealed record DashboardBriefingCardDto(
    CompanyBriefingDto? Daily,
    CompanyBriefingDto? Weekly);

public sealed record CompanyBriefingDeliveryPreferenceDto(
    Guid CompanyId,
    Guid UserId,
    bool InAppEnabled,
    bool MobileEnabled,
    bool DailyEnabled,
    bool WeeklyEnabled,
    TimeOnly PreferredDeliveryTime,
    string? PreferredTimezone,
    DateTime? UpdatedUtc);

public sealed record UpdateCompanyBriefingDeliveryPreferenceCommand(
    bool InAppEnabled,
    bool MobileEnabled,
    bool DailyEnabled,
    bool WeeklyEnabled,
    TimeOnly? PreferredDeliveryTime = null,
    string? PreferredTimezone = null);

public sealed record BriefingPreferenceDto(
    Guid CompanyId,
    Guid UserId,
    string DeliveryFrequency,
    IReadOnlyList<string> IncludedFocusAreas,
    string PriorityThreshold,
    string Source,
    DateTime? UpdatedUtc);

public sealed record TenantBriefingDefaultDto(
    Guid CompanyId,
    string DeliveryFrequency,
    IReadOnlyList<string> IncludedFocusAreas,
    string PriorityThreshold,
    DateTime? UpdatedUtc);

public sealed record UpsertBriefingPreferenceCommand(
    string DeliveryFrequency,
    IReadOnlyList<string> IncludedFocusAreas,
    string PriorityThreshold);

public sealed record EffectiveBriefingPreferenceDto(
    Guid CompanyId,
    Guid UserId,
    string Source,
    string DeliveryFrequency,
    IReadOnlyList<string> IncludedFocusAreas,
    string PriorityThreshold,
    DateTime? UpdatedUtc);

public sealed record BriefingSchedulerRunResult(
    bool LockAcquired,
    int CompaniesScanned,
    int BriefingsGenerated,
    int NotificationsCreated,
    int Failures);

public sealed record CompanyBriefingGenerationResult(
    CompanyBriefingDto Briefing,
    bool AlreadyExisted,
    int NotificationsCreated);

public sealed class BriefingValidationException : Exception
{
    public BriefingValidationException(IDictionary<string, string[]> errors)
        : base("Briefing validation failed.") =>
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public interface IExecutiveDashboardAggregateCache
{
    Task<CachedExecutiveDashboardAggregateDto?> TryGetAsync(
        Guid companyId,
        string briefingType,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        CancellationToken cancellationToken);

    Task SetAsync(CachedExecutiveDashboardAggregateDto snapshot, CancellationToken cancellationToken);
}

public interface ICompanyBriefingService
{
    Task<BriefingAggregateResultDto> AggregateAsync(
        Guid companyId,
        GenerateCompanyBriefingCommand command,
        CancellationToken cancellationToken);

    Task<CompanyBriefingGenerationResult> GenerateAsync(
        Guid companyId,
        GenerateCompanyBriefingCommand command,
        CancellationToken cancellationToken);

    Task<BriefingSchedulerRunResult> GenerateDueAsync(
        GenerateDueBriefingsCommand command,
        CancellationToken cancellationToken);

    Task<DashboardBriefingCardDto> GetLatestDashboardBriefingsAsync(Guid companyId, CancellationToken cancellationToken);

    Task<CompanyBriefingDeliveryPreferenceDto> GetDeliveryPreferenceAsync(Guid companyId, CancellationToken cancellationToken);

    Task<CompanyBriefingDeliveryPreferenceDto> UpdateDeliveryPreferenceAsync(
        Guid companyId,
        UpdateCompanyBriefingDeliveryPreferenceCommand command,
        CancellationToken cancellationToken);

    Task<BriefingPreferenceDto> GetUserBriefingPreferenceAsync(Guid companyId, CancellationToken cancellationToken);

    Task<BriefingPreferenceDto> UpsertUserBriefingPreferenceAsync(
        Guid companyId,
        UpsertBriefingPreferenceCommand command,
        CancellationToken cancellationToken);

    Task<TenantBriefingDefaultDto?> GetTenantBriefingDefaultAsync(Guid companyId, CancellationToken cancellationToken);

    Task<TenantBriefingDefaultDto> UpsertTenantBriefingDefaultAsync(
        Guid companyId,
        UpsertBriefingPreferenceCommand command,
        CancellationToken cancellationToken);

    Task<EffectiveBriefingPreferenceDto> ResolveEffectiveBriefingPreferenceAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken);
}
