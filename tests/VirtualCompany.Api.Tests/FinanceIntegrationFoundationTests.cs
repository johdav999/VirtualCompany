using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Auth;
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
        Assert.IsAssignableFrom<IFinanceIntegrationOAuthService>(provider.OAuth);
        Assert.IsAssignableFrom<IFinanceIntegrationSyncService>(provider.Sync);
        Assert.IsAssignableFrom<IFinanceIntegrationWriteCommandService>(provider.WriteCommands);
        Assert.IsAssignableFrom<IFinanceIntegrationMapper>(provider.Mapper);
        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, provider.OAuth.ProviderKey);
        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, provider.Sync.ProviderKey);
        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, provider.WriteCommands.ProviderKey);
        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, provider.Mapper.ProviderKey);
        Assert.IsType<FortnoxFinanceIntegrationProvider>(
            scope.ServiceProvider.GetRequiredService<FortnoxFinanceIntegrationProvider>());
    }

    [Fact]
    public void Infrastructure_registration_exposes_fortnox_services_through_generic_provider_abstraction()
    {
        using var serviceProvider = BuildInfrastructureServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var provider = scope.ServiceProvider
            .GetRequiredService<IFinanceIntegrationProviderResolver>()
            .GetRequired(FinanceIntegrationProviderKeys.Fortnox);

        Assert.Same(
            scope.ServiceProvider.GetRequiredService<FortnoxFinanceIntegrationOAuthService>(),
            provider.OAuth);
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<FortnoxFinanceIntegrationSyncService>(),
            provider.Sync);
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<FinanceIntegrationWriteApprovalService>(),
            provider.WriteCommands);
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<FortnoxFinanceIntegrationMapper>(),
            provider.Mapper);
    }

    [Fact]
    public async Task Fortnox_sync_can_be_triggered_through_generic_adapter()
    {
        var companyId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var fortnoxSyncService = new StubFortnoxSyncService(companyId, connectionId);
        var adapter = new FortnoxFinanceIntegrationSyncService(fortnoxSyncService);

        var result = await adapter.SyncAsync(
            new RunFinanceIntegrationSyncCommand(
                FinanceIntegrationProviderKeys.Fortnox,
                companyId,
                connectionId,
                "correlation"),
            CancellationToken.None);

        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, result.ProviderKey);
        Assert.Equal(companyId, result.CompanyId);
        Assert.Equal(connectionId, result.ConnectionId);
        Assert.Equal("correlation", fortnoxSyncService.LastSyncCommand?.CorrelationId);
        Assert.Contains(result.Entities, entity => entity.EntityType == "invoices" && entity.Created == 1);
    }

    [Fact]
    public async Task Fortnox_oauth_token_handling_is_available_through_generic_adapter()
    {
        var companyId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var fortnoxOAuthService = new StubFortnoxOAuthService();
        var adapter = new FortnoxFinanceIntegrationOAuthService(fortnoxOAuthService);

        var result = await adapter.GetValidAccessTokenAsync(
            new RefreshFinanceIntegrationAccessTokenCommand(
                FinanceIntegrationProviderKeys.Fortnox,
                companyId,
                connectionId),
            CancellationToken.None);

        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, result.ProviderKey);
        Assert.True(result.Succeeded);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(companyId, fortnoxOAuthService.LastRefreshCommand?.CompanyId);
        Assert.Equal(connectionId, fortnoxOAuthService.LastRefreshCommand?.ConnectionId);
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

    [Fact]
    public async Task Provider_key_status_route_uses_registered_provider()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var oauth = new CapturingFinanceIntegrationOAuthService();
        var controller = CreateFinanceIntegrationController(companyId, userId, new StubFinanceIntegrationProvider(oauth: oauth));

        var response = await controller.StatusAsync(companyId, FinanceIntegrationProviderKeys.Fortnox, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<FinanceIntegrationConnectionStatusResult>(ok.Value);
        Assert.True(result.IsConnected);
        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, result.ProviderKey);
        Assert.Equal(companyId, oauth.LastStatusQuery?.CompanyId);
        Assert.Equal(userId, oauth.LastStatusQuery?.UserId);
    }

    [Fact]
    public async Task Provider_metadata_route_returns_registered_provider_status()
    {
        var companyId = Guid.NewGuid();
        var provider = new StubFinanceIntegrationProvider();
        var controller = CreateFinanceIntegrationController(companyId, Guid.NewGuid(), provider);

        var response = await controller.ProvidersAsync(companyId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var providers = Assert.IsAssignableFrom<IReadOnlyList<FinanceIntegrationConnectionsController.FinanceIntegrationProviderMetadataResponse>>(ok.Value);
        var metadata = Assert.Single(providers);
        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, metadata.ProviderKey);
        Assert.Equal("Fortnox", metadata.DisplayName);
        Assert.Contains("invoices", metadata.Capabilities);
        Assert.True(metadata.Status.IsConnected);
    }

    [Fact]
    public async Task Unknown_provider_key_route_returns_clear_not_found()
    {
        var companyId = Guid.NewGuid();
        var controller = CreateFinanceIntegrationController(companyId, Guid.NewGuid(), new StubFinanceIntegrationProvider());

        var response = await controller.StatusAsync(companyId, "unknown", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(404, problem.Status);
        Assert.Contains("unknown", problem.Detail);
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

    private static FinanceIntegrationConnectionsController CreateFinanceIntegrationController(
        Guid companyId,
        Guid userId,
        IFinanceIntegrationProvider provider) =>
        new(
            new TestCompanyContextAccessor(companyId, userId),
            null!,
            null!,
            null!,
            new SingleProviderRegistry(provider),
            new TestWebHostEnvironment(),
            NullLogger<FinanceIntegrationConnectionsController>.Instance);

    private sealed class StubFortnoxSyncService(Guid companyId, Guid connectionId) : IFortnoxSyncService
    {
        public RunFortnoxSyncCommand? LastSyncCommand { get; private set; }

        public Task<FortnoxSyncResult> SyncAsync(RunFortnoxSyncCommand command, CancellationToken cancellationToken)
        {
            LastSyncCommand = command;
            return Task.FromResult(new FortnoxSyncResult(
                companyId,
                connectionId,
                DateTime.UtcNow.AddMinutes(-1),
                DateTime.UtcNow,
                "succeeded",
                Created: 1,
                Updated: 2,
                Skipped: 3,
                Errors: 0,
                [new FortnoxEntitySyncResult("invoices", Created: 1, Updated: 0, Skipped: 0, Errors: 0)]));
        }

        public Task<FortnoxSyncHistoryResult> GetHistoryAsync(GetFortnoxSyncHistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new FortnoxSyncHistoryResult(query.CompanyId, []));

        public Task<FortnoxSupplierInvoiceRepairResult> RepairDanglingSupplierInvoicesAsync(
            RepairFortnoxSupplierInvoicesCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FortnoxSupplierInvoiceRepairResult(
                command.CompanyId,
                command.ConnectionId,
                Repaired: 0,
                Skipped: 0,
                Rejected: command.Invoices.Count,
                []));
    }

    private sealed class StubFortnoxOAuthService : IFortnoxOAuthService
    {
        public RefreshFortnoxAccessTokenCommand? LastRefreshCommand { get; private set; }

        public Task<FortnoxOAuthStartResult> BuildAuthorizationUrlAsync(StartFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FortnoxOAuthStartResult(new Uri("https://apps.fortnox.se/oauth-v1/auth"), DateTime.UtcNow.AddMinutes(10)));

        public Task<FortnoxOAuthCompletionResult> HandleCallbackAsync(CompleteFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FortnoxOAuthCompletionResult(Guid.NewGuid(), command.CompanyId, "connected", command.ProviderError is null ? null : new Uri("https://localhost/")));

        public Task<FortnoxAccessTokenResult> GetValidAccessTokenAsync(RefreshFortnoxAccessTokenCommand command, CancellationToken cancellationToken)
        {
            LastRefreshCommand = command;
            return Task.FromResult(FortnoxAccessTokenResult.Success("access-token", DateTime.UtcNow.AddMinutes(30)));
        }

        public Task<FortnoxConnectionStatusResult> GetStatusAsync(GetFortnoxConnectionStatusQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new FortnoxConnectionStatusResult(true, Guid.NewGuid(), "connected", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30), null, null, DateTime.UtcNow));

        public Task MarkNeedsReconnectAsync(Guid companyId, Guid connectionId, string safeReason, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<FortnoxConnectionDisconnectResult> DisconnectAsync(DisconnectFortnoxConnectionCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FortnoxConnectionDisconnectResult(command.CompanyId, Guid.NewGuid(), "disconnected", DateTime.UtcNow, "Fortnox has been disconnected."));
    }

    private sealed class SingleProviderRegistry(IFinanceIntegrationProvider provider) : IFinanceIntegrationProviderRegistry
    {
        public IReadOnlyCollection<IFinanceIntegrationProvider> Providers { get; } = [provider];

        public IFinanceIntegrationProvider Resolve(string providerKey) => GetRequired(providerKey);

        public IFinanceIntegrationProvider GetRequired(string providerKey) =>
            string.Equals(providerKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase)
                ? provider
                : throw new FinanceIntegrationProviderNotFoundException(providerKey);
    }

    private sealed class StubFinanceIntegrationProvider(
        IFinanceIntegrationOAuthService? oauth = null,
        IFinanceIntegrationSyncService? sync = null,
        IFinanceIntegrationWriteCommandService? writeCommands = null,
        IFinanceIntegrationMapper? mapper = null) : IFinanceIntegrationProvider
    {
        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
        public string DisplayName => "Fortnox";
        public IReadOnlyCollection<string> Capabilities { get; } = ["invoices", "payments", "write"];
        public IFinanceIntegrationOAuthService OAuth { get; } = oauth ?? new CapturingFinanceIntegrationOAuthService();
        public IFinanceIntegrationSyncService Sync { get; } = sync ?? new StubFinanceIntegrationSyncService();
        public IFinanceIntegrationWriteCommandService WriteCommands { get; } = writeCommands ?? new StubFinanceIntegrationWriteCommandService();
        public IFinanceIntegrationMapper Mapper { get; } = mapper ?? new StubFinanceIntegrationMapper();
    }

    private sealed class CapturingFinanceIntegrationOAuthService : IFinanceIntegrationOAuthService
    {
        public GetFinanceIntegrationConnectionStatusQuery? LastStatusQuery { get; private set; }
        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

        public Task<FinanceIntegrationOAuthResult> BuildAuthorizationUrlAsync(StartFinanceIntegrationOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceIntegrationOAuthResult(ProviderKey, new Uri("https://apps.fortnox.se/oauth-v1/auth"), DateTime.UtcNow.AddMinutes(10)));

        public Task<FinanceIntegrationOAuthCompletionResult> HandleCallbackAsync(CompleteFinanceIntegrationOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceIntegrationOAuthCompletionResult(ProviderKey, Guid.NewGuid(), command.CompanyId, "connected", null));

        public Task<FinanceIntegrationAccessTokenResult> GetValidAccessTokenAsync(RefreshFinanceIntegrationAccessTokenCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceIntegrationAccessTokenResult(ProviderKey, true, "access-token", DateTime.UtcNow.AddMinutes(30), false, null));

        public Task<FinanceIntegrationConnectionStatusResult> GetStatusAsync(GetFinanceIntegrationConnectionStatusQuery query, CancellationToken cancellationToken)
        {
            LastStatusQuery = query;
            return Task.FromResult(new FinanceIntegrationConnectionStatusResult(ProviderKey, true, Guid.NewGuid(), "connected", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30), null, null));
        }

        public Task MarkNeedsReconnectAsync(Guid companyId, Guid connectionId, string safeReason, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<FinanceIntegrationConnectionDisconnectResult> DisconnectAsync(DisconnectFinanceIntegrationConnectionCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceIntegrationConnectionDisconnectResult(ProviderKey, command.CompanyId, Guid.NewGuid(), "disconnected", DateTime.UtcNow, "Disconnected."));
    }

    private sealed class StubFinanceIntegrationSyncService : IFinanceIntegrationSyncService
    {
        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

        public Task<FinanceIntegrationSyncResult> SyncAsync(RunFinanceIntegrationSyncCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceIntegrationSyncResult(ProviderKey, command.CompanyId, command.ConnectionId ?? Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, "succeeded", 0, 0, 0, 0, []));

        public Task<FinanceIntegrationSyncHistoryResult> GetHistoryAsync(GetFinanceIntegrationSyncHistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceIntegrationSyncHistoryResult(ProviderKey, query.CompanyId, []));
    }

    private sealed class StubFinanceIntegrationWriteCommandService : IFinanceIntegrationWriteCommandService
    {
        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

        public Task<FinanceIntegrationWriteResult> RequestApprovalAsync(FinanceIntegrationWriteCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceIntegrationWriteResult(ProviderKey, command.WriteRequestId, Guid.NewGuid(), "awaiting_approval", "Approval required.", false));

        public Task<FinanceIntegrationWriteResult> EnsureApprovedForExecutionAsync(FinanceIntegrationWriteCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceIntegrationWriteResult(ProviderKey, command.WriteRequestId, command.ApprovedApprovalId, "approved", "Approved.", true));

        public Task RecordExecutionSucceededAsync(FinanceIntegrationWriteCommand command, object? responsePayload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RecordExecutionFailedAsync(FinanceIntegrationWriteCommand command, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubFinanceIntegrationMapper : IFinanceIntegrationMapper
    {
        public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
    }

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; } = userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "VirtualCompany.Api.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
