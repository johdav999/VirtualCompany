using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceReadService
{
    public async Task<IReadOnlyList<FinanceBudgetDto>> GetBudgetsAsync(
        GetFinanceBudgetsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var startUtc = NormalizePlanningPeriod(query.PeriodStartUtc);
        var endExclusiveUtc = query.PeriodEndUtc.HasValue
            ? NormalizePlanningPeriod(query.PeriodEndUtc.Value).AddMonths(1)
            : startUtc.AddMonths(1);
        if (endExclusiveUtc <= startUtc)
        {
            throw new ArgumentException("Period end must be the same month or after period start.", nameof(query));
        }

        var budgets = _dbContext.Budgets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.PeriodStartUtc >= startUtc && x.PeriodStartUtc < endExclusiveUtc);

        if (!string.IsNullOrWhiteSpace(query.Version))
        {
            var normalizedVersion = query.Version.Trim();
            budgets = budgets.Where(x => x.Version == normalizedVersion);
        }

        if (query.FinanceAccountId.HasValue)
        {
            budgets = budgets.Where(x => x.FinanceAccountId == query.FinanceAccountId.Value);
        }

        if (query.CostCenterId.HasValue)
        {
            budgets = budgets.Where(x => x.CostCenterId == query.CostCenterId.Value);
        }

        try
        {
            return await budgets
                .OrderBy(x => x.PeriodStartUtc)
                .ThenBy(x => x.FinanceAccount.Code)
                .ThenBy(x => x.Version)
                .Select(x => new FinanceBudgetDto(
                    x.Id,
                    x.CompanyId,
                    x.FinanceAccountId,
                    x.FinanceAccount.Code,
                    x.FinanceAccount.Name,
                    x.PeriodStartUtc,
                    x.Version,
                    x.CostCenterId,
                    x.Amount,
                    x.Currency,
                    x.CreatedUtc,
                    x.UpdatedUtc))
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (IsMissingPlanningSchemaTable(ex))
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<FinanceForecastDto>> GetForecastsAsync(
        GetFinanceForecastsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var startUtc = NormalizePlanningPeriod(query.PeriodStartUtc);
        var endExclusiveUtc = NormalizePlanningPeriod(query.PeriodEndUtc).AddMonths(1);
        if (endExclusiveUtc <= startUtc)
        {
            throw new ArgumentException("Forecast period end must be the same month or after period start.", nameof(query));
        }

        var forecasts = _dbContext.Forecasts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.PeriodStartUtc >= startUtc && x.PeriodStartUtc < endExclusiveUtc);

        if (query.FinanceAccountId.HasValue)
        {
            forecasts = forecasts.Where(x => x.FinanceAccountId == query.FinanceAccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Version))
        {
            var normalizedVersion = query.Version.Trim();
            forecasts = forecasts.Where(x => x.Version == normalizedVersion);
        }

        try
        {
            return await forecasts
                .OrderBy(x => x.PeriodStartUtc)
                .ThenBy(x => x.FinanceAccount.Code)
                .ThenBy(x => x.Version)
                .Select(x => new FinanceForecastDto(
                    x.Id,
                    x.CompanyId,
                    x.FinanceAccountId,
                    x.FinanceAccount.Code,
                    x.FinanceAccount.Name,
                    x.PeriodStartUtc,
                    x.Version,
                    x.CostCenterId,
                    x.Amount,
                    x.Currency,
                    x.CreatedUtc,
                    x.UpdatedUtc))
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (IsMissingPlanningSchemaTable(ex))
        {
            return [];
        }
    }

    private static DateTime NormalizePlanningPeriod(DateTime value)
    {
        if (value == default)
        {
            throw new ArgumentException("Planning period is required.", nameof(value));
        }

        var normalized = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTime(normalized.Year, normalized.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
