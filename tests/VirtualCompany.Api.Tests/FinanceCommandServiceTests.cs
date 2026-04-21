using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceCommandServiceTests
{
    [Fact]
    public async Task Update_invoice_approval_status_succeeds_for_authorized_tenant()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedFinanceWriteScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);
        var result = await service.UpdateInvoiceApprovalStatusAsync(
            new UpdateFinanceInvoiceApprovalStatusCommand(companyId, seed.InvoiceId, "approved"),
            CancellationToken.None);

        Assert.Equal("approved", result.Status);
        Assert.Equal("approved", await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Update_invoice_approval_status_allows_pending_approval_transition()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedFinanceWriteScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);
        var result = await service.UpdateInvoiceApprovalStatusAsync(
            new UpdateFinanceInvoiceApprovalStatusCommand(companyId, seed.InvoiceId, "pending_approval"),
            CancellationToken.None);

        Assert.Equal("pending_approval", result.Status);
        Assert.Equal("pending_approval", await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Update_transaction_category_succeeds_for_authorized_tenant()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedFinanceWriteScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);
        var result = await service.UpdateTransactionCategoryAsync(
            new UpdateFinanceTransactionCategoryCommand(companyId, seed.TransactionId, "software_subscriptions"),
            CancellationToken.None);

        Assert.Equal("software_subscriptions", result.TransactionType);
        Assert.Equal("software_subscriptions", await dbContext.FinanceTransactions.IgnoreQueryFilters().Where(x => x.Id == seed.TransactionId).Select(x => x.TransactionType).SingleAsync());
    }

    [Fact]
    public async Task Update_transaction_category_rejects_unsupported_category_with_field_error()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedFinanceWriteScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);
        var exception = await Assert.ThrowsAsync<FinanceValidationException>(() =>
            service.UpdateTransactionCategoryAsync(
                new UpdateFinanceTransactionCategoryCommand(companyId, seed.TransactionId, "totally_invalid_category"),
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey("Category"));
        Assert.Equal("customer_payment", await dbContext.FinanceTransactions.IgnoreQueryFilters().Where(x => x.Id == seed.TransactionId).Select(x => x.TransactionType).SingleAsync());
    }

    [Fact]
    public async Task Create_counterparty_rejects_negative_credit_limit_with_field_error()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Counterparty Validation Company"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);
        var exception = await Assert.ThrowsAsync<FinanceValidationException>(() =>
            service.CreateCounterpartyAsync(
                new CreateFinanceCounterpartyCommand(
                    companyId,
                    "customer",
                    new FinanceCounterpartyUpsertDto(
                        "Fourth Coffee",
                        "finance@fourthcoffee.example",
                        "Net30",
                        null,
                        -1m,
                        "bank_transfer",
                        "1100")),
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey("CreditLimit"));
        Assert.Equal(0, await dbContext.FinanceCounterparties.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId));
    }

    [Fact]
    public async Task Finance_writes_reject_records_from_another_tenant()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyAId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seedB = SeedFinanceWriteScenario(dbContext, companyBId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateInvoiceApprovalStatusAsync(
                new UpdateFinanceInvoiceApprovalStatusCommand(companyAId, seedB.InvoiceId, "approved"),
                CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateTransactionCategoryAsync(
                new UpdateFinanceTransactionCategoryCommand(companyAId, seedB.TransactionId, "software_subscriptions"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Finance_writes_reject_invalid_invoice_status_transition()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedFinanceWriteScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);
        await service.UpdateInvoiceApprovalStatusAsync(
            new UpdateFinanceInvoiceApprovalStatusCommand(companyId, seed.InvoiceId, "approved"),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateInvoiceApprovalStatusAsync(
                new UpdateFinanceInvoiceApprovalStatusCommand(companyId, seed.InvoiceId, "open"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Finance_writes_reject_invoice_approval_above_policy_limit()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedFinanceWriteScenario(dbContext, companyId);
        dbContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(
            Guid.NewGuid(),
            companyId,
            "USD",
            100m,
            7500m,
            true,
            -2500m,
            15000m,
            120,
            45));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateInvoiceApprovalStatusAsync(
                new UpdateFinanceInvoiceApprovalStatusCommand(companyId, seed.InvoiceId, "approved"),
                CancellationToken.None));
        Assert.Equal("open", await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Policy_configuration_is_persisted_and_retrieved_as_typed_configuration()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Policy Company"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);
        var saved = await service.UpsertPolicyConfigurationAsync(
            new UpsertFinancePolicyConfigurationCommand(
                companyId,
                new FinancePolicyConfigurationDto(
                    companyId,
                    "eur",
                    12500m,
                    7500m,
                    true,
                    -2500m,
                    15000m,
                    120,
                    45)),
            CancellationToken.None);

        var retrieved = await service.GetPolicyConfigurationAsync(
            new GetFinancePolicyConfigurationQuery(companyId),
            CancellationToken.None);

        Assert.Equal("EUR", saved.ApprovalCurrency);
        Assert.Equal(saved, retrieved);
        Assert.Equal(1, await dbContext.FinancePolicyConfigurations.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId));
    }

    [Fact]
    public async Task Policy_configuration_rejects_invalid_control_bounds()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Invalid Policy Company"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpsertPolicyConfigurationAsync(
                new UpsertFinancePolicyConfigurationCommand(
                    companyId,
                    new FinancePolicyConfigurationDto(
                        companyId,
                        "USD",
                        12500m,
                        7500m,
                        true,
                        15000m,
                        -2500m,
                        120,
                        45)),
                CancellationToken.None));
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection, ICompanyContextAccessor accessor) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options,
            accessor);

    private static FinanceWriteSeed SeedFinanceWriteScenario(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var accountId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        dbContext.Companies.Add(new Company(companyId, $"Finance Write Company {companyId:N}"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            1000m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(
            counterpartyId,
            companyId,
            "Customer",
            "customer",
            "customer@example.com"));
        dbContext.FinanceInvoices.Add(new FinanceInvoice(
            invoiceId,
            companyId,
            counterpartyId,
            $"INV-{invoiceId:N}"[..16],
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            2000m,
            "USD",
            "open"));
        dbContext.FinanceTransactions.Add(new FinanceTransaction(
            transactionId,
            companyId,
            accountId,
            counterpartyId,
            invoiceId,
            null,
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            "customer_payment",
            2000m,
            "USD",
            "Customer payment",
            $"PAY-{transactionId:N}"[..20]));

        return new FinanceWriteSeed(invoiceId, transactionId);
    }

    private sealed record FinanceWriteSeed(Guid InvoiceId, Guid TransactionId);

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid companyId)
        {
            CompanyId = companyId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IsResolved => Membership is not null;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }

        public void SetCompanyId(Guid? companyId)
        {
            CompanyId = companyId;
            Membership = null;
            UserId = null;
        }

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}