using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSeedDatasetGeneratorTests
{
    [Fact]
    public async Task Generates_60_to_90_days_of_linked_financial_history()
    {
        var companyId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Seed Dataset Company"));

        var dataset = DeterministicFinanceSeedDatasetGenerator.Generate(
            dbContext,
            companyId,
            913,
            new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        Assert.Equal(90, (dataset.WindowEndUtc - dataset.WindowStartUtc).Days + 1);
        Assert.Empty(dataset.ValidationErrors);
        Assert.NotEmpty(dataset.InvoiceIds);
        Assert.NotEmpty(dataset.BillIds);
        Assert.NotEmpty(dataset.SupplierIds);
        Assert.NotEmpty(dataset.RecurringExpenses);
        Assert.NotEmpty(await dbContext.Payments.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync());
        Assert.NotEmpty(dataset.CategoryIds);
        Assert.NotEmpty(dataset.PaymentIds);
        Assert.InRange(dataset.CategoryIds.Count, 10, int.MaxValue);

        var transactions = await dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync();

        var payments = await dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync();

        Assert.All(transactions, transaction =>
        {
            Assert.InRange(transaction.TransactionUtc, dataset.WindowStartUtc, dataset.WindowEndUtc);
            Assert.Contains(transaction.Id, dataset.TransactionIds);
            Assert.Contains(transaction.TransactionType, dataset.CategoryIds);
        });

        Assert.All(payments, payment => Assert.InRange(payment.PaymentDate, dataset.WindowStartUtc, dataset.WindowEndUtc));
        Assert.Contains(payments, payment => payment.PaymentType == "incoming");
        Assert.Contains(payments, payment => payment.PaymentType == "outgoing");
    }

    [Fact]
    public async Task Generated_records_have_no_broken_foreign_keys()
    {
        var companyId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Foreign Key Seed Company"));

        DeterministicFinanceSeedDatasetGenerator.Generate(dbContext, companyId, 42);
        await dbContext.SaveChangesAsync();

        var accountIds = await dbContext.FinanceAccounts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).Select(x => x.Id).ToArrayAsync();
        var counterpartyIds = await dbContext.FinanceCounterparties.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).Select(x => x.Id).ToArrayAsync();
        var invoiceIds = await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).Select(x => x.Id).ToArrayAsync();
        var billIds = await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).Select(x => x.Id).ToArrayAsync();
        var documentIds = await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).Select(x => x.Id).ToArrayAsync();
        var transactions = await dbContext.FinanceTransactions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync();

        Assert.All(transactions, transaction =>
        {
            Assert.Contains(transaction.AccountId, accountIds);
            if (transaction.CounterpartyId.HasValue)
            {
                Assert.Contains(transaction.CounterpartyId.Value, counterpartyIds);
            }

            if (transaction.InvoiceId.HasValue)
            {
                Assert.Contains(transaction.InvoiceId.Value, invoiceIds);
            }

            if (transaction.BillId.HasValue)
            {
                Assert.Contains(transaction.BillId.Value, billIds);
            }

            if (transaction.DocumentId.HasValue)
            {
                Assert.Contains(transaction.DocumentId.Value, documentIds);
            }
        });
    }

    [Fact]
    public async Task Recurring_expenses_create_transaction_instances_aligned_to_cadence()
    {
        var companyId = Guid.Parse("cccccccc-dddd-eeee-ffff-111111111111");
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Recurring Seed Company"));

        var dataset = DeterministicFinanceSeedDatasetGenerator.Generate(
            dbContext,
            companyId,
            77,
            new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var transactions = await dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync();

        foreach (var recurringExpense in dataset.RecurringExpenses)
        {
            var expectedDates = recurringExpense.GetOccurrences(dataset.WindowStartUtc, dataset.WindowEndUtc).ToArray();
            var actualDates = transactions
                .Where(x => x.CounterpartyId == recurringExpense.SupplierId)
                .Where(x => x.TransactionType == recurringExpense.CategoryId)
                .Where(x => x.Amount == -recurringExpense.Amount)
                .Select(x => x.TransactionUtc)
                .OrderBy(x => x)
                .ToArray();

            Assert.Equal(expectedDates, actualDates);
        }
    }

    [Fact]
    public async Task Same_company_and_seed_value_produce_identical_output()
    {
        var companyId = Guid.Parse("dddddddd-eeee-ffff-1111-222222222222");
        var anchorUtc = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        var first = await GenerateSnapshotAsync(companyId, 1234, anchorUtc);
        var second = await GenerateSnapshotAsync(companyId, 1234, anchorUtc);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Validation_confirms_internal_ledger_invoice_bill_and_link_consistency()
    {
        var companyId = Guid.Parse("eeeeeeee-ffff-1111-2222-333333333333");
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Validation Seed Company"));

        var dataset = DeterministicFinanceSeedDatasetGenerator.Generate(dbContext, companyId, 501);
        await dbContext.SaveChangesAsync();

        Assert.Empty(dataset.ValidationErrors);

        var invoices = await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync();
        var bills = await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync();
        var transactions = await dbContext.FinanceTransactions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync();
        var accounts = await dbContext.FinanceAccounts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync();
        var balances = await dbContext.FinanceBalances.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync();

        Assert.All(invoices, invoice => Assert.Equal(invoice.Amount, transactions.Where(x => x.InvoiceId == invoice.Id).Sum(x => x.Amount)));
        Assert.All(bills, bill => Assert.Equal(bill.Amount, transactions.Where(x => x.BillId == bill.Id).Sum(x => Math.Abs(x.Amount))));
        Assert.All(balances, balance =>
        {
            var account = accounts.Single(x => x.Id == balance.AccountId);
            Assert.Equal(account.OpeningBalance + transactions.Where(x => x.AccountId == account.Id).Sum(x => x.Amount), balance.Amount);
        });
    }

    [Fact]
    public async Task Consistency_validator_reports_zero_errors_for_generated_dataset()
    {
        var companyId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Validator Seed Company"));

        var dataset = DeterministicFinanceSeedDatasetGenerator.Generate(dbContext, companyId, 160102);
        await dbContext.SaveChangesAsync();

        var errors = FinanceSeedDatasetConsistencyValidator.Validate(new FinanceSeedDatasetValidationInput(
            companyId,
            dataset.WindowStartUtc,
            dataset.WindowEndUtc,
            dataset.CategoryIds,
            await dbContext.FinanceAccounts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync(),
            await dbContext.FinanceCounterparties.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync(),
            await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync(),
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync(),
            await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync(),
            dataset.RecurringExpenses,
            await dbContext.FinanceTransactions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync(),
            await dbContext.FinanceBalances.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync()));

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Anomaly_injection_adds_traceable_queryable_finance_scenarios()
    {
        var companyId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Anomaly Seed Company"));

        var dataset = DeterministicFinanceSeedDatasetGenerator.Generate(
            dbContext,
            companyId,
            160201,
            new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
            new FinanceAnomalyInjectionOptions(true, "baseline"));
        await dbContext.SaveChangesAsync();

        Assert.Empty(dataset.ValidationErrors);
        Assert.Equal(
            [
                "category_mismatch",
                "duplicate_vendor_charge",
                "missing_receipt",
                "unusually_high_invoice"
            ],
            dataset.Anomalies.Select(x => x.AnomalyType).Order(StringComparer.Ordinal).ToArray());

        foreach (var anomaly in dataset.Anomalies)
        {
            var stored = await dbContext.FinanceSeedAnomalies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(x => x.Id == anomaly.Id);

            Assert.Equal(companyId, stored.CompanyId);
            Assert.Equal(anomaly.AnomalyType, stored.AnomalyType);
            Assert.NotEmpty(stored.GetAffectedRecordIds());
            Assert.Contains("expectedDetector", stored.ExpectedDetectionMetadataJson);
        }

        var readService = new CompanyFinanceReadService(dbContext);
        foreach (var anomaly in dataset.Anomalies)
        {
            var queried = await readService.GetSeedAnomalyByIdAsync(
                new GetFinanceSeedAnomalyByIdQuery(companyId, anomaly.Id),
                CancellationToken.None);

            Assert.NotNull(queried);
            Assert.Equal(anomaly.Id, queried.Id);
            Assert.Equal(anomaly.AnomalyType, queried.AnomalyType);
        }

        var queriedByCompany = await readService.GetSeedAnomaliesAsync(
            new GetFinanceSeedAnomaliesQuery(companyId),
            CancellationToken.None);

        Assert.Equal(dataset.Anomalies.Count, queriedByCompany.Count);
        Assert.Equal(
            dataset.Anomalies.Select(x => x.Id).Order().ToArray(),
            queriedByCompany.Select(x => x.Id).Order().ToArray());

        var queriedByType = await readService.GetSeedAnomaliesAsync(
            new GetFinanceSeedAnomaliesQuery(companyId, AnomalyType: "duplicate_vendor_charge"),
            CancellationToken.None);

        Assert.Single(queriedByType);
        Assert.Equal("duplicate_vendor_charge", queriedByType[0].AnomalyType);

        var affectedRecordId = queriedByType[0].AffectedRecordIds[0];
        var queriedByAffectedRecord = await readService.GetSeedAnomaliesAsync(
            new GetFinanceSeedAnomaliesQuery(companyId, AffectedRecordId: affectedRecordId),
            CancellationToken.None);

        Assert.Contains(queriedByAffectedRecord, x => x.Id == queriedByType[0].Id);

        var transactions = await dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync();
        var invoices = await dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync();

        var highInvoice = dataset.Anomalies.Single(x => x.AnomalyType == "unusually_high_invoice");
        Assert.Contains(invoices, x => highInvoice.GetAffectedRecordIds().Contains(x.Id) && x.Amount > 10000m);

        var duplicateCharge = dataset.Anomalies.Single(x => x.AnomalyType == "duplicate_vendor_charge");
        Assert.Equal(2, transactions.Count(x => duplicateCharge.GetAffectedRecordIds().Contains(x.Id)));

        var categoryMismatch = dataset.Anomalies.Single(x => x.AnomalyType == "category_mismatch");
        Assert.Contains(transactions, x => categoryMismatch.GetAffectedRecordIds().Contains(x.Id) && x.TransactionType == "office_supplies" && x.DocumentId.HasValue);

        var missingReceipt = dataset.Anomalies.Single(x => x.AnomalyType == "missing_receipt");
        Assert.Contains(transactions, x => missingReceipt.GetAffectedRecordIds().Contains(x.Id) && !x.DocumentId.HasValue);
    }

    [Fact]
    public async Task Anomaly_injection_can_be_disabled()
    {
        var companyId = Guid.Parse("22222222-3333-4444-5555-666666666666");
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "No Anomaly Seed Company"));

        var dataset = DeterministicFinanceSeedDatasetGenerator.Generate(dbContext, companyId, 160202, anomalyOptions: FinanceAnomalyInjectionOptions.Disabled);
        await dbContext.SaveChangesAsync();

        Assert.Empty(dataset.Anomalies);
        Assert.Empty(await dbContext.FinanceSeedAnomalies.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync());
    }

    private static async Task<IReadOnlyList<FinanceTransactionSnapshot>> GenerateSnapshotAsync(Guid companyId, int seedValue, DateTime anchorUtc)
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, $"Snapshot Company {companyId:N}"));
        DeterministicFinanceSeedDatasetGenerator.Generate(dbContext, companyId, seedValue, anchorUtc);
        await dbContext.SaveChangesAsync();

        return await dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.TransactionUtc)
            .ThenBy(x => x.ExternalReference)
            .Select(x => new FinanceTransactionSnapshot(
                x.Id,
                x.AccountId,
                x.CounterpartyId,
                x.InvoiceId,
                x.BillId,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.ExternalReference))
            .ToArrayAsync();
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);

    private sealed record FinanceTransactionSnapshot(
        Guid Id,
        Guid AccountId,
        Guid? CounterpartyId,
        Guid? InvoiceId,
        Guid? BillId,
        DateTime TransactionUtc,
        string TransactionType,
        decimal Amount,
        string ExternalReference);
}
