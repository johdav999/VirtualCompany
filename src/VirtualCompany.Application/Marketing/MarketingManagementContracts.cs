namespace VirtualCompany.Application.Marketing;
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
