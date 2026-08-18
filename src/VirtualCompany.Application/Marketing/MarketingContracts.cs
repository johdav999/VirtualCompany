namespace VirtualCompany.Application.Marketing;

public sealed record MarketingObjectiveDto(Guid Id, string Name, string ObjectiveType, decimal TargetValue,
    string Unit, decimal? BaselineValue, DateTime PeriodStartUtc, DateTime PeriodEndUtc, string Status, int Version);
public sealed record CreateMarketingObjectiveRequest(string Name, string ObjectiveType, decimal TargetValue,
    string Unit, DateTime PeriodStartUtc, DateTime PeriodEndUtc, decimal? BaselineValue = null);

public sealed record MarketingPlanDto(Guid Id, string Name, string Summary, DateTime StartsUtc, DateTime EndsUtc,
    decimal? PlannedBudget, string BudgetCurrency, string Status, int Version);
public sealed record CreateMarketingPlanRequest(string Name, string Summary, DateTime StartsUtc, DateTime EndsUtc,
    decimal? PlannedBudget, string BudgetCurrency, IReadOnlyList<Guid>? ObjectiveIds = null);
public sealed record MarketingPlanProposalDto(string ProposalKey, string Name, string Summary, DateTime StartsUtc,
    DateTime EndsUtc, decimal? PlannedBudget, string BudgetCurrency, IReadOnlyList<Guid> ObjectiveIds,
    IReadOnlyList<string> Assumptions, IReadOnlyList<string> Risks, IReadOnlyList<string> MissingEvidence);
public sealed record CommitMarketingPlanRequest(string IdempotencyKey, CreateMarketingPlanRequest Plan);

public sealed record MarketingCalendarItemDto(Guid Id, string Kind, string Name, DateTime StartsUtc, DateTime EndsUtc,
    string Status, Guid? CampaignId, Guid? OwnerAgentId, bool IsSpan = false, string SourceRecordType = "marketing",
    Guid? SourceRecordId = null, Guid? PlanId = null, string AttentionState = "none", string? NavigationTarget = null);

public sealed record MarketingContentBriefDto(Guid Id, Guid? CampaignId, Guid? PlanId, string Title, string Purpose,
    string Audience, string Channel, string Language, string Tone, string CallToAction, DateTime? DueUtc,
    string Status, int Version, IReadOnlyList<MarketingContentVariantDto> Variants,
    Guid? SegmentVersionId = null, string MeasurableObjective = "", string FunnelStage = "",
    string CustomerInsight = "", string KeyMessage = "", string SupportingPointsJson = "[]",
    string Offer = "", string RequiredClaimsJson = "[]", string ProhibitedClaimsJson = "[]",
    string SeoRequirementsJson = "{}", string VisualDirection = "", string DesiredFormatsJson = "[]",
    string VariantRequirementsJson = "{}", string EvidenceRequirementsJson = "{}",
    string ApprovalPolicyJson = "{}");
public sealed record MarketingContentVariantDto(Guid Id, Guid VariantFamilyId, int VersionNumber, string Name,
    string Body, string ContentFormat, string SourceReferences, bool GeneratedByAi, Guid? GenerationRunId,
    string CapabilityVersion, string PromptVersion, string Status, DateTime CreatedUtc);
public sealed record CreateMarketingContentBriefRequest(Guid? CampaignId, Guid? PlanId, string Title, string Purpose,
    string Audience, string Channel, string Language, string Tone, string CallToAction, DateTime? DueUtc,
    Guid? SegmentVersionId = null, string MeasurableObjective = "", string FunnelStage = "awareness",
    string? CustomerInsight = null, string KeyMessage = "", string SupportingPointsJson = "[]",
    string Offer = "", string RequiredClaimsJson = "[]", string ProhibitedClaimsJson = "[]",
    string SeoRequirementsJson = "{}", string VisualDirection = "", string DesiredFormatsJson = "[]",
    string VariantRequirementsJson = "{}", string EvidenceRequirementsJson = "{}",
    string ApprovalPolicyJson = "{}");
public sealed record CreateMarketingContentVariantRequest(string Name, string Body, string SourceReferences,
    bool GeneratedByAi = false);
public sealed record CreateMarketingContentVariantVersionRequest(string Name, string Body, string SourceReferences);
public sealed record GenerateMarketingContentVariantsRequest(Guid AgentId, string ContentFormat, int VariantCount,
    string Instructions, string IdempotencyKey);
public sealed record GenerateMarketingContentVariantsResult(Guid RunId, string Status,
    IReadOnlyList<MarketingContentVariantDto> Variants, IReadOnlyList<string> MissingEvidence, bool RequiresReview);
public sealed record ReviewMarketingContentRequest(bool Approved);
public sealed record CompleteMarketingExperimentRequest(string Decision);
public sealed record MarketingContentPreflightIssueDto(string Code, string Severity, string Explanation,
    Guid? VariantId = null);
public sealed record MarketingContentPreflightDto(Guid BriefId, bool ReadyForReview,
    IReadOnlyList<MarketingContentPreflightIssueDto> Issues);

public sealed record MarketingSalesHandoffDto(Guid Id, Guid? CampaignId, Guid? ContactId, Guid? CustomerCompanyId,
    Guid? LinkedLeadId, Guid? LinkedDealId, string Reason, string SuggestedAction, string Urgency,
    DateTime ExpiresUtc, string EvidenceReferences, string Status, string? DecisionReason, DateTime UpdatedUtc);
public sealed record CreateMarketingSalesHandoffRequest(Guid? CampaignId, Guid? ContactId, Guid? CustomerCompanyId,
    string Reason, string SuggestedAction, string Urgency, DateTime ExpiresUtc, string EvidenceReferences,
    string IdempotencyKey);
public sealed record DecideMarketingSalesHandoffRequest(bool Accepted, string Reason, Guid? LeadId, Guid? DealId);

public sealed record MarketingObservationDto(Guid Id, Guid? CampaignId, Guid? ActivityId, string Provider,
    string MetricCode, decimal Value, string Unit, DateTime PeriodStartUtc, DateTime PeriodEndUtc,
    string SourceReference, DateTime RetrievedUtc, Guid? CorrectionOfObservationId = null, bool IsSuperseded = false);
public sealed record CreateMarketingObservationRequest(Guid? CampaignId, Guid? ActivityId, string Provider,
    string MetricCode, decimal Value, string Unit, DateTime PeriodStartUtc, DateTime PeriodEndUtc,
    string SourceReference, string IdempotencyKey, Guid? CorrectionOfObservationId = null);

public sealed record MarketingExperimentDto(Guid Id, Guid? CampaignId, string Name, string Hypothesis,
    string PrimaryMetric, string GuardrailMetric, int MinimumSampleSize, DateTime StartsUtc, DateTime EndsUtc,
    string Status, string? Decision);
public sealed record CreateMarketingExperimentRequest(Guid? CampaignId, string Name, string Hypothesis,
    string PrimaryMetric, string GuardrailMetric, int MinimumSampleSize, DateTime StartsUtc, DateTime EndsUtc);

public sealed record MarketingQualificationDefinitionDto(Guid Id, string Name, string AudienceType,
    string RequiredChannel, decimal Threshold, int FreshnessDays, bool RequiresCustomerCompany,
    DateTime EffectiveFromUtc, DateTime? EffectiveToUtc, string RulesJson, string ExclusionsJson,
    string Status, int Version);
public sealed record CreateMarketingQualificationDefinitionRequest(string Name, string AudienceType,
    string RequiredChannel, decimal Threshold, int FreshnessDays, bool RequiresCustomerCompany,
    DateTime EffectiveFromUtc, DateTime? EffectiveToUtc, string RulesJson, string ExclusionsJson);
public sealed record EvaluateMarketingContactRequest(Guid DefinitionId, Guid ContactId, string IdempotencyKey);
public sealed record MarketingQualificationEvaluationDto(Guid Id, Guid DefinitionId, int DefinitionVersion,
    Guid ContactId, string ContactName, decimal Score, string Status, IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> EvidenceReferences, DateTime EvidenceObservedUtc, DateTime EvaluatedUtc);
public sealed record RecordMarketingQualificationFeedbackRequest(string Decision, string Reason,
    Guid? LeadId, Guid? DealId);
public sealed record MarketingQualificationFeedbackDto(Guid Id, Guid EvaluationId, string Decision,
    string Reason, Guid? LeadId, Guid? DealId, DateTime CreatedUtc);

public sealed record MarketingMetricDto(string Name, decimal? Value, string Unit, string State, string Explanation);
public sealed record MarketingDashboardDto(Guid CompanyId, DateTime GeneratedUtc,
    IReadOnlyList<MarketingMetricDto> Metrics, IReadOnlyList<MarketingObjectiveDto> Objectives,
    IReadOnlyList<MarketingPlanDto> Plans, IReadOnlyList<MarketingCalendarItemDto> Calendar,
    IReadOnlyList<MarketingContentBriefDto> Content, IReadOnlyList<MarketingSalesHandoffDto> Handoffs,
    IReadOnlyList<MarketingExperimentDto> Experiments,
    IReadOnlyList<MarketingQualificationDefinitionDto> QualificationDefinitions,
    IReadOnlyList<MarketingQualificationEvaluationDto> QualificationEvaluations);

public partial interface IMarketingOperationsService
{
    Task<MarketingDashboardDto> GetDashboardAsync(Guid companyId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<IReadOnlyList<MarketingObjectiveDto>> ListObjectivesAsync(Guid companyId, CancellationToken ct);
    Task<MarketingObjectiveDto> CreateObjectiveAsync(Guid companyId, Guid userId, CreateMarketingObjectiveRequest request, CancellationToken ct);
    Task<MarketingObjectiveDto?> ActivateObjectiveAsync(Guid companyId, Guid objectiveId, CancellationToken ct);
    Task<IReadOnlyList<MarketingPlanDto>> ListPlansAsync(Guid companyId, CancellationToken ct);
    Task<MarketingPlanProposalDto> PreparePlanProposalAsync(Guid companyId, CreateMarketingPlanRequest request, CancellationToken ct);
    Task<MarketingPlanDto> CommitPlanAsync(Guid companyId, Guid userId, CommitMarketingPlanRequest request, CancellationToken ct);
    Task<MarketingPlanDto> CreatePlanAsync(Guid companyId, Guid userId, CreateMarketingPlanRequest request, CancellationToken ct);
    Task<MarketingPlanDto?> ActivatePlanAsync(Guid companyId, Guid planId, CancellationToken ct);
    Task<IReadOnlyList<MarketingContentBriefDto>> ListContentAsync(Guid companyId, CancellationToken ct);
    Task<MarketingContentBriefDto> CreateContentBriefAsync(Guid companyId, Guid userId, CreateMarketingContentBriefRequest request, CancellationToken ct);
    Task<MarketingContentVariantDto?> AddContentVariantAsync(Guid companyId, Guid briefId, CreateMarketingContentVariantRequest request, CancellationToken ct);
    Task<MarketingContentVariantDto?> CreateContentVariantVersionAsync(Guid companyId, Guid variantId,
        CreateMarketingContentVariantVersionRequest request, CancellationToken ct);
    Task<bool> RetireContentVariantAsync(Guid companyId, Guid variantId, CancellationToken ct);
    Task<GenerateMarketingContentVariantsResult> GenerateContentVariantsAsync(Guid companyId, Guid userId,
        Guid briefId, GenerateMarketingContentVariantsRequest request, CancellationToken ct);
    Task<MarketingContentPreflightDto?> PreflightContentAsync(Guid companyId, Guid briefId, CancellationToken ct);
    Task<bool> SubmitContentAsync(Guid companyId, Guid briefId, CancellationToken ct);
    Task<bool> ReviewContentAsync(Guid companyId, Guid briefId, ReviewMarketingContentRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingSalesHandoffDto>> ListHandoffsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingSalesHandoffDto> CreateHandoffAsync(Guid companyId, CreateMarketingSalesHandoffRequest request, CancellationToken ct);
    Task<MarketingSalesHandoffDto?> DecideHandoffAsync(Guid companyId, Guid handoffId, DecideMarketingSalesHandoffRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingObservationDto>> ListObservationsAsync(Guid companyId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<MarketingObservationDto> RecordObservationAsync(Guid companyId, CreateMarketingObservationRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingExperimentDto>> ListExperimentsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingExperimentDto> CreateExperimentAsync(Guid companyId, CreateMarketingExperimentRequest request, CancellationToken ct);
    Task<MarketingExperimentDto?> ActivateExperimentAsync(Guid companyId, Guid experimentId, CancellationToken ct);
    Task<MarketingExperimentDto?> CompleteExperimentAsync(Guid companyId, Guid experimentId, CompleteMarketingExperimentRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingQualificationDefinitionDto>> ListQualificationDefinitionsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingQualificationDefinitionDto> CreateQualificationDefinitionAsync(Guid companyId, Guid userId, CreateMarketingQualificationDefinitionRequest request, CancellationToken ct);
    Task<MarketingQualificationDefinitionDto?> ActivateQualificationDefinitionAsync(Guid companyId, Guid definitionId, CancellationToken ct);
    Task<IReadOnlyList<MarketingQualificationEvaluationDto>> ListQualificationEvaluationsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingQualificationEvaluationDto> EvaluateContactAsync(Guid companyId, EvaluateMarketingContactRequest request, CancellationToken ct);
    Task<MarketingQualificationFeedbackDto> RecordQualificationFeedbackAsync(Guid companyId, Guid userId, Guid evaluationId, RecordMarketingQualificationFeedbackRequest request, CancellationToken ct);
}
