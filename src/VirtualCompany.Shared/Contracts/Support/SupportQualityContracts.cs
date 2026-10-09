

namespace VirtualCompany.Application.Support;

public sealed record SupportCapacityProposalSummary(Guid Id, string Name, Guid OwnerId, string Owner,
    int Year, int Month, int TargetYear, int TargetMonth, DateTime SavedUtc, Guid? PreviousId);

public sealed record SupportQualityMetrics(int Arrivals, int ResolvedCases, int ReopenedCases, decimal? ReopenRate,
    int ResponseSample, decimal? MeanResponseMinutes, decimal? MeanResponseBusinessMinutes,
    int ResolutionSample, decimal? MeanResolutionMinutes);


public sealed record SupportQualityQuery(int Year, int Month, string? Category = null);

public sealed record SupportCapacityPreview(SupportQualityReport Report, SupportCapacityAssumptions Assumptions,
    SupportCapacityResult Result, string Fingerprint);

public sealed record CorrectSupportIssueGrouping(Guid RequestId, SupportQualityQuery Query, Guid CaseId,
    string Group, string Reason, string ExpectedFingerprint, Guid? PreviousId = null);

public sealed record SupportQualityPeriod(DateTime StartUtc, DateTime EndUtc, DateTime ActivityEndUtc, string TimeZoneId);

public sealed record SaveSupportCapacityProposal(Guid RequestId, string Name, PreviewSupportCapacity Input,
    string ExpectedFingerprint, Guid? PreviousId = null);

public sealed record SupportCapacityAssumptions(int TargetYear, int TargetMonth, int ExpectedArrivals,
    int BacklogToClear, decimal HandlingMinutes, decimal AvailablePeople, decimal HoursPerBusinessDay,
    decimal UtilizationPercent, int ResponseTargetMinutes, string Explanation);

public sealed record SupportQualityCase(Guid Id, string Number, string Subject, string Category, string Group,
    bool ReviewedGroup, Guid? GroupRevisionId, DateTime CreatedUtc, bool Arrival, bool ResolutionCohort,
    DateTime? ResolutionUtc, DateTime? ReopenedUtc, decimal? ResponseMinutes, decimal? ResponseBusinessMinutes,
    decimal? ResolutionMinutes, DateTime? StateCoverageStartUtc, bool CompleteStateHistory, bool Merged,
    IReadOnlyList<SupportQualitySource> Sources, string DeepLink);

public sealed record SupportIssueGroup(string Key, bool Reviewed, IReadOnlyList<Guid> CaseIds);

public sealed record SupportQualityExport(string FileName, string ContentType, string Content, string Fingerprint);

public sealed record SupportIssueGroupingSummary(Guid Id, Guid CaseId, string Group, string Reason,
    Guid ActorId, DateTime SavedUtc, Guid? PreviousId, string SourceFingerprint);

public sealed record SupportQualityBacklog(DateTime CutoffUtc, int? Open, int? Waiting, int KnownOpen, int UnknownCases);

public sealed record SupportQualitySource(Guid Id, string Type, DateTime OccurredUtc, string Summary);

public sealed record PreviewSupportCapacity(SupportQualityQuery Query, SupportCapacityAssumptions Assumptions);

public sealed record SupportQualityReport(Guid CompanyId, SupportQualityQuery Query, string CalculationVersion,
    DateTime AsOfUtc, DateTime FollowupEndUtc, SupportQualityPeriod Period,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(SupportQualityCalendarConverter))] SupportBusinessCalendarDto Calendar,
    SupportQualityMetrics Metrics, SupportQualityMetrics Previous, IReadOnlyList<SupportQualityBacklog> Backlog,
    IReadOnlyList<SupportQualityCase> Cases, IReadOnlyList<SupportIssueGroup> Groups,
    bool CompleteSourceCoverage, string Coverage, string Definition, string Fingerprint, string AccountableOwner);

public sealed record SupportCapacityProposal(SupportCapacityProposalSummary Summary, SupportCapacityPreview Preview,
    string Checksum);

public sealed record SupportCapacityResult(int ObservedArrivals, int AssumedArrivals, int BacklogToClear,
    int BusinessDays, decimal ConfiguredHoursPerDay, decimal RequiredHours, decimal AvailableHours,
    int CaseCapacity, decimal ShortfallHours, decimal? RequiredPeople, string Limits);
