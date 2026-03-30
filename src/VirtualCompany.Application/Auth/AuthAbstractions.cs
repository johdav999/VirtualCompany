using System.Security.Claims;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Auth;

public static class CurrentUserClaimTypes
{
    public const string UserId = "virtual_company:user_id";
    public const string AuthProvider = "virtual_company:auth_provider";
    public const string AuthSubject = "virtual_company:auth_subject";
}

public sealed record ExternalIdentityKey(
    string Provider,
    string Subject);

public sealed record ExternalUserIdentity(
    ExternalIdentityKey Key,
    string? Email,
    string? DisplayName)
{
    public string Provider => Key.Provider;
    public string Subject => Key.Subject;
}

public sealed record AuthenticatedUserIdentity(
    bool IsAuthenticated,
    Guid? UserId,
    ExternalUserIdentity? ExternalIdentity);

public sealed record ResolvedUserIdentity(
    Guid UserId,
    string Email,
    string DisplayName,
    ExternalUserIdentity ExternalIdentity)
{
}

public sealed record ResolvedCompanyMembershipContext(
    Guid MembershipId,
    Guid CompanyId,
    Guid UserId,
    string CompanyName,
    CompanyMembershipRole MembershipRole,
    CompanyMembershipStatus Status);

public interface ICurrentUserAccessor
{
    ClaimsPrincipal Principal { get; }
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    AuthenticatedUserIdentity Current { get; }
}

public interface IExternalUserIdentityAccessor
{
    ExternalUserIdentity? GetCurrentIdentity();
}

public interface IExternalUserIdentityResolver
{
    Task<ResolvedUserIdentity> ResolveAsync(ExternalUserIdentity externalIdentity, CancellationToken cancellationToken);
}

public interface ICompanyContextAccessor
{
    Guid? CompanyId { get; }
    Guid? UserId { get; }
    bool IsResolved { get; }
    ResolvedCompanyMembershipContext? Membership { get; }
    void SetCompanyId(Guid? companyId);
    void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext);
}