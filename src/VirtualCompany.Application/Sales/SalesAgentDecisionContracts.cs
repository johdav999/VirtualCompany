using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Sales;

public sealed record SalesIntelligenceBriefRequest(Guid? LeadId = null, Guid? DealId = null, DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SalesIntelligenceFactDto(string Label, string Value, string SourceId, DateTime AsOfUtc);
public sealed record SalesIntelligenceBriefResult(RoleAgentAnalysisResult Advice, string SubjectType, Guid SubjectId,
    string Title, IReadOnlyList<SalesIntelligenceFactDto> ConfirmedFacts, IReadOnlyList<string> QualificationGaps,
    IReadOnlyList<string> BuyingSignals, IReadOnlyList<string> RiskSignals, IReadOnlyList<string> SourceIds, bool RequiresReview);

public sealed record SalesNextBestActionRequest(Guid? LeadId = null, Guid? DealId = null, int Limit = 30,
    DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SalesNextBestActionItemDto(string SubjectType, Guid SubjectId, string Title, int PriorityScore,
    string Action, string Timing, string Channel, bool RequiresApproval, bool CommunicationAllowed,
    IReadOnlyList<string> ReasonCodes, IReadOnlyList<string> SourceIds);
public sealed record SalesNextBestActionResult(RoleAgentAnalysisResult Advice,
    IReadOnlyList<SalesNextBestActionItemDto> Actions, IReadOnlyList<string> MissingEvidence, bool RequiresReview);

public sealed record SalesDealStrategyRequest(Guid DealId, DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SalesMutualActionPlanItemDto(int Order, string Milestone, string Owner, DateTime? DueUtc,
    string Status, IReadOnlyList<string> Dependencies, IReadOnlyList<string> SourceIds);
public sealed record SalesDealStrategyResult(RoleAgentAnalysisResult Advice, Guid DealId, decimal? RiskScore,
    string RiskBand, IReadOnlyList<string> RiskFactors, IReadOnlyList<string> Unknowns,
    IReadOnlyList<SalesMutualActionPlanItemDto> RecoveryPlan, bool RequiresReview);

public sealed record SalesForecastScenarioRequest(int HorizonDays = 90, decimal UpsideProbabilityAdjustment = .10m,
    decimal DownsideProbabilityAdjustment = -.10m, DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SalesForecastScenarioDto(string Scenario, decimal GrossPipeline, decimal ExpectedRevenue,
    decimal ChangeFromBaseline, string Currency, int DealCount, int HighRiskDeals, int UnknownRiskDeals,
    IReadOnlyList<string> Assumptions, string SourceId);
public sealed record SalesForecastScenarioResult(RoleAgentAnalysisResult Advice,
    IReadOnlyList<SalesForecastScenarioDto> Scenarios, IReadOnlyList<string> ConcentrationWarnings,
    IReadOnlyList<string> MissingEvidence, bool RequiresReview);

public sealed record SalesCampaignOptimizationRequest(Guid? CampaignId = null, DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SalesCampaignExperimentDto(Guid CampaignId, string CampaignName, int Sent, int Delivered,
    int Replied, int Converted, decimal DeliveryRate, decimal ReplyRate, decimal ConversionRate,
    string Recommendation, bool LaunchAllowed, bool RequiresApproval, IReadOnlyList<string> SourceIds);
public sealed record SalesCampaignOptimizationResult(RoleAgentAnalysisResult Advice,
    IReadOnlyList<SalesCampaignExperimentDto> Campaigns, IReadOnlyList<string> MissingEvidence, bool RequiresReview);

public sealed record SalesProposalAdviceRequest(Guid DealId, string? RequestedProduct = null,
    decimal? RequestedPrice = null, string? Currency = null, string? RequestedTerms = null,
    DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SalesProposalValidationDto(string Code, string Status, string Message, IReadOnlyList<string> SourceIds);
public sealed record SalesProposalAdviceResult(RoleAgentAnalysisResult Advice, Guid DealId,
    IReadOnlyList<SalesProposalValidationDto> Validations, IReadOnlyList<string> ApprovedClaims,
    IReadOnlyList<string> Unknowns, bool PricingApprovalRequired, bool TermsApprovalRequired, bool RequiresReview);

public interface ISalesAgentDecisionService
{
    Task<SalesIntelligenceBriefResult> BuildIntelligenceBriefAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SalesIntelligenceBriefRequest request, CancellationToken cancellationToken);
    Task<SalesNextBestActionResult> RecommendNextActionsAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SalesNextBestActionRequest request, CancellationToken cancellationToken);
    Task<SalesDealStrategyResult> AnalyzeDealStrategyAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SalesDealStrategyRequest request, CancellationToken cancellationToken);
    Task<SalesForecastScenarioResult> AnalyzeForecastAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SalesForecastScenarioRequest request, CancellationToken cancellationToken);
    Task<SalesCampaignOptimizationResult> OptimizeCampaignsAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SalesCampaignOptimizationRequest request, CancellationToken cancellationToken);
    Task<SalesProposalAdviceResult> AdviseProposalAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SalesProposalAdviceRequest request, CancellationToken cancellationToken);
}
