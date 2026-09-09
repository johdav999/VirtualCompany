using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Security;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsAppOnlyTokenProviderTests
{
    private static readonly Guid TenantId = Guid.Parse("40000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task Caches_valid_token_and_force_refresh_uses_rotated_credential_result()
    {
        var source = new RotatingTokenSource(
            Token([TeamsPresenterApplicationPermissions.JoinGroupCall], "first"),
            Token([TeamsPresenterApplicationPermissions.JoinGroupCall], "second"));
        var provider = CreateProvider(source);
        var request = new TeamsAppOnlyTokenRequest(TenantId, [TeamsPresenterApplicationPermissions.JoinGroupCall]);

        var first = await provider.AcquireAsync(request, CancellationToken.None);
        var cached = await provider.AcquireAsync(request, CancellationToken.None);
        var rotated = await provider.AcquireAsync(request with { ForceRefresh = true }, CancellationToken.None);

        Assert.Equal(first.AccessToken, cached.AccessToken);
        Assert.NotEqual(first.AccessToken, rotated.AccessToken);
        Assert.Equal(2, source.Count);
    }

    [Theory]
    [InlineData("missing", TeamsIdentityFailureCodes.PermissionMissing)]
    [InlineData("excess", TeamsIdentityFailureCodes.PermissionExcess)]
    [InlineData("tenant", TeamsIdentityFailureCodes.TokenTenantMismatch)]
    public async Task Rejects_permission_and_tenant_claim_mismatches(string scenario, string expectedCode)
    {
        var roles = scenario switch
        {
            "missing" => Array.Empty<string>(),
            "excess" => [TeamsPresenterApplicationPermissions.JoinGroupCall, "Directory.Read.All"],
            _ => [TeamsPresenterApplicationPermissions.JoinGroupCall]
        };
        var tenant = scenario == "tenant" ? Guid.NewGuid() : TenantId;
        var source = new RotatingTokenSource(Token(roles, "invalid", tenant));
        var provider = CreateProvider(source);

        var exception = await Assert.ThrowsAsync<TeamsIdentityException>(() => provider.AcquireAsync(
            new TeamsAppOnlyTokenRequest(TenantId, [TeamsPresenterApplicationPermissions.JoinGroupCall]),
            CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain(source.LastToken, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_rejects_client_secret_credentials_before_reading_the_secret_store()
    {
        var options = TeamsPresenterPackageBuilderTests.ValidOptions();
        options.CredentialMode = "client_secret";
        options.ClientSecretReference = "keyvault://virtual-company/teams-presenter-client-secret";
        var secrets = new ThrowingSecretStore();
        var source = new AzureTeamsCredentialTokenSource(
            Options.Create(options),
            secrets,
            new TestHostEnvironment(Environments.Production));

        var exception = await Assert.ThrowsAsync<TeamsIdentityException>(() =>
            source.AcquireAsync(TenantId, CancellationToken.None));

        Assert.Equal(TeamsIdentityFailureCodes.CredentialUnavailable, exception.Code);
        Assert.Equal(0, secrets.ReadCount);
    }

    private static AzureTeamsAppOnlyTokenProvider CreateProvider(ITeamsCredentialTokenSource source)
    {
        var options = TeamsPresenterPackageBuilderTests.ValidOptions();
        options.TokenRefreshSkewMinutes = 5;
        return new AzureTeamsAppOnlyTokenProvider(
            Options.Create(options), source, TimeProvider.System, NullLogger<AzureTeamsAppOnlyTokenProvider>.Instance);
    }

    private static AccessToken Token(IReadOnlyCollection<string> roles, string marker, Guid? tenantId = null)
    {
        var header = Encode(new { alg = "none", typ = "JWT" });
        var payload = Encode(new
        {
            aud = "00000003-0000-0000-c000-000000000000",
            tid = (tenantId ?? TenantId).ToString("D"),
            roles,
            marker
        });
        return new AccessToken($"{header}.{payload}.signature", DateTimeOffset.UtcNow.AddHours(1));
    }

    private static string Encode<T>(T value) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class RotatingTokenSource(params AccessToken[] tokens) : ITeamsCredentialTokenSource
    {
        private int index;
        public int Count { get; private set; }
        public string LastToken { get; private set; } = string.Empty;

        public Task<AccessToken> AcquireAsync(Guid entraTenantId, CancellationToken cancellationToken)
        {
            Count++;
            var token = tokens[Math.Min(index++, tokens.Length - 1)];
            LastToken = token.Token;
            return Task.FromResult(token);
        }
    }

    private sealed class ThrowingSecretStore : IPlatformSecretStore
    {
        public int ReadCount { get; private set; }
        public string BackendName => "test";
        public bool SupportsWrites => false;

        public Task<PlatformSecretValue?> GetAsync(string name, string? version, CancellationToken cancellationToken)
        {
            ReadCount++;
            throw new InvalidOperationException("The production guard must run before this store is accessed.");
        }

        public Task<PlatformSecretWriteResult> SetAsync(string name, string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = nameof(TeamsAppOnlyTokenProviderTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
