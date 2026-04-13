namespace VirtualCompany.Shared;

public sealed record SharedBriefingSourceReference(
    string EntityType,
    Guid EntityId,
    string Label,
    string? Status = null,
    string? Route = null);

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
    DateTime GeneratedUtc);

public sealed record SharedCompanyBriefingDeliveryPreference(
    bool InAppEnabled,
    bool MobileEnabled,
    bool DailyEnabled,
    bool WeeklyEnabled,
    TimeOnly PreferredDeliveryTime,
    string? PreferredTimezone);