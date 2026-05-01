using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceIntegrationFoundationTests
{
    [Fact]
    public async Task EnsureCreated_maps_finance_integration_tables_and_source_tracking_columns()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var connectionColumns = await ReadColumnsAsync(connection, "finance_integration_connections");
        Assert.Contains("company_id", connectionColumns);
        Assert.Contains("provider_key", connectionColumns);
        Assert.Contains("status", connectionColumns);
        Assert.Contains("provider_tenant_id", connectionColumns);
        Assert.Contains("scopes_json", connectionColumns);
        Assert.Contains("metadata_json", connectionColumns);
        Assert.Contains("last_sync_at", connectionColumns);

        var tokenColumns = await ReadColumnsAsync(connection, "finance_integration_tokens");
        Assert.Contains("connection_id", tokenColumns);
        Assert.Contains("provider_key", tokenColumns);
        Assert.Contains("token_type", tokenColumns);
        Assert.Contains("encrypted_token", tokenColumns);
        Assert.DoesNotContain("access_token", tokenColumns);
        Assert.DoesNotContain("refresh_token", tokenColumns);

        var syncStateColumns = await ReadColumnsAsync(connection, "finance_integration_sync_states");
        Assert.Contains("connection_id", syncStateColumns);
        Assert.Contains("entity_type", syncStateColumns);
        Assert.Contains("scope_key", syncStateColumns);
        Assert.Contains("cursor", syncStateColumns);
        Assert.Contains("consecutive_failure_count", syncStateColumns);

        var externalReferenceColumns = await ReadColumnsAsync(connection, "finance_external_references");
        Assert.Contains("connection_id", externalReferenceColumns);
        Assert.Contains("provider_key", externalReferenceColumns);
        Assert.Contains("entity_type", externalReferenceColumns);
        Assert.Contains("internal_record_id", externalReferenceColumns);
        Assert.Contains("external_id", externalReferenceColumns);
        Assert.Contains("external_number", externalReferenceColumns);

        var auditColumns = await ReadColumnsAsync(connection, "finance_integration_audit_events");
        Assert.Contains("provider_key", auditColumns);
        Assert.Contains("event_type", auditColumns);
        Assert.Contains("outcome", auditColumns);
        Assert.Contains("correlation_id", auditColumns);
        Assert.Contains("created_count", auditColumns);
        Assert.Contains("updated_count", auditColumns);
        Assert.Contains("skipped_count", auditColumns);
        Assert.Contains("error_count", auditColumns);
        Assert.Contains("metadata_json", auditColumns);

        foreach (var tableName in SourceTrackedTables)
        {
            var columns = await ReadColumnsAsync(connection, tableName);
            Assert.Contains("source_type", columns);
            Assert.Contains("provider_key", columns);
            Assert.Contains("provider_external_id", columns);
            Assert.Contains("finance_external_reference_id", columns);

            var indexes = await ReadIndexesAsync(connection, tableName);
            Assert.Contains($"IX_{tableName}_company_id_source_type", indexes);
            Assert.Contains($"IX_{tableName}_company_id_provider_key_provider_external_id", indexes);
        }

        var connectionIndexes = await ReadIndexesAsync(connection, "finance_integration_connections");
        Assert.Contains("IX_finance_integration_connections_company_id_provider_key", connectionIndexes);

        var externalReferenceIndexes = await ReadIndexesAsync(connection, "finance_external_references");
        Assert.Contains("IX_finance_external_references_company_id_provider_key_entity_type_external_id", externalReferenceIndexes);
        Assert.Contains("IX_finance_external_references_company_id_entity_type_internal_record_id", externalReferenceIndexes);
    }

    [Fact]
    public void Infrastructure_registration_resolves_fortnox_provider_by_key_and_concrete_type()
    {
        using var serviceProvider = BuildInfrastructureServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IFinanceIntegrationProviderResolver>();
        var registry = scope.ServiceProvider.GetRequiredService<IFinanceIntegrationProviderRegistry>();

        var provider = resolver.GetRequired(" FORTNOX ");

        Assert.IsType<FortnoxFinanceIntegrationProvider>(provider);
        Assert.Same(provider, registry.Resolve(FinanceIntegrationProviderKeys.Fortnox));
        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, provider.ProviderKey);
        Assert.IsType<FortnoxFinanceIntegrationProvider>(
            scope.ServiceProvider.GetRequiredService<FortnoxFinanceIntegrationProvider>());
    }

    [Fact]
    public void Infrastructure_registration_rejects_unknown_finance_provider_key()
    {
        using var serviceProvider = BuildInfrastructureServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IFinanceIntegrationProviderResolver>();

        var exception = Assert.Throws<FinanceIntegrationProviderNotFoundException>(
            () => resolver.GetRequired("unknown"));

        Assert.Equal("unknown", exception.ProviderKey);
    }

    private static ServiceProvider BuildInfrastructureServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:VirtualCompanyDb"] = "Data Source=:memory:"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddVirtualCompanyInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    private static readonly string[] SourceTrackedTables =
    [
        "finance_accounts",
        "finance_counterparties",
        "finance_invoices",
        "finance_bills",
        "finance_transactions",
        "finance_balances",
        "finance_payments",
        "company_bank_accounts",
        "bank_transactions",
        "finance_assets"
    ];

    private static async Task<HashSet<string>> ReadColumnsAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static async Task<HashSet<string>> ReadIndexesAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options);
}