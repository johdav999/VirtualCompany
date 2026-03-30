using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Companies;

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string AuthProvider,
    string AuthSubject);

public sealed record CompanyMembershipDto(
    Guid MembershipId,
    Guid CompanyId,
    string CompanyName,
    CompanyMembershipRole Role,
    CompanyMembershipStatus Status);

public sealed record ResolvedCompanyContextDto(
    Guid MembershipId,
    Guid CompanyId,
    string CompanyName,
    CompanyMembershipRole Role,
    CompanyMembershipStatus Status);

public sealed record CurrentUserContextDto(
    CurrentUserDto User,
    IReadOnlyList<CompanyMembershipDto> Memberships,
    ResolvedCompanyContextDto? ActiveCompany,
    bool CompanySelectionRequired);

public sealed record CompanyAccessDto(
    Guid CompanyId,
    string CompanyName,
    CompanyMembershipRole Role,
    CompanyMembershipStatus Status);

public sealed record CompanySelectionDto(
    Guid CompanyId,
    string HeaderName,
    string HeaderValue,
    ResolvedCompanyContextDto ActiveCompany);

public sealed record CompanyNoteDto(Guid Id, Guid CompanyId, string Title, string Content);

public interface ICurrentUserCompanyService
{
    Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken);
    Task<CurrentUserContextDto?> GetCurrentUserContextAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyMembershipDto>> GetMembershipsAsync(CancellationToken cancellationToken);
    Task<ResolvedCompanyContextDto?> GetResolvedActiveCompanyAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanyAccessDto?> GetCompanyAccessAsync(Guid companyId, CancellationToken cancellationToken);
    Task<bool> CanAccessCompanyAsync(Guid companyId, CancellationToken cancellationToken);
}

public interface ICompanyNoteService
{
    Task<CompanyNoteDto?> GetNoteAsync(Guid companyId, Guid noteId, CancellationToken cancellationToken);
}