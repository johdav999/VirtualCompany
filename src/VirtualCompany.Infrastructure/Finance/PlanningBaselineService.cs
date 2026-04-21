using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class PlanningBaselineService : IPlanningBaselineService
{
    private const int BaselineHorizonMonths = 12;

    private readonly VirtualCompanyDbContext _dbContext;

    public PlanningBaselineService(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> BackfillAllCompaniesAsync(CancellationToken cancellationToken)
    {
        var companyIds = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var inserted = 0;
        foreach (var companyId in companyIds)
        {
            inserted += await EnsureBaselineAsync(companyId, cancellationToken);
        }

        return inserted;
    }

    public async Task<int> EnsureBaselineAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        if (company is null)
        {
            throw new KeyNotFoundException($"Company '{companyId}' was not found.");
        }

        if (!await HasPlanningTablesAsync(cancellationToken))
        {
            return 0;
        }

        try
        {
            var accounts = await _dbContext.FinanceAccounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Code)
                .Select(x => new PlanningAccountRow(x.Id, x.Currency))
                .ToListAsync(cancellationToken);

            if (accounts.Count == 0)
            {
                return 0;
            }

            var horizonStartUtc = await ResolveHorizonStartAsync(companyId, company.CreatedUtc, cancellationToken);
            var periods = Enumerable.Range(0, BaselineHorizonMonths)
                .Select(offset => horizonStartUtc.AddMonths(offset))
                .ToArray();
            var periodEndExclusiveUtc = periods[^1].AddMonths(1);

            var existingBudgetKeys = await _dbContext.Budgets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.Version == FinancePlanningVersions.Baseline &&
                    x.CostCenterId == null &&
                    x.PeriodStartUtc >= horizonStartUtc &&
                    x.PeriodStartUtc < periodEndExclusiveUtc)
                .Select(x => new PlanningKey(x.FinanceAccountId, x.PeriodStartUtc))
                .ToHashSetAsync(cancellationToken);

            var existingForecastKeys = await _dbContext.Forecasts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.Version == FinancePlanningVersions.Baseline &&
                    x.CostCenterId == null &&
                    x.PeriodStartUtc >= horizonStartUtc &&
                    x.PeriodStartUtc < periodEndExclusiveUtc)
                .Select(x => new PlanningKey(x.FinanceAccountId, x.PeriodStartUtc))
                .ToHashSetAsync(cancellationToken);

            var budgetsToAdd = new List<Budget>();
            var forecastsToAdd = new List<Forecast>();

            foreach (var account in accounts)
            {
                foreach (var period in periods)
                {
                    var key = new PlanningKey(account.AccountId, period);
                    if (!existingBudgetKeys.Contains(key))
                    {
                        budgetsToAdd.Add(new Budget(
                            Guid.NewGuid(),
                            companyId,
                            account.AccountId,
                            period,
                            FinancePlanningVersions.Baseline,
                            0m,
                            account.Currency));
                    }

                    if (!existingForecastKeys.Contains(key))
                    {
                        forecastsToAdd.Add(new Forecast(
                            Guid.NewGuid(),
                            companyId,
                            account.AccountId,
                            period,
                            FinancePlanningVersions.Baseline,
                            0m,
                            account.Currency));
                    }
                }
            }

            if (budgetsToAdd.Count == 0 && forecastsToAdd.Count == 0)
            {
                return 0;
            }

            if (budgetsToAdd.Count > 0)
            {
                _dbContext.Budgets.AddRange(budgetsToAdd);
            }

            if (forecastsToAdd.Count > 0)
            {
                _dbContext.Forecasts.AddRange(forecastsToAdd);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return budgetsToAdd.Count + forecastsToAdd.Count;
        }
        catch (Exception ex) when (IsMissingPlanningSchemaTable(ex))
        {
            _dbContext.ChangeTracker.Clear();
            return 0;
        }
        catch (DbUpdateException ex) when (IsDuplicatePlanningEntry(ex))
        {
            _dbContext.ChangeTracker.Clear();
            return 0;
        }
    }

    private static bool IsDuplicatePlanningEntry(DbUpdateException exception)
    {
        var message = exception.ToString();
        return message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
               (message.Contains("IX_", StringComparison.OrdinalIgnoreCase) &&
                (message.Contains("budget", StringComparison.OrdinalIgnoreCase) || message.Contains("forecast", StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<DateTime> ResolveHorizonStartAsync(Guid companyId, DateTime companyCreatedUtc, CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync("finance_fiscal_periods", cancellationToken))
        {
            var normalizedCreatedUtc = companyCreatedUtc.Kind == DateTimeKind.Utc ? companyCreatedUtc : companyCreatedUtc.ToUniversalTime();
            return new DateTime(normalizedCreatedUtc.Year, normalizedCreatedUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        DateTime? latestFiscalPeriodStartUtc;
        try
        {
            latestFiscalPeriodStartUtc = await _dbContext.FiscalPeriods
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.StartUtc)
                .Select(x => (DateTime?)x.StartUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex) when (IsMissingPlanningSchemaTable(ex))
        {
            latestFiscalPeriodStartUtc = null;
        }

        var anchorUtc = latestFiscalPeriodStartUtc ?? companyCreatedUtc;
        var normalizedAnchorUtc = anchorUtc.Kind == DateTimeKind.Utc ? anchorUtc : anchorUtc.ToUniversalTime();
        return new DateTime(normalizedAnchorUtc.Year, normalizedAnchorUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private async Task<bool> HasPlanningTablesAsync(CancellationToken cancellationToken)
    {
        return await TableExistsAsync("budgets", cancellationToken) &&
               await TableExistsAsync("forecasts", cancellationToken);
    }

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM sys.tables AS t
                    WHERE t.name = @tableName
                ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool exists && exists;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static bool IsMissingPlanningSchemaTable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException && sqlException.Number == 208)
            {
                if (ContainsMissingPlanningTableName(sqlException.Message))
                {
                    return true;
                }
            }

            if (ContainsMissingPlanningTableName(current.Message) &&
                (current.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("invalid object name", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsMissingPlanningTableName(string message) =>
        message.Contains("finance_fiscal_periods", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("budgets", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("forecasts", StringComparison.OrdinalIgnoreCase);

    private sealed record PlanningAccountRow(Guid AccountId, string Currency);

    private sealed record PlanningKey(Guid FinanceAccountId, DateTime PeriodStartUtc);
}
