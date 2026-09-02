using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyTodayWorkspaceLensResolver : ITodayWorkspaceLensResolver
{
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuthorizationService _authorization;

    public CompanyTodayWorkspaceLensResolver(
        VirtualCompanyDbContext db,
        ICompanyMembershipContextResolver memberships,
        ICurrentUserAccessor currentUser,
        IAuthorizationService authorization)
    {
        _db = db;
        _memberships = memberships;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    public async Task<TodayWorkspaceLensResolution> ResolveAsync(
        Guid companyId,
        string? requestedLens,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));

        var normalizedRequest = TodayWorkspaceLenses.Normalize(requestedLens);
        if (normalizedRequest.Length > 0 && !TodayWorkspaceLenses.All.Contains(normalizedRequest))
        {
            throw new ArgumentException(
                $"Unsupported Today lens '{requestedLens}'. Allowed values: {string.Join(", ", TodayWorkspaceLenses.Ordered)}.",
                nameof(requestedLens));
        }

        var membership = await _memberships.ResolveAsync(companyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Active company membership is required.");
        if (_currentUser.UserId is not Guid currentUserId || currentUserId != membership.UserId)
        {
            throw new UnauthorizedAccessException("The Today workspace is not available for the current user.");
        }

        var rows = await _db.CompanyResponsibilityAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ResponsibilityArea)
            .ThenBy(x => x.AssignmentKind)
            .ThenBy(x => x.Id)
            .Select(x => new AssignmentRow(
                x.Id,
                x.ResponsibilityArea,
                x.AssignmentKind,
                x.AssignedMembershipId,
                x.AssignedMembership.User != null ? x.AssignedMembership.User.DisplayName : x.AssignedMembership.InvitedEmail ?? "Company member",
                x.PrimaryAgent != null ? x.PrimaryAgent.DisplayName : null,
                x.PrimaryAgentId,
                x.Version,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        var canViewFinance = (await _authorization.AuthorizeAsync(
            _currentUser.Principal,
            companyId,
            CompanyPolicies.FinanceView)).Succeeded;
        var canManageResponsibilities = (await _authorization.AuthorizeAsync(
            _currentUser.Principal,
            companyId,
            CompanyPolicies.CompanyOwnerOrAdmin)).Succeeded;
        var canRequestReview = (await _authorization.AuthorizeAsync(
            _currentUser.Principal,
            companyId,
            CompanyPolicies.CompanyManager)).Succeeded;

        var accesses = rows.Count == 0
            ? BuildFallback(membership.MembershipRole, membership.MembershipId, membership.UserId, canViewFinance)
            : BuildConfigured(rows, membership.MembershipId, membership.UserId, canViewFinance);

        if (accesses.Count == 0)
        {
            accesses.Add(MemberCompanyAccess(membership.MembershipId));
        }

        var ordered = accesses
            .GroupBy(x => x.Lens, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => x.IsPrimary)
                .ThenByDescending(x => x.IsExecutiveOversight)
                .First())
            .OrderBy(x => TodayWorkspaceLenses.Ordered.ToList().IndexOf(x.Lens))
            .ToList();

        var defaultLens = ordered.FirstOrDefault(x => x.Lens == TodayWorkspaceLenses.Company)?.Lens
            ?? ordered[0].Lens;
        var activeLens = normalizedRequest.Length == 0 ? defaultLens : normalizedRequest;
        if (!ordered.Any(x => string.Equals(x.Lens, activeLens, StringComparison.OrdinalIgnoreCase)))
        {
            activeLens = defaultLens;
        }

        return new TodayWorkspaceLensResolution(
            companyId,
            membership.UserId,
            membership.MembershipId,
            membership.MembershipRole,
            membership.CompanyName,
            activeLens,
            defaultLens,
            BuildRevision(rows),
            ordered,
            rows.Count > 0,
            canManageResponsibilities,
            canRequestReview);
    }

    private static List<TodayWorkspaceLensAccess> BuildConfigured(
        IReadOnlyList<AssignmentRow> rows,
        Guid membershipId,
        Guid userId,
        bool canViewFinance)
    {
        var mine = rows.Where(x => x.AssignedMembershipId == membershipId).ToList();
        if (mine.Count == 0) return [MemberCompanyAccess(membershipId)];

        var result = new List<TodayWorkspaceLensAccess>();
        foreach (var row in mine)
        {
            var lens = TodayWorkspaceLenses.FromResponsibility(row.Area);
            if (lens == TodayWorkspaceLenses.Finance && !canViewFinance) continue;
            var workingAssignment = row.WorkingAgentId.HasValue
                ? row
                : rows.FirstOrDefault(x => x.Area == row.Area && x.Kind == ResponsibilityAssignmentKind.Primary && x.WorkingAgentId.HasValue);
            result.Add(new TodayWorkspaceLensAccess(
                lens,
                TodayWorkspaceLenses.Label(lens),
                row.Kind == ResponsibilityAssignmentKind.Primary ? "Primary responsibility" : "Executive oversight",
                row.Kind == ResponsibilityAssignmentKind.Primary,
                row.Kind == ResponsibilityAssignmentKind.ExecutiveOversight,
                row.AssignedMembershipId,
                string.IsNullOrWhiteSpace(row.ResponsiblePerson) ? $"User {userId:N}" : row.ResponsiblePerson,
                workingAssignment?.WorkingAgent,
                workingAssignment?.WorkingAgentId));
        }

        if (mine.Any(x => x.Kind == ResponsibilityAssignmentKind.ExecutiveOversight) &&
            result.All(x => x.Lens != TodayWorkspaceLenses.Company))
        {
            result.Add(new TodayWorkspaceLensAccess(
                TodayWorkspaceLenses.Company,
                TodayWorkspaceLenses.Label(TodayWorkspaceLenses.Company),
                "Executive oversight",
                false,
                true,
                membershipId,
                mine.First().ResponsiblePerson,
                null));
        }

        return result;
    }

    private static List<TodayWorkspaceLensAccess> BuildFallback(
        CompanyMembershipRole role,
        Guid membershipId,
        Guid userId,
        bool canViewFinance)
    {
        var responsible = role.ToDisplayName();
        IEnumerable<string> lenses = role switch
        {
            CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager =>
                TodayWorkspaceLenses.Ordered,
            CompanyMembershipRole.FinanceApprover or CompanyMembershipRole.Accountant =>
                [TodayWorkspaceLenses.Finance],
            CompanyMembershipRole.SupportSupervisor => [TodayWorkspaceLenses.Customers],
            _ => [TodayWorkspaceLenses.Company]
        };

        return lenses
            .Where(lens => lens != TodayWorkspaceLenses.Finance || canViewFinance)
            .Select(lens => new TodayWorkspaceLensAccess(
                lens,
                TodayWorkspaceLenses.Label(lens),
                "Membership role fallback because responsibilities are not configured",
                false,
                role is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin,
                membershipId,
                string.IsNullOrWhiteSpace(responsible) ? $"User {userId:N}" : responsible,
                null))
            .ToList();
    }

    private static TodayWorkspaceLensAccess MemberCompanyAccess(Guid membershipId) => new(
        TodayWorkspaceLenses.Company,
        TodayWorkspaceLenses.Label(TodayWorkspaceLenses.Company),
        "Active company member; no direct responsibility is assigned",
        false,
        false,
        membershipId,
        "Company team",
        null);

    private static string BuildRevision(IReadOnlyList<AssignmentRow> rows)
    {
        if (rows.Count == 0) return "unconfigured";
        var input = string.Join('|', rows.Select(x => $"{x.Id:N}:{x.Version}:{x.UpdatedUtc.Ticks}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant()[..16];
    }

    private sealed record AssignmentRow(
        Guid Id,
        ResponsibilityArea Area,
        ResponsibilityAssignmentKind Kind,
        Guid AssignedMembershipId,
        string ResponsiblePerson,
        string? WorkingAgent,
        Guid? WorkingAgentId,
        long Version,
        DateTime UpdatedUtc);
}
