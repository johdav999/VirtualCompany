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
    Task<SalesDealDetailResponse?> LinkDealCustomerCompanyAsync(Guid companyId, Guid userId, Guid dealId, LinkDealCustomerCompanyRequest request, CancellationToken cancellationToken);
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
    Task<RevenueForecastSnapshotDto> CalculateForecastAsync(Guid companyId, DateTime asOfUtc, CancellationToken cancellationToken);
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

public interface ISalesLeadEmailEvidenceService
{
    Task<IReadOnlyList<SalesLeadSourceEmailResponse>> ListAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken);
}

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
