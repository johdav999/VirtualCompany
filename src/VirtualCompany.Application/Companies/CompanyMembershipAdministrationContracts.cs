using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Companies;

public sealed record CompanyMemberDirectoryEntryDto(
    Guid MembershipId,
    Guid CompanyId,
    string CompanyName,
    Guid? UserId,
    string Email,
    string? DisplayName,
    [property: JsonPropertyName("membershipRole")] CompanyMembershipRole MembershipRole,
    CompanyMembershipStatus Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CompanyInvitationDto(
    Guid InvitationId,
    Guid CompanyId,
    string Email,
    [property: JsonPropertyName("membershipRole")] CompanyMembershipRole MembershipRole,
    CompanyInvitationStatus Status,
    Guid InvitedByUserId,
    Guid? AcceptedByUserId,
    DateTime ExpiresAtUtc,
    DateTime? LastSentUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CompanyInvitationDeliveryDto(
    CompanyInvitationDto Invitation,
    string AcceptanceToken,
    bool IsReinvite);

public sealed record InviteUserToCompanyRequest(
    string Email,
    [property: JsonPropertyName("membershipRole")] CompanyMembershipRole MembershipRole);

public sealed record ChangeCompanyMembershipRoleRequest(
    [property: JsonPropertyName("membershipRole")] CompanyMembershipRole MembershipRole);

public sealed record AcceptCompanyInvitationRequest(
    string Token);

public sealed record AcceptCompanyInvitationResultDto(
    Guid CompanyId,
    string CompanyName,
    Guid MembershipId,
    [property: JsonPropertyName("membershipRole")] CompanyMembershipRole MembershipRole,
    CompanyMembershipStatus Status);

public interface ICompanyMembershipAdministrationService
{
    Task<IReadOnlyList<CompanyMemberDirectoryEntryDto>> GetMembershipsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyInvitationDto>> GetInvitationsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanyInvitationDeliveryDto> InviteUserAsync(Guid companyId, InviteUserToCompanyRequest request, CancellationToken cancellationToken);
    Task<CompanyInvitationDeliveryDto> ReinviteUserAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken);
    Task<CompanyInvitationDto> RevokeInvitationAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken);
    Task<CompanyMemberDirectoryEntryDto> ChangeMembershipRoleAsync(Guid companyId, Guid membershipId, ChangeCompanyMembershipRoleRequest request, CancellationToken cancellationToken);
    Task<AcceptCompanyInvitationResultDto> AcceptInvitationAsync(AcceptCompanyInvitationRequest request, CancellationToken cancellationToken);
}

public sealed class CompanyMembershipAdministrationValidationException : Exception
{
    public CompanyMembershipAdministrationValidationException(IDictionary<string, string[]> errors)
        : base("Company membership administration validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(
            new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
