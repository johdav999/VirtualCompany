using VirtualCompany.Shared;

namespace VirtualCompany.Web.Services;

public enum FinanceAccessStateKind
{
    Allowed = 1,
    Forbidden = 2,
    CompanySelectionRequired = 3
}

public sealed record FinanceAccessState(
    FinanceAccessStateKind Kind,
    Guid? CompanyId,
    string? CompanyName,
    string? MembershipRole,
    string Message)
{
    public bool IsAllowed => Kind == FinanceAccessStateKind.Allowed;
    public bool IsForbidden => Kind == FinanceAccessStateKind.Forbidden;
    public bool RequiresCompanySelection => Kind == FinanceAccessStateKind.CompanySelectionRequired;

    public static FinanceAccessState Allowed(Guid companyId, string companyName, string membershipRole) =>
        new(FinanceAccessStateKind.Allowed, companyId, companyName, membershipRole, string.Empty);

    public static FinanceAccessState Forbidden(string message) =>
        new(FinanceAccessStateKind.Forbidden, null, null, null, message);

    public static FinanceAccessState CompanySelectionRequired(string message) =>
        new(FinanceAccessStateKind.CompanySelectionRequired, null, null, null, message);
}

public sealed class FinanceAccessResolver
{
    public FinanceAccessState Resolve(CurrentUserContextViewModel? currentUser, Guid? requestedCompanyId)
    {
        var activeMemberships = currentUser?.Memberships
            .Where(membership => IsActive(membership.Status))
            .ToList()
            ?? [];
        var financeMemberships = activeMemberships
            .Where(membership => HasFinanceViewAccess(membership.MembershipRole))
            .ToList();

        if (requestedCompanyId is Guid companyId)
        {
            if (currentUser?.ActiveCompany is { } activeCompany &&
                activeCompany.CompanyId == companyId &&
                IsActive(activeCompany.Status))
            {
                return HasFinanceViewAccess(activeCompany.MembershipRole)
                    ? FinanceAccessState.Allowed(activeCompany.CompanyId, activeCompany.CompanyName, activeCompany.MembershipRole)
                    : FinanceAccessState.Forbidden("Finance access requires the finance.view permission for the selected company.");
            }

            var requestedMembership = activeMemberships.FirstOrDefault(membership => membership.CompanyId == companyId);
            if (requestedMembership is null)
            {
                return FinanceAccessState.Forbidden("Finance pages are only available inside a company you can access.");
            }

            return HasFinanceViewAccess(requestedMembership.MembershipRole)
                ? FinanceAccessState.Allowed(
                    requestedMembership.CompanyId,
                    requestedMembership.CompanyName,
                    requestedMembership.MembershipRole)
                : FinanceAccessState.Forbidden("Finance access requires the finance.view permission for the selected company.");
        }

        if (currentUser?.ActiveCompany is { } resolvedCompany && IsActive(resolvedCompany.Status))
        {
            return HasFinanceViewAccess(resolvedCompany.MembershipRole)
                ? FinanceAccessState.Allowed(
                    resolvedCompany.CompanyId,
                    resolvedCompany.CompanyName,
                    resolvedCompany.MembershipRole)
                : FinanceAccessState.Forbidden("Finance access requires the finance.view permission for the active company.");
        }

        if (financeMemberships.Count == 1)
        {
            var membership = financeMemberships[0];
            return FinanceAccessState.Allowed(
                membership.CompanyId,
                membership.CompanyName,
                membership.MembershipRole);
        }

        if (financeMemberships.Count > 1)
        {
            return FinanceAccessState.CompanySelectionRequired(
                "Finance pages stay scoped to the active company. Select a company before opening the finance workspace.");
        }

        return activeMemberships.Count > 0
            ? FinanceAccessState.Forbidden("Finance access requires the finance.view permission for the selected company.")
            : FinanceAccessState.Forbidden("Finance access requires an active company membership.");
    }

    public bool CanShowNavigation(CurrentUserContextViewModel? currentUser) =>
        Resolve(currentUser, null).IsAllowed;

    public string? BuildNavigationHref(CurrentUserContextViewModel? currentUser) =>
        BuildNavigationHref(currentUser, null);

    public string? BuildNavigationHref(CurrentUserContextViewModel? currentUser, Guid? requestedCompanyId)
    {
        var state = Resolve(currentUser, requestedCompanyId);
        return state.IsAllowed
            ? FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, state.CompanyId)
            : null;
    }

    private static bool IsActive(string? status) =>
        string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);

    private static bool HasFinanceViewAccess(string? membershipRole) =>
        FinanceAccess.CanView(membershipRole);
}
