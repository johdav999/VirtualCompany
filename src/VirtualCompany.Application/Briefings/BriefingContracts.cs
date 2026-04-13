using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

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
    IReadOnlyList<BriefingSourceReferenceDto> References);

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
    public bool HasItems =>
        Alerts.Count > 0 ||
        PendingApprovals.Count > 0 ||
        KpiHighlights.Count > 0 ||
        Anomalies.Count > 0 ||
        NotableAgentUpdates.Count > 0;
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
    DateTime GeneratedUtc);

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
}