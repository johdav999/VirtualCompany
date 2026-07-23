using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceReadService
{
    private static readonly string[] CostCenterFeatureFlagKeys =
    [
        "cost_centers",
        "costcenters",
        "finance_cost_centers",
        "financecostcenters"
    ];

    public async Task<FinanceVarianceResultDto> GetVarianceAsync(
        GetFinanceVarianceQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var comparisonType = FinanceVarianceComparisonTypes.Normalize(query.ComparisonType);
        var periodStartUtc = NormalizePlanningPeriod(query.PeriodStartUtc);
        var periodEndUtc = query.PeriodEndUtc.HasValue
            ? NormalizePlanningPeriod(query.PeriodEndUtc.Value)
            : periodStartUtc;
        var endExclusiveUtc = periodEndUtc.AddMonths(1);
        if (endExclusiveUtc <= periodStartUtc)
        {
            throw new ArgumentException("Variance period end must be the same month or after period start.", nameof(query));
        }

        var normalizedVersion = NormalizeOptionalText(query.Version);
        var includesCostCenters = await AreCostCentersEnabledAsync(query.CompanyId, cancellationToken);
        var comparisonRows = await LoadComparisonRowsAsync(
            query,
            comparisonType,
            periodStartUtc,
            endExclusiveUtc,
            normalizedVersion,
            includesCostCenters,
            cancellationToken);

        if (comparisonRows.Count == 0)
        {
            return new FinanceVarianceResultDto(
                query.CompanyId,
                comparisonType,
                periodStartUtc,
                periodEndUtc,
                normalizedVersion,
                includesCostCenters,
                []);
        }

        List<ActualLedgerRow> actualRows;
        try
        {
            actualRows = await _dbContext.LedgerEntryLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == query.CompanyId &&
                    x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                    (!query.FinanceAccountId.HasValue || x.FinanceAccountId == query.FinanceAccountId.Value) &&
                    (!includesCostCenters || !query.CostCenterId.HasValue || x.CostCenterId == query.CostCenterId.Value))
                .Select(x => new ActualLedgerRow(
                    x.LedgerEntry.PostedAtUtc ?? x.LedgerEntry.EntryUtc,
                    x.FinanceAccountId,
                    NormalizeVarianceCategory(x.FinanceAccount.AccountType),
                    x.CostCenterId,
                    x.DebitAmount - x.CreditAmount,
                    x.Currency))
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (IsMissingLedgerReportingSchemaTable(ex))
        {
            actualRows = [];
        }

        var actualLookup = actualRows
            .Where(x => x.PostedAtUtc >= periodStartUtc && x.PostedAtUtc < endExclusiveUtc)
            .GroupBy(x => new VarianceGroupingKey(
                NormalizePlanningPeriod(x.PostedAtUtc),
                x.FinanceAccountId,
                x.CategoryKey,
                includesCostCenters ? x.CostCenterId : null))
            .ToDictionary(
                group => group.Key,
                group => new AggregatedActualRow(
                    Math.Round(group.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero),
                    ResolveVarianceCurrency(group.Select(x => x.Currency))));

        var comparisonGroups = comparisonRows
            .GroupBy(x => new VarianceGroupingKey(x.PeriodStartUtc, x.FinanceAccountId, x.CategoryKey, x.CostCenterId))
            .Select(group => new AggregatedComparisonRow(
                group.Key,
                group.First().AccountCode,
                group.First().AccountName,
                group.First().CategoryKey,
                group.First().CategoryName,
                group.First().CostCenterId,
                Math.Round(group.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero),
                ResolveVarianceCurrency(group.Select(x => x.Currency))))
            .OrderBy(x => x.Key.PeriodStartUtc)
            .ThenBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.CostCenterId ?? Guid.Empty)
            .ToList();

        var rows = comparisonGroups
            .Select(group =>
            {
                actualLookup.TryGetValue(group.Key, out var actual);
                var actualAmount = actual?.Amount ?? 0m;
                var comparisonAmount = group.Amount;
                var varianceAmount = Math.Round(actualAmount - comparisonAmount, 2, MidpointRounding.AwayFromZero);

                return new FinanceVarianceRowDto(
                    group.Key.PeriodStartUtc,
                    group.Key.FinanceAccountId,
                    group.AccountCode,
                    group.AccountName,
                    group.CategoryKey,
                    group.CategoryName,
                    includesCostCenters ? group.CostCenterId : null,
                    null,
                    null,
                    actualAmount,
                    comparisonAmount,
                    varianceAmount,
                    CalculateVariancePercentage(varianceAmount, comparisonAmount),
                    ResolveVarianceCurrency([group.Currency, actual?.Currency]));
            })
            .ToList();

        return new FinanceVarianceResultDto(
            query.CompanyId,
            comparisonType,
            periodStartUtc,
            periodEndUtc,
            normalizedVersion,
            includesCostCenters,
            rows);
    }

    private async Task<List<PlanningComparisonRow>> LoadComparisonRowsAsync(
        GetFinanceVarianceQuery query,
        string comparisonType,
        DateTime periodStartUtc,
        DateTime endExclusiveUtc,
        string? normalizedVersion,
        bool includesCostCenters,
        CancellationToken cancellationToken)
    {
        try
        {
            if (comparisonType == FinanceVarianceComparisonTypes.Budget)
            {
                var budgets = _dbContext.Budgets
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.CompanyId == query.CompanyId && x.PeriodStartUtc >= periodStartUtc && x.PeriodStartUtc < endExclusiveUtc);

                if (!string.IsNullOrWhiteSpace(normalizedVersion))
                {
                    budgets = budgets.Where(x => x.Version == normalizedVersion);
                }

                if (query.FinanceAccountId.HasValue)
                {
                    budgets = budgets.Where(x => x.FinanceAccountId == query.FinanceAccountId.Value);
                }

                if (includesCostCenters && query.CostCenterId.HasValue)
                {
                    budgets = budgets.Where(x => x.CostCenterId == query.CostCenterId.Value);
                }

                return await budgets
                    .Select(x => new PlanningComparisonRow(
                        x.PeriodStartUtc,
                        x.FinanceAccountId,
                        x.FinanceAccount.Code,
                        x.FinanceAccount.Name,
                        NormalizeVarianceCategory(x.FinanceAccount.AccountType),
                        FormatVarianceCategoryName(x.FinanceAccount.AccountType),
                        includesCostCenters ? x.CostCenterId : null,
                        x.Amount,
                        x.Currency))
                    .ToListAsync(cancellationToken);
            }

            var forecasts = _dbContext.Forecasts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.PeriodStartUtc >= periodStartUtc && x.PeriodStartUtc < endExclusiveUtc);

            if (!string.IsNullOrWhiteSpace(normalizedVersion))
            {
                forecasts = forecasts.Where(x => x.Version == normalizedVersion);
            }

            if (query.FinanceAccountId.HasValue)
            {
                forecasts = forecasts.Where(x => x.FinanceAccountId == query.FinanceAccountId.Value);
            }

            if (includesCostCenters && query.CostCenterId.HasValue)
            {
                forecasts = forecasts.Where(x => x.CostCenterId == query.CostCenterId.Value);
            }

            return await forecasts
                .Select(x => new PlanningComparisonRow(
                    x.PeriodStartUtc,
                    x.FinanceAccountId,
                    x.FinanceAccount.Code,
                    x.FinanceAccount.Name,
                    NormalizeVarianceCategory(x.FinanceAccount.AccountType),
                    FormatVarianceCategoryName(x.FinanceAccount.AccountType),
                    includesCostCenters ? x.CostCenterId : null,
                    x.Amount,
                    x.Currency))
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (IsMissingPlanningSchemaTable(ex))
        {
            return [];
        }
    }

    private async Task<bool> AreCostCentersEnabledAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => x.Settings)
            .SingleOrDefaultAsync(cancellationToken);

        if (settings?.FeatureFlags is null || settings.FeatureFlags.Count == 0)
        {
            return false;
        }

        return CostCenterFeatureFlagKeys.Any(key => settings.FeatureFlags.TryGetValue(key, out var enabled) && enabled);
    }

    // Variance percentage is always calculated as (actual - comparison) / comparison * 100.
    private static decimal? CalculateVariancePercentage(decimal varianceAmount, decimal comparisonAmount) =>
        comparisonAmount == 0m
            ? null
            : Math.Round(varianceAmount / comparisonAmount * 100m, 2, MidpointRounding.AwayFromZero);

    private static string NormalizeVarianceCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "uncategorized";
        }

        return value.Trim().Replace(' ', '_').ToLowerInvariant();
    }

    private static string FormatVarianceCategoryName(string? value)
    {
        var normalized = NormalizeVarianceCategory(value).Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }

    private static string ResolveVarianceCurrency(IEnumerable<string?> currencies)
    {
        var normalized = currencies
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length switch
        {
            0 => "USD",
            1 => normalized[0],
            _ => "MIXED"
        };
    }

    private sealed record VarianceGroupingKey(DateTime PeriodStartUtc, Guid FinanceAccountId, string CategoryKey, Guid? CostCenterId);
    private sealed record PlanningComparisonRow(DateTime PeriodStartUtc, Guid FinanceAccountId, string AccountCode, string AccountName, string CategoryKey, string CategoryName, Guid? CostCenterId, decimal Amount, string Currency);
    private sealed record ActualLedgerRow(DateTime PostedAtUtc, Guid FinanceAccountId, string CategoryKey, Guid? CostCenterId, decimal Amount, string Currency);
    private sealed record AggregatedActualRow(decimal Amount, string Currency);
    private sealed record AggregatedComparisonRow(VarianceGroupingKey Key, string AccountCode, string AccountName, string CategoryKey, string CategoryName, Guid? CostCenterId, decimal Amount, string Currency);
}
