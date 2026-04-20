using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

public static class DeterministicFinanceSeedDatasetGenerator
{
    private const string Currency = "USD";
    private static readonly DateTime DefaultSeedAnchorUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static FinanceSeedDataset Generate(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        int seedValue,
        FinanceAnomalyInjectionOptions? anomalyOptions = null,
        bool includeTransactions = true) =>
        Generate(dbContext, companyId, seedValue, DefaultSeedAnchorUtc, anomalyOptions, includeTransactions);

    public static FinanceSeedDataset Generate(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        int seedValue,
        DateTime seedAnchorUtc,
        FinanceAnomalyInjectionOptions? anomalyOptions = null,
        bool includeTransactions = true)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        var anchorUtc = seedAnchorUtc.Kind == DateTimeKind.Utc
            ? seedAnchorUtc
            : seedAnchorUtc.ToUniversalTime();
        var windowStartUtc = anchorUtc.Date.AddDays(-89);
        var windowEndUtc = anchorUtc.Date;
        var random = new Random(StableSeed(companyId, seedValue));

        var accounts = CreateAccounts(companyId, windowStartUtc);
        var counterparties = CreateCounterparties(companyId);
        var documents = CreateDocuments(companyId);
        var categoryIds = new[]
        {
            "customer_payment",
            "consulting_revenue",
            "software_subscriptions",
            "cloud_hosting",
            "office_supplies",
            "telecom",
            "insurance",
            "rent",
            "utilities",
            "payroll"
        };

        var allCategoryIds = categoryIds.Append("supplier_payment").ToArray();
        var invoices = CreateInvoices(companyId, counterparties, documents, windowStartUtc, random).ToList();
        var bills = CreateBills(companyId, counterparties, documents, windowStartUtc, random);
        var recurringExpenses = CreateRecurringExpenses(companyId, counterparties);
        var transactions = includeTransactions
            ? CreateTransactions(
                companyId,
                accounts,
                counterparties,
                documents,
                invoices,
                bills,
                recurringExpenses,
                windowStartUtc,
                windowEndUtc,
                random).ToList()
            : [];
        var anomalies = CreateAnomalies(
            companyId,
            seedValue,
            anomalyOptions ?? FinanceAnomalyInjectionOptions.Disabled,
            accounts,
            counterparties,
            documents,
            invoices,
            transactions,
            windowEndUtc);
        var balances = CreateBalances(companyId, accounts, transactions, windowEndUtc);
        var policy = new FinancePolicyConfiguration(
            StableId(companyId, seedValue, "policy:default"),
            companyId,
            Currency,
            10000m,
            6000m,
            true,
            -15000m,
            20000m,
            90,
            30);

        dbContext.FinanceAccounts.AddRange(accounts);
        dbContext.FinanceCounterparties.AddRange(counterparties);
        dbContext.CompanyKnowledgeDocuments.AddRange(documents);
        dbContext.FinanceInvoices.AddRange(invoices);
        dbContext.FinanceBills.AddRange(bills);
        if (includeTransactions)
        {
            dbContext.FinanceTransactions.AddRange(transactions);
        }
        dbContext.FinanceBalances.AddRange(balances);
        dbContext.FinancePolicyConfigurations.Add(policy);
        dbContext.FinanceSeedAnomalies.AddRange(anomalies);

        var dataset = new FinanceSeedDataset(
            companyId,
            seedValue,
            windowStartUtc,
            windowEndUtc,
            accounts.Select(x => x.Id).ToArray(),
            counterparties.Select(x => x.Id).ToArray(),
            counterparties.Where(x => x.CounterpartyType is "supplier" or "vendor").Select(x => x.Id).ToArray(),
            allCategoryIds,
            invoices.Select(x => x.Id).ToArray(),
            bills.Select(x => x.Id).ToArray(),
            recurringExpenses,
            transactions.Select(x => x.Id).ToArray(),
            balances.Select(x => x.Id).ToArray(),
            documents.Select(x => x.Id).ToArray(),
            policy.Id,
            FinanceSeedDatasetConsistencyValidator.Validate(new FinanceSeedDatasetValidationInput(
                companyId,
                windowStartUtc,
                windowEndUtc,
                includeTransactions,
                allCategoryIds,
                accounts,
                counterparties,
                documents,
                invoices,
                bills,
                recurringExpenses,
                transactions,
                balances)),
            anomalies);

        return dataset;
    }

    private static IReadOnlyList<FinanceAccount> CreateAccounts(Guid companyId, DateTime windowStartUtc) =>
    [
        new(
            StableId(companyId, 0, "account:operating-cash"),
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            Currency,
            42000m,
            windowStartUtc),
        new(
            StableId(companyId, 0, "account:receivables"),
            companyId,
            "1100",
            "Receivables",
            "asset",
            Currency,
            0m,
            windowStartUtc),
        new(
            StableId(companyId, 0, "account:payables"),
            companyId,
            "2000",
            "Payables",
            "liability",
            Currency,
            0m,
            windowStartUtc),
        new(
            StableId(companyId, 0, "account:expense-clearing"),
            companyId,
            "6100",
            "Expense Clearing",
            "expense",
            Currency,
            0m,
            windowStartUtc)
    ];

    private static IReadOnlyList<FinanceCounterparty> CreateCounterparties(Guid companyId) =>
    [
        new(
            StableId(companyId, 0, "counterparty:alpine-studio"),
            companyId,
            "Alpine Studio",
            "customer",
            "ap@alpine.example"),
        new(
            StableId(companyId, 0, "counterparty:bluebird-retail"),
            companyId,
            "Bluebird Retail",
            "customer",
            "finance@bluebird.example"),
        new(
            StableId(companyId, 0, "counterparty:contoso-cloud"),
            companyId,
            "Contoso Cloud",
            "supplier",
            "billing@contoso.example"),
        new(
            StableId(companyId, 0, "counterparty:fabrikam-office"),
            companyId,
            "Fabrikam Office",
            "supplier",
            "ar@fabrikam.example"),
        new(
            StableId(companyId, 0, "counterparty:tailspin-telecom"),
            companyId,
            "Tailspin Telecom",
            "supplier",
            "billing@tailspin.example"),
        new(
            StableId(companyId, 0, "counterparty:northwind-insurance"),
            companyId,
            "Northwind Insurance",
            "supplier",
            "accounts@northwind.example"),
        new(
            StableId(companyId, 0, "counterparty:wide-world-rentals"),
            companyId,
            "Wide World Rentals",
            "supplier",
            "rent@wideworld.example")
    ];

    private static IReadOnlyList<CompanyKnowledgeDocument> CreateDocuments(Guid companyId) =>
        Enumerable.Range(1, 12)
            .Select(index => new CompanyKnowledgeDocument(
                StableId(companyId, 0, $"finance-document:{index:000}"),
                companyId,
                $"Seed finance document {index:000}",
                CompanyKnowledgeDocumentType.Report,
                $"seed-finance/{companyId:N}/document-{index:000}.pdf",
                null,
                $"seed-finance-document-{index:000}.pdf",
                "application/pdf",
                ".pdf",
                2048 + index,
                new Dictionary<string, JsonNode?>
                {
                    ["category"] = JsonValue.Create("finance"),
                    ["department"] = JsonValue.Create("finance"),
                    ["seed"] = JsonValue.Create(true)
                },
                new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility),
                $"deterministic-finance-seed:{index:000}"))
            .ToArray();

    private static IReadOnlyList<FinanceInvoice> CreateInvoices(
        Guid companyId,
        IReadOnlyList<FinanceCounterparty> counterparties,
        IReadOnlyList<CompanyKnowledgeDocument> documents,
        DateTime windowStartUtc,
        Random random)
    {
        var customers = counterparties.Where(x => x.CounterpartyType == "customer").ToArray();
        return Enumerable.Range(0, 9)
            .Select(index =>
            {
                var issuedUtc = windowStartUtc.AddDays(6 + (index * 9));
                var amount = Money(2800m + (index * 315m) + random.Next(0, 6) * 125m);
                return new FinanceInvoice(
                    StableId(companyId, 0, $"invoice:{index:000}"),
                    companyId,
                    customers[index % customers.Length].Id,
                    $"INV-{issuedUtc:yyyyMM}-{index + 1:000}",
                    issuedUtc,
                    issuedUtc.AddDays(21),
                    amount,
                    Currency,
                    "paid",
                    documents[index % documents.Count].Id);
            })
            .ToArray();
    }

    private static IReadOnlyList<FinanceBill> CreateBills(
        Guid companyId,
        IReadOnlyList<FinanceCounterparty> counterparties,
        IReadOnlyList<CompanyKnowledgeDocument> documents,
        DateTime windowStartUtc,
        Random random)
    {
        var suppliers = counterparties.Where(x => x.CounterpartyType == "supplier").ToArray();
        return Enumerable.Range(0, 8)
            .Select(index =>
            {
                var receivedUtc = windowStartUtc.AddDays(8 + (index * 10));
                var amount = Money(900m + (index * 155m) + random.Next(0, 5) * 85m);
                return new FinanceBill(
                    StableId(companyId, 0, $"bill:{index:000}"),
                    companyId,
                    suppliers[index % suppliers.Length].Id,
                    $"BILL-{receivedUtc:yyyyMM}-{index + 1:000}",
                    receivedUtc,
                    receivedUtc.AddDays(14),
                    amount,
                    Currency,
                    "paid",
                    documents[(index + 3) % documents.Count].Id);
            })
            .ToArray();
    }

    private static IReadOnlyList<FinanceRecurringExpenseSeed> CreateRecurringExpenses(
        Guid companyId,
        IReadOnlyList<FinanceCounterparty> counterparties)
    {
        var suppliers = counterparties.Where(x => x.CounterpartyType == "supplier").ToArray();
        return
        [
            new(
                StableId(companyId, 0, "recurring:cloud-hosting"),
                companyId,
                suppliers[0].Id,
                "cloud_hosting",
                "Monthly cloud hosting",
                1250m,
                Currency,
                "monthly",
                5),
            new(
                StableId(companyId, 0, "recurring:office-rent"),
                companyId,
                suppliers[4].Id,
                "rent",
                "Monthly office rent",
                3200m,
                Currency,
                "monthly",
                1),
            new(
                StableId(companyId, 0, "recurring:telecom"),
                companyId,
                suppliers[2].Id,
                "telecom",
                "Biweekly telecom service",
                275m,
                Currency,
                "biweekly",
                3),
            new(
                StableId(companyId, 0, "recurring:insurance"),
                companyId,
                suppliers[3].Id,
                "insurance",
                "Monthly insurance premium",
                640m,
                Currency,
                "monthly",
                15)
        ];
    }

    private static IReadOnlyList<FinanceTransaction> CreateTransactions(
        Guid companyId,
        IReadOnlyList<FinanceAccount> accounts,
        IReadOnlyList<FinanceCounterparty> counterparties,
        IReadOnlyList<CompanyKnowledgeDocument> documents,
        IReadOnlyList<FinanceInvoice> invoices,
        IReadOnlyList<FinanceBill> bills,
        IReadOnlyList<FinanceRecurringExpenseSeed> recurringExpenses,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        Random random)
    {
        var operatingCash = accounts.Single(x => x.Code == "1000");
        var expenseClearing = accounts.Single(x => x.Code == "6100");
        var suppliers = counterparties.Where(x => x.CounterpartyType == "supplier").ToArray();
        var transactions = new List<FinanceTransaction>();

        foreach (var invoice in invoices)
        {
            var paymentUtc = invoice.IssuedUtc.AddDays(12);
            if (paymentUtc > windowEndUtc)
            {
                paymentUtc = windowEndUtc;
            }

            transactions.Add(new FinanceTransaction(
                StableId(companyId, 0, $"transaction:invoice-payment:{invoice.InvoiceNumber}"),
                companyId,
                operatingCash.Id,
                invoice.CounterpartyId,
                invoice.Id,
                null,
                paymentUtc,
                "customer_payment",
                invoice.Amount,
                Currency,
                $"Payment for {invoice.InvoiceNumber}",
                $"PAY-{invoice.InvoiceNumber}",
                invoice.DocumentId));
        }

        foreach (var bill in bills)
        {
            transactions.Add(new FinanceTransaction(
                StableId(companyId, 0, $"transaction:bill-payment:{bill.BillNumber}"),
                companyId,
                operatingCash.Id,
                bill.CounterpartyId,
                null,
                bill.Id,
                bill.ReceivedUtc.AddDays(9),
                "supplier_payment",
                -bill.Amount,
                Currency,
                $"Payment for {bill.BillNumber}",
                $"PMT-{bill.BillNumber}",
                bill.DocumentId));
        }

        foreach (var recurringExpense in recurringExpenses)
        {
            foreach (var occurrenceUtc in recurringExpense.GetOccurrences(windowStartUtc, windowEndUtc))
            {
                transactions.Add(new FinanceTransaction(
                    StableId(companyId, 0, $"transaction:recurring:{recurringExpense.Id:N}:{occurrenceUtc:yyyyMMdd}"),
                    companyId,
                    operatingCash.Id,
                    recurringExpense.SupplierId,
                    null,
                    null,
                    occurrenceUtc,
                    recurringExpense.CategoryId,
                    -recurringExpense.Amount,
                    recurringExpense.Currency,
                    recurringExpense.Name,
                    $"REC-{occurrenceUtc:yyyyMMdd}-{recurringExpense.Id:N}"[..32],
                    null));
            }
        }

        for (var dayOffset = 0; dayOffset < 90; dayOffset += 4)
        {
            var transactionUtc = windowStartUtc.AddDays(dayOffset);
            var supplier = suppliers[(dayOffset / 4) % suppliers.Length];
            var amount = Money(95m + random.Next(0, 13) * 18m);
            var category = ((dayOffset / 4) % 3) switch
            {
                0 => "office_supplies",
                1 => "software_subscriptions",
                _ => "utilities"
            };

            transactions.Add(new FinanceTransaction(
                StableId(companyId, 0, $"transaction:operating-expense:{dayOffset:000}"),
                companyId,
                expenseClearing.Id,
                supplier.Id,
                null,
                null,
                transactionUtc,
                category,
                -amount,
                Currency,
                $"Operating expense {dayOffset:000}",
                $"OPEX-{transactionUtc:yyyyMMdd}-{dayOffset:000}",
                documents[dayOffset % documents.Count].Id));
        }

        return transactions
            .OrderBy(x => x.TransactionUtc)
            .ThenBy(x => x.ExternalReference, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<FinanceSeedAnomaly> CreateAnomalies(
        Guid companyId,
        int seedValue,
        FinanceAnomalyInjectionOptions options,
        IReadOnlyList<FinanceAccount> accounts,
        IReadOnlyList<FinanceCounterparty> counterparties,
        IReadOnlyList<CompanyKnowledgeDocument> documents,
        List<FinanceInvoice> invoices,
        List<FinanceTransaction> transactions,
        DateTime windowEndUtc)
    {
        if (!options.Enabled)
        {
            return [];
        }

        var profile = NormalizeProfile(options.ScenarioProfile);
        var operatingCash = accounts.Single(x => x.Code == "1000");
        var expenseClearing = accounts.Single(x => x.Code == "6100");
        var customer = counterparties.First(x => x.CounterpartyType == "customer");
        var suppliers = counterparties.Where(x => x.CounterpartyType is "supplier" or "vendor").ToArray();
        var anomalies = new List<FinanceSeedAnomaly>();

        if (ScenarioIncludes(profile, "unusually_high_invoice"))
        {
            var invoice = new FinanceInvoice(
                StableId(companyId, seedValue, "anomaly:unusually-high-invoice:invoice"),
                companyId,
                customer.Id,
                $"ANOM-HIGH-{seedValue:000000}",
                windowEndUtc.AddDays(-12),
                windowEndUtc.AddDays(9),
                48000m,
                Currency,
                "paid",
                documents[0].Id);
            var transaction = new FinanceTransaction(
                StableId(companyId, seedValue, "anomaly:unusually-high-invoice:transaction"),
                companyId,
                operatingCash.Id,
                customer.Id,
                invoice.Id,
                null,
                invoice.IssuedUtc.AddDays(4),
                "customer_payment",
                invoice.Amount,
                Currency,
                $"Anomalous payment for {invoice.InvoiceNumber}",
                $"ANOM-HIGH-PAY-{seedValue:000000}",
                invoice.DocumentId);

            invoices.Add(invoice);
            transactions.Add(transaction);
            anomalies.Add(CreateAnomaly(
                companyId,
                seedValue,
                profile,
                "unusually_high_invoice",
                [invoice.Id, transaction.Id, documents[0].Id],
                new JsonObject
                {
                    ["expectedDetector"] = JsonValue.Create("invoice_amount_outlier"),
                    ["thresholdAmount"] = JsonValue.Create(10000m),
                    ["observedAmount"] = JsonValue.Create(invoice.Amount),
                    ["confidence"] = JsonValue.Create(0.96m)
                }));
        }

        if (ScenarioIncludes(profile, "duplicate_vendor_charge"))
        {
            var supplier = suppliers[0];
            var amount = 1187.42m;
            var first = new FinanceTransaction(
                StableId(companyId, seedValue, "anomaly:duplicate-vendor-charge:first"),
                companyId,
                operatingCash.Id,
                supplier.Id,
                null,
                null,
                windowEndUtc.AddDays(-7),
                "software_subscriptions",
                -amount,
                Currency,
                "Duplicate vendor subscription charge",
                $"ANOM-DUP-A-{seedValue:000000}",
                documents[1].Id);
            var second = new FinanceTransaction(
                StableId(companyId, seedValue, "anomaly:duplicate-vendor-charge:second"),
                companyId,
                operatingCash.Id,
                supplier.Id,
                null,
                null,
                first.TransactionUtc,
                "software_subscriptions",
                -amount,
                Currency,
                "Duplicate vendor subscription charge",
                $"ANOM-DUP-B-{seedValue:000000}",
                documents[2].Id);

            transactions.Add(first);
            transactions.Add(second);
            anomalies.Add(CreateAnomaly(
                companyId,
                seedValue,
                profile,
                "duplicate_vendor_charge",
                [first.Id, second.Id, supplier.Id],
                new JsonObject
                {
                    ["expectedDetector"] = JsonValue.Create("duplicate_vendor_charge"),
                    ["matchFields"] = new JsonArray("counterpartyId", "amount", "transactionUtc", "description"),
                    ["confidence"] = JsonValue.Create(0.94m)
                }));
        }

        if (ScenarioIncludes(profile, "category_mismatch"))
        {
            var supplier = suppliers[1];
            var transaction = new FinanceTransaction(
                StableId(companyId, seedValue, "anomaly:category-mismatch:transaction"),
                companyId,
                expenseClearing.Id,
                supplier.Id,
                null,
                null,
                windowEndUtc.AddDays(-5),
                "office_supplies",
                -842m,
                Currency,
                "Security software renewal categorized as office supplies",
                $"ANOM-CAT-{seedValue:000000}",
                documents[3].Id);

            transactions.Add(transaction);
            anomalies.Add(CreateAnomaly(
                companyId,
                seedValue,
                profile,
                "category_mismatch",
                [transaction.Id, supplier.Id, documents[3].Id],
                new JsonObject
                {
                    ["expectedDetector"] = JsonValue.Create("category_document_mismatch"),
                    ["observedCategory"] = JsonValue.Create("office_supplies"),
                    ["expectedCategory"] = JsonValue.Create("software_subscriptions"),
                    ["confidence"] = JsonValue.Create(0.91m)
                }));
        }

        if (ScenarioIncludes(profile, "missing_receipt"))
        {
            var supplier = suppliers[2];
            var transaction = new FinanceTransaction(
                StableId(companyId, seedValue, "anomaly:missing-receipt:transaction"),
                companyId,
                expenseClearing.Id,
                supplier.Id,
                null,
                null,
                windowEndUtc.AddDays(-3),
                "office_supplies",
                -319m,
                Currency,
                "Card expense missing receipt attachment",
                $"ANOM-RECEIPT-{seedValue:000000}",
                null);

            transactions.Add(transaction);
            anomalies.Add(CreateAnomaly(
                companyId,
                seedValue,
                profile,
                "missing_receipt",
                [transaction.Id, supplier.Id],
                new JsonObject
                {
                    ["expectedDetector"] = JsonValue.Create("missing_receipt"),
                    ["requiredDocumentType"] = JsonValue.Create("receipt"),
                    ["confidence"] = JsonValue.Create(0.9m)
                }));
        }

        return anomalies;
    }

    private static FinanceSeedAnomaly CreateAnomaly(
        Guid companyId,
        int seedValue,
        string profile,
        string anomalyType,
        IReadOnlyCollection<Guid> affectedRecordIds,
        JsonObject expectedDetectionMetadata) =>
        new(
            StableId(companyId, seedValue, $"anomaly:{profile}:{anomalyType}"),
            companyId,
            anomalyType,
            profile,
            affectedRecordIds,
            expectedDetectionMetadata.ToJsonString());

    private static bool ScenarioIncludes(string profile, string anomalyType) =>
        profile switch
        {
            "all" or "baseline" => true,
            "invoice_only" => anomalyType == "unusually_high_invoice",
            "vendor_risk" => anomalyType is "duplicate_vendor_charge" or "category_mismatch",
            "document_audit" => anomalyType is "category_mismatch" or "missing_receipt",
            _ => false
        };

    private static string NormalizeProfile(string? profile) =>
        string.IsNullOrWhiteSpace(profile)
            ? "baseline"
            : profile.Trim().Replace('-', '_').ToLowerInvariant();

    private static IReadOnlyList<FinanceBalance> CreateBalances(
        Guid companyId,
        IReadOnlyList<FinanceAccount> accounts,
        IReadOnlyList<FinanceTransaction> transactions,
        DateTime windowEndUtc) =>
        accounts
            .Select(account =>
            {
                var posted = transactions
                    .Where(transaction => transaction.AccountId == account.Id)
                    .Sum(transaction => transaction.Amount);
                return new FinanceBalance(
                    StableId(companyId, 0, $"balance:{account.Code}"),
                    companyId,
                    account.Id,
                    windowEndUtc,
                    account.OpeningBalance + posted,
                    account.Currency);
            })
            .ToArray();

    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static int StableSeed(Guid companyId, int seedValue)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{companyId:N}:{seedValue}"));
        return BitConverter.ToInt32(hash, 0);
    }

    private static Guid StableId(Guid companyId, int seedValue, string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"{companyId:N}:{seedValue}:{name}"));

        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash);
    }
}

public sealed record FinanceSeedDataset(
    Guid CompanyId,
    int SeedValue,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    IReadOnlyList<Guid> AccountIds,
    IReadOnlyList<Guid> CounterpartyIds,
    IReadOnlyList<Guid> SupplierIds,
    IReadOnlyList<string> CategoryIds,
    IReadOnlyList<Guid> InvoiceIds,
    IReadOnlyList<Guid> BillIds,
    IReadOnlyList<FinanceRecurringExpenseSeed> RecurringExpenses,
    IReadOnlyList<Guid> TransactionIds,
    IReadOnlyList<Guid> BalanceIds,
    IReadOnlyList<Guid> DocumentIds,
    Guid PolicyConfigurationId,
    IReadOnlyList<FinanceSeedValidationError> ValidationErrors,
    IReadOnlyList<FinanceSeedAnomaly> Anomalies);

public sealed record FinanceAnomalyInjectionOptions(bool Enabled, string ScenarioProfile = "baseline")
{
    public static FinanceAnomalyInjectionOptions Disabled { get; } = new(false);
}

public sealed record FinanceRecurringExpenseSeed(
    Guid Id,
    Guid CompanyId,
    Guid SupplierId,
    string CategoryId,
    string Name,
    decimal Amount,
    string Currency,
    string Cadence,
    int DayOfPeriod)
{
    public IEnumerable<DateTime> GetOccurrences(DateTime windowStartUtc, DateTime windowEndUtc)
    {
        if (Cadence.Equals("monthly", StringComparison.OrdinalIgnoreCase))
        {
            var current = new DateTime(windowStartUtc.Year, windowStartUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            while (current <= windowEndUtc)
            {
                var day = Math.Min(DayOfPeriod, DateTime.DaysInMonth(current.Year, current.Month));
                var occurrence = new DateTime(current.Year, current.Month, day, 0, 0, 0, DateTimeKind.Utc);
                if (occurrence >= windowStartUtc && occurrence <= windowEndUtc)
                {
                    yield return occurrence;
                }

                current = current.AddMonths(1);
            }

            yield break;
        }

        if (Cadence.Equals("biweekly", StringComparison.OrdinalIgnoreCase))
        {
            var current = windowStartUtc.AddDays(Math.Max(0, DayOfPeriod - 1));
            while (current <= windowEndUtc)
            {
                yield return current;
                current = current.AddDays(14);
            }

            yield break;
        }

        throw new InvalidOperationException($"Unsupported recurring expense cadence '{Cadence}'.");
    }
}

public sealed record FinanceSeedValidationError(string Code, string Message);
