using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Sales;

public interface IConversionAnalyticsService
{
    Task<SalesMessagePerformanceDto> RecordMessagePerformanceEventAsync(RecordMessagePerformanceEventCommand command, CancellationToken cancellationToken);
    Task<SalesMessagePerformanceDto?> RecordDealCreatedForContactAsync(Guid companyId, Guid contactId, Guid dealId, DateTime occurredUtc, CancellationToken cancellationToken);
    Task<CampaignPerformanceSummaryDto?> GetCampaignPerformanceAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken);
    Task<SequencePerformanceSummaryDto?> GetSequencePerformanceAsync(Guid companyId, Guid sequenceId, CancellationToken cancellationToken);
    Task<StepPerformanceSummaryDto?> GetStepPerformanceAsync(Guid companyId, Guid sequenceStepId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactPerformanceSummaryDto>> GetContactPerformanceAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VariantPerformanceSummaryDto>> GetVariantPerformanceAsync(Guid companyId, Guid? campaignId, Guid? sequenceId, Guid? sequenceStepId, CancellationToken cancellationToken);
    Task<SalesAnalyticsDashboardDto> GetDashboardAnalyticsAsync(Guid companyId, CancellationToken cancellationToken);
}

public sealed record RecordMessagePerformanceEventCommand(
    Guid CompanyId,
    string MessageKey,
    Guid ContactId,
    ConversionAnalyticsEventType EventType,
    DateTime OccurredUtc,
    Guid? CampaignId = null,
    Guid? SequenceId = null,
    Guid? SequenceStepId = null,
    Guid? SequenceExecutionStepId = null,
    Guid? DealId = null,
    string? Provider = null,
    string? ProviderMessageId = null,
    string? ProviderThreadId = null,
    string? InternetMessageId = null,
    string? VariantKey = null,
    int? StepOrder = null,
    decimal? ExpectedRevenueAmount = null,
    string? ExpectedRevenueCurrency = null,
    DateTime? ExpectedCloseUtc = null,
    decimal? PipelineRiskScore = null,
    DateTime? RiskCalculatedUtc = null);

public sealed record SalesMessagePerformanceDto(
    Guid Id,
    Guid CompanyId,
    string MessageKey,
    Guid? CampaignId,
    Guid? SequenceId,
    Guid? SequenceStepId,
    Guid? SequenceExecutionStepId,
    Guid ContactId,
    Guid? DealId,
    string? VariantKey,
    DateTime? SentAt,
    DateTime? DeliveredAt,
    DateTime? BouncedAt,
    DateTime? OpenedAt,
    DateTime? RepliedAt,
    DateTime? DealCreatedAt,
    DateTime? ConvertedAt,
    decimal? ExpectedRevenueAmount,
    string? ExpectedRevenueCurrency,
    DateTime? ExpectedCloseAt,
    decimal? PipelineRiskScore);

public sealed record PerformanceFunnelCounts(
    int Sent,
    int Delivered,
    int Bounced,
    int Opened,
    int Replied,
    int DealCreated,
    int Converted);

public sealed record PerformanceFunnelRates(
    decimal DeliveryRate,
    decimal OpenRate,
    decimal ReplyRate,
    decimal ConversionRate);

public sealed record RevenueWindowSummary(
    decimal ExpectedRevenue30Days,
    decimal ExpectedRevenue60Days,
    decimal ExpectedRevenue90Days,
    string? Currency);

public sealed record RiskDistributionSummary(
    int Unknown,
    int Low,
    int Medium,
    int High);

public sealed record CampaignPerformanceSummaryDto(
    Guid CompanyId,
    Guid CampaignId,
    PerformanceFunnelCounts Counts,
    PerformanceFunnelRates Rates,
    RevenueWindowSummary RevenueWindows,
    RiskDistributionSummary RiskDistribution);

public sealed record SequencePerformanceSummaryDto(
    Guid CompanyId,
    Guid SequenceId,
    PerformanceFunnelCounts Counts,
    PerformanceFunnelRates Rates);

public sealed record StepPerformanceSummaryDto(
    Guid CompanyId,
    Guid SequenceStepId,
    PerformanceFunnelCounts Counts,
    PerformanceFunnelRates Rates);

public sealed record ContactPerformanceSummaryDto(
    Guid Id,
    string MessageKey,
    Guid? CampaignId,
    Guid? SequenceStepId,
    string? VariantKey,
    PerformanceFunnelCounts Counts,
    DateTime UpdatedUtc);

public sealed record VariantPerformanceSummaryDto(
    Guid? CampaignId,
    Guid? SequenceId,
    Guid? SequenceStepId,
    string VariantKey,
    PerformanceFunnelCounts Counts,
    PerformanceFunnelRates Rates);

public sealed record CampaignPerformanceListItemDto(
    Guid CampaignId,
    string CampaignName,
    Guid? SequenceId,
    PerformanceFunnelCounts Counts,
    PerformanceFunnelRates Rates);

public sealed record SalesAnalyticsDashboardDto(
    Guid CompanyId,
    PerformanceFunnelCounts Funnel,
    PerformanceFunnelRates Rates,
    IReadOnlyList<CampaignPerformanceListItemDto> Campaigns,
    IReadOnlyList<VariantPerformanceSummaryDto> Variants);
