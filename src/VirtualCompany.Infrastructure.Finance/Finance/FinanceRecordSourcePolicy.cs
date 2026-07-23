using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class FinanceRecordSourcePolicy(VirtualCompanyDbContext dbContext)
{
    public IQueryable<TEntity> ApplyFilter<TEntity>(
        IQueryable<TEntity> source,
        Guid companyId,
        string? sourceFilter,
        params string[] externalEntityTypes)
        where TEntity : class
    {
        var normalized = FinanceDataSources.Normalize(sourceFilter);
        if (normalized == FinanceDataSources.All)
        {
            return source;
        }

        var fortnoxReferenceIds = dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .Where(reference =>
                reference.CompanyId == companyId &&
                reference.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                externalEntityTypes.Contains(reference.EntityType))
            .Select(reference => reference.InternalRecordId);

        return normalized switch
        {
            FinanceDataSources.Fortnox => source.Where(x =>
                EF.Property<string>(x, "SourceType") == FinanceRecordSourceTypes.Fortnox ||
                EF.Property<string?>(x, "ProviderKey") == FinanceIntegrationProviderKeys.Fortnox ||
                fortnoxReferenceIds.Contains(EF.Property<Guid>(x, "Id"))),
            FinanceDataSources.Simulation => source.Where(x =>
                EF.Property<string>(x, "SourceType") == FinanceRecordSourceTypes.Simulation &&
                !fortnoxReferenceIds.Contains(EF.Property<Guid>(x, "Id"))),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sourceFilter),
                sourceFilter,
                "Source filter must be all, fortnox, or simulation.")
        };
    }

    public async Task<HashSet<Guid>> LoadFortnoxReferenceIdsAsync(
        Guid companyId,
        IReadOnlyCollection<string> entityTypes,
        IEnumerable<Guid> internalRecordIds,
        CancellationToken cancellationToken)
    {
        var ids = internalRecordIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0 || entityTypes.Count == 0)
        {
            return [];
        }

        var referenceIds = await dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(reference =>
                reference.CompanyId == companyId &&
                reference.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                entityTypes.Contains(reference.EntityType) &&
                ids.Contains(reference.InternalRecordId))
            .Select(reference => reference.InternalRecordId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return referenceIds.ToHashSet();
    }

    public Task<bool> HasFortnoxReferenceAsync(
        Guid companyId,
        IReadOnlyCollection<string> entityTypes,
        Guid internalRecordId,
        CancellationToken cancellationToken)
    {
        if (internalRecordId == Guid.Empty || entityTypes.Count == 0)
        {
            return Task.FromResult(false);
        }

        return dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(reference =>
                reference.CompanyId == companyId &&
                reference.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                entityTypes.Contains(reference.EntityType) &&
                reference.InternalRecordId == internalRecordId,
                cancellationToken);
    }

    public static string ResolveSource(string sourceType, string? providerKey, bool hasFortnoxReference) =>
        hasFortnoxReference ||
        string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sourceType, FinanceRecordSourceTypes.Fortnox, StringComparison.OrdinalIgnoreCase)
            ? FinanceDataSources.Fortnox
            : string.Equals(sourceType, FinanceRecordSourceTypes.Simulation, StringComparison.OrdinalIgnoreCase)
                ? FinanceDataSources.Simulation
                : FinanceDataSources.Manual;
}
