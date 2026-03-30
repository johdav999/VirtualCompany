using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyQueryService : ICurrentUserCompanyService, ICompanyNoteService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public CompanyQueryService(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUserAccessor)
    {
        _dbContext = dbContext;
        _currentUserAccessor = currentUserAccessor;
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        if (_currentUserAccessor.UserId is not Guid userId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new CurrentUserDto(x.Id, x.Email, x.DisplayName, x.AuthProvider, x.AuthSubject))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyMembershipDto>> GetMembershipsAsync(CancellationToken cancellationToken)
    {
        if (_currentUserAccessor.UserId is not Guid userId)
        {
            return Array.Empty<CompanyMembershipDto>();
        }

        return await _dbContext.CompanyMemberships.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Company.Name)
            .Select(x => new CompanyMembershipDto(
                x.Id,
                x.CompanyId,
                x.Company.Name,
                x.Role,
                x.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyAccessDto?> GetCompanyAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (_currentUserAccessor.UserId is not Guid userId)
        {
            return null;
        }

        return await _dbContext.CompanyMemberships.AsNoTracking()
            .Where(x => x.UserId == userId &&
                        x.CompanyId == companyId &&
                        x.Status == CompanyMembershipStatus.Active)
            .Select(x => new CompanyAccessDto(
                x.CompanyId,
                x.Company.Name,
                x.Role,
                x.Status))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> CanAccessCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (_currentUserAccessor.UserId is not Guid userId)
        {
            return Task.FromResult(false);
        }

        return _dbContext.CompanyMemberships.AsNoTracking().AnyAsync(x =>
            x.UserId == userId &&
            x.CompanyId == companyId &&
            x.Status == CompanyMembershipStatus.Active,
            cancellationToken);
    }

    public async Task<CompanyNoteDto?> GetNoteAsync(Guid companyId, Guid noteId, CancellationToken cancellationToken)
    {
        return await _dbContext.CompanyNotes.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == noteId)
            .Select(x => new CompanyNoteDto(x.Id, x.CompanyId, x.Title, x.Content))
            .SingleOrDefaultAsync(cancellationToken);
    }
}