using System.Security.Claims;

namespace VirtualCompany.Application.Auth;

public static class CurrentUserClaimTypes
{
    public const string UserId = "virtual_company:user_id";
    public const string AuthProvider = "virtual_company:auth_provider";
    public const string AuthSubject = "virtual_company:auth_subject";
}

public sealed record ExternalUserIdentity(
    string Provider,
    string Subject,
    string? Email,
    string? DisplayName);

public interface ICurrentUserAccessor
{
    ClaimsPrincipal Principal { get; }
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
}

public interface IExternalUserIdentityAccessor
{
    ExternalUserIdentity? GetCurrentIdentity();
}

public interface ICompanyContextAccessor
{
    Guid? CompanyId { get; }
    void SetCompanyId(Guid? companyId);
}