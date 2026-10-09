namespace VirtualCompany.Application.Marketing;

public sealed record MarketingManagementQuery(int Year, int Month, string? Currency = null,
    Guid? CampaignId = null, Guid? SegmentVersionId = null, Guid? ModelId = null);
public sealed record MarketingCostSource(Guid Id, string SubjectType, Guid SubjectId, string Channel,
    DateTime OccurredUtc, decimal? Cost, string? Currency, string SourceReference, int SourceVersion,
    bool Included, string Coverage);
public sealed record MarketingAttributedSource(Guid TouchId, decimal Weight, decimal Value, string EvidenceVersion);
public sealed record MarketingAttributionEvidence(Guid Id, string SubjectType, Guid SubjectId, string Model,
    Guid? ModelId, int? ModelVersion, int? LookbackDays, string Limitations, string Classification,
    decimal Value, string Unit, DateTime StartUtc, DateTime EndUtc, bool Included, string Coverage,
    IReadOnlyList<MarketingAttributedSource> Allocations);
public sealed record MarketingAcquisitionMeasure(string Unit, decimal AttributedValue, decimal? CostPerAttributedUnit);
public sealed record MarketingChannelPerformance(string Channel, string? Currency, decimal? KnownCost,
    int IncludedTouches, int UnknownCostTouches, int AttributedTouches,
    IReadOnlyList<MarketingAcquisitionMeasure> Economics, string Coverage);
public sealed record MarketingSegmentEvidence(Guid LinkId, Guid SegmentVersionId, int Version, string Rationale);
public sealed record MarketingManagementCampaign(Guid Id, string Name, string Currency, decimal? RecordedBudget,
    IReadOnlyList<MarketingSegmentEvidence> Segments);
public sealed record MarketingPlanAllocationEvidence(Guid CampaignId, decimal? Amount, string Currency);
public sealed record MarketingPlanBudgetEvidence(Guid Id, string Name, string Currency, decimal? Ceiling,
    IReadOnlyList<MarketingPlanAllocationEvidence> Allocations);
public sealed record MarketingExperimentEvidence(Guid Id, string Name, Guid? CampaignId, string Hypothesis,
    string PrimaryMetric, string GuardrailMetric, int MinimumSample, DateTime StartUtc, DateTime EndUtc,
    MarketingExperimentDecisionDto? Decision, int RecordedExposures, string Coverage);
public sealed record MarketingMeasurementHealth(string Provider, string Name, string ConnectionState, string HealthState,
    DateTime? CheckedUtc, string Coverage);
public sealed record MarketingManagementReport(Guid CompanyId, MarketingManagementQuery Query, string Timezone,
    DateTime StartUtc, DateTime EndUtc, DateTime AsOfUtc, string CalculationVersion,
    IReadOnlyList<MarketingChannelPerformance> Channels, IReadOnlyList<MarketingCostSource> Costs,
    IReadOnlyList<MarketingAttributionEvidence> Attribution, IReadOnlyList<MarketingAttributionModelDto> Models,
    IReadOnlyList<MarketingExperimentEvidence> Experiments, IReadOnlyList<MarketingManagementCampaign> Campaigns,
    IReadOnlyList<MarketingPlanBudgetEvidence> Plans, IReadOnlyList<string> Coverage,
    IReadOnlyList<MarketingMeasurementHealth>? SourceHealth = null);
public sealed record MarketingBudgetAllocation(Guid CampaignId, decimal Amount, string ImpactAssumption);
public sealed record MarketingBudgetAssumptions(string Currency, decimal ProposedTotal, decimal? AssumedCeiling,
    IReadOnlyList<MarketingBudgetAllocation> Allocations, string Notes);
public sealed record MarketingBudgetDifference(Guid CampaignId, string Name, decimal? CurrentBudget,
    decimal ProposedBudget, decimal? Difference, string ImpactAssumption);
public sealed record MarketingBudgetResult(string Currency, decimal ProposedTotal, decimal? CurrentTotal,
    decimal? Difference, IReadOnlyList<MarketingBudgetDifference> Allocations, IReadOnlyList<string> Constraints);
public sealed record SaveMarketingBudgetProposal(Guid RequestId, MarketingManagementQuery Query,
    MarketingBudgetAssumptions Assumptions, Guid? PreviousId = null, int? ExpectedRevision = null);
public sealed record MarketingBudgetProposalSummary(Guid Id, Guid SeriesId, int Revision, Guid? PreviousId,
    Guid AccountableUserId, DateTime SavedAtUtc, DateTime SourceAsOfUtc, int Year, int Month, string Currency);
public sealed record MarketingBudgetProposal(MarketingBudgetProposalSummary Summary, MarketingManagementReport Report,
    MarketingBudgetAssumptions Assumptions, MarketingBudgetResult Result, string Checksum, string Retention);
public sealed record MarketingManagementExport(string FileName, string Csv);
public interface IMarketingManagementService
{
    Task<MarketingManagementReport> ReportAsync(Guid company, MarketingManagementQuery query, CancellationToken ct);
    Task<MarketingManagementExport> ExportAsync(Guid company, MarketingManagementQuery query, CancellationToken ct);
    Task<MarketingBudgetProposal> SaveAsync(Guid company, SaveMarketingBudgetProposal command, CancellationToken ct);
    Task<MarketingBudgetProposal> OpenAsync(Guid company, Guid id, CancellationToken ct);
    Task<IReadOnlyList<MarketingBudgetProposalSummary>> HistoryAsync(Guid company, int skip, CancellationToken ct);
}

public static class MarketingBudgetCalculation
{
    public static MarketingBudgetResult Calculate(MarketingBudgetAssumptions value, MarketingManagementReport report)
    {
        if (value is null || value.Currency is null || value.Currency != report.Query.Currency ||
            value.ProposedTotal is < 0 or > 1000000000 || value.AssumedCeiling is < 0 or > 1000000000 ||
            value.AssumedCeiling.HasValue && value.ProposedTotal > value.AssumedCeiling ||
            value.Notes is null || value.Notes.Length > 2000 || value.Allocations is null ||
            value.Allocations.Count is < 1 or > 100 || value.Allocations.Any(x => x is null || x.CampaignId == Guid.Empty ||
                x.Amount is < 0 or > 1000000000 || x.ImpactAssumption is null || x.ImpactAssumption.Length > 1000) ||
            value.Allocations.Select(x => x.CampaignId).Distinct().Count() != value.Allocations.Count ||
            value.Allocations.Sum(x => x.Amount) != value.ProposedTotal)
            throw new ArgumentException("Choose one currency and unique campaigns, with allocations equal to the bounded total and within any assumed ceiling.");
        var differences = value.Allocations.Select(x =>
        {
            var campaign = report.Campaigns.SingleOrDefault(c => c.Id == x.CampaignId);
            if (campaign is null || campaign.Currency != value.Currency)
                throw new ArgumentException("Every allocation must refer to an included campaign in the proposal currency.");
            return new MarketingBudgetDifference(x.CampaignId, campaign.Name, campaign.RecordedBudget, x.Amount,
                campaign.RecordedBudget.HasValue ? x.Amount - campaign.RecordedBudget : null, x.ImpactAssumption);
        }).ToArray();
        var constraints = new List<string>();
        foreach (var plan in report.Plans.Where(x => x.Allocations.Any(a => value.Allocations.Any(v => v.CampaignId == a.CampaignId))))
        {
            var outside = plan.Allocations.Where(x => !value.Allocations.Any(v => v.CampaignId == x.CampaignId)).ToArray();
            var proposed = value.Allocations.Where(x => plan.Allocations.Any(a => a.CampaignId == x.CampaignId)).Sum(x => x.Amount);
            if (plan.Currency != value.Currency || plan.Allocations.Any(x => x.Currency != plan.Currency) ||
                !plan.Ceiling.HasValue || outside.Any(x => !x.Amount.HasValue))
                constraints.Add($"{plan.Name}: recorded portfolio ceiling comparison is unavailable or currency coverage is incomplete.");
            else
            {
                if (outside.Sum(x => x.Amount!.Value) + proposed > plan.Ceiling)
                    throw new ArgumentException($"Proposed allocations exceed the recorded planning ceiling for {plan.Name} after other campaign allocations.");
                constraints.Add($"{plan.Name}: selected allocations plus other recorded allocations fit its {plan.Ceiling} {plan.Currency} planning ceiling.");
            }
        }
        if (constraints.Count == 0) constraints.Add("No applicable recorded portfolio ceiling. The assumed ceiling is a planning assumption, not spend authority.");
        decimal? current = differences.All(x => x.CurrentBudget.HasValue) ? differences.Sum(x => x.CurrentBudget!.Value) : null;
        return new(value.Currency, value.ProposedTotal, current, current.HasValue ? value.ProposedTotal - current : null,
            differences, constraints);
    }
}
