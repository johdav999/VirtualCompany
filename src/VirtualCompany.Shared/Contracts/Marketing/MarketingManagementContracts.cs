

namespace VirtualCompany.Application.Marketing;


public sealed record MarketingManagementQuery(int Year, int Month, string? Currency = null,
    Guid? CampaignId = null, Guid? SegmentVersionId = null, Guid? ModelId = null);

public sealed record MarketingCostSource(Guid Id, string SubjectType, Guid SubjectId, string Channel,
    DateTime OccurredUtc, decimal? Cost, string? Currency, string SourceReference, int SourceVersion,
    bool Included, string Coverage);

public sealed record MarketingAttributedSource(Guid TouchId, decimal Weight, decimal Value, string EvidenceVersion);

public sealed record MarketingSegmentEvidence(Guid LinkId, Guid SegmentVersionId, int Version, string Rationale);

public sealed record MarketingBudgetProposal(MarketingBudgetProposalSummary Summary, MarketingManagementReport Report,
    MarketingBudgetAssumptions Assumptions, MarketingBudgetResult Result, string Checksum, string Retention);

public sealed record MarketingAcquisitionMeasure(string Unit, decimal AttributedValue, decimal? CostPerAttributedUnit);

public sealed record MarketingMeasurementHealth(string Provider, string Name, string ConnectionState, string HealthState,
    DateTime? CheckedUtc, string Coverage);

public sealed record MarketingChannelPerformance(string Channel, string? Currency, decimal? KnownCost,
    int IncludedTouches, int UnknownCostTouches, int AttributedTouches,
    IReadOnlyList<MarketingAcquisitionMeasure> Economics, string Coverage);

public sealed record MarketingExperimentEvidence(Guid Id, string Name, Guid? CampaignId, string Hypothesis,
    string PrimaryMetric, string GuardrailMetric, int MinimumSample, DateTime StartUtc, DateTime EndUtc,
    MarketingExperimentDecisionDto? Decision, int RecordedExposures, string Coverage);

public sealed record MarketingBudgetAllocation(Guid CampaignId, decimal Amount, string ImpactAssumption);

public sealed record MarketingAttributionEvidence(Guid Id, string SubjectType, Guid SubjectId, string Model,
    Guid? ModelId, int? ModelVersion, int? LookbackDays, string Limitations, string Classification,
    decimal Value, string Unit, DateTime StartUtc, DateTime EndUtc, bool Included, string Coverage,
    IReadOnlyList<MarketingAttributedSource> Allocations);

public sealed record SaveMarketingBudgetProposal(Guid RequestId, MarketingManagementQuery Query,
    MarketingBudgetAssumptions Assumptions, Guid? PreviousId = null, int? ExpectedRevision = null);

public sealed record MarketingBudgetProposalSummary(Guid Id, Guid SeriesId, int Revision, Guid? PreviousId,
    Guid AccountableUserId, DateTime SavedAtUtc, DateTime SourceAsOfUtc, int Year, int Month, string Currency);

public sealed record MarketingPlanBudgetEvidence(Guid Id, string Name, string Currency, decimal? Ceiling,
    IReadOnlyList<MarketingPlanAllocationEvidence> Allocations);

public sealed record MarketingBudgetDifference(Guid CampaignId, string Name, decimal? CurrentBudget,
    decimal ProposedBudget, decimal? Difference, string ImpactAssumption);

public sealed record MarketingBudgetAssumptions(string Currency, decimal ProposedTotal, decimal? AssumedCeiling,
    IReadOnlyList<MarketingBudgetAllocation> Allocations, string Notes);

public sealed record MarketingManagementReport(Guid CompanyId, MarketingManagementQuery Query, string Timezone,
    DateTime StartUtc, DateTime EndUtc, DateTime AsOfUtc, string CalculationVersion,
    IReadOnlyList<MarketingChannelPerformance> Channels, IReadOnlyList<MarketingCostSource> Costs,
    IReadOnlyList<MarketingAttributionEvidence> Attribution, IReadOnlyList<MarketingAttributionModelDto> Models,
    IReadOnlyList<MarketingExperimentEvidence> Experiments, IReadOnlyList<MarketingManagementCampaign> Campaigns,
    IReadOnlyList<MarketingPlanBudgetEvidence> Plans, IReadOnlyList<string> Coverage,
    IReadOnlyList<MarketingMeasurementHealth>? SourceHealth = null);

public sealed record MarketingManagementCampaign(Guid Id, string Name, string Currency, decimal? RecordedBudget,
    IReadOnlyList<MarketingSegmentEvidence> Segments);

public sealed record MarketingBudgetResult(string Currency, decimal ProposedTotal, decimal? CurrentTotal,
    decimal? Difference, IReadOnlyList<MarketingBudgetDifference> Allocations, IReadOnlyList<string> Constraints);

public sealed record MarketingManagementExport(string FileName, string Csv);

public sealed record MarketingPlanAllocationEvidence(Guid CampaignId, decimal? Amount, string Currency);
