using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanySelectionService : ICompanySelectionService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditEventWriter _audit;

    public CompanySelectionService(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUser,
        IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<ResolvedCompanyContextDto?> SelectAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        var userId = _currentUser.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("An authenticated user is required.");
        }

        var membership = await _dbContext.CompanyMemberships
            .AsNoTracking()
            .Where(item => item.UserId == userId &&
                           item.CompanyId == companyId &&
                           item.Status == CompanyMembershipStatus.Active)
            .Select(item => new ResolvedCompanyContextDto(
                item.Id,
                item.CompanyId,
                item.Company.Name,
                item.Role,
                item.Status,
                item.Company.Timezone,
                item.Company.Currency))
            .SingleOrDefaultAsync(cancellationToken);
        if (membership is null)
        {
            return null;
        }

        var preference = await _dbContext.UserPreferences
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (preference is null)
        {
            preference = new UserPreference(userId.Value, SupportedUserCultures.Default);
            _dbContext.UserPreferences.Add(preference);
        }

        var previousCompanyId = preference.PreferredCompanyId;
        if (!preference.SelectCompany(companyId))
        {
            return membership;
        }

        await _audit.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                userId,
                AuditEventActions.CompanySelected,
                AuditTargetTypes.Company,
                companyId.ToString("N"),
                AuditEventOutcomes.Succeeded,
                "Selected this company as the active workspace.",
                Metadata: new Dictionary<string, string?>
                {
                    ["previousCompanyId"] = previousCompanyId?.ToString("N"),
                    ["selectedCompanyId"] = companyId.ToString("N")
                }),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return membership;
    }
}
