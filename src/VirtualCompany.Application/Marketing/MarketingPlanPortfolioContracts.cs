namespace VirtualCompany.Application.Marketing;

public static class MarketingPlanReadinessReasons
{
    public const string Ready = "ready"; public const string StrategyMissing = "strategy_missing";
    public const string StrategyStale = "strategy_stale"; public const string StrategyUnavailable = "strategy_unavailable";
    public const string SegmentMissing = "segment_missing"; public const string SegmentUnavailable = "segment_unavailable";
    public const string ObjectiveMissing = "objective_missing"; public const string ObjectiveOutsidePeriod = "objective_outside_period";
    public const string PrimarySegmentMissing = "primary_segment_missing"; public const string EvidenceMissing = "evidence_missing";
    public const string BudgetExceeded = "budget_exceeded"; public const string CurrencyMismatch = "currency_mismatch";
    public const string CampaignOutsidePlan = "campaign_outside_plan"; public const string DuplicateCampaign = "duplicate_campaign";
    public const string StaleVersion = "stale_version"; public const string ApprovalRequired = "approval_required";
}

public sealed record MarketingPolicyDecisionDto(bool Allowed, string ReasonCode, string Explanation,
    bool RequiresApproval, IReadOnlyList<string> EvidenceReferences);
public sealed record MarketingPlanSegmentSelection(Guid SegmentVersionId, string Role, int Priority,
    string Rationale, string ExpectedContribution);
public sealed record CreateGroundedMarketingPlanRequest(string Name, string Summary, Guid StrategyId,
    int ExpectedStrategyVersion, DateTime StartsUtc, DateTime EndsUtc, decimal? PlannedBudget,
    string BudgetCurrency, IReadOnlyList<Guid> ObjectiveIds, IReadOnlyList<MarketingPlanSegmentSelection> Segments,
    string Rationale, IReadOnlyList<string> EvidenceReferences, IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks, IReadOnlyList<string> MissingEvidence, string IdempotencyKey,
    Guid? OwnerAgentId = null);
public sealed record MarketingPlanSegmentDto(Guid Id, Guid SegmentVersionId, int SegmentVersionNumber,
    string SegmentName, string Role, int Priority, string Rationale, string ExpectedContribution, string Status);
public sealed record MarketingPlanObjectiveSummaryDto(Guid Id, string Name, string Status, DateTime StartsUtc, DateTime EndsUtc);
public sealed record MarketingPlanCampaignDto(Guid Id, Guid CampaignId, string CampaignName, string Purpose,
    Guid? ObjectiveId, string ObjectiveContribution, IReadOnlyList<Guid> SegmentVersionIds, decimal? AllocatedBudget,
    string BudgetCurrency, int Priority, string Status, string CampaignLifecycleStatus, DateTime? PlanningStartsUtc,
    DateTime? LaunchUtc, DateTime? ReviewUtc, DateTime? EndsUtc, Guid? OwnerAgentId, IReadOnlyList<string> ReadinessGaps);
public sealed record MarketingCoverageFindingDto(string Code, string Label, string Explanation, string Severity,
    Guid? ObjectiveId = null, Guid? SegmentVersionId = null, Guid? CampaignId = null);
public sealed record MarketingPlanListItemDto(Guid Id, string Name, string? StrategyTitle, int? StrategyVersion,
    DateTime StartsUtc, DateTime EndsUtc, decimal? PlannedBudget, decimal AllocatedBudget, decimal? RemainingBudget,
    string BudgetCurrency, int ObjectiveCount, int SegmentCount, int CampaignCount, string ReadinessLabel,
    string StatusLabel, Guid? OwnerAgentId, int Version, string? AttentionReason);
public sealed record MarketingPlanDetailDto(MarketingPlanListItemDto Summary, string Description, string Rationale,
    IReadOnlyList<string> EvidenceReferences, IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<MarketingPlanObjectiveSummaryDto> Objectives, IReadOnlyList<MarketingPlanSegmentDto> Segments,
    IReadOnlyList<MarketingPlanCampaignDto> Campaigns, IReadOnlyList<MarketingCoverageFindingDto> Coverage,
    Guid? ApprovalRequestId, IReadOnlyList<string> AllowedActions, bool StrategyGroundingAvailable);

public sealed record MarketingCampaignPortfolioItemRequest(string Name, string Purpose, Guid ObjectiveId,
    string ObjectiveContribution, IReadOnlyList<Guid> SegmentVersionIds, decimal? AllocatedBudget, string BudgetCurrency,
    int Priority, string CampaignType, string AudienceType, decimal ObjectiveTarget, string ObjectiveUnit,
    DateTime ObjectiveTargetUtc, DateTime PlanningStartsUtc, DateTime LaunchUtc, DateTime ReviewUtc, DateTime EndsUtc,
    string TimeZoneId, string? CommunicationLanguage, IReadOnlyList<string> Channels, string? OfferBasis,
    IReadOnlyList<string> Activities, IReadOnlyList<string> ContentNeeds, string AudienceApproach,
    string MeasurementApproach, IReadOnlyList<string> EvidenceReferences, IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string>? TaskNeeds = null, IReadOnlyList<string>? Assumptions = null, IReadOnlyList<string>? Risks = null);
public sealed record PrepareMarketingCampaignPortfolioRequest(Guid PlanId, int ExpectedPlanVersion,
    IReadOnlyList<MarketingCampaignPortfolioItemRequest> Campaigns, string IdempotencyKey, Guid? AgentId = null);
public sealed record MarketingCampaignPortfolioProposalDto(string ProposalKey, Guid PlanId, int PlanVersion,
    MarketingPolicyDecisionDto Decision, IReadOnlyList<MarketingCoverageFindingDto> Findings,
    IReadOnlyList<MarketingCampaignPortfolioItemRequest> Campaigns);
public sealed record CommitMarketingCampaignPortfolioRequest(PrepareMarketingCampaignPortfolioRequest Portfolio);
public sealed record MarketingCampaignPortfolioResultDto(Guid PlanId, int PlanVersion,
    IReadOnlyList<MarketingPlanCampaignDto> Campaigns, bool Idempotent, string Outcome);

public sealed record MarketingWorkNeedDto(string ReasonCode, string Label, string Urgency, bool Actionable,
    IReadOnlyList<Guid> AffectedIds, IReadOnlyList<string> EvidenceReferences, string Explanation,
    string RecommendedTool, bool RequiresApproval, string Fingerprint);
public sealed record MarketingWorkNeedAssessmentDto(DateTime AssessedUtc, IReadOnlyList<MarketingWorkNeedDto> Needs,
    IReadOnlyList<string> CheckedEvidence, bool HasActionableWork);
public interface IMarketingWorkNeedAssessment
{
    Task<MarketingWorkNeedAssessmentDto> AssessAsync(Guid companyId, DateTime asOfUtc, CancellationToken ct);
}

public sealed record MarketingDailyReviewDto(Guid RunId, DateTime RunDateUtc, string OutcomeLabel,
    string Summary, IReadOnlyList<string> CheckedEvidence, IReadOnlyList<MarketingWorkNeedDto> Needs,
    IReadOnlyList<string> Actions, IReadOnlyList<string> Blockers, string? NextHumanAction);
public sealed record TransitionMarketingPlanRequest(int ExpectedVersion, string Rationale);

public partial interface IMarketingOperationsService
{
    Task<MarketingPlanListItemDto[]> ListPlanPortfolioAsync(Guid companyId, CancellationToken ct);
    Task<MarketingPlanDetailDto?> GetPlanPortfolioAsync(Guid companyId, Guid planId, CancellationToken ct);
    Task<MarketingPolicyDecisionDto> AssessPlanReadinessAsync(Guid companyId, CreateGroundedMarketingPlanRequest request, CancellationToken ct);
    Task<MarketingPlanDetailDto> CreateGroundedPlanAsync(Guid companyId, Guid userId, CreateGroundedMarketingPlanRequest request, CancellationToken ct);
    Task<MarketingCampaignPortfolioProposalDto> PrepareCampaignPortfolioAsync(Guid companyId, PrepareMarketingCampaignPortfolioRequest request, CancellationToken ct);
    Task<MarketingCampaignPortfolioResultDto> CommitCampaignPortfolioAsync(Guid companyId, Guid userId, CommitMarketingCampaignPortfolioRequest request, CancellationToken ct);
    Task<MarketingDailyReviewDto?> GetDailyReviewAsync(Guid companyId, DateTime dateUtc, CancellationToken ct);
    Task<MarketingPlanDetailDto?> SubmitPlanForReviewAsync(Guid companyId, Guid userId, Guid planId, int expectedVersion, CancellationToken ct);
    Task<MarketingPlanDetailDto?> ActivateGroundedPlanAsync(Guid companyId, Guid userId, Guid planId, int expectedVersion, CancellationToken ct);
    Task<MarketingPlanDetailDto?> CompletePlanAsync(Guid companyId, Guid planId, TransitionMarketingPlanRequest request, CancellationToken ct);
    Task<MarketingPlanDetailDto?> CancelPlanAsync(Guid companyId, Guid planId, TransitionMarketingPlanRequest request, CancellationToken ct);
}
