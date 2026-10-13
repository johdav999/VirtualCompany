using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingPlansState
{
    public IReadOnlyList<MarketingPlanListItemViewModel> PlanPortfolio { get; set; } = [];
    public MarketingPlanDetailViewModel? SelectedPlan { get; set; }
    public MarketingDailyReviewViewModel? DailyReview { get; set; }
    public MarketingCampaignPortfolioProposalViewModel? PortfolioProposal { get; set; }
    public PrepareMarketingCampaignPortfolioViewModel? PendingPortfolio { get; set; }
    public string? PlanActionMessage { get; set; }
    public bool ShowPlanForm { get; set; } = false;
    public GuidedWorkSessionViewModel? PlanWorkshopDraft { get; set; }
    public PlanFormModel PlanForm { get; set; } = new();
    public IReadOnlyList<MarketingStrategyViewModel> Strategies { get; set; } = [];
    public IReadOnlyList<MarketingObjectiveViewModel> Objectives { get; set; } = [];
    public IReadOnlyList<MarketingSegmentViewModel> Segments { get; set; } = [];

    public IEnumerable<MarketingStrategyViewModel> AvailablePlanStrategies => Strategies.Where(x => x.Status is "approved" or "active");
    public IEnumerable<MarketingObjectiveViewModel> AvailablePlanObjectives => Objectives.Where(x => x.Status.Equals("active", StringComparison.OrdinalIgnoreCase));
    public IEnumerable<(string Name, MarketingSegmentVersionViewModel Version)> AvailablePlanSegments
    {
        get
        {
            var linked = Strategies.FirstOrDefault(x => x.Id == PlanForm.StrategyId)?.SegmentVersionIds ?? [];
            return Segments.SelectMany(segment => segment.Versions
                .Where(version => linked.Contains(version.Id) && version.Status is "approved" or "active")
                .Select(version => (segment.Name, version)));
        }
    }
    public bool CanCreateGroundedPlan => !string.IsNullOrWhiteSpace(PlanForm.Name) && !string.IsNullOrWhiteSpace(PlanForm.Summary)
        && AvailablePlanStrategies.Any(x => x.Id == PlanForm.StrategyId)
        && AvailablePlanObjectives.Any(x => x.Id == PlanForm.ObjectiveId)
        && AvailablePlanSegments.Any(x => x.Version.Id == PlanForm.SegmentVersionId)
        && PlanForm.EndsUtc > PlanForm.StartsUtc && PlanForm.Budget is not < 0 && PlanForm.Currency.Trim().Length == 3;
    public void InitializePlanFormSelections()
    {
        if (PlanForm.StrategyId == Guid.Empty || !AvailablePlanStrategies.Any(x => x.Id == PlanForm.StrategyId))
            PlanForm.StrategyId = AvailablePlanStrategies.FirstOrDefault()?.Id ?? Guid.Empty;
        if (PlanForm.ObjectiveId == Guid.Empty || !AvailablePlanObjectives.Any(x => x.Id == PlanForm.ObjectiveId))
            PlanForm.ObjectiveId = AvailablePlanObjectives.FirstOrDefault()?.Id ?? Guid.Empty;
        if (PlanForm.SegmentVersionId == Guid.Empty || !AvailablePlanSegments.Any(x => x.Version.Id == PlanForm.SegmentVersionId))
            PlanForm.SegmentVersionId = AvailablePlanSegments.FirstOrDefault().Version?.Id ?? Guid.Empty;
        var strategy = Strategies.FirstOrDefault(x => x.Id == PlanForm.StrategyId);
        if (strategy is not null && PlanForm.StartsUtc < strategy.ValidFromUtc) PlanForm.StartsUtc = strategy.ValidFromUtc.Date;
        if (strategy is not null && PlanForm.EndsUtc > strategy.ValidToUtc) PlanForm.EndsUtc = strategy.ValidToUtc.Date;
    }

    public sealed class PlanFormModel
    {
        public string Name { get; set; } = "";
        public string Summary { get; set; } = "";
        public Guid StrategyId { get; set; }
        public Guid ObjectiveId { get; set; }
        public Guid SegmentVersionId { get; set; }
        public DateTime StartsUtc { get; set; } = DateTime.UtcNow.Date;
        public DateTime EndsUtc { get; set; } = DateTime.UtcNow.Date.AddMonths(3);
        public decimal? Budget { get; set; }
        public string Currency { get; set; } = "SEK";
    }
}
