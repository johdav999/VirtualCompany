using System.Security.Cryptography;
using System.Text;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

public static class FinanceSeedData
{
    private static readonly DateTime DefaultSeedAnchorUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static FinanceSeedResult AddMockFinanceData(VirtualCompanyDbContext dbContext, Guid companyId) =>
        AddMockFinanceData(dbContext, companyId, DefaultSeedAnchorUtc);

    public static FinanceSeedResult AddMockFinanceData(VirtualCompanyDbContext dbContext, Guid companyId, DateTime seedAnchorUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        var anchorUtc = seedAnchorUtc.Kind == DateTimeKind.Utc
            ? seedAnchorUtc
            : seedAnchorUtc.ToUniversalTime();

        var accounts = new[]
        {
            new FinanceAccount(StableId(companyId, "account:operating-cash"), companyId, "1000", "Operating Cash", "asset", "USD", 25000m, anchorUtc.AddDays(-90)),
            new FinanceAccount(StableId(companyId, "account:receivables"), companyId, "1100", "Receivables", "asset", "USD", 12000m, anchorUtc.AddDays(-90)),
            new FinanceAccount(StableId(companyId, "account:payables"), companyId, "2000", "Payables", "liability", "USD", -8000m, anchorUtc.AddDays(-90))
        };

        var counterparties = new[]
        {
            new FinanceCounterparty(StableId(companyId, "counterparty:northwind-analytics"), companyId, "Northwind Analytics", "customer", "ap@northwind.example"),
            new FinanceCounterparty(StableId(companyId, "counterparty:contoso-supplies"), companyId, "Contoso Supplies", "vendor", "billing@contoso.example"),
            new FinanceCounterparty(StableId(companyId, "counterparty:fabrikam-cloud"), companyId, "Fabrikam Cloud", "vendor", "finance@fabrikam.example"),
            new FinanceCounterparty(StableId(companyId, "counterparty:adventure-works"), companyId, "Adventure Works", "customer", "payables@adventure.example"),
            new FinanceCounterparty(StableId(companyId, "counterparty:tailspin-logistics"), companyId, "Tailspin Logistics", "vendor", "ar@tailspin.example")
        };

        var documents = Enumerable.Range(1, 5)
            .Select(index => new CompanyKnowledgeDocument(
                StableId(companyId, $"finance-document:{index:000}"),
                companyId,
                $"Finance supporting document {index:000}",
                CompanyKnowledgeDocumentType.Report,
                $"mock-finance/{companyId:N}/document-{index:000}.pdf",
                null,
                $"finance-document-{index:000}.pdf",
                "application/pdf",
                ".pdf",
                1024 + index,
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                {
                    ["category"] = System.Text.Json.Nodes.JsonValue.Create("finance"),
                    ["department"] = System.Text.Json.Nodes.JsonValue.Create("finance"),
                    ["seed"] = System.Text.Json.Nodes.JsonValue.Create(true)
                },
                BuildDocumentAccessScope(companyId, index),
                $"finance-seed:{index:000}"))
            .ToArray();

        var invoices = Enumerable.Range(1, 5)
            .Select(index => new FinanceInvoice(
                StableId(companyId, $"invoice:{index:000}"),
                companyId,
                counterparties[index % 2 == 0 ? 0 : 3].Id,
                $"INV-{anchorUtc:yyyyMM}-{index:000}",
                anchorUtc.AddDays(-45 + index),
                anchorUtc.AddDays(-15 + index),
                2400m + (index * 325m),
                "USD",
                index % 3 == 0 ? "paid" : "open",
                documents[index - 1].Id))
            .ToArray();

        var bills = Enumerable.Range(1, 5)
            .Select(index => new FinanceBill(
                StableId(companyId, $"bill:{index:000}"),
                companyId,
                counterparties[1 + (index % 3)].Id,
                $"BILL-{anchorUtc:yyyyMM}-{index:000}",
                anchorUtc.AddDays(-40 + index),
                anchorUtc.AddDays(-10 + index),
                900m + (index * 210m),
                "USD",
                index % 2 == 0 ? "paid" : "open",
                documents[(index + 1) % documents.Length].Id))
            .ToArray();

        var transactions = new List<FinanceTransaction>(capacity: 50);
        for (var index = 0; index < 50; index++)
        {
            var isRevenue = index % 2 == 0;
            var account = isRevenue ? accounts[index % 2] : accounts[2];
            var counterparty = counterparties[index % counterparties.Length];
            var invoice = isRevenue && index < invoices.Length * 2 ? invoices[index % invoices.Length] : null;
            var bill = !isRevenue && index < bills.Length * 2 ? bills[index % bills.Length] : null;
            var amount = isRevenue
                ? 450m + (index * 17.5m)
                : -125m - (index * 9.25m);

            transactions.Add(new FinanceTransaction(
                StableId(companyId, $"transaction:{index:0000}"),
                companyId,
                account.Id,
                counterparty.Id,
                invoice?.Id,
                bill?.Id,
                anchorUtc.AddDays(-49 + index),
                isRevenue ? "customer_payment" : "vendor_payment",
                amount,
                "USD",
                isRevenue ? "Customer receipt" : "Vendor disbursement",
                $"FIN-{companyId:N}-{index:0000}",
                index < documents.Length || index >= 45 ? documents[index % documents.Length].Id : null));
        }

        var balances = accounts
            .Select(account =>
            {
                var posted = transactions
                    .Where(transaction => transaction.AccountId == account.Id)
                    .Sum(transaction => transaction.Amount);

                return new FinanceBalance(StableId(companyId, $"balance:{account.Code}"), companyId, account.Id, anchorUtc, account.OpeningBalance + posted, account.Currency);
            })
            .ToArray();

        var policy = new FinancePolicyConfiguration(StableId(companyId, "policy:default"), companyId, "USD", 10000m, 5000m, true, -10000m, 10000m, 90, 30);

        dbContext.FinanceAccounts.AddRange(accounts);
        dbContext.FinanceCounterparties.AddRange(counterparties);
        dbContext.CompanyKnowledgeDocuments.AddRange(documents);
        dbContext.FinanceInvoices.AddRange(invoices);
        dbContext.FinanceBills.AddRange(bills);
        dbContext.FinanceTransactions.AddRange(transactions);
        dbContext.FinanceBalances.AddRange(balances);
        dbContext.FinancePolicyConfigurations.Add(policy);

        return new FinanceSeedResult(
            accounts.Select(x => x.Id).ToArray(),
            counterparties.Select(x => x.Id).ToArray(),
            invoices.Select(x => x.Id).ToArray(),
            bills.Select(x => x.Id).ToArray(),
            transactions.Select(x => x.Id).ToArray(),
            balances.Select(x => x.Id).ToArray(),
            documents.Select(x => x.Id).ToArray(),
            policy.Id);
    }

    private static Guid StableId(Guid companyId, string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"{companyId:N}:{name}"));

        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash);
    }

    private static CompanyKnowledgeDocumentAccessScope BuildDocumentAccessScope(Guid companyId, int index)
    {
        Dictionary<string, System.Text.Json.Nodes.JsonNode?>? additionalProperties = null;
        if (index == 5)
        {
            additionalProperties = new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["restricted"] = System.Text.Json.Nodes.JsonValue.Create(true),
                ["roles"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonValue.Create("owner"))
            };
        }

        return new CompanyKnowledgeDocumentAccessScope(
            companyId,
            CompanyKnowledgeDocumentAccessScope.CompanyVisibility,
            additionalProperties);
    }
}

public sealed record FinanceSeedResult(
    IReadOnlyList<Guid> AccountIds,
    IReadOnlyList<Guid> CounterpartyIds,
    IReadOnlyList<Guid> InvoiceIds,
    IReadOnlyList<Guid> BillIds,
    IReadOnlyList<Guid> TransactionIds,
    IReadOnlyList<Guid> BalanceIds,
    IReadOnlyList<Guid> DocumentIds,
    Guid PolicyConfigurationId);
