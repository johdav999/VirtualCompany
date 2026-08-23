using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxProviderSwitchAdapterTests
{
    [Fact]
    public async Task Missing_scope_is_reported_as_not_authorized_without_calling_provider()
    {
        await using var fixture = await Fixture.CreateAsync(["bookkeeping", "customer"]);
        var handler = new CapturingHandler();
        var adapter = new FortnoxProviderSwitchAdapter(FortnoxApiClientTestFactory.Create(handler), fixture.Context, TimeProvider.System);

        var capability = await adapter.GetCapabilityProfileAsync(fixture.CompanyId, Endpoint(), "scope-test", CancellationToken.None);
        var result = await adapter.ExtractInventoryAsync(Request(fixture.CompanyId, "invoices"), CancellationToken.None);

        Assert.Equal("unknown", capability.Capabilities.Single(x => x.Key == "invoices").Level);
        Assert.Equal("invoice,supplierinvoice", capability.Capabilities.Single(x => x.Key == "invoices").RequiredScope);
        Assert.Equal("not_authorized", result.Availability);
        Assert.Equal("provider_scope_missing", result.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Paged_inventory_returns_resumable_cursor_counts_totals_and_hashes()
    {
        await using var fixture = await Fixture.CreateAsync(FortnoxScopeDefaults.ImportSync);
        var handler = new CapturingHandler(
            Response("""
                {"Invoices":[{"DocumentNumber":"1001","Total":125.50}],"MetaInformation":{"@CurrentPage":1,"@TotalPages":2,"@TotalResources":2,"@Limit":1}}
                """),
            Response("""
                {"Invoices":[{"DocumentNumber":"1002","Total":74.50}],"MetaInformation":{"@CurrentPage":2,"@TotalPages":2,"@TotalResources":2,"@Limit":1}}
                """),
            Response("""
                {"SupplierInvoices":[],"MetaInformation":{"@CurrentPage":1,"@TotalPages":1,"@TotalResources":0,"@Limit":1}}
                """));
        var adapter = new FortnoxProviderSwitchAdapter(FortnoxApiClientTestFactory.Create(handler), fixture.Context, TimeProvider.System);

        var first = await adapter.ExtractInventoryAsync(Request(fixture.CompanyId, "invoices"), CancellationToken.None);
        var second = await adapter.ExtractInventoryAsync(Request(fixture.CompanyId, "invoices", first.NextCursor), CancellationToken.None);
        var supplier = await adapter.ExtractInventoryAsync(Request(fixture.CompanyId, "invoices", second.NextCursor), CancellationToken.None);

        Assert.False(first.IsComplete);
        Assert.Equal("customer:2", first.NextCursor);
        Assert.Equal(1, first.RecordCount);
        Assert.Equal(125.50m, first.FinancialTotal);
        Assert.False(second.IsComplete);
        Assert.Equal("supplier:1", second.NextCursor);
        Assert.True(supplier.IsComplete);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("page=2", handler.Requests[1].RequestUri!.Query);
        Assert.All(new[] { first, second, supplier }, page => Assert.Equal(64, page.IntegrityHash.Length));
    }

    [Fact]
    public async Task Unverified_dataset_is_not_returned_never_confirmed_absent()
    {
        await using var fixture = await Fixture.CreateAsync(FortnoxScopeDefaults.ImportSync);
        var adapter = new FortnoxProviderSwitchAdapter(FortnoxApiClientTestFactory.Create(new CapturingHandler()), fixture.Context, TimeProvider.System);
        var result = await adapter.ExtractInventoryAsync(Request(fixture.CompanyId, "attachments"), CancellationToken.None);
        Assert.Equal("not_returned", result.Availability);
        Assert.Equal("dataset_not_returned", result.FailureCode);
    }

    private static AccountingProviderSwitchEndpointDto Endpoint() => new("external", "fortnox", "Fortnox");
    private static ProviderSwitchInventoryExtractionRequest Request(Guid companyId, string dataset, string? cursor = null) =>
        new(companyId, Guid.NewGuid(), "source", Endpoint(), dataset, cursor, 1, "adapter-test");
    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context, Guid companyId)
        {
            _connection = connection;
            Context = context;
            CompanyId = companyId;
        }
        public VirtualCompanyDbContext Context { get; }
        public Guid CompanyId { get; }

        public static async Task<Fixture> CreateAsync(IEnumerable<string> scopes)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var context = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var companyId = Guid.NewGuid();
            context.Companies.Add(new Company(companyId, "Fortnox assessment company"));
            var provider = new FinanceIntegrationConnection(Guid.NewGuid(), companyId, "fortnox", "connected", null, DateTime.UtcNow);
            provider.Scopes.AddRange(scopes);
            context.FinanceIntegrationConnections.Add(provider);
            await context.SaveChangesAsync();
            return new Fixture(connection, context, companyId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
