using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Tenancy;

public interface ICompanyMembershipContextResolver
{
    Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken);
    Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid companyId, CancellationToken cancellationToken);
}

public sealed class CompanyMembershipContextResolver : ICompanyMembershipContextResolver
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ICompanyContextAccessor _companyContextAccessor;

    public CompanyMembershipContextResolver(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUserAccessor,
        ICompanyContextAccessor companyContextAccessor)
    {
        _dbContext = dbContext;
        _currentUserAccessor = currentUserAccessor;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_companyContextAccessor.CompanyId is not Guid companyId)
        {
            return null;
        }

        return await ResolveAsync(companyId, cancellationToken);
    }

    public async Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (_currentUserAccessor.UserId is not Guid userId)
        {
            if (_companyContextAccessor.CompanyId == companyId)
            {
                _companyContextAccessor.SetCompanyContext(null);
            }

            return null;
        }

        // Human tenant membership role is resolved from the current persisted membership so role
        // changes affect human authorization only and are not reused as agent capability grants.
        // changes take effect on the next authorization check without trusting stale request state.
        var membership = await _dbContext.CompanyMemberships.AsNoTracking()
            .Where(x => x.UserId == userId && x.CompanyId == companyId && x.Status == CompanyMembershipStatus.Active)
            .Select(x => new ResolvedCompanyMembershipContext(x.Id, x.CompanyId, x.UserId!.Value, x.Company.Name, x.Role, x.Status, x.Company.Timezone, x.Company.Currency, x.Company.SizeBand))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (membership?.MembershipRole == CompanyMembershipRole.Accountant)
        {
            var now = DateTime.UtcNow;
            var hasEffectiveGrant = await _dbContext.AccountantCompanyGrants.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.MembershipId == membership.MembershipId &&
                    x.AccountantUserId == userId && x.Status == AccountantGrantStatuses.Active &&
                    x.EffectiveFromUtc <= now && (!x.EffectiveUntilUtc.HasValue || x.EffectiveUntilUtc > now),
                    cancellationToken).ConfigureAwait(false);
            if (!hasEffectiveGrant) membership = null;
        }

        if (_companyContextAccessor.CompanyId == companyId)
        {
            _companyContextAccessor.SetCompanyContext(membership);
        }

        return membership;
    }
}
