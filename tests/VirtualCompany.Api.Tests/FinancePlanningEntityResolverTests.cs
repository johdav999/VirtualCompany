using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePlanningEntityResolverTests
{
    [Fact]
    public async Task Authoritative_resolver_supports_all_declared_reference_types()
    {
        await using var db = CreateContext();
        var companyId = Guid.NewGuid();
        var customer = new FinanceCounterparty(Guid.NewGuid(), companyId, "Northwind", "customer");
        var supplier = new FinanceCounterparty(Guid.NewGuid(), companyId, "Contoso", "supplier");
        var period = new FiscalPeriod(Guid.NewGuid(), companyId, "August 2026",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var invoice = new FinanceInvoice(Guid.NewGuid(), companyId, customer.Id, "1042",
            DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(20), 100m, "SEK", "open");
        var bill = new FinanceBill(Guid.NewGuid(), companyId, supplier.Id, "B-88",
            DateTime.UtcNow.AddDays(-8), DateTime.UtcNow.AddDays(12), 80m, "SEK", "open");
        var migration = new AccountingProviderSwitch(
            Guid.NewGuid(), companyId,
            new AccountingProviderEndpoint(AccountingProviderEndpointKinds.Internal, null),
            new AccountingProviderEndpoint(AccountingProviderEndpointKinds.External, "fortnox"),
            period.Id,
            AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems,
            "Test migration",
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "migration-reference",
            DateTime.UtcNow);
        var account = new FinanceAccount(Guid.NewGuid(), companyId, "6540", "IT services", "expense", "SEK", 0m,
            DateTime.UtcNow, accountClass: FinanceAccountClassValues.Expense, normalBalance: "debit", isPostingEnabled: true);
        var voucherSeries = new VoucherSeries(Guid.NewGuid(), companyId, "A", "General journals", "A", true, DateTime.UtcNow);
        var journal = new LedgerEntry(Guid.NewGuid(), companyId, period.Id, "A-42", DateTime.UtcNow,
            LedgerEntryStatuses.Posted, "Hosting", postingDate: new DateOnly(2026, 8, 15), baseCurrency: "SEK");
        var definition = new ReportDefinition(Guid.NewGuid(), companyId, "P_AND_L", "Profit and loss", "profit_and_loss",
            "system-profit-and-loss", Guid.NewGuid(), DateTime.UtcNow);
        var definitionVersion = new ReportDefinitionVersion(Guid.NewGuid(), companyId, definition.Id, 1,
            definition.Name, definition.ReportKind, Guid.NewGuid(), DateTime.UtcNow);
        var section = new ReportDefinitionSection(Guid.NewGuid(), companyId, definitionVersion.Id, "EXPENSES", "Expenses", 1);
        var reportLine = new ReportDefinitionLine(Guid.NewGuid(), companyId, definitionVersion.Id, section.Id,
            "IT_COSTS", "IT costs", ReportDefinitionLineTypes.Detail, 1, null, ReportDefinitionSignRules.Normal,
            1, 0, false, ReportDefinitionCurrencyModes.Functional, null, null);
        db.AddRange(customer, supplier, period, invoice, bill, migration, account, voucherSeries, journal,
            definition, definitionVersion, section, reportLine);
        await db.SaveChangesAsync();
        var resolver = new FinancePlanningEntityResolver(db);

        var cases = new[]
        {
            (FinancePlanningReferenceTypes.Invoice, "1042", invoice.Id),
            (FinancePlanningReferenceTypes.Bill, "B-88", bill.Id),
            (FinancePlanningReferenceTypes.Customer, "Northwind", customer.Id),
            (FinancePlanningReferenceTypes.Supplier, "Contoso", supplier.Id),
            (FinancePlanningReferenceTypes.FiscalPeriod, "August 2026", period.Id),
            (FinancePlanningReferenceTypes.Migration, migration.Id.ToString(), migration.Id),
            (FinancePlanningReferenceTypes.Account, "6540", account.Id),
            (FinancePlanningReferenceTypes.Journal, "A-42", journal.Id),
            (FinancePlanningReferenceTypes.VoucherSeries, "A", voucherSeries.Id),
            (FinancePlanningReferenceTypes.ReportDefinition, "P_AND_L", definition.Id),
            (FinancePlanningReferenceTypes.ReportLine, "IT_COSTS", reportLine.Id)
        };
        foreach (var (type, value, expectedId) in cases)
        {
            var result = await resolver.ResolveAsync(
                new FinanceEntityResolutionRequest(companyId, type, value, 5), default);
            Assert.Equal(FinanceEntityResolutionStates.Resolved, result.State);
            var match = Assert.Single(result.Candidates);
            Assert.Equal(expectedId.ToString(), match.EntityId);
            Assert.NotEmpty(match.SourceVersion);
        }
    }

    [Fact]
    public async Task Duplicate_accessible_invoice_is_ambiguous_and_foreign_invoice_is_not_revealed()
    {
        await using var db = CreateContext();
        var companyId = Guid.NewGuid();
        var foreignCompanyId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        db.FinanceInvoices.AddRange(
            Invoice(companyId, counterpartyId, "1042"),
            Invoice(companyId, counterpartyId, "1042"),
            Invoice(foreignCompanyId, Guid.NewGuid(), "FOREIGN-9"));
        await db.SaveChangesAsync();
        var resolver = new FinancePlanningEntityResolver(db);

        var ambiguous = await resolver.ResolveAsync(new FinanceEntityResolutionRequest(
            companyId, FinancePlanningReferenceTypes.Invoice, "1042", 5), default);
        var foreign = await resolver.ResolveAsync(new FinanceEntityResolutionRequest(
            companyId, FinancePlanningReferenceTypes.Invoice, "FOREIGN-9", 5), default);

        Assert.Equal(FinanceEntityResolutionStates.Ambiguous, ambiguous.State);
        Assert.Equal(2, ambiguous.Candidates.Count);
        Assert.Equal(FinanceEntityResolutionStates.NotFound, foreign.State);
        Assert.Empty(foreign.Candidates);
    }

    private static FinanceInvoice Invoice(Guid companyId, Guid counterpartyId, string number) =>
        new(Guid.NewGuid(), companyId, counterpartyId, number,
            DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(28), 100m, "SEK", "open");

    private static VirtualCompanyDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
