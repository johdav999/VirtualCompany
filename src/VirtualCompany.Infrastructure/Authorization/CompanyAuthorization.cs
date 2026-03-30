using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Authorization;

public sealed class CompanyMembershipRequirement : IAuthorizationRequirement
{
}

public sealed class CompanyRoleRequirement : IAuthorizationRequirement
{
    public CompanyRoleRequirement(params CompanyMembershipRole[] allowedRoles)
    {
        AllowedRoles = allowedRoles.ToHashSet();
    }

    public IReadOnlySet<CompanyMembershipRole> AllowedRoles { get; }
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

public sealed class CompanyRoleAuthorizationHandler
    : AuthorizationHandler<CompanyRoleRequirement>
{
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;

    public CompanyRoleAuthorizationHandler(
        ICompanyMembershipContextResolver companyMembershipContextResolver)
    {
        _companyMembershipContextResolver = companyMembershipContextResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyRoleRequirement requirement)
    {
        var companyContext = await _companyMembershipContextResolver.ResolveAsync(CancellationToken.None);
        if (companyContext is not null && requirement.AllowedRoles.Contains(companyContext.Role))
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class CompanyRoleResourceAuthorizationHandler
    : AuthorizationHandler<CompanyRoleRequirement, Guid>
{
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;

    public CompanyRoleResourceAuthorizationHandler(
        ICompanyMembershipContextResolver companyMembershipContextResolver)
    {
        _companyMembershipContextResolver = companyMembershipContextResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyRoleRequirement requirement,
        Guid companyId)
    {
        var companyContext = await _companyMembershipContextResolver.ResolveAsync(companyId, CancellationToken.None);
        if (companyContext is not null && requirement.AllowedRoles.Contains(companyContext.Role))
        {
            context.Succeed(requirement);
        }
    }
}