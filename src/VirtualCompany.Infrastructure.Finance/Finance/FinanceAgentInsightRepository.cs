using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceAgentInsightRepository : IFinanceAgentInsightRepository
{
    private readonly VirtualCompanyDbContext _dbContext;

    public FinanceAgentInsightRepository(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FinanceAgentInsight?> GetByIdentityAsync(
        Guid companyId,
        string checkCode,
        string conditionKey,
        string entityType,
        string entityId,
        CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);

        return await _dbContext.FinanceAgentInsights
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.CheckCode == NormalizeRequired(checkCode, nameof(checkCode)) &&
                     x.ConditionKey == NormalizeRequired(conditionKey, nameof(conditionKey)).ToLowerInvariant() &&
                     x.EntityType == NormalizeRequired(entityType, nameof(entityType)).ToLowerInvariant() &&
                     x.EntityId == NormalizeRequired(entityId, nameof(entityId)),
                cancellationToken);
    }

    public async Task<IReadOnlyList<FinanceAgentInsight>> ListByCheckCodesAsync(
        Guid companyId,
        IReadOnlyList<string> checkCodes,
        CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var normalizedCodes = checkCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedCodes.Length == 0)
        {
            return [];
        }

        return await _dbContext.FinanceAgentInsights
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && normalizedCodes.Contains(x.CheckCode))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FinanceAgentInsight>> ListAsync(
        Guid companyId,
        bool includeResolved,
        CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var query = _dbContext.FinanceAgentInsights
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (!includeResolved)
        {
            query = query.Where(x => x.Status == Domain.Enums.FinanceInsightStatus.Active);
        }

        return await query
            .OrderBy(x => x.Status == Domain.Enums.FinanceInsightStatus.Resolved)
            .ThenByDescending(x => x.UpdatedUtc)
            .ThenBy(x => x.CheckCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FinanceAgentInsight>> QueryAsync(
        GetNormalizedFinanceInsightsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureCompany(query.CompanyId);
        ValidateRange(query.CreatedFromUtc, query.CreatedToUtc, nameof(query.CreatedFromUtc), nameof(query.CreatedToUtc));
        ValidateRange(query.UpdatedFromUtc, query.UpdatedToUtc, nameof(query.UpdatedFromUtc), nameof(query.UpdatedToUtc));

        var insights = _dbContext.FinanceAgentInsights
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = FinanceInsightStatusValues.Parse(query.Status.Trim());
            insights = insights.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Severity))
        {
            var severity = FinancialCheckSeverityValues.Parse(query.Severity.Trim());
            insights = insights.Where(x => x.Severity == severity);
        }

        if (!string.IsNullOrWhiteSpace(query.CheckCode))
        {
            var normalizedCheckCode = NormalizeRequired(query.CheckCode, nameof(query.CheckCode)).ToLowerInvariant();
            insights = insights.Where(x => x.CheckCode == normalizedCheckCode);
        }

        if (query.CreatedFromUtc.HasValue)
        {
            var createdFromUtc = NormalizeUtc(query.CreatedFromUtc.Value);
            insights = insights.Where(x => x.CreatedUtc >= createdFromUtc);
        }

        if (query.CreatedToUtc.HasValue)
        {
            var createdToUtc = NormalizeUtc(query.CreatedToUtc.Value);
            insights = insights.Where(x => x.CreatedUtc <= createdToUtc);
        }

        if (query.UpdatedFromUtc.HasValue)
        {
            var updatedFromUtc = NormalizeUtc(query.UpdatedFromUtc.Value);
            insights = insights.Where(x => x.UpdatedUtc >= updatedFromUtc);
        }

        if (query.UpdatedToUtc.HasValue)
        {
            var updatedToUtc = NormalizeUtc(query.UpdatedToUtc.Value);
            insights = insights.Where(x => x.UpdatedUtc <= updatedToUtc);
        }

        return await ApplySort(insights, query.SortBy, query.SortDirection).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(FinanceAgentInsight insight, CancellationToken cancellationToken) =>
        await _dbContext.FinanceAgentInsights.AddAsync(insight, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    private static string NormalizeRequired(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim();

    private static IQueryable<FinanceAgentInsight> ApplySort(
        IQueryable<FinanceAgentInsight> insights,
        string? sortBy,
        string? sortDirection)
    {
        var normalizedSortBy = FinanceInsightSortFields.Normalize(sortBy);
        var normalizedSortDirection = string.IsNullOrWhiteSpace(sortDirection)
            ? FinanceInsightSortDirections.Desc
            : sortDirection.Trim().ToLowerInvariant();
        var descending = normalizedSortDirection switch
        {
            FinanceInsightSortDirections.Asc => false,
            FinanceInsightSortDirections.Desc => true,
            _ => throw new ArgumentOutOfRangeException(nameof(sortDirection), sortDirection, "SortDirection must be asc or desc.")
        };

        return (normalizedSortBy, descending) switch
        {
            (FinanceInsightSortFields.CreatedAt, true) => insights.OrderByDescending(x => x.CreatedUtc).ThenByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id),
            (FinanceInsightSortFields.CreatedAt, false) => insights.OrderBy(x => x.CreatedUtc).ThenBy(x => x.UpdatedUtc).ThenBy(x => x.Id),
            (_, true) => insights.OrderByDescending(x => x.UpdatedUtc).ThenByDescending(x => x.CreatedUtc).ThenBy(x => x.Id),
            _ => insights.OrderBy(x => x.UpdatedUtc).ThenBy(x => x.CreatedUtc).ThenBy(x => x.Id)
        };
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static void ValidateRange(DateTime? fromUtc, DateTime? toUtc, string fromName, string toName)
    {
        if (fromUtc.HasValue && toUtc.HasValue && NormalizeUtc(fromUtc.Value) > NormalizeUtc(toUtc.Value))
        {
            throw new ArgumentException($"{fromName} must be earlier than or equal to {toName}.");
        }
    }
}