using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingStrategyState
{
    public IReadOnlyList<MarketingStrategyViewModel> Strategies { get; set; } = [];
    public MarketingStrategyProposalViewModel? StrategyProposal { get; set; }
    public MarketingDecompositionProposalViewModel? DecompositionProposal { get; set; }
    public IReadOnlyList<OutboundCampaignSummaryResponse> SalesCampaigns { get; set; } = [];
    public GuidedWorkSessionViewModel? StrategyWorkshopDraft { get; set; }
    public StrategyProposalFormModel StrategyProposalForm { get; set; } = new();
    public DecompositionFormModel DecompositionForm { get; set; } = new();


    public sealed class StrategyProposalFormModel
    {
        public string Title { get; set; } = "Growth strategy";
        public string Objective { get; set; } = "Create an evidence-grounded 4P and STP strategy aligned to company goals, product facts, customer needs, price sensitivity, channel presence, and competitive evidence.";
        public Guid SegmentVersionId { get; set; }
    }
    public sealed class DecompositionFormModel
    {
        public Guid StrategyId { get; set; }
        public Guid CampaignId { get; set; }
        public Guid ObjectiveId { get; set; }
        public string PlanName { get; set; } = "Strategy execution plan";
    }
}
