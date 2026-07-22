using VirtualCompany.Application.Orchestration;

namespace VirtualCompany.Application.Communication;

public interface ICommunicationLanguageService
{
    Task<CommunicationLanguagePreferenceDto?> GetContactAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken);
    Task<CommunicationLanguagePreferenceDto?> UpdateContactAsync(Guid companyId, Guid userId, Guid contactId, UpdateCommunicationLanguageRequest request, CancellationToken cancellationToken);
    Task<CommunicationLanguagePreferenceDto?> GetCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken);
    Task<CommunicationLanguagePreferenceDto?> UpdateCampaignAsync(Guid companyId, Guid userId, Guid campaignId, UpdateCommunicationLanguageRequest request, CancellationToken cancellationToken);
    Task<CommunicationLanguagePreferenceDto?> GetSupportCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken);
    Task<CommunicationLanguagePreferenceDto?> UpdateSupportCaseAsync(Guid companyId, Guid userId, Guid supportCaseId, UpdateCommunicationLanguageRequest request, CancellationToken cancellationToken);
    Task<CommunicationLanguageResolution> ResolveAsync(Guid companyId, Guid? contactId, Guid? supportCaseId, Guid? campaignId, CancellationToken cancellationToken);
}

public sealed record UpdateCommunicationLanguageRequest(string? LanguageTag);

public sealed record CommunicationLanguagePreferenceDto(
    Guid TargetId,
    string TargetType,
    string? LanguageTag,
    DateTime UpdatedUtc);

public sealed class CommunicationLanguageValidationException : Exception
{
    public CommunicationLanguageValidationException(string message)
        : base(message)
    {
    }
}
