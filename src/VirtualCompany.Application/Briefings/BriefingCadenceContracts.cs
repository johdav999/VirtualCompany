namespace VirtualCompany.Application.Briefings;
public sealed record BriefingCadenceDeliveryRequest(Guid CompanyId, Guid DeliveryId);
public interface IBriefingCadenceService
{
    Task<BriefingCadenceContext> GetAsync(Guid companyId, CancellationToken ct);
    Task<BriefingCadenceContext> SaveAsync(Guid companyId, BriefingCadenceSettings settings, CancellationToken ct);
    Task<BriefingCadencePreview> PreviewAsync(Guid companyId, CancellationToken ct);
    Task<BriefingCadencePreview> OpenAsync(Guid companyId, Guid deliveryId, CancellationToken ct);
    Task<int> ScheduleDueAsync(Guid companyId, DateTime nowUtc, CancellationToken ct);
    Task<CompanyBriefingGenerationResult> GenerateAsync(BriefingGenerationJobContext job, CancellationToken ct);
    Task DeliverAsync(BriefingCadenceDeliveryRequest request, CancellationToken ct);
}
