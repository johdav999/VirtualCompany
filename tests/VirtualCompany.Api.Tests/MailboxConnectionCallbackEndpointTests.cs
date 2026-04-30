using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Mailbox;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MailboxConnectionCallbackEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    private static readonly Uri LocalDevelopmentBaseAddress = new("http://localhost:5301");

    public MailboxConnectionCallbackEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("gmail", "http://localhost:5301/api/mailbox-connections/gmail/callback", MailboxProvider.Gmail)]
    [InlineData("microsoft365", "http://localhost:5301/api/mailbox-connections/microsoft365/callback", MailboxProvider.Microsoft365)]
    public async Task Start_flow_uses_local_development_stable_callback_uri_and_protected_state(
        string provider,
        string expectedRedirectUri,
        MailboxProvider expectedProvider)
    {
        using var factory = CreateFactoryWithMailboxOAuthOptions();
        var seed = await SeedMailboxCompanyAsync(factory);
        using var client = CreateAuthenticatedClient(factory, LocalDevelopmentBaseAddress);
        var returnUri = $"https://localhost/finance/mailbox?companyId={seed.CompanyId:D}";

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId:D}/mailbox-connections/{provider}/start",
            new
            {
                returnUri
            });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<StartMailboxConnectionResponse>();
        Assert.NotNull(payload);
        var authorizationUrl = new Uri(payload!.AuthorizationUrl);
        var query = QueryHelpers.ParseQuery(authorizationUrl.Query);
        var redirectUri = Assert.Single(query["redirect_uri"]);
        var protectedState = Assert.Single(query["state"]);
        var state = factory.Services.GetRequiredService<IMailboxOAuthStateProtector>().Unprotect(protectedState);

        Assert.Equal(expectedRedirectUri, redirectUri);
        Assert.DoesNotContain($"/api/companies/{seed.CompanyId:D}/", redirectUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(seed.CompanyId, state.CompanyId);
        Assert.Equal(seed.UserId, state.UserId);
        Assert.Equal(expectedProvider, state.Provider);
        Assert.Equal(new Uri(returnUri), state.ReturnUri);
    }

    [Fact]
    public async Task Start_flow_requires_authenticated_tenant_context_before_state_is_issued()
    {
        using var factory = CreateFactoryWithMailboxOAuthOptions();
        var seed = await SeedMailboxCompanyAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId:D}/mailbox-connections/gmail/start",
            new { returnUri = $"https://localhost/finance/mailbox?companyId={seed.CompanyId:D}" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("gmail")]
    [InlineData("microsoft365")]
    public async Task Start_flow_rejects_missing_tenant_membership_before_state_is_issued(string provider)
    {
        using var factory = CreateFactoryWithMailboxOAuthOptions();
        using var client = CreateAuthenticatedClient(factory);
        var companyId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId:D}/mailbox-connections/{provider}/start",
            new { returnUri = $"https://localhost/finance/mailbox?companyId={companyId:D}" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/mailbox-connections/gmail/callback")]
    [InlineData("/api/mailbox-connections/microsoft365/callback")]
    public async Task Provider_scoped_callback_routes_are_reachable_and_require_protected_state(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"{path}?code=oauth-code");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/companies/{0}/mailbox-connections/gmail/callback")]
    [InlineData("/api/companies/{0}/mailbox-connections/microsoft365/callback")]
    public async Task Legacy_company_scoped_callback_routes_are_reachable_and_require_protected_state(string pathTemplate)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(string.Format(
            pathTemplate,
            Guid.NewGuid().ToString("D")) + "?code=oauth-code");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("gmail")]
    [InlineData("microsoft365")]
    public async Task Legacy_callback_route_ignores_route_company_and_uses_protected_state_return_uri(string provider)
    {
        using var client = _factory.CreateClient();
        var stateCompanyId = Guid.NewGuid();
        var routeCompanyId = Guid.NewGuid();
        var state = ProtectState(new MailboxOAuthState(
            stateCompanyId,
            Guid.NewGuid(),
            MailboxProviderValues.Parse(provider),
            [new MailboxFolderSelection("INBOX", "Inbox")],
            DateTime.UtcNow.AddMinutes(10),
            new Uri($"http://localhost/finance/mailbox?companyId={stateCompanyId:D}&tab=connections")));

        var response = await client.GetAsync($"/api/companies/{routeCompanyId:D}/mailbox-connections/{provider}/callback?error=access_denied&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("localhost", location.Host);
        Assert.Contains($"companyId={stateCompanyId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"companyId={routeCompanyId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mailboxConnection=denied", location.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_callback_fallback_redirect_uses_state_company_when_return_uri_is_missing()
    {
        using var client = _factory.CreateClient();
        var stateCompanyId = Guid.NewGuid();
        var routeCompanyId = Guid.NewGuid();
        var state = ProtectState(new MailboxOAuthState(
            stateCompanyId,
            Guid.NewGuid(),
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            DateTime.UtcNow.AddMinutes(10)));

        var response = await client.GetAsync($"/api/companies/{routeCompanyId:D}/mailbox-connections/gmail/callback?error=access_denied&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Contains($"companyId={stateCompanyId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"companyId={routeCompanyId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mailboxConnection=denied", location.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("gmail", MailboxProvider.Gmail, "http://localhost:5301/api/mailbox-connections/gmail/callback")]
    [InlineData("microsoft365", MailboxProvider.Microsoft365, "http://localhost:5301/api/mailbox-connections/microsoft365/callback")]
    public async Task Provider_scoped_callback_with_valid_protected_state_completes_connection(
        string provider,
        MailboxProvider expectedProvider,
        string expectedCallbackUri)
    {
        var registry = new FakeMailboxProviderRegistry(expectedProvider);
        using var factory = new MailboxProviderTestFactory(registry);
        var seed = await SeedMailboxCompanyAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = LocalDevelopmentBaseAddress
        });
        var returnUri = new Uri($"http://localhost:5301/finance/mailbox?companyId={seed.CompanyId:D}&tab=connections");
        var state = ProtectState(factory, new MailboxOAuthState(
            seed.CompanyId,
            seed.UserId,
            expectedProvider,
            [new MailboxFolderSelection(expectedProvider == MailboxProvider.Gmail ? "INBOX" : "inbox", "Inbox")],
            DateTime.UtcNow.AddMinutes(10),
            returnUri));

        var response = await client.GetAsync($"/api/mailbox-connections/{provider}/callback?code=oauth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("/finance/mailbox", location.AbsolutePath);
        Assert.Contains($"companyId={seed.CompanyId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mailboxConnection=connected", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new Uri(expectedCallbackUri), registry.ProviderClient.LastTokenExchangeRequest?.CallbackUri);

        var connections = await CountMailboxConnectionsAsync(factory);
        Assert.Equal(1, connections);
    }

    [Fact]
    public async Task Successful_legacy_callback_completes_with_state_company_and_redirects_to_valid_state_return_uri()
    {
        using var factory = new MailboxProviderTestFactory(new FakeMailboxProviderRegistry(MailboxProvider.Gmail));
        var seed = await SeedMailboxCompanyAsync(factory);
        using var client = factory.CreateClient();
        var routeCompanyId = Guid.NewGuid();
        var returnUri = new Uri($"http://localhost/finance/mailbox?companyId={seed.CompanyId:D}&tab=connections");
        var state = ProtectState(factory, new MailboxOAuthState(
            seed.CompanyId,
            seed.UserId,
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            DateTime.UtcNow.AddMinutes(10),
            returnUri));

        var response = await client.GetAsync($"/api/companies/{routeCompanyId:D}/mailbox-connections/gmail/callback?code=oauth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("/finance/mailbox", location.AbsolutePath);
        Assert.Contains($"companyId={seed.CompanyId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"companyId={routeCompanyId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tab=connections", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mailboxConnection=connected", location.Query, StringComparison.OrdinalIgnoreCase);

        var connections = await CountMailboxConnectionsAsync(factory);
        Assert.Equal(1, connections);
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var connection = await dbContext.MailboxConnections.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(seed.CompanyId, connection.CompanyId);
            Assert.Equal(seed.UserId, connection.UserId);
        });
    }

    [Fact]
    public async Task Successful_callback_ignores_query_tenant_hints_and_fallback_redirect_uses_state_company_when_return_uri_is_rejected()
    {
        using var factory = new MailboxProviderTestFactory(new FakeMailboxProviderRegistry(MailboxProvider.Microsoft365));
        var seed = await SeedMailboxCompanyAsync(factory);
        using var client = factory.CreateClient();
        var spoofedCompanyId = Guid.NewGuid();
        var spoofedUserId = Guid.NewGuid();
        var state = ProtectState(factory, new MailboxOAuthState(
            seed.CompanyId,
            seed.UserId,
            MailboxProvider.Microsoft365,
            [new MailboxFolderSelection("inbox", "Inbox")],
            DateTime.UtcNow.AddMinutes(10),
            new Uri("https://evil.example.test/finance/mailbox?companyId=" + spoofedCompanyId.ToString("D"))));

        var response = await client.GetAsync(
            $"/api/mailbox-connections/microsoft365/callback?code=oauth-code&state={Uri.EscapeDataString(state)}&companyId={spoofedCompanyId:D}&userId={spoofedUserId:D}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("/finance/mailbox", location.AbsolutePath);
        Assert.Equal("localhost", location.Host);
        Assert.Contains($"companyId={seed.CompanyId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"companyId={spoofedCompanyId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"userId={spoofedUserId:D}", location.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mailboxConnection=connected", location.Query, StringComparison.OrdinalIgnoreCase);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var connection = await dbContext.MailboxConnections.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(seed.CompanyId, connection.CompanyId);
            Assert.Equal(seed.UserId, connection.UserId);
            Assert.Equal(MailboxProvider.Microsoft365, connection.Provider);
        });
    }

    [Theory]
    [InlineData("gmail")]
    [InlineData("microsoft365")]
    public async Task Invalid_state_on_provider_scoped_callback_does_not_persist_mailbox_connection(string provider)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/mailbox-connections/{provider}/callback?code=oauth-code&state=tampered-state");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountMailboxConnectionsAsync());
    }

    [Theory]
    [InlineData("gmail")]
    [InlineData("microsoft365")]
    public async Task Company_and_user_query_spoofing_is_ignored_without_valid_protected_state(string provider)
    {
        using var client = _factory.CreateClient();
        var spoofedCompanyId = Guid.NewGuid();
        var spoofedUserId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/mailbox-connections/{provider}/callback?code=oauth-code&state=tampered-state&companyId={spoofedCompanyId:D}&userId={spoofedUserId:D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountMailboxConnectionsAsync());
    }

    [Theory]
    [InlineData("gmail", MailboxProvider.Gmail)]
    [InlineData("microsoft365", MailboxProvider.Microsoft365)]
    public async Task Expired_state_on_provider_scoped_callback_returns_authentication_failure_without_persistence(
        string provider,
        MailboxProvider expectedProvider)
    {
        using var client = _factory.CreateClient();
        var state = ProtectState(new MailboxOAuthState(
            Guid.NewGuid(),
            Guid.NewGuid(),
            expectedProvider,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new Uri("https://localhost/finance/mailbox")));

        var response = await client.GetAsync($"/api/mailbox-connections/{provider}/callback?code=oauth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountMailboxConnectionsAsync());
    }

    [Fact]
    public async Task Provider_mismatch_between_route_and_state_returns_authentication_failure_without_persistence()
    {
        using var client = _factory.CreateClient();
        var state = ProtectState(new MailboxOAuthState(
            Guid.NewGuid(),
            Guid.NewGuid(),
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            DateTime.UtcNow.AddMinutes(10),
            new Uri("https://localhost/finance/mailbox")));

        var response = await client.GetAsync($"/api/mailbox-connections/microsoft365/callback?code=oauth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountMailboxConnectionsAsync());
    }

    [Fact]
    public async Task Company_and_user_query_spoofing_are_ignored_when_valid_state_fails_provider_match()
    {
        using var client = _factory.CreateClient();
        var stateCompanyId = Guid.NewGuid();
        var stateUserId = Guid.NewGuid();
        var state = ProtectState(new MailboxOAuthState(
            stateCompanyId,
            stateUserId,
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            DateTime.UtcNow.AddMinutes(10),
            new Uri("https://localhost/finance/mailbox")));

        var response = await client.GetAsync($"/api/mailbox-connections/microsoft365/callback?code=oauth-code&state={Uri.EscapeDataString(state)}&companyId={Guid.NewGuid():D}&userId={Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountMailboxConnectionsAsync());
    }

    [Fact]
    public async Task Provider_token_exchange_failure_returns_authentication_failure_without_persistence()
    {
        using var factory = new MailboxProviderTestFactory(
            new FakeMailboxProviderRegistry(MailboxProvider.Gmail, throwOnExchange: true));
        var seed = await SeedMailboxCompanyAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = LocalDevelopmentBaseAddress
        });
        var state = ProtectState(factory, new MailboxOAuthState(
            seed.CompanyId,
            seed.UserId,
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            DateTime.UtcNow.AddMinutes(10),
            new Uri($"http://localhost:5301/finance/mailbox?companyId={seed.CompanyId:D}")));

        var response = await client.GetAsync($"/api/mailbox-connections/gmail/callback?code=oauth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountMailboxConnectionsAsync(factory));
    }

    [Fact]
    public async Task Callback_rejects_cross_tenant_completion_when_request_company_context_differs_from_protected_state()
    {
        using var factory = new MailboxProviderTestFactory(new FakeMailboxProviderRegistry(MailboxProvider.Gmail));
        using var client = factory.CreateClient();
        var stateCompanyId = Guid.NewGuid();
        var requestCompanyId = Guid.NewGuid();
        var state = ProtectState(factory, new MailboxOAuthState(
            stateCompanyId,
            Guid.NewGuid(),
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            DateTime.UtcNow.AddMinutes(10),
            new Uri($"http://localhost/finance/mailbox?companyId={stateCompanyId:D}")));

        client.DefaultRequestHeaders.Add("X-Company-Id", requestCompanyId.ToString("D"));

        var response = await client.GetAsync($"/api/mailbox-connections/gmail/callback?code=oauth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(stateCompanyId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(requestCompanyId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);

        await factory.ExecuteDbContextAsync(dbContext =>
            Task.FromResult(Assert.Empty(dbContext.MailboxConnections.IgnoreQueryFilters())));
    }

    private Task<int> CountMailboxConnectionsAsync() =>
        CountMailboxConnectionsAsync(_factory);

    private static Task<int> CountMailboxConnectionsAsync(TestWebApplicationFactory factory) =>
        factory.ExecuteDbContextAsync(dbContext =>
            Task.FromResult(dbContext.MailboxConnections.IgnoreQueryFilters().Count()));

    private string ProtectState(MailboxOAuthState state) =>
        ProtectState(_factory, state);

    private static string ProtectState(TestWebApplicationFactory factory, MailboxOAuthState state) =>
        factory.Services.GetRequiredService<IMailboxOAuthStateProtector>().Protect(state);

    private static TestWebApplicationFactory CreateFactoryWithMailboxOAuthOptions() =>
        new(new Dictionary<string, string?>
        {
            [$"{MailboxIntegrationOptions.SectionName}:Gmail:ClientId"] = "gmail-client",
            [$"{MailboxIntegrationOptions.SectionName}:Gmail:ClientSecret"] = "gmail-secret",
            [$"{MailboxIntegrationOptions.SectionName}:Gmail:AuthorizationEndpoint"] = "https://provider.example.test/gmail/oauth",
            [$"{MailboxIntegrationOptions.SectionName}:Microsoft365:ClientId"] = "microsoft-client",
            [$"{MailboxIntegrationOptions.SectionName}:Microsoft365:ClientSecret"] = "microsoft-secret",
            [$"{MailboxIntegrationOptions.SectionName}:Microsoft365:AuthorizationEndpoint"] = "https://provider.example.test/microsoft365/oauth"
        });

    private static HttpClient CreateAuthenticatedClient(TestWebApplicationFactory factory, Uri? baseAddress = null)
    {
        var client = baseAddress is null
            ? factory.CreateClient()
            : factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = baseAddress
            });
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "mailbox-owner");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "mailbox-owner@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Mailbox Owner");
        return client;
    }

    private static async Task<MailboxStartSeed> SeedMailboxCompanyAsync(TestWebApplicationFactory factory)
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(
                userId,
                "mailbox-owner@example.com",
                "Mailbox Owner",
                "dev-header",
                "mailbox-owner"));
            dbContext.Companies.Add(new Company(companyId, "Mailbox Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new MailboxStartSeed(companyId, userId);
    }

    private sealed record MailboxStartSeed(Guid CompanyId, Guid UserId);

    private sealed record StartMailboxConnectionResponse(string AuthorizationUrl);

    private sealed class MailboxProviderTestFactory : TestWebApplicationFactory
    {
        private readonly IMailboxProviderRegistry _providerRegistry;

        public MailboxProviderTestFactory(IMailboxProviderRegistry providerRegistry)
        {
            _providerRegistry = providerRegistry;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMailboxProviderRegistry>();
                services.AddSingleton(_providerRegistry);
            });
        }
    }

    private sealed class FakeMailboxProviderRegistry : IMailboxProviderRegistry
    {
        private readonly FakeMailboxProviderClient _provider;

        public FakeMailboxProviderRegistry(MailboxProvider provider, bool throwOnExchange = false)
        {
            _provider = new FakeMailboxProviderClient(provider, throwOnExchange);
        }

        public FakeMailboxProviderClient ProviderClient => _provider;

        public IMailboxProviderClient Resolve(MailboxProvider provider) =>
            _provider.Provider == provider
                ? _provider
                : throw new InvalidOperationException("Unexpected mailbox provider.");
    }

    private sealed class FakeMailboxProviderClient : IMailboxProviderClient
    {
        private readonly bool _throwOnExchange;

        public FakeMailboxProviderClient(MailboxProvider provider, bool throwOnExchange = false)
        {
            Provider = provider;
            _throwOnExchange = throwOnExchange;
        }

        public MailboxProvider Provider { get; }
        public IReadOnlyCollection<string> DefaultScopes { get; } = ["mail.read"];
        public MailboxTokenExchangeRequest? LastTokenExchangeRequest { get; private set; }

        public Uri BuildAuthorizationUrl(MailboxAuthorizationRequest request) =>
            new($"https://provider.example.test/oauth?redirect_uri={Uri.EscapeDataString(request.CallbackUri.ToString())}&state={Uri.EscapeDataString(request.State)}");

        public Task<MailboxOAuthTokenResult> ExchangeCodeAsync(MailboxTokenExchangeRequest request, CancellationToken cancellationToken)
        {
            LastTokenExchangeRequest = request;
            if (_throwOnExchange)
            {
                throw new HttpRequestException("OAuth token endpoint rejected the mailbox credentials.");
            }

            return Task.FromResult(new MailboxOAuthTokenResult(
                    "access-token",
                    "refresh-token",
                    DateTime.UtcNow.AddHours(1),
                    DefaultScopes));
        }

        public Task<MailboxOAuthTokenResult> RefreshTokenAsync(MailboxRefreshTokenRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MailboxOAuthTokenResult("access-token", "refresh-token", DateTime.UtcNow.AddHours(1), DefaultScopes));

        public Task<MailboxAccountProfile> GetAccountProfileAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(new MailboxAccountProfile("ap@example.com", "AP", "provider-account"));

        public Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(string accessToken, MailboxMessageQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MailboxMessageSummary>>([]);
    }
}
