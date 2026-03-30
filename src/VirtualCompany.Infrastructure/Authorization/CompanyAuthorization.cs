using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

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
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ICompanyContextAccessor _companyContextAccessor;

    public CompanyMembershipAuthorizationHandler(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUserAccessor,
        ICompanyContextAccessor companyContextAccessor)
    {
        _dbContext = dbContext;
        _currentUserAccessor = currentUserAccessor;
        _companyContextAccessor = companyContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyMembershipRequirement requirement)
    {
        if (_currentUserAccessor.UserId is not Guid userId || _companyContextAccessor.CompanyId is not Guid companyId)
        {
            return;
        }

        var hasMembership = await _dbContext.CompanyMemberships.AsNoTracking().AnyAsync(x =>
            x.UserId == userId &&
            x.CompanyId == companyId &&
            x.Status == CompanyMembershipStatus.Active);

        if (hasMembership)
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class CompanyRoleAuthorizationHandler
    : AuthorizationHandler<CompanyRoleRequirement>
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ICompanyContextAccessor _companyContextAccessor;

    public CompanyRoleAuthorizationHandler(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUserAccessor,
        ICompanyContextAccessor companyContextAccessor)
    {
        _dbContext = dbContext;
        _currentUserAccessor = currentUserAccessor;
        _companyContextAccessor = companyContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompanyRoleRequirement requirement)
    {
        if (_currentUserAccessor.UserId is not Guid userId || _companyContextAccessor.CompanyId is not Guid companyId)
        {
            return;
        }

        var hasRequiredRole = await _dbContext.CompanyMemberships.AsNoTracking().AnyAsync(x =>
            x.UserId == userId &&
            x.CompanyId == companyId &&
            x.Status == CompanyMembershipStatus.Active &&
            requirement.AllowedRoles.Contains(x.Role));

        if (hasRequiredRole)
        {
            context.Succeed(requirement);
        }
    }
}