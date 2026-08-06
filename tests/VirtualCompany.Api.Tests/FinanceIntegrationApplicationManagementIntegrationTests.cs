using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Security;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceIntegrationApplicationManagementIntegrationTests : IDisposable
{
    private readonly ManagedSecretTestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Platform_administrator_can_discover_registered_provider_without_secret_values()
    {
        using var client = CreateAuthenticatedClient("alice", "alice@example.com");

        using var response = await client.GetAsync("/api/platform/finance-integration-applications");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, body);
        Assert.Contains("\"providerKey\":\"fortnox\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"clientSecret\":", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentialSecret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ordinary_authenticated_user_cannot_manage_platform_provider_applications()
    {
        using var client = CreateAuthenticatedClient("bob", "bob@example.com");

        using var response = await client.GetAsync("/api/platform/finance-integration-applications");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Saving_configuration_versions_secret_and_persists_only_secret_reference_in_sql()
    {
        using var client = CreateAuthenticatedClient("alice", "alice@example.com");
        var request = new
        {
            enabled = true,
            clientId = "fortnox-client-id",
            clientSecret = "fortnox-client-secret",
            redirectUri = "https://api.example.com/finance/integrations/fortnox/callback",
            scopes = new[] { "supplier", "supplierinvoice" }
        };

        using var response = await client.PutAsJsonAsync(
            "/api/platform/finance-integration-applications/fortnox",
            request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, body);
        Assert.DoesNotContain("fortnox-client-id", body, StringComparison.Ordinal);
        Assert.DoesNotContain("fortnox-client-secret", body, StringComparison.Ordinal);

        var persisted = await _factory.ExecuteDbContextAsync(db => db.FinanceIntegrationProviderConfigurations
            .AsNoTracking()
            .SingleAsync(x => x.ProviderKey == "fortnox"));
        var audit = await _factory.ExecuteDbContextAsync(db => db.FinanceIntegrationProviderConfigurationAudits
            .AsNoTracking()
            .SingleAsync(x => x.ProviderKey == "fortnox"));
        var storedSecret = await _factory.SecretStore.GetAsync(
            persisted.CredentialSecretName!,
            persisted.CredentialSecretVersion,
            CancellationToken.None);

        Assert.DoesNotContain("fortnox-client", persisted.ScopesJson, StringComparison.Ordinal);
        Assert.DoesNotContain("fortnox-client-secret", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("fortnox-client-secret", storedSecret!.Value, StringComparison.Ordinal);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed class ManagedSecretTestWebApplicationFactory : TestWebApplicationFactory
    {
        public ManagedSecretTestWebApplicationFactory()
            : base(new Dictionary<string, string?>
            {
                ["PlatformAdministration:AdministratorIdentities:0"] = "dev-header:alice"
            })
        {
        }

        public InMemoryPlatformSecretStore SecretStore { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPlatformSecretStore>();
                services.AddSingleton<IPlatformSecretStore>(SecretStore);
            });
        }
    }

    private sealed class InMemoryPlatformSecretStore : IPlatformSecretStore
    {
        private readonly Dictionary<(string Name, string Version), string> _values = [];
        private readonly Dictionary<string, string> _currentVersions = new(StringComparer.OrdinalIgnoreCase);

        public string BackendName => "Test secret store";
        public bool SupportsWrites => true;

        public Task<PlatformSecretValue?> GetAsync(string name, string? version, CancellationToken cancellationToken)
        {
            var resolvedVersion = version;
            if (string.IsNullOrWhiteSpace(resolvedVersion) &&
                !_currentVersions.TryGetValue(name, out resolvedVersion))
            {
                return Task.FromResult<PlatformSecretValue?>(null);
            }

            return Task.FromResult(
                _values.TryGetValue((name, resolvedVersion!), out var value)
                    ? new PlatformSecretValue(value, resolvedVersion!, DateTime.UtcNow)
                    : null);
        }

        public Task<PlatformSecretWriteResult> SetAsync(string name, string value, CancellationToken cancellationToken)
        {
            var version = Guid.NewGuid().ToString("N");
            _values[(name, version)] = value;
            _currentVersions[name] = version;
            return Task.FromResult(new PlatformSecretWriteResult(version, DateTime.UtcNow));
        }
    }
}
