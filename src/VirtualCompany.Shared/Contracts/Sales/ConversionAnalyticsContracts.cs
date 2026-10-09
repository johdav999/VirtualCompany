

namespace VirtualCompany.Application.Sales;


public sealed record PerformanceFunnelRates(
    decimal DeliveryRate,
    decimal OpenRate,
    decimal ReplyRate,
    decimal ConversionRate);


public sealed record RiskDistributionSummary(
    int Unknown,
    int Low,
    int Medium,
    int High);


public sealed record VariantPerformanceSummaryDto(
    Guid? CampaignId,
    Guid? SequenceId,
    Guid? SequenceStepId,
    string VariantKey,
    PerformanceFunnelCounts Counts,
    PerformanceFunnelRates Rates);


public sealed record PerformanceFunnelCounts(
    int Sent,
    int Delivered,
    int Bounced,
    int Opened,
    int Replied,
    int DealCreated,
    int Converted);


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
