using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingSegmentsState
{
    public IReadOnlyList<MarketingSegmentViewModel> Segments { get; set; } = [];
    public GuidedWorkSessionViewModel? SegmentWorkshopDraft { get; set; }
    public MarketingSegmentImpactViewModel? SegmentImpact { get; set; }
    public MarketingSegmentDecisionDataViewModel? SegmentDecisionData { get; set; }
    public MarketingSegmentProposalViewModel? SegmentProposal { get; set; }
    public SegmentProposalFormModel SegmentProposalForm { get; set; } = new();


    public sealed class SegmentProposalFormModel
    {
        public string Name { get; set; } = "Evidence-grounded target segment";
        public string Objective { get; set; } = "Propose a lawful strategic segment using customer needs, behaviours, channel presence, price sensitivity, size and economics evidence. Classify every claim and identify missing evidence.";
    }
}
