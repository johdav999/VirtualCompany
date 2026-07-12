namespace VirtualCompany.Application.Sales;

public interface ISalesSourceService
{
    Task<SalesSourceTouchDto> RecordAsync(Guid companyId, RecordSalesSourceTouchRequest request, CancellationToken ct);
    Task<SalesSourceTouchDto> StageAsync(Guid companyId, RecordSalesSourceTouchRequest request, CancellationToken ct);
    Task<SalesAttributionDto?> GetAsync(Guid companyId, string subjectType, Guid subjectId, CancellationToken ct);
    Task<IReadOnlyList<SalesSourceTouchDto>> TimelineAsync(Guid companyId, string subjectType, Guid subjectId, CancellationToken ct);
    Task<IReadOnlyList<SalesAcquisitionCampaignDto>> ListCampaignsAsync(Guid companyId, CancellationToken ct);
    Task<SalesAcquisitionCampaignDto> CreateCampaignAsync(Guid companyId, SaveSalesAcquisitionCampaignRequest request, CancellationToken ct);
}

public sealed record RecordSalesSourceTouchRequest(string SubjectType, Guid SubjectId, string Category, string Provider,
    string Channel, string InteractionType, string SourceReference, DateTime? ObservedUtc, string ActorType,
    string? ActorReference, Guid? CampaignId = null, string? Evidence = null, string? LandingPage = null,
    string? Referrer = null, string? UtmSource = null, string? UtmMedium = null, string? UtmCampaign = null,
    string? UtmContent = null, string? UtmTerm = null, decimal? Cost = null, string? Currency = null,
    string? MetadataJson = null, bool IsConversion = false);
public sealed record SalesSourceTouchDto(Guid Id, string Category, string Provider, string Channel, string InteractionType,
    string SourceReference, string? Evidence, DateTime ObservedUtc, Guid? CampaignId, decimal? Cost, string? Currency);
public sealed record SalesAttributionDto(Guid SubjectId, string SubjectType, Guid OriginalTouchId, Guid FirstTouchId,
    Guid LastTouchId, Guid? ConversionTouchId, int TouchCount, decimal TotalAcquisitionCost, string? Currency,
    IReadOnlyList<SalesSourceTouchDto> Timeline);
public sealed record SaveSalesAcquisitionCampaignRequest(string Name, string Category, string Provider, string? ExternalReference, decimal? Budget, string? Currency, DateTime? StartsUtc, DateTime? EndsUtc);
public sealed record SalesAcquisitionCampaignDto(Guid Id, string Name, string Category, string Provider, string? ExternalReference, decimal? Budget, string? Currency, DateTime? StartsUtc, DateTime? EndsUtc, string Status);
