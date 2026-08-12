using VirtualCompany.Application.CustomerMemory;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Sales;

public interface ISalesPersistenceRepository
{
    Task<IReadOnlyList<Lead>> ListLeadsAsync(
        Guid companyId,
        string? status,
        CancellationToken cancellationToken);

    Task<Lead?> GetLeadAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken);

    Task AddLeadAsync(Lead lead, CancellationToken cancellationToken);

    Task AddDealAsync(Deal deal, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ISalesOperationsService
{
    Task<SalesDashboardResponse> GetDashboardAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesLeadSummaryResponse>> ListLeadsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<SalesLeadDetailResponse?> GetLeadAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken);
    Task<SalesLeadDetailResponse?> QualifyLeadAsync(Guid companyId, Guid userId, Guid leadId, SalesActionRequest request, CancellationToken cancellationToken);
    Task<SalesLeadDetailResponse?> UpdateLeadQualificationAsync(Guid companyId, Guid userId, Guid leadId, UpdateLeadQualificationRequest request, CancellationToken cancellationToken);
    Task<SalesLeadDetailResponse?> RejectLeadAsync(Guid companyId, Guid userId, Guid leadId, SalesActionRequest request, CancellationToken cancellationToken);
    Task<SalesDealDetailResponse?> ConvertLeadAsync(Guid companyId, Guid userId, Guid leadId, ConvertLeadRequest request, CancellationToken cancellationToken);
    Task<SalesPipelineResponse> GetPipelineAsync(Guid companyId, CancellationToken cancellationToken);
    Task<SalesDealDetailResponse?> GetDealAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesActivityResponse>> ListDealActivitiesAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesEmailTimelineResponse>> ListDealEmailsAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesRecommendationResponse>> ListRecommendationsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<SalesDealDetailResponse?> ChangeDealStageAsync(Guid companyId, Guid userId, Guid dealId, ChangeDealStageRequest request, CancellationToken cancellationToken);
    Task<SalesDealDetailResponse?> MarkDealWonAsync(Guid companyId, Guid userId, Guid dealId, SalesActionRequest request, CancellationToken cancellationToken);
    Task<SalesDealDetailResponse?> MarkDealLostAsync(Guid companyId, Guid userId, Guid dealId, SalesActionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesRecommendationResponse>> DetectFollowUpRecommendationsAsync(Guid companyId, Guid userId, CancellationToken cancellationToken);
    Task<SalesRecommendationResponse?> ApproveRecommendationAsync(Guid companyId, Guid userId, Guid recommendationId, SalesActionRequest request, CancellationToken cancellationToken);
    Task<SalesRecommendationResponse?> RetryRecommendationAsync(Guid companyId, Guid userId, Guid recommendationId, CancellationToken cancellationToken);
    Task<SalesAutomationPolicyResponse> GetAutomationPolicyAsync(Guid companyId, CancellationToken cancellationToken);
    Task<SalesAutomationPolicyResponse> UpdateAutomationPolicyAsync(Guid companyId, Guid userId, UpdateSalesAutomationPolicyRequest request, CancellationToken cancellationToken);
    Task<SalesFinanceHandoffResponse?> GetFinanceHandoffAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken);
    Task<SalesFinanceHandoffResponse?> ApproveFinanceHandoffAsync(Guid companyId, Guid userId, Guid dealId, SalesActionRequest request, CancellationToken cancellationToken);
    Task<SalesFinanceHandoffResponse?> RetryFinanceHandoffAsync(Guid companyId, Guid userId, Guid dealId, CancellationToken cancellationToken);
    Task<ProcessSalesEmailResponse> ProcessEmailAsync(Guid companyId, Guid userId, ProcessSalesEmailRequest request, CancellationToken cancellationToken);
}

public interface IRevenueForecastService
{
    Task<RevenueForecastSnapshotDto> CalculateAndPersistForecastAsync(Guid companyId, DateTime asOfUtc, CancellationToken cancellationToken);
    Task<RevenueForecastSnapshotDto?> GetLatestForecastAsync(Guid companyId, CancellationToken cancellationToken);
    Task<DealRiskScoreDto?> GetLatestDealRiskScoreAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken);
}

public interface IPipelineRiskScoringJobRunner
{
    Task<PipelineRiskScoringRunResult> RunDailyAsync(DateTime asOfUtc, CancellationToken cancellationToken);
}

public sealed class SalesValidationException : Exception
{
    public SalesValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("The sales request is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static void ThrowIfEmpty(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw new SalesValidationException(new Dictionary<string, string[]> { [field] = ["This field is required."] });
        }
    }
}

public sealed record SalesDashboardResponse(
    decimal PipelineValue,
    string Currency,
    int NewLeads,
    int HotLeads,
    int DealsNeedingAttention,
    decimal ForecastRevenue,
    IReadOnlyList<SalesDealSummaryResponse> DealsRequiringAction,
    IReadOnlyList<SalesRecommendationResponse> AgentRecommendations,
    IReadOnlyList<SalesActivityResponse> RecentActivity);

public sealed record SalesLeadSummaryResponse(
    Guid Id,
    string Title,
    string Status,
    string Temperature,
    string? SourceEmail,
    string QualificationStatus,
    decimal? ConfidenceScore,
    string SuggestedNextAction,
    decimal? EstimatedValue,
    string? Currency,
    string? Fit,
    string? Priority,
    DateTime? QualifiedUtc,
    Guid? QualifiedByUserId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record SalesLeadDetailResponse(
    Guid Id,
    string Title,
    string Status,
    string QualificationStatus,
    string Temperature,
    string? SourceEmail,
    string? ContactName,
    string? CustomerCompanyName,
    decimal? EstimatedValue,
    string? Currency,
    string SuggestedNextAction,
    string? Fit,
    string? Priority,
    DateTime? QualifiedUtc,
    Guid? QualifiedByUserId,
    IReadOnlyList<SalesActivityResponse> Activities,
    IReadOnlyList<SalesRecommendationResponse> Recommendations);

public sealed record SalesLeadSourceEmailResponse(
    Guid LinkId,
    string ProviderMessageId,
    string? InternetMessageId,
    string? Subject,
    string? SenderName,
    string? SenderEmail,
    IReadOnlyList<string> Recipients,
    DateTime? ReceivedUtc,
    string? PlainTextBody,
    string? DetectedIntent,
    string? ProductOrServiceInterest,
    decimal? Confidence,
    string? ClassificationEvidence,
    string? SafeFailureMessage);

public interface ISalesLeadEmailEvidenceService
{
    Task<IReadOnlyList<SalesLeadSourceEmailResponse>> ListAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken);
}
public sealed record SalesDealSummaryResponse(
    Guid Id,
    string Title,
    Guid StageId,
    string StageName,
    string Status,
    decimal Amount,
    string Currency,
    string? CustomerCompanyName,
    string? ContactName,
    DateTime? ExpectedCloseUtc,
    DateTime UpdatedUtc);

public sealed record SalesDealDetailResponse(
    Guid Id,
    string Title,
    Guid StageId,
    string StageName,
    string Status,
    decimal Amount,
    string Currency,
    string Summary,
    string? ContactName,
    string? ContactEmail,
    string? CustomerCompanyName,
    string AgentAnalysis,
    string SuggestedReply,
    IReadOnlyList<SalesActivityResponse> Activities,
    IReadOnlyList<SalesRecommendationResponse> Recommendations,
    IReadOnlyList<string> AvailableActions,
    SalesFinanceHandoffResponse? FinanceHandoff,
    CustomerMemoryContext? CustomerMemory = null,
    Guid? SourceLeadId = null);

public sealed record SalesPipelineResponse(IReadOnlyList<SalesPipelineStageResponse> Stages);
public sealed record SalesPipelineStageResponse(Guid StageId, string Name, int DisplayOrder, decimal TotalValue, int DealCount, IReadOnlyList<SalesDealSummaryResponse> Deals);
public sealed record SalesActivityResponse(Guid Id, string ActivityType, string Summary, string Status, DateTime OccurredUtc, Guid? LeadId, Guid? DealId);
public sealed record SalesRecommendationResponse(
    Guid Id,
    string Recommendation,
    string Rationale,
    string Status,
    Guid? LeadId,
    Guid? DealId,
    string Category,
    string TriggerCondition,
    string ActionType,
    string RiskLevel,
    bool RequiresApproval,
    string ApprovalStatus,
    string ExecutionStatus,
    string? FailureSummary,
    bool CanRetryExecution,
    int ExecutionAttemptCount,
    string? LastExecutionErrorCode,
    string? Provider,
    Guid? MailboxConnectionId,
    string? ProviderThreadId,
    string? ProviderMessageId,
    string? ProviderDraftId,
    Guid? ActivityId,
    DateTime CreatedUtc);

public sealed record SalesAutomationPolicyResponse(
    Guid Id,
    string Mode,
    bool FinanceDocumentsAlwaysRequireApproval,
    bool OutboundEnabled,
    int MaxEmailsPerDay,
    bool RequireApprovalFirstContact,
    bool RequireApprovalPricingDiscussion,
    bool RequireApprovalFollowUps,
    bool RequireApprovalReEngagement,
    int WebsiteLeadDeduplicationWindowMinutes,
    Guid? WebsiteLeadFollowUpSequenceId,
    DateTime UpdatedUtc);

public sealed record SalesFinanceHandoffResponse(
    Guid Id,
    Guid DealId,
    string Status,
    string ApprovalStatus,
    string ExecutionStatus,
    string Summary,
    string DocumentType,
    string ExternalSystem,
    string? ExternalDocumentId,
    string? ExternalDocumentNumber,
    Guid? ApprovalId,
    Guid? WriteRequestId,
    string IdempotencyKey,
    string? FailureSummary,
    bool CanRetry,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? ApprovedUtc,
    DateTime? ExecutedUtc,
    DateTime? FailedUtc,
    DateTime? RetriedUtc);
public sealed record SalesEmailTimelineResponse(
    Guid Id,
    string ProviderMessageId,
    string Status,
    string? DetectedIntent,
    string? ProductOrServiceInterest,
    decimal? Confidence,
    string? Rationale,
    DateTime OccurredUtc,
    Guid? LeadId,
    Guid? DealId);

public sealed record SalesActionRequest(string? Note);
public sealed record UpdateLeadQualificationRequest(
    string Fit,
    string Temperature,
    string Priority,
    string SuggestedNextAction,
    string? Note);

public sealed record UpdateSalesAutomationPolicyRequest(
    string Mode,
    bool? OutboundEnabled = null,
    int? MaxEmailsPerDay = null,
    bool? RequireApprovalFirstContact = null,
    bool? RequireApprovalPricingDiscussion = null,
    bool? RequireApprovalFollowUps = null,
    bool? RequireApprovalReEngagement = null,
    int? WebsiteLeadDeduplicationWindowMinutes = null,
    Guid? WebsiteLeadFollowUpSequenceId = null);
public sealed record ConvertLeadRequest(decimal Amount, string Currency, DateTime? ExpectedCloseUtc, string? Note);
public sealed record ChangeDealStageRequest(Guid StageId, string? Note);
public sealed record ProcessSalesEmailRequest(
    string ProviderMessageId,
    string SenderEmail,
    string? SenderName,
    string? CompanyName,
    string Subject,
    string Body,
    string? Intent,
    string? ProductOrServiceInterest,
    decimal Confidence,
    bool CreateLead = true);

public sealed record ProcessSalesEmailResponse(
    string Status,
    Guid? LeadId,
    Guid? ActivityId,
    Guid EmailLinkId);

public sealed record RevenueForecastSnapshotDto(
    Guid Id,
    Guid CompanyId,
    DateTime AsOfUtc,
    DateTime CalculatedUtc,
    string Currency,
    IReadOnlyList<RevenueForecastWindowDto> Windows,
    RiskDistributionSummary RiskDistribution);

public sealed record RevenueForecastWindowDto(
    int Days,
    decimal GrossPipelineValue,
    decimal ExpectedRevenue,
    int DealCount);

public sealed record DealRiskScoreDto(
    Guid Id,
    Guid CompanyId,
    Guid DealId,
    decimal Score,
    string Band,
    DateTime CalculatedUtc,
    string FactorsSummary);

public sealed record PipelineRiskScoringRunResult(
    int CompanyCount,
    int DealCount,
    int ForecastSnapshotCount);

public static class RevenueForecastWindows
{
    public static IReadOnlyList<int> SupportedDays { get; } = [30, 60, 90];
}

public static class DealRiskBands
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
}
