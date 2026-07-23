using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPersistenceRepository : ISalesPersistenceRepository
{
    private readonly VirtualCompanyDbContext _dbContext;

    public SalesPersistenceRepository(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Lead>> ListLeadsAsync(Guid companyId, string? status, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);

        var query = _dbContext.Leads
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == normalizedStatus);
        }

        return await query
            .OrderByDescending(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Lead?> GetLeadAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureId(leadId, nameof(leadId));

        return await _dbContext.Leads
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == leadId && !x.IsDeleted, cancellationToken);
    }

    public async Task AddLeadAsync(Lead lead, CancellationToken cancellationToken)
    {
        EnsureCompany(lead.CompanyId);
        await ValidateStageAsync(lead.CompanyId, lead.PipelineStageId, cancellationToken);
        await ValidateOptionalTenantEntityAsync(_dbContext.Contacts, lead.CompanyId, lead.PrimaryContactId, nameof(lead.PrimaryContactId), cancellationToken);
        await ValidateOptionalTenantEntityAsync(_dbContext.CustomerCompanies, lead.CompanyId, lead.CustomerCompanyId, nameof(lead.CustomerCompanyId), cancellationToken);
        await _dbContext.Leads.AddAsync(lead, cancellationToken);
    }

    public async Task AddDealAsync(Deal deal, CancellationToken cancellationToken)
    {
        EnsureCompany(deal.CompanyId);
        await ValidateStageAsync(deal.CompanyId, deal.PipelineStageId, cancellationToken);
        await ValidateOptionalTenantEntityAsync(_dbContext.Leads, deal.CompanyId, deal.SourceLeadId, nameof(deal.SourceLeadId), cancellationToken);
        await ValidateOptionalTenantEntityAsync(_dbContext.Contacts, deal.CompanyId, deal.PrimaryContactId, nameof(deal.PrimaryContactId), cancellationToken);
        await ValidateOptionalTenantEntityAsync(_dbContext.CustomerCompanies, deal.CompanyId, deal.CustomerCompanyId, nameof(deal.CustomerCompanyId), cancellationToken);
        await _dbContext.Deals.AddAsync(deal, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private async Task ValidateStageAsync(Guid companyId, Guid stageId, CancellationToken cancellationToken)
    {
        EnsureId(stageId, nameof(stageId));

        var valid = await _dbContext.SalesPipelineStages
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.Id == stageId &&
                     !x.IsDeleted &&
                     x.IsActive &&
                     (x.CompanyId == SalesPipelineStage.SystemCompanyId || x.CompanyId == companyId),
                cancellationToken);

        if (!valid)
        {
            throw new InvalidOperationException("Sales pipeline stage is not available for this company.");
        }
    }

    private static async Task ValidateOptionalTenantEntityAsync<TEntity>(
        DbSet<TEntity> set,
        Guid companyId,
        Guid? entityId,
        string name,
        CancellationToken cancellationToken)
        where TEntity : class, ICompanyOwnedEntity
    {
        if (!entityId.HasValue)
        {
            return;
        }

        var exists = await set
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.CompanyId == companyId &&
                     EF.Property<Guid>(x, "Id") == entityId.Value &&
                     !EF.Property<bool>(x, "IsDeleted"), cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException($"{name} does not belong to this company.");
        }
    }

    private static void EnsureCompany(Guid companyId) =>
        _ = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;

    private static void EnsureId(Guid id, string name) =>
        _ = id == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : id;
}