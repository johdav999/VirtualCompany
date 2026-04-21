using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceCommandService
{
    public async Task<FinanceBudgetDto> CreateBudgetAsync(
        CreateFinanceBudgetCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        ValidatePlanningEntry(command.Budget);

        var financeAccount = await LoadFinanceAccountAsync(command.CompanyId, command.Budget.FinanceAccountId, cancellationToken);
        var budget = new Budget(
            Guid.NewGuid(),
            command.CompanyId,
            financeAccount.Id,
            command.Budget.PeriodStartUtc,
            command.Budget.Version,
            command.Budget.Amount,
            ResolvePlanningCurrency(command.Budget.Currency, financeAccount.Currency),
            command.Budget.CostCenterId);

        _dbContext.Budgets.Add(budget);
        await SavePlanningChangesAsync(cancellationToken);
        return MapBudget(budget, financeAccount);
    }

    public async Task<FinanceBudgetDto> UpdateBudgetAsync(
        UpdateFinanceBudgetCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        if (command.BudgetId == Guid.Empty)
        {
            throw new ArgumentException("Budget id is required.", nameof(command));
        }

        ValidatePlanningEntry(command.Budget);

        var budget = await _dbContext.Budgets
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == command.BudgetId, cancellationToken);
        EnsureRecordTenant(budget, command.CompanyId, "budget");

        var financeAccount = await LoadFinanceAccountAsync(command.CompanyId, command.Budget.FinanceAccountId, cancellationToken);
        budget!.Update(
            financeAccount.Id,
            command.Budget.PeriodStartUtc,
            command.Budget.Version,
            command.Budget.Amount,
            ResolvePlanningCurrency(command.Budget.Currency, financeAccount.Currency),
            command.Budget.CostCenterId);

        await SavePlanningChangesAsync(cancellationToken);
        return MapBudget(budget, financeAccount);
    }

    private async Task<FinanceAccount> LoadFinanceAccountAsync(
        Guid companyId,
        Guid financeAccountId,
        CancellationToken cancellationToken)
    {
        if (financeAccountId == Guid.Empty)
        {
            throw CreateValidationException(nameof(FinancePlanningEntryUpsertDto.FinanceAccountId), "Finance account id is required.");
        }

        var financeAccount = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == financeAccountId, cancellationToken);

        return financeAccount ?? throw CreateValidationException(
            nameof(FinancePlanningEntryUpsertDto.FinanceAccountId),
            "The selected finance account was not found in the active company.");
    }

    private async Task SavePlanningChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicatePlanningEntry(ex))
        {
            throw CreateValidationException(
                "Budget",
                "A planning entry already exists for the selected company, period, account, version, and cost center.");
        }
    }

    private static bool IsDuplicatePlanningEntry(DbUpdateException exception)
    {
        var message = exception.ToString();
        return message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("IX_budgets_company_id_period_start_at_finance_account_id_version_cost_center_id", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("IX_budgets_company_id_period_start_at_finance_account_id_version_null_cost_center", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("IX_forecasts_company_id_period_start_at_finance_account_id_version_cost_center_id", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("IX_forecasts_company_id_period_start_at_finance_account_id_version_null_cost_center", StringComparison.OrdinalIgnoreCase);
    }

    private static FinanceBudgetDto MapBudget(Budget budget, FinanceAccount financeAccount) =>
        new(
            budget.Id,
            budget.CompanyId,
            budget.FinanceAccountId,
            financeAccount.Code,
            financeAccount.Name,
            budget.PeriodStartUtc,
            budget.Version,
            budget.CostCenterId,
            budget.Amount,
            budget.Currency,
            budget.CreatedUtc,
            budget.UpdatedUtc);

    private static void ValidatePlanningEntry(FinancePlanningEntryUpsertDto entry)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (entry.FinanceAccountId == Guid.Empty)
        {
            errors[nameof(FinancePlanningEntryUpsertDto.FinanceAccountId)] = ["Finance account id is required."];
        }

        if (entry.PeriodStartUtc == default)
        {
            errors[nameof(FinancePlanningEntryUpsertDto.PeriodStartUtc)] = ["Period start is required."];
        }

        if (string.IsNullOrWhiteSpace(entry.Version))
        {
            errors[nameof(FinancePlanningEntryUpsertDto.Version)] = ["Version is required."];
        }
        else if (entry.Version.Trim().Length > 64)
        {
            errors[nameof(FinancePlanningEntryUpsertDto.Version)] = ["Version must be 64 characters or fewer."];
        }

        if (!string.IsNullOrWhiteSpace(entry.Currency))
        {
            var normalizedCurrency = entry.Currency.Trim();
            if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsLetter))
            {
                errors[nameof(FinancePlanningEntryUpsertDto.Currency)] = ["Currency must be a three-letter ISO code when supplied."];
            }
        }

        if (entry.CostCenterId == Guid.Empty)
        {
            errors[nameof(FinancePlanningEntryUpsertDto.CostCenterId)] = ["Cost center id cannot be empty."];
        }

        if (errors.Count > 0)
        {
            throw new FinanceValidationException(errors);
        }
    }

    private static string ResolvePlanningCurrency(string? requestedCurrency, string fallbackCurrency) =>
        string.IsNullOrWhiteSpace(requestedCurrency)
            ? fallbackCurrency.Trim().ToUpperInvariant()
            : requestedCurrency.Trim().ToUpperInvariant();
}