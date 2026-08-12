namespace VirtualCompany.Application.Marketing;

public sealed record MarketingStrategyDto(Guid Id, string Title, string Summary, string BusinessContext,
    DateTime ValidFromUtc, DateTime ValidToUtc, string SectionsJson, string EvidenceReferencesJson,
    string MissingEvidenceJson, string Status, Guid? ApprovalRequestId, int Version, DateTime UpdatedUtc,
    IReadOnlyList<Guid> SegmentVersionIds);
public sealed record SaveMarketingStrategyRequest(string Title, string Summary, string BusinessContext,
    DateTime ValidFromUtc, DateTime ValidToUtc, string SectionsJson, string EvidenceReferencesJson,
    string MissingEvidenceJson, string IdempotencyKey, IReadOnlyList<Guid>? SegmentVersionIds = null, int? ExpectedVersion = null);
public sealed record CancelMarketingStrategyRequest(int ExpectedVersion, string Rationale);
public sealed record PrepareMarketingStrategyProposalRequest(Guid AgentId, string Objective, string Title,
    DateTime ValidFromUtc, DateTime ValidToUtc, IReadOnlyList<Guid> TargetSegmentVersionIds);
public sealed record MarketingStrategyRecommendationDto(string Area, string Recommendation, string Classification,
    decimal Confidence, IReadOnlyList<Guid> TargetSegmentVersionIds, IReadOnlyList<string> SourceIds);
public sealed record MarketingStrategyProposalDto(Guid RunId, Guid AgentId, string Status, string Title,
    string Summary, string BusinessContext, DateTime ValidFromUtc, DateTime ValidToUtc,
    IReadOnlyList<MarketingStrategyRecommendationDto> MarketCustomerSynthesis,
    IReadOnlyList<MarketingStrategyRecommendationDto> StpAndPositioning,
    IReadOnlyList<MarketingStrategyRecommendationDto> FourPs,
    IReadOnlyList<MarketingStrategyRecommendationDto> CompetitiveAnalysis,
    IReadOnlyList<MarketingStrategyRecommendationDto> SwotAndFiveForces,
    IReadOnlyList<Agents.AgentAiSource> Sources, IReadOnlyList<string> MissingEvidence,
    bool RequiresReview, string CapabilityVersion, string PromptVersion);
public sealed record CommitMarketingStrategyProposalRequest(Guid AgentId, Guid RunId, string Title,
    string BusinessContext, DateTime ValidFromUtc, DateTime ValidToUtc,
    IReadOnlyList<Guid> TargetSegmentVersionIds, string IdempotencyKey);
public sealed record MarketingDecompositionActivityRequest(string Name, string ActivityType, string Channel,
    DateTime StartsUtc, DateTime DueUtc, Guid? OwnerAgentId = null, string? DependsOnName = null,
    bool ContentRequired = false);
public sealed record PrepareMarketingDecompositionRequest(Guid StrategyId, Guid CampaignId,
    Guid TargetSegmentVersionId, Guid ObjectiveId, string PlanName, string PlanSummary, DateTime StartsUtc,
    DateTime EndsUtc, decimal? PlannedBudget, string BudgetCurrency,
    IReadOnlyList<MarketingDecompositionActivityRequest> Activities);
public sealed record MarketingDecompositionProposalDto(string ProposalKey, Guid StrategyId, Guid CampaignId,
    Guid TargetSegmentVersionId, Guid ObjectiveId, string PlanName, string PlanSummary, DateTime StartsUtc,
    DateTime EndsUtc, decimal? PlannedBudget, string BudgetCurrency,
    IReadOnlyList<MarketingDecompositionActivityRequest> Activities, IReadOnlyList<string> ReadinessGaps,
    bool ReadyToCommit);
public sealed record CommitMarketingDecompositionRequest(string IdempotencyKey,
    PrepareMarketingDecompositionRequest Decomposition);
public sealed record MarketingDecompositionResultDto(Guid Id, Guid StrategyId, Guid PlanId, Guid CampaignId,
    Guid TargetSegmentVersionId, IReadOnlyList<Guid> ActivityIds, IReadOnlyList<Guid> TaskIds,
    IReadOnlyList<string> ReadinessGaps, string Status);

public sealed record MarketingIntelligenceDto(Guid Id, string Kind, string Subject, string Summary,
    string Classification, decimal Confidence, string SourceType, string SourceReference, DateTime ObservedUtc,
    DateTime ReviewDueUtc, string DimensionsJson, string ReviewStatus, bool IsArchived, int Version);
public sealed record CreateMarketingIntelligenceRequest(string Kind, string Subject, string Summary,
    string Classification, decimal Confidence, string SourceType, string SourceReference, DateTime ObservedUtc,
    DateTime ReviewDueUtc, string DimensionsJson);
public sealed record UpdateMarketingIntelligenceRequest(string Subject, string Summary, string Classification,
    decimal Confidence, string SourceType, string SourceReference, DateTime ObservedUtc, DateTime ReviewDueUtc,
    string DimensionsJson, int ExpectedVersion);
public sealed record ReviewMarketingIntelligenceRequest(bool Verified, string Rationale, int ExpectedVersion);
public sealed record ArchiveMarketingIntelligenceRequest(int ExpectedVersion);
public sealed record MarketingIntelligenceReviewDto(Guid Id, Guid IntelligenceId, int ReviewNumber,
    Guid ReviewerUserId, string Outcome, string Rationale, string BeforeJson, string AfterJson, DateTime CreatedUtc);

public sealed record MarketingSegmentDto(Guid Id, string Name, string Description, bool IsArchived,
    IReadOnlyList<MarketingSegmentVersionDto> Versions);
public sealed record MarketingSegmentVersionDto(Guid Id, Guid SegmentId, int VersionNumber, string CriteriaJson,
    string NeedsJson, string BehaviorsJson, string ChannelsJson, string PricingJson, long? SizeLow, long? SizeHigh,
    string SizeMethod, decimal Confidence, string EconomicsJson, string ScorecardJson, decimal AttractivenessScore,
    string EvidenceJson, DateTime EvidenceObservedUtc, string Status, string TargetState, string? TargetRationale,
    Guid? ApprovalRequestId, int ConcurrencyVersion);
public sealed record CreateMarketingSegmentRequest(string Name, string Description);
public sealed record CreateMarketingSegmentVersionRequest(string CriteriaJson, string NeedsJson,
    string BehaviorsJson, string ChannelsJson, string PricingJson, long? SizeLow, long? SizeHigh,
    string SizeMethod, decimal Confidence, string EconomicsJson, string ScorecardJson,
    IReadOnlyDictionary<string, decimal> ScoreDimensions, string EvidenceJson, DateTime EvidenceObservedUtc,
    string IdempotencyKey, IReadOnlyList<CreateMarketingSegmentSizeEstimateRequest>? SizeEstimates = null,
    IReadOnlyList<CreateMarketingSegmentEconomicEstimateRequest>? EconomicEstimates = null,
    CreateMarketingSegmentScorePolicyRequest? ScorePolicy = null);
public sealed record CreateMarketingSegmentSizeEstimateRequest(decimal? Low, decimal? High, string Unit,
    string Period, string Geography, string? Currency, string Method, string AssumptionsJson,
    string SourceIdsJson, decimal Confidence, DateTime ObservedUtc, DateTime AsOfUtc, string Classification);
public sealed record CreateMarketingSegmentEconomicEstimateRequest(string MetricCode, decimal? Low, decimal? High,
    string Unit, string? Currency, string Method, decimal Confidence, string SourceIdsJson,
    DateTime ObservedUtc, string Classification);
public sealed record CreateMarketingSegmentScoreDimensionRequest(string Code, decimal Weight, decimal? Score, string EvidenceJson);
public sealed record CreateMarketingSegmentScorePolicyRequest(decimal TargetThreshold, string MissingEvidenceBehavior,
    string ExclusionsJson, string RiskJson, IReadOnlyList<CreateMarketingSegmentScoreDimensionRequest> Dimensions);
public sealed record MarketingSegmentSizeEstimateDto(Guid Id, Guid SegmentVersionId, decimal? Low, decimal? High,
    string Unit, string Period, string Geography, string? Currency, string Method, string AssumptionsJson,
    string SourceIdsJson, decimal Confidence, DateTime ObservedUtc, DateTime AsOfUtc, string Classification);
public sealed record MarketingSegmentEconomicEstimateDto(Guid Id, Guid SegmentVersionId, string MetricCode,
    decimal? Low, decimal? High, string Unit, string? Currency, string Method, decimal Confidence,
    string SourceIdsJson, DateTime ObservedUtc, string Classification);
public sealed record MarketingSegmentScoreDimensionDto(Guid Id, string Code, decimal Weight, decimal? Score, string EvidenceJson);
public sealed record MarketingSegmentScorePolicyDto(Guid Id, Guid SegmentVersionId, decimal TargetThreshold,
    string MissingEvidenceBehavior, string ExclusionsJson, string RiskJson, decimal? CalculatedScore,
    string Decision, IReadOnlyList<MarketingSegmentScoreDimensionDto> Dimensions);
public sealed record CreateMarketingSegmentTargetDecisionRequest(string TargetType, string Rationale,
    string ExpectedImpactJson, decimal Confidence, string RisksJson, DateTime ReviewUtc, string IdempotencyKey);
public sealed record MarketingSegmentTargetDecisionDto(Guid Id, Guid SegmentVersionId, string TargetType,
    string Rationale, string ExpectedImpactJson, decimal Confidence, string RisksJson, DateTime ReviewUtc,
    string ApprovalStatus, Guid ActorId, Guid? ApprovalRequestId, string IdempotencyKey, DateTime DecidedUtc);
public sealed record CreateMarketingSegmentArtifactMappingRequest(string MappingType, Guid ArtifactId,
    string Label, string IdempotencyKey);
public sealed record MarketingSegmentArtifactMappingDto(Guid Id, Guid SegmentVersionId, string MappingType,
    Guid ArtifactId, string Label, DateTime CreatedUtc);
public sealed record MarketingSegmentDecisionDataDto(Guid SegmentVersionId,
    IReadOnlyList<MarketingSegmentSizeEstimateDto> SizeEstimates,
    IReadOnlyList<MarketingSegmentEconomicEstimateDto> EconomicEstimates,
    MarketingSegmentScorePolicyDto? ScorePolicy,
    IReadOnlyList<MarketingSegmentTargetDecisionDto> TargetDecisions,
    IReadOnlyList<MarketingSegmentArtifactMappingDto> Mappings);
public sealed record PrepareMarketingSegmentProposalRequest(Guid AgentId, string SegmentName, string Objective);
public sealed record MarketingSegmentProposalDto(Guid RunId, Guid AgentId, string SegmentName, string Summary,
    IReadOnlyList<MarketingStrategyRecommendationDto> Claims, IReadOnlyList<Agents.AgentAiSource> Sources,
    IReadOnlyList<string> MissingEvidence, decimal Confidence, bool RequiresReview,
    bool CanCreateDraft, string CapabilityVersion, string PromptVersion, string StructuredAnalysisJson = "{}");
public sealed record CommitMarketingSegmentProposalRequest(Guid AgentId, Guid RunId, string SegmentName,
    string Description, CreateMarketingSegmentVersionRequest Version, string IdempotencyKey);
public sealed record ActivateMarketingTargetRequest(string TargetState, string Rationale);
public sealed record MarketingSegmentImpactItemDto(string ArtifactType, Guid ArtifactId, string Label,
    string Status, string ReviewReason);
public sealed record MarketingSegmentImpactDto(Guid SegmentVersionId, bool IsCurrentVersion,
    bool RequiresReview, IReadOnlyList<MarketingSegmentImpactItemDto> Artifacts, DateTime AssessedUtc);
public sealed record MarketingSegmentDimensionDto(Guid Id, Guid SegmentVersionId, string Category,
    string Path, string Value, string Classification, decimal? NumericValue);

public interface IMarketingStrategyService
{
    Task<IReadOnlyList<MarketingStrategyDto>> ListStrategiesAsync(Guid companyId, CancellationToken ct);
    Task<MarketingStrategyDto?> GetStrategyAsync(Guid companyId, Guid strategyId, CancellationToken ct);
    Task<MarketingStrategyDto> CreateStrategyAsync(Guid companyId, Guid userId, SaveMarketingStrategyRequest request, CancellationToken ct);
    Task<MarketingStrategyDto?> UpdateStrategyAsync(Guid companyId, Guid userId, Guid strategyId, SaveMarketingStrategyRequest request, CancellationToken ct);
    Task<MarketingStrategyDto?> SubmitStrategyAsync(Guid companyId, Guid userId, Guid strategyId, CancellationToken ct);
    Task<MarketingStrategyDto?> ActivateStrategyAsync(Guid companyId, Guid userId, Guid strategyId, CancellationToken ct);
    Task<MarketingStrategyDto?> CancelStrategyAsync(Guid companyId, Guid userId, Guid strategyId, CancelMarketingStrategyRequest request, CancellationToken ct);
    Task<MarketingStrategyProposalDto> PrepareStrategyProposalAsync(Guid companyId, Guid userId,
        PrepareMarketingStrategyProposalRequest request, CancellationToken ct);
    Task<MarketingStrategyDto> CommitStrategyProposalAsync(Guid companyId, Guid userId,
        CommitMarketingStrategyProposalRequest request, CancellationToken ct);
    Task<MarketingDecompositionProposalDto> PrepareDecompositionAsync(Guid companyId,
        PrepareMarketingDecompositionRequest request, CancellationToken ct);
    Task<MarketingDecompositionResultDto> CommitDecompositionAsync(Guid companyId, Guid userId,
        CommitMarketingDecompositionRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingIntelligenceDto>> ListIntelligenceAsync(Guid companyId, bool freshnessQueue, CancellationToken ct);
    Task<MarketingIntelligenceDto?> GetIntelligenceAsync(Guid companyId, Guid intelligenceId, CancellationToken ct);
    Task<MarketingIntelligenceDto> CreateIntelligenceAsync(Guid companyId, Guid userId, CreateMarketingIntelligenceRequest request, CancellationToken ct);
    Task<MarketingIntelligenceDto?> UpdateIntelligenceAsync(Guid companyId, Guid userId, Guid intelligenceId, UpdateMarketingIntelligenceRequest request, CancellationToken ct);
    Task<MarketingIntelligenceDto?> ReviewIntelligenceAsync(Guid companyId, Guid userId, Guid intelligenceId, ReviewMarketingIntelligenceRequest request, CancellationToken ct);
    Task<MarketingIntelligenceDto?> ArchiveIntelligenceAsync(Guid companyId, Guid userId, Guid intelligenceId, ArchiveMarketingIntelligenceRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingIntelligenceReviewDto>> ListIntelligenceReviewsAsync(Guid companyId, Guid intelligenceId, CancellationToken ct);
    Task<IReadOnlyList<MarketingSegmentDto>> ListSegmentsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingSegmentDto> CreateSegmentAsync(Guid companyId, Guid userId, CreateMarketingSegmentRequest request, CancellationToken ct);
    Task<MarketingSegmentVersionDto?> CreateSegmentVersionAsync(Guid companyId, Guid userId, Guid segmentId, CreateMarketingSegmentVersionRequest request, CancellationToken ct);
    Task<MarketingSegmentProposalDto> PrepareSegmentProposalAsync(Guid companyId, Guid userId, PrepareMarketingSegmentProposalRequest request, CancellationToken ct);
    Task<MarketingSegmentVersionDto> CommitSegmentProposalAsync(Guid companyId, Guid userId, CommitMarketingSegmentProposalRequest request, CancellationToken ct);
    Task<MarketingSegmentVersionDto?> SubmitSegmentVersionAsync(Guid companyId, Guid userId, Guid versionId, CancellationToken ct);
    Task<MarketingSegmentVersionDto?> ActivateTargetAsync(Guid companyId, Guid versionId, ActivateMarketingTargetRequest request, CancellationToken ct);
    Task<MarketingSegmentImpactDto?> GetSegmentImpactAsync(Guid companyId, Guid versionId, CancellationToken ct);
    Task<IReadOnlyList<MarketingSegmentDimensionDto>> ListSegmentDimensionsAsync(Guid companyId,
        Guid versionId, CancellationToken ct);
    Task<MarketingSegmentDecisionDataDto?> GetSegmentDecisionDataAsync(Guid companyId, Guid versionId, CancellationToken ct);
    Task<MarketingSegmentTargetDecisionDto?> RecommendTargetAsync(Guid companyId, Guid actorId, Guid versionId,
        CreateMarketingSegmentTargetDecisionRequest request, CancellationToken ct);
    Task<MarketingSegmentArtifactMappingDto?> MapSegmentArtifactAsync(Guid companyId, Guid versionId,
        CreateMarketingSegmentArtifactMappingRequest request, CancellationToken ct);
}
