using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Authorization;

public sealed class CompanyMembershipRequirement : IAuthorizationRequirement
{
}

public sealed class CompanyMembershipRoleRequirement : IAuthorizationRequirement
{
    public CompanyMembershipRoleRequirement(params CompanyMembershipRole[] allowedMembershipRoles)
    {
        AllowedMembershipRoles = allowedMembershipRoles.ToHashSet();
    }

    // These roles govern human company access only.
    // Agent execution permissions belong to dedicated agent policy components.
    public IReadOnlySet<CompanyMembershipRole> AllowedMembershipRoles { get; }
}

public sealed class CompanyPermissionRequirement : IAuthorizationRequirement
{
    public CompanyPermissionRequirement(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            throw new ArgumentException("Permission is required.", nameof(permission));
        }

        Permission = permission.Trim();
    }

    public string Permission { get; }
}

public sealed class CompanyMembershipAuthorizationHandler
    : AuthorizationHandler<CompanyMembershipRequirement>
{
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;

    public CompanyMembershipAuthorizationHandler(
        ICompanyMembershipContextResolver companyMembershipContextResolver)
    {
        _companyMembershipContextResolver = companyMembershipContextResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyMembershipRequirement requirement)
    {
        var companyContext = await _companyMembershipContextResolver.ResolveAsync(CancellationToken.None);
        if (companyContext is not null)
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class CompanyMembershipResourceAuthorizationHandler
    : AuthorizationHandler<CompanyMembershipRequirement, Guid>
{
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;

    public CompanyMembershipResourceAuthorizationHandler(
        ICompanyMembershipContextResolver companyMembershipContextResolver)
    {
        _companyMembershipContextResolver = companyMembershipContextResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyMembershipRequirement requirement,
        Guid companyId)
    {
        var companyContext = await _companyMembershipContextResolver.ResolveAsync(companyId, CancellationToken.None);
        if (companyContext is not null)
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class CompanyMembershipRoleAuthorizationHandler
    : AuthorizationHandler<CompanyMembershipRoleRequirement>
{
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;

    public CompanyMembershipRoleAuthorizationHandler(
        ICompanyMembershipContextResolver companyMembershipContextResolver)
    {
        _companyMembershipContextResolver = companyMembershipContextResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyMembershipRoleRequirement requirement)
    {
        var companyContext = await _companyMembershipContextResolver.ResolveAsync(CancellationToken.None);
        if (companyContext is not null && requirement.AllowedMembershipRoles.Contains(companyContext.MembershipRole))
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class CompanyPermissionAuthorizationHandler
    : AuthorizationHandler<CompanyPermissionRequirement>
{
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;

    public CompanyPermissionAuthorizationHandler(
        ICompanyMembershipContextResolver companyMembershipContextResolver)
    {
        _companyMembershipContextResolver = companyMembershipContextResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyPermissionRequirement requirement)
    {
        var companyContext = await _companyMembershipContextResolver.ResolveAsync(CancellationToken.None);
        if (companyContext is not null &&
            CompanyPermissionAuthorizationRules.HasPermission(companyContext.MembershipRole, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class CompanyPermissionResourceAuthorizationHandler
    : AuthorizationHandler<CompanyPermissionRequirement, Guid>
{
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;

    public CompanyPermissionResourceAuthorizationHandler(
        ICompanyMembershipContextResolver companyMembershipContextResolver)
    {
        _companyMembershipContextResolver = companyMembershipContextResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyPermissionRequirement requirement,
        Guid companyId)
    {
        var companyContext = await _companyMembershipContextResolver.ResolveAsync(companyId, CancellationToken.None);
        if (companyContext is not null &&
            CompanyPermissionAuthorizationRules.HasPermission(companyContext.MembershipRole, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

internal static class CompanyPermissionAuthorizationRules
{
    public static bool HasPermission(CompanyMembershipRole membershipRole, string permission) =>
        permission switch
        {
            FinancePermissions.SandboxAdmin => FinanceAccess.CanAccessSandboxAdmin(membershipRole.ToStorageValue()),
            FinancePermissions.Approve => FinanceAccess.CanApproveInvoices(membershipRole.ToStorageValue()),
            FinancePermissions.Edit => FinanceAccess.CanEdit(membershipRole.ToStorageValue()),
            FinancePermissions.View => FinanceAccess.CanView(membershipRole.ToStorageValue()),
            _ => false
        };
}

public sealed class CompanyMembershipRoleResourceAuthorizationHandler
    : AuthorizationHandler<CompanyMembershipRoleRequirement, Guid>
{
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;

    public CompanyMembershipRoleResourceAuthorizationHandler(
        ICompanyMembershipContextResolver companyMembershipContextResolver)
    {
        _companyMembershipContextResolver = companyMembershipContextResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyMembershipRoleRequirement requirement,
        Guid companyId)
    {
        var companyContext = await _companyMembershipContextResolver.ResolveAsync(companyId, CancellationToken.None);
        if (companyContext is not null && requirement.AllowedMembershipRoles.Contains(companyContext.MembershipRole))
        {
            context.Succeed(requirement);
        }
    }
}