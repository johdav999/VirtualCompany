namespace VirtualCompany.Application.Marketing;

public sealed record MarketingOperationalFilter(DateTime FromUtc, DateTime ToUtc, Guid? CampaignId = null, string? Currency = null, string? State = null);
public sealed record MarketingCampaignEvidence(Guid Id, string Name, string State, string Audience, DateTime? LaunchUtc,
    DateTime UpdatedUtc, decimal? Budget, string? Currency, decimal? KnownSpend, int UnknownCostTouches,
    decimal? ObservedLeads, int AttributedSubjects, int OutcomeSubjects, IReadOnlyList<string> Gaps);
public sealed record MarketingDeliveryEvidence(Guid Id, Guid? CampaignId, Guid? BriefId, string State, DateTime? ScheduledUtc,
    DateTime UpdatedUtc, int Attempts, Guid? ApprovalId, string? ProviderReference, string? FailureCode);
public sealed record MarketingSpendEvidence(Guid Id, Guid CampaignId, DateTime OccurredUtc, decimal? Cost, string? Currency, string SourceReference);
public sealed record MarketingLeadEvidence(Guid Id, Guid CampaignId, decimal Value, string Unit, string Classification,
    string SourceReference, DateTime ObservedUtc);
public sealed record MarketingOperationalReport(Guid CompanyId, DateTime AsOfUtc, MarketingOperationalFilter Filter,
    string CalculationVersion, IReadOnlyList<MarketingCampaignEvidence> Campaigns, IReadOnlyList<MarketingDeliveryEvidence> Deliveries,
    IReadOnlyList<MarketingSpendEvidence> Spend, IReadOnlyList<MarketingLeadEvidence> Leads, IReadOnlyList<string> Coverage);
public sealed record MarketingCampaignReview(Guid CompanyId, DateTime AsOfUtc, MarketingCampaignEvidence? Campaign,
    IReadOnlyList<MarketingContentBriefDto> Content, IReadOnlyList<MarketingCreativeAssetDto> Assets,
    IReadOnlyList<MarketingDeliveryEvidence> Deliveries, IReadOnlyList<MarketingReviewAudience> Audiences);
public sealed record MarketingReviewAudience(Guid Id, int Version, string State, string Evidence, string MissingEvidence);
public interface IMarketingOperationalReportService
{
    Task<MarketingOperationalReport> GetAsync(Guid companyId, MarketingOperationalFilter filter, CancellationToken ct);
    Task<MarketingCampaignReview> GetReviewAsync(Guid companyId, Guid? campaignId, Guid? briefId, CancellationToken ct);
}
