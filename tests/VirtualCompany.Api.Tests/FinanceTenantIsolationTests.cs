using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceTenantIsolationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceTenantIsolationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Finance_seed_creates_realistic_balanced_tenant_data()
    {
        var companyId = Guid.NewGuid();
        FinanceSeedResult seed = null!;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(companyId, "Finance Company"));
            seed = FinanceSeedData.AddMockFinanceData(dbContext, companyId);
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        Assert.Equal(3, await dbContext.FinanceAccounts.CountAsync());
        Assert.Equal(50, await dbContext.FinanceTransactions.CountAsync());
        Assert.Equal(10, await dbContext.FinanceInvoices.CountAsync() + await dbContext.FinanceBills.CountAsync());
        Assert.Equal(seed.CounterpartyIds.Count, await dbContext.FinanceCounterparties.CountAsync());
        Assert.Equal(3, await dbContext.FinanceBalances.CountAsync());
        Assert.Equal(1, await dbContext.FinancePolicyConfigurations.CountAsync());
        Assert.Equal(5, await dbContext.CompanyKnowledgeDocuments.CountAsync());

        var balances = await dbContext.FinanceBalances.AsNoTracking().ToListAsync();
        foreach (var balance in balances)
        {
            var account = await dbContext.FinanceAccounts.AsNoTracking().SingleAsync(x => x.Id == balance.AccountId);
            var postedAmount = await dbContext.FinanceTransactions
                .Where(x => x.AccountId == account.Id)
                .SumAsync(x => x.Amount);

            Assert.Equal(account.OpeningBalance + postedAmount, balance.Amount);
        }
    }

    [Fact]
    public void Finance_seed_is_deterministic_for_same_tenant_and_anchor()
    {
        var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var anchorUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        using var firstContext = CreateFinanceSeedContext();
        using var secondContext = CreateFinanceSeedContext();

        var first = FinanceSeedData.AddMockFinanceData(firstContext, companyId, anchorUtc);
        var second = FinanceSeedData.AddMockFinanceData(secondContext, companyId, anchorUtc);

        Assert.Equal(first.AccountIds, second.AccountIds);
        Assert.Equal(first.CounterpartyIds, second.CounterpartyIds);
        Assert.Equal(first.InvoiceIds, second.InvoiceIds);
        Assert.Equal(first.BillIds, second.BillIds);
        Assert.Equal(first.TransactionIds, second.TransactionIds);
        Assert.Equal(first.BalanceIds, second.BalanceIds);
        Assert.Equal(first.DocumentIds, second.DocumentIds);
        Assert.Equal(first.PolicyConfigurationId, second.PolicyConfigurationId);

        var firstTransactions = firstContext.ChangeTracker.Entries<FinanceTransaction>()
            .Select(entry => entry.Entity)
            .OrderBy(transaction => transaction.ExternalReference)
            .Select(transaction => new { transaction.Id, transaction.TransactionUtc, transaction.Amount, transaction.ExternalReference })
            .ToArray();

        var secondTransactions = secondContext.ChangeTracker.Entries<FinanceTransaction>()
            .Select(entry => entry.Entity)
            .OrderBy(transaction => transaction.ExternalReference)
            .Select(transaction => new { transaction.Id, transaction.TransactionUtc, transaction.Amount, transaction.ExternalReference })
            .ToArray();

        Assert.Equal(firstTransactions, secondTransactions);

        var firstBalances = firstContext.ChangeTracker.Entries<FinanceBalance>()
            .Select(entry => new { entry.Entity.AccountId, entry.Entity.AsOfUtc, entry.Entity.Amount })
            .OrderBy(balance => balance.AccountId)
            .ToArray();

        var secondBalances = secondContext.ChangeTracker.Entries<FinanceBalance>()
            .Select(entry => new { entry.Entity.AccountId, entry.Entity.AsOfUtc, entry.Entity.Amount })
            .OrderBy(balance => balance.AccountId)
            .ToArray();

        Assert.Equal(firstBalances, secondBalances);
    }

    [Fact]
    public async Task Finance_queries_are_scoped_to_active_company_context()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();

        await SeedTwoCompaniesAsync(companyAId, companyBId);

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyAId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        Assert.All(await dbContext.FinanceAccounts.AsNoTracking().ToListAsync(), account => Assert.Equal(companyAId, account.CompanyId));
        Assert.All(await dbContext.FinanceTransactions.AsNoTracking().ToListAsync(), transaction => Assert.Equal(companyAId, transaction.CompanyId));
        Assert.All(await dbContext.FinanceInvoices.AsNoTracking().ToListAsync(), invoice => Assert.Equal(companyAId, invoice.CompanyId));
        Assert.All(await dbContext.FinanceBills.AsNoTracking().ToListAsync(), bill => Assert.Equal(companyAId, bill.CompanyId));
        Assert.All(await dbContext.FinanceCounterparties.AsNoTracking().ToListAsync(), counterparty => Assert.Equal(companyAId, counterparty.CompanyId));
        Assert.All(await dbContext.FinanceBalances.AsNoTracking().ToListAsync(), balance => Assert.Equal(companyAId, balance.CompanyId));
        Assert.All(await dbContext.FinancePolicyConfigurations.AsNoTracking().ToListAsync(), policy => Assert.Equal(companyAId, policy.CompanyId));
    }

    [Fact]
    public async Task Finance_records_from_another_company_cannot_be_mutated()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();

        await SeedTwoCompaniesAsync(companyAId, companyBId);

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyBId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var accountFromCompanyA = await dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .FirstAsync(x => x.CompanyId == companyAId);

        accountFromCompanyA.Rename("Cross-tenant rename attempt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Finance_relationships_do_not_leave_orphaned_balance_records()
    {
        var companyId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(companyId, "Relationship Company"));
            FinanceSeedData.AddMockFinanceData(dbContext, companyId);
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var orphanedBalances = await dbContext.FinanceBalances
            .Where(balance => !dbContext.FinanceAccounts.Any(account => account.Id == balance.AccountId))
            .CountAsync();

        Assert.Equal(0, orphanedBalances);
        Assert.All(await dbContext.FinanceTransactions.AsNoTracking().ToListAsync(), transaction => Assert.NotEqual(Guid.Empty, transaction.AccountId));
    }

    private async Task SeedTwoCompaniesAsync(Guid companyAId, Guid companyBId)
    {
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(
                new Company(companyAId, "Finance Company A"),
                new Company(companyBId, "Finance Company B"));

            FinanceSeedData.AddMockFinanceData(dbContext, companyAId);
            FinanceSeedData.AddMockFinanceData(dbContext, companyBId);
            return Task.CompletedTask;
        });
    }

    private static VirtualCompanyDbContext CreateFinanceSeedContext() =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
}
