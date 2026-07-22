namespace VirtualCompany.Web.Services;

public sealed class SalesIntelligenceBriefRequestViewModel
{
    public Guid? LeadId { get; set; }
    public Guid? DealId { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SalesIntelligenceBriefResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public string SubjectType { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<SalesIntelligenceFactViewModel> ConfirmedFacts { get; set; } = [];
    public List<string> QualificationGaps { get; set; } = [];
    public List<string> BuyingSignals { get; set; } = [];
    public List<string> RiskSignals { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SalesIntelligenceFactViewModel
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public DateTime AsOfUtc { get; set; }
}

public sealed class SalesNextBestActionRequestViewModel
{
    public Guid? LeadId { get; set; }
    public Guid? DealId { get; set; }
    public int Limit { get; set; } = 30;
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SalesNextBestActionViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public List<SalesNextBestActionItemViewModel> Actions { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SalesNextBestActionItemViewModel
{
    public string SubjectType { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int PriorityScore { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool CommunicationAllowed { get; set; }
    public List<string> ReasonCodes { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
}

public sealed class SalesForecastScenarioRequestViewModel
{
    public int HorizonDays { get; set; } = 90;
    public decimal UpsideProbabilityAdjustment { get; set; } = .10m;
    public decimal DownsideProbabilityAdjustment { get; set; } = -.10m;
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SalesDealStrategyRequestViewModel
{
    public Guid DealId { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SalesDealStrategyResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public Guid DealId { get; set; }
    public decimal? RiskScore { get; set; }
    public string RiskBand { get; set; } = string.Empty;
    public List<string> RiskFactors { get; set; } = [];
    public List<string> Unknowns { get; set; } = [];
    public List<SalesMutualActionPlanItemViewModel> RecoveryPlan { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SalesMutualActionPlanItemViewModel
{
    public int Order { get; set; }
    public string Milestone { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTime? DueUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<string> Dependencies { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
}

public sealed class SalesProposalAdviceRequestViewModel
{
    public Guid DealId { get; set; }
    public string? RequestedProduct { get; set; }
    public decimal? RequestedPrice { get; set; }
    public string? Currency { get; set; }
    public string? RequestedTerms { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SalesProposalAdviceResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public Guid DealId { get; set; }
    public List<SalesProposalValidationViewModel> Validations { get; set; } = [];
    public List<string> ApprovedClaims { get; set; } = [];
    public List<string> Unknowns { get; set; } = [];
    public bool PricingApprovalRequired { get; set; }
    public bool TermsApprovalRequired { get; set; }
    public bool RequiresReview { get; set; }
}

public sealed class SalesProposalValidationViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> SourceIds { get; set; } = [];
}

public sealed class SalesForecastScenarioResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public List<SalesForecastScenarioViewModel> Scenarios { get; set; } = [];
    public List<string> ConcentrationWarnings { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SalesForecastScenarioViewModel
{
    public string Scenario { get; set; } = string.Empty;
    public decimal GrossPipeline { get; set; }
    public decimal ExpectedRevenue { get; set; }
    public decimal ChangeFromBaseline { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DealCount { get; set; }
    public int HighRiskDeals { get; set; }
    public int UnknownRiskDeals { get; set; }
    public List<string> Assumptions { get; set; } = [];
    public string SourceId { get; set; } = string.Empty;
}

public sealed class SalesCampaignOptimizationRequestViewModel
{
    public Guid? CampaignId { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SalesCampaignOptimizationResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public List<SalesCampaignExperimentViewModel> Campaigns { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SalesCampaignExperimentViewModel
{
    public Guid CampaignId { get; set; }
    public string CampaignName { get; set; } = string.Empty;
    public int Sent { get; set; }
    public int Delivered { get; set; }
    public int Replied { get; set; }
    public int Converted { get; set; }
    public decimal DeliveryRate { get; set; }
    public decimal ReplyRate { get; set; }
    public decimal ConversionRate { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public bool LaunchAllowed { get; set; }
    public bool RequiresApproval { get; set; }
    public List<string> SourceIds { get; set; } = [];
}
