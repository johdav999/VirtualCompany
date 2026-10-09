using VirtualCompany.Application.CustomerMemory;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Sales;

public sealed record ChangeDealStageRequest(Guid StageId, string? Note);

public sealed record SalesActivityResponse(Guid Id, string ActivityType, string Summary, string Status, DateTime OccurredUtc, Guid? LeadId, Guid? DealId);


public sealed record SalesDashboardResponse(
    decimal PipelineValue,
    string Currency,
    int NewLeads,
    int HotLeads,
    int DealsNeedingAttention,
    decimal ForecastRevenue,
    IReadOnlyList<SalesDealSummaryResponse> DealsRequiringAction,
    IReadOnlyList<SalesRecommendationResponse> AgentRecommendations,
    IReadOnlyList<SalesActivityResponse> RecentActivity,
    IReadOnlyList<string>? Currencies = null);


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

public sealed record LinkDealCustomerCompanyRequest(string CompanyName);


public sealed record RevenueForecastWindowDto(
    int Days,
    decimal GrossPipelineValue,
    decimal ExpectedRevenue,
    int DealCount);


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
    Guid? SourceLeadId = null,
    IReadOnlyList<SalesDealMeetingResponse>? Meetings = null);


public sealed record SalesPipelineResponse(IReadOnlyList<SalesPipelineStageResponse> Stages);


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
    DateTime UpdatedUtc,
    IReadOnlyList<SalesDealMeetingResponse>? Meetings = null);

public sealed record UpdateLeadQualificationRequest(
    string Fit,
    string Temperature,
    string Priority,
    string SuggestedNextAction,
    string? Note);


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

public sealed record SalesDealMeetingResponse(Guid Id, Guid LeadId, Guid? DealId, string Title,
    DateTime StartsUtc, DateTime EndsUtc, string TimeZoneId, string Status, Guid? BrowserRoomId = null);

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

public sealed record ConvertLeadRequest(decimal Amount, string Currency, DateTime? ExpectedCloseUtc, string? Note);

public sealed record SalesPipelineStageResponse(Guid StageId, string Name, int DisplayOrder, decimal TotalValue, int DealCount, IReadOnlyList<SalesDealSummaryResponse> Deals);


public sealed record SalesActionRequest(string? Note);


public sealed record RevenueForecastSnapshotDto(
    Guid Id,
    Guid CompanyId,
    DateTime AsOfUtc,
    DateTime CalculatedUtc,
    string Currency,
    IReadOnlyList<RevenueForecastWindowDto> Windows,
    RiskDistributionSummary RiskDistribution);
