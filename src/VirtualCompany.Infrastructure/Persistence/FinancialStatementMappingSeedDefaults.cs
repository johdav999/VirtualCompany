using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal static class FinancialStatementMappingSeedDefaults
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<MappingTemplate>> Templates =
        new Dictionary<string, IReadOnlyList<MappingTemplate>>(StringComparer.OrdinalIgnoreCase)
        {
            ["1000"] =
            [
                new(
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetAssets,
                    FinancialStatementLineClassification.CurrentAsset)
            ],
            ["1100"] =
            [
                new(
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetAssets,
                    FinancialStatementLineClassification.CurrentAsset),
                new(
                    FinancialStatementType.CashFlow,
                    FinancialStatementReportSection.CashFlowOperatingActivities,
                    FinancialStatementLineClassification.WorkingCapital)
            ],
            ["2000"] =
            [
                new(
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetLiabilities,
                    FinancialStatementLineClassification.CurrentLiability),
                new(
                    FinancialStatementType.CashFlow,
                    FinancialStatementReportSection.CashFlowOperatingActivities,
                    FinancialStatementLineClassification.WorkingCapital)
            ],
            ["4000"] =
            [
                new(
                    FinancialStatementType.ProfitAndLoss,
                    FinancialStatementReportSection.ProfitAndLossRevenue,
                    FinancialStatementLineClassification.Revenue),
                new(
                    FinancialStatementType.CashFlow,
                    FinancialStatementReportSection.CashFlowOperatingActivities,
                    FinancialStatementLineClassification.CashReceipt)
            ],
            ["6100"] =
            [
                new(
                    FinancialStatementType.ProfitAndLoss,
                    FinancialStatementReportSection.ProfitAndLossOperatingExpenses,
                    FinancialStatementLineClassification.OperatingExpense),
                new(
                    FinancialStatementType.CashFlow,
                    FinancialStatementReportSection.CashFlowOperatingActivities,
                    FinancialStatementLineClassification.CashDisbursement)
            ]
        };

    public static void Apply(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        IReadOnlyCollection<FinanceAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(accounts);

        if (companyId == Guid.Empty || accounts.Count == 0)
        {
            return;
        }

        var companyAccounts = accounts
            .Where(account => account.CompanyId == companyId)
            .GroupBy(account => account.Id)
            .Select(group => group.First())
            .ToArray();
        if (companyAccounts.Length == 0)
        {
            return;
        }

        var accountIds = companyAccounts.Select(account => account.Id).ToHashSet();
        var existingKeys = dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(mapping => mapping.CompanyId == companyId && accountIds.Contains(mapping.FinanceAccountId))
            .Select(mapping => new MappingKey(mapping.FinanceAccountId, mapping.StatementType))
            .ToHashSet();

        foreach (var entry in dbContext.ChangeTracker.Entries<FinancialStatementMapping>())
        {
            if (entry.State == EntityState.Deleted || entry.Entity.CompanyId != companyId || !accountIds.Contains(entry.Entity.FinanceAccountId))
            {
                continue;
            }

            existingKeys.Add(new MappingKey(entry.Entity.FinanceAccountId, entry.Entity.StatementType));
        }

        foreach (var account in companyAccounts.OrderBy(account => account.Code, StringComparer.OrdinalIgnoreCase))
        {
            if (!Templates.TryGetValue(account.Code, out var mappings))
            {
                continue;
            }

            foreach (var template in mappings)
            {
                var key = new MappingKey(account.Id, template.StatementType);
                if (!existingKeys.Add(key))
                {
                    continue;
                }

                dbContext.FinancialStatementMappings.Add(
                    new FinancialStatementMapping(
                        StableId(companyId, account.Id, template.StatementType),
                        companyId,
                        account.Id,
                        template.StatementType,
                        template.ReportSection,
                        template.LineClassification,
                        isActive: true,
                        createdUtc: account.CreatedUtc,
                        updatedUtc: account.UpdatedUtc));
            }
        }
    }

    private static Guid StableId(Guid companyId, Guid financeAccountId, FinancialStatementType statementType)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(
            $"{companyId:N}:{financeAccountId:N}:{statementType.ToStorageValue()}"));

        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash);
    }

    private readonly record struct MappingKey(
        Guid FinanceAccountId,
        FinancialStatementType StatementType);

    private sealed record MappingTemplate(
        FinancialStatementType StatementType,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification);
}
