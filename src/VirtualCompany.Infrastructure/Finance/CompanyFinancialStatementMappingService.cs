using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinancialStatementMappingService : IFinancialStatementMappingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;

    public CompanyFinancialStatementMappingService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
    }

    public async Task<IReadOnlyList<FinancialStatementMappingDto>> ListAsync(
        ListFinancialStatementMappingsQuery query,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(query.CompanyId, cancellationToken);

        var rows = await _dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new MappingRow(
                x.Id,
                x.CompanyId,
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.StatementType,
                x.ReportSection,
                x.LineClassification,
                x.IsActive,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AccountId)
            .ThenBy(x => x.StatementType.ToStorageValue(), StringComparer.Ordinal)
            .ThenBy(x => x.Id)
            .Select(MapDto)
            .ToList();
    }

    public async Task<FinancialStatementMappingDto> CreateAsync(
        CreateFinancialStatementMappingCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(command.CompanyId, cancellationToken);

        var validated = await ValidateCommandAsync(
            command.CompanyId,
            command.AccountId,
            command.StatementType,
            command.ReportSection,
            command.LineClassification,
            cancellationToken);

        var mapping = new FinancialStatementMapping(
            Guid.NewGuid(),
            command.CompanyId,
            validated.Account.Id,
            validated.StatementType,
            validated.ReportSection,
            validated.LineClassification,
            command.IsActive);

        _dbContext.FinancialStatementMappings.Add(mapping);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateActiveMappingViolation(ex))
        {
            throw BuildConflictException();
        }

        return MapDto(new MappingRow(
            mapping.Id,
            mapping.CompanyId,
            mapping.FinanceAccountId,
            validated.Account.Code,
            validated.Account.Name,
            mapping.StatementType,
            mapping.ReportSection,
            mapping.LineClassification,
            mapping.IsActive,
            mapping.CreatedUtc,
            mapping.UpdatedUtc));
    }

    public async Task<FinancialStatementMappingDto> UpdateAsync(
        UpdateFinancialStatementMappingCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(command.CompanyId, cancellationToken);

        var mapping = await _dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == command.CompanyId && x.Id == command.MappingId,
                cancellationToken);

        if (mapping is null)
        {
            throw new KeyNotFoundException("Financial statement mapping was not found in the requested company.");
        }

        var validated = await ValidateCommandAsync(
            command.CompanyId,
            command.AccountId,
            command.StatementType,
            command.ReportSection,
            command.LineClassification,
            cancellationToken);

        mapping.ReassignAccount(validated.Account.Id);
        mapping.ReassignStatement(validated.StatementType, validated.ReportSection, validated.LineClassification);
        mapping.SetActive(command.IsActive);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateActiveMappingViolation(ex))
        {
            throw BuildConflictException();
        }

        return MapDto(new MappingRow(
            mapping.Id,
            mapping.CompanyId,
            mapping.FinanceAccountId,
            validated.Account.Code,
            validated.Account.Name,
            mapping.StatementType,
            mapping.ReportSection,
            mapping.LineClassification,
            mapping.IsActive,
            mapping.CreatedUtc,
            mapping.UpdatedUtc));
    }

    public async Task<FinancialStatementMappingValidationResultDto> ValidateAsync(
        ValidateFinancialStatementMappingsQuery query,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(query.CompanyId, cancellationToken);

        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new AccountRow(x.Id, x.CompanyId, x.Code, x.Name))
            .ToListAsync(cancellationToken);

        var mappings = await _dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new MappingRow(
                x.Id,
                x.CompanyId,
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.StatementType,
                x.ReportSection,
                x.LineClassification,
                x.IsActive,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        var activeMappings = mappings.Where(x => x.IsActive).ToList();
        var activeMappingLookup = activeMappings
            .GroupBy(x => x.AccountId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var issues = new List<FinancialStatementMappingValidationIssueDto>();

        // Finance accounts do not yet model explicit lifecycle/reportability flags.
        // Until those fields exist, every company finance account is treated as active and reportable.
        foreach (var account in accounts)
        {
            if (activeMappingLookup.ContainsKey(account.Id))
            {
                continue;
            }

            issues.Add(new FinancialStatementMappingValidationIssueDto(
                FinancialStatementMappingValidationErrorCodes.UnmappedActiveReportableAccount,
                $"Finance account '{account.Code}' is active and reportable but has no active financial statement mapping.",
                account.Id,
                account.Code,
                account.Name,
                null,
                null));
        }

        foreach (var duplicateGroup in activeMappings
                     .GroupBy(x => new { x.AccountId, x.StatementType })
                     .Where(x => x.Count() > 1))
        {
            foreach (var mapping in duplicateGroup
                         .OrderBy(x => x.CreatedUtc)
                         .ThenBy(x => x.Id))
            {
                issues.Add(new FinancialStatementMappingValidationIssueDto(
                    FinancialStatementMappingValidationErrorCodes.ConflictDuplicateActive,
                    $"Finance account '{mapping.AccountCode}' has more than one active mapping for statement type '{mapping.StatementType.ToStorageValue()}'.",
                    mapping.AccountId,
                    mapping.AccountCode,
                    mapping.AccountName,
                    mapping.Id,
                    mapping.StatementType.ToStorageValue()));
            }
        }

        var orderedIssues = issues
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AccountId)
            .ThenBy(x => x.StatementType ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.MappingId ?? Guid.Empty)
            .ToList();

        return new FinancialStatementMappingValidationResultDto(
            query.CompanyId,
            accounts.Count,
            mappings.Count,
            orderedIssues.Count,
            orderedIssues.Count(x => x.Code == FinancialStatementMappingValidationErrorCodes.UnmappedActiveReportableAccount),
            orderedIssues.Count(x => x.Code == FinancialStatementMappingValidationErrorCodes.ConflictDuplicateActive),
            orderedIssues);
    }

    private async Task<ValidatedCommand> ValidateCommandAsync(
        Guid companyId,
        Guid accountId,
        string statementTypeValue,
        string reportSectionValue,
        string lineClassificationValue,
        CancellationToken cancellationToken)
    {
        var errors = new List<FinancialStatementMappingCommandErrorDto>();
        var account = await ResolveAccountAsync(accountId, companyId, errors, cancellationToken);

        var hasStatementType = FinancialStatementTypeValues.TryParse(statementTypeValue, out var statementType);
        if (!hasStatementType)
        {
            errors.Add(new FinancialStatementMappingCommandErrorDto(
                FinancialStatementMappingValidationErrorCodes.InvalidStatementType,
                nameof(CreateFinancialStatementMappingCommand.StatementType),
                $"StatementType must be one of: {string.Join(", ", FinancialStatementTypeValues.AllowedValues)}."));
        }

        var hasReportSection = FinancialStatementReportSectionValues.TryParse(reportSectionValue, out var reportSection);
        if (!hasReportSection)
        {
            errors.Add(new FinancialStatementMappingCommandErrorDto(
                FinancialStatementMappingValidationErrorCodes.InvalidReportSection,
                nameof(CreateFinancialStatementMappingCommand.ReportSection),
                $"ReportSection must be one of: {string.Join(", ", FinancialStatementReportSectionValues.AllowedValues)}."));
        }

        var hasLineClassification = FinancialStatementLineClassificationValues.TryParse(lineClassificationValue, out var lineClassification);
        if (!hasLineClassification)
        {
            errors.Add(new FinancialStatementMappingCommandErrorDto(
                FinancialStatementMappingValidationErrorCodes.InvalidLineClassification,
                nameof(CreateFinancialStatementMappingCommand.LineClassification),
                $"LineClassification must be one of: {string.Join(", ", FinancialStatementLineClassificationValues.AllowedValues)}."));
        }

        if (hasStatementType && hasReportSection && !IsSectionCompatible(statementType, reportSection))
        {
            errors.Add(new FinancialStatementMappingCommandErrorDto(
                FinancialStatementMappingValidationErrorCodes.InvalidReportSection,
                nameof(CreateFinancialStatementMappingCommand.ReportSection),
                $"ReportSection '{reportSectionValue}' is not valid for statement type '{statementType.ToStorageValue()}'."));
        }

        if (hasStatementType && hasLineClassification && !IsLineClassificationCompatible(statementType, lineClassification))
        {
            errors.Add(new FinancialStatementMappingCommandErrorDto(
                FinancialStatementMappingValidationErrorCodes.InvalidLineClassification,
                nameof(CreateFinancialStatementMappingCommand.LineClassification),
                $"LineClassification '{lineClassificationValue}' is not valid for statement type '{statementType.ToStorageValue()}'."));
        }

        if (errors.Count > 0 || account is null || !hasStatementType || !hasReportSection || !hasLineClassification)
        {
            throw BuildValidationException(errors);
        }

        return new ValidatedCommand(account, statementType, reportSection, lineClassification);
    }

    private async Task<AccountRow?> ResolveAccountAsync(
        Guid accountId,
        Guid companyId,
        ICollection<FinancialStatementMappingCommandErrorDto> errors,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty)
        {
            errors.Add(new FinancialStatementMappingCommandErrorDto(
                FinancialStatementMappingValidationErrorCodes.AccountNotFound,
                nameof(CreateFinancialStatementMappingCommand.AccountId),
                "AccountId must reference an existing finance account."));
            return null;
        }

        var account = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == accountId)
            .Select(x => new AccountRow(x.Id, x.CompanyId, x.Code, x.Name))
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            errors.Add(new FinancialStatementMappingCommandErrorDto(
                FinancialStatementMappingValidationErrorCodes.AccountNotFound,
                nameof(CreateFinancialStatementMappingCommand.AccountId),
                "AccountId must reference an existing finance account."));
            return null;
        }

        if (account.CompanyId != companyId)
        {
            errors.Add(new FinancialStatementMappingCommandErrorDto(
                FinancialStatementMappingValidationErrorCodes.AccountCompanyMismatch,
                nameof(CreateFinancialStatementMappingCommand.AccountId),
                "AccountId must reference a finance account in the requested company."));
            return null;
        }

        return account;
    }

    private async Task RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (await _membershipContextResolver.ResolveAsync(companyId, cancellationToken) is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }
    }

    private static FinancialStatementMappingCommandException BuildValidationException(IEnumerable<FinancialStatementMappingCommandErrorDto> errors)
    {
        var materialized = errors.ToList();
        var code = materialized.Count == 1
            ? materialized[0].Code
            : FinancialStatementMappingValidationErrorCodes.ValidationFailed;

        return new FinancialStatementMappingCommandException(
            StatusCodes.Status400BadRequest,
            code,
            "Financial statement mapping validation failed",
            "The financial statement mapping request contains invalid account references or classification values.",
            materialized);
    }

    private static FinancialStatementMappingCommandException BuildConflictException() =>
        new(
            StatusCodes.Status409Conflict,
            FinancialStatementMappingValidationErrorCodes.ConflictDuplicateActive,
            "Financial statement mapping conflict",
            "The requested company account already has an active mapping for the selected statement type.",
            [
                new FinancialStatementMappingCommandErrorDto(
                    FinancialStatementMappingValidationErrorCodes.ConflictDuplicateActive,
                    nameof(CreateFinancialStatementMappingCommand.StatementType),
                    "Only one active mapping is allowed for the same company account and statement type.")
            ]);

    private static bool IsDuplicateActiveMappingViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("IX_financial_statement_mappings_company_id_finance_account_id_statement_type", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("financial_statement_mappings", StringComparison.OrdinalIgnoreCase) &&
                current.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSectionCompatible(FinancialStatementType statementType, FinancialStatementReportSection reportSection) =>
        statementType switch
        {
            FinancialStatementType.BalanceSheet => reportSection is FinancialStatementReportSection.BalanceSheetAssets or FinancialStatementReportSection.BalanceSheetLiabilities or FinancialStatementReportSection.BalanceSheetEquity,
            FinancialStatementType.ProfitAndLoss => reportSection is FinancialStatementReportSection.ProfitAndLossRevenue or FinancialStatementReportSection.ProfitAndLossCostOfSales or FinancialStatementReportSection.ProfitAndLossOperatingExpenses or FinancialStatementReportSection.ProfitAndLossOtherIncomeExpense or FinancialStatementReportSection.ProfitAndLossTaxes,
            FinancialStatementType.CashFlow => reportSection is FinancialStatementReportSection.CashFlowOperatingActivities or FinancialStatementReportSection.CashFlowInvestingActivities or FinancialStatementReportSection.CashFlowFinancingActivities or FinancialStatementReportSection.CashFlowSupplementalDisclosures,
            _ => false
        };

    private static bool IsLineClassificationCompatible(FinancialStatementType statementType, FinancialStatementLineClassification lineClassification) =>
        statementType switch
        {
            FinancialStatementType.BalanceSheet => lineClassification is FinancialStatementLineClassification.CurrentAsset or FinancialStatementLineClassification.NonCurrentAsset or FinancialStatementLineClassification.CurrentLiability or FinancialStatementLineClassification.NonCurrentLiability or FinancialStatementLineClassification.Equity,
            FinancialStatementType.ProfitAndLoss => lineClassification is FinancialStatementLineClassification.Revenue or FinancialStatementLineClassification.ContraRevenue or FinancialStatementLineClassification.CostOfSales or FinancialStatementLineClassification.OperatingExpense or FinancialStatementLineClassification.NonOperatingIncome or FinancialStatementLineClassification.NonOperatingExpense or FinancialStatementLineClassification.IncomeTax,
            FinancialStatementType.CashFlow => lineClassification is FinancialStatementLineClassification.DepreciationAndAmortization or FinancialStatementLineClassification.WorkingCapital or FinancialStatementLineClassification.NonCashAdjustment or FinancialStatementLineClassification.CashReceipt or FinancialStatementLineClassification.CashDisbursement or FinancialStatementLineClassification.InvestingCashInflow or FinancialStatementLineClassification.InvestingCashOutflow or FinancialStatementLineClassification.FinancingCashInflow or FinancialStatementLineClassification.FinancingCashOutflow or FinancialStatementLineClassification.SupplementalDisclosure,
            _ => false
        };

    private static FinancialStatementMappingDto MapDto(MappingRow row) =>
        new(
            row.Id,
            row.CompanyId,
            row.AccountId,
            row.AccountCode,
            row.AccountName,
            row.StatementType.ToStorageValue(),
            row.ReportSection.ToStorageValue(),
            row.LineClassification.ToStorageValue(),
            row.IsActive,
            row.CreatedUtc,
            row.UpdatedUtc);

    private sealed record AccountRow(
        Guid Id,
        Guid CompanyId,
        string Code,
        string Name);

    private sealed record MappingRow(
        Guid Id,
        Guid CompanyId,
        Guid AccountId,
        string AccountCode,
        string AccountName,
        FinancialStatementType StatementType,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification,
        bool IsActive,
        DateTime CreatedUtc,
        DateTime UpdatedUtc);

    private sealed record ValidatedCommand(
        AccountRow Account,
        FinancialStatementType StatementType,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification);
}
