using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

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