using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class InternalLedgerProviderSwitchAdapterTests
{
    [Fact]
    public async Task Capability_profile_is_explicit_and_inventory_is_company_scoped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var context = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var company = Guid.NewGuid();
        var otherCompany = Guid.NewGuid();
        context.Companies.AddRange(new Company(company, "Internal inventory company"), new Company(otherCompany, "Other company"));
        context.FinanceAccounts.Add(new FinanceAccount(Guid.NewGuid(), otherCompany, "1930", "Bank", "asset", "SEK", 100m, DateTime.UtcNow));
        await context.SaveChangesAsync();
        var adapter = new InternalLedgerProviderSwitchAdapter(context, TimeProvider.System);
        var endpoint = new AccountingProviderSwitchEndpointDto("internal", null, "Virtual Company");

        var capabilities = await adapter.GetCapabilityProfileAsync(company, endpoint, "internal-capabilities", CancellationToken.None);
        var empty = await adapter.ExtractInventoryAsync(new(company, Guid.NewGuid(), "source", endpoint,
            "accounts", null, 100, "internal-empty"), CancellationToken.None);
        var foreign = await adapter.ExtractInventoryAsync(new(otherCompany, Guid.NewGuid(), "source", endpoint,
            "accounts", null, 100, "internal-foreign"), CancellationToken.None);

        Assert.Equal(AccountingProviderSwitchCapabilityKeys.All.Length, capabilities.Capabilities.Count);
        Assert.Equal("unsupported", capabilities.Capabilities.Single(x => x.Key == "exchange_rates").Level);
        Assert.Equal("partial", capabilities.Capabilities.Single(x => x.Key == "attachments").Level);
        Assert.Equal("confirmed_absent", empty.Availability);
        Assert.Equal(0, empty.RecordCount);
        Assert.Equal("available", foreign.Availability);
        Assert.Equal(1, foreign.RecordCount);
        Assert.Equal("native-ledger-v1", foreign.SourceVersion);
        Assert.Equal(64, foreign.IntegrityHash.Length);
    }
}
