namespace VirtualCompany.Web.Services;

public sealed record MarketingOperationalFilterViewModel(DateTime FromUtc, DateTime ToUtc, Guid? CampaignId = null, string? Currency = null, string? State = null);
public sealed record MarketingCampaignEvidenceViewModel(Guid Id, string Name, string State, string Audience, DateTime? LaunchUtc,
    DateTime UpdatedUtc, decimal? Budget, string? Currency, decimal? KnownSpend, int UnknownCostTouches,
    decimal? ObservedLeads, int AttributedSubjects, int OutcomeSubjects, IReadOnlyList<string> Gaps);
public sealed record MarketingDeliveryEvidenceViewModel(Guid Id, Guid? CampaignId, Guid? BriefId, string State, DateTime? ScheduledUtc,
    DateTime UpdatedUtc, int Attempts, Guid? ApprovalId, string? ProviderReference, string? FailureCode);
public sealed record MarketingSpendEvidenceViewModel(Guid Id, Guid CampaignId, DateTime OccurredUtc, decimal? Cost, string? Currency, string SourceReference);
public sealed record MarketingLeadEvidenceViewModel(Guid Id, Guid CampaignId, decimal Value, string Unit, string Classification,
    string SourceReference, DateTime ObservedUtc);
public sealed record MarketingOperationalReportViewModel(Guid CompanyId, DateTime AsOfUtc, MarketingOperationalFilterViewModel Filter,
    string CalculationVersion, IReadOnlyList<MarketingCampaignEvidenceViewModel> Campaigns, IReadOnlyList<MarketingDeliveryEvidenceViewModel> Deliveries,
    IReadOnlyList<MarketingSpendEvidenceViewModel> Spend, IReadOnlyList<MarketingLeadEvidenceViewModel> Leads, IReadOnlyList<string> Coverage);
public sealed record MarketingCampaignReviewViewModel(Guid CompanyId, DateTime AsOfUtc, MarketingCampaignEvidenceViewModel? Campaign,
    IReadOnlyList<MarketingContentBriefViewModel> Content, IReadOnlyList<MarketingCreativeAssetViewModel> Assets,
    IReadOnlyList<MarketingDeliveryEvidenceViewModel> Deliveries, IReadOnlyList<MarketingReviewAudienceViewModel> Audiences);
public sealed record MarketingReviewAudienceViewModel(Guid Id, int Version, string State, string Evidence, string MissingEvidence);

