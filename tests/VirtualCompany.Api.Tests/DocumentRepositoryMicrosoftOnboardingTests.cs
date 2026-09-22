using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DocumentRepositoryMicrosoftOnboardingTests
{
    [Fact]
    public async Task Begin_uses_pkce_protects_setup_material_and_rejects_open_redirects()
    {
        using var factory = new OnboardingFactory(configured: true);
        var seed = await Seed(factory, includeOtherCompany: true);
        using var client = Client(factory, seed.Subject, seed.Email);

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding",
            new BeginMicrosoft365OnboardingCommand($"/settings/document-repositories?companyId={seed.CompanyId:D}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var start = (await response.Content.ReadFromJsonAsync<Microsoft365OnboardingStartDto>())!;
        var uri = new Uri(start.AuthorizationUrl);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal(start.SessionHandle, query["state"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Equal("consent", query["prompt"].ToString());
        var requestedScopes = query["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(requestedScopes.Length, requestedScopes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("client_secret", start.AuthorizationUrl, StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var session = await db.CompanyDocumentRepositoryOnboardingSessions.IgnoreQueryFilters().SingleAsync();
            Assert.DoesNotContain(start.SessionHandle, session.ProtectedSetupMaterial!, StringComparison.Ordinal);
            Assert.DoesNotContain(query["nonce"].ToString(), session.ProtectedSetupMaterial!, StringComparison.Ordinal);
            var material = scope.ServiceProvider.GetRequiredService<IMicrosoft365OnboardingMaterialProtector>().UnprotectSetup(session.ProtectedSetupMaterial!);
            var expectedChallenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(material.CodeVerifier)));
            Assert.Equal(expectedChallenge, query["code_challenge"].ToString());
            Assert.Equal(material.Nonce, query["nonce"].ToString());
        }

        var redirect = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding",
            new BeginMicrosoft365OnboardingCommand("https://evil.example/steal"));
        Assert.Equal(HttpStatusCode.BadRequest, redirect.StatusCode);
    }

    [Fact]
    public async Task Status_is_tenant_and_user_bound_and_cancellation_destroys_material_and_denies_replay()
    {
        using var factory = new OnboardingFactory(configured: true);
        var seed = await Seed(factory, includeOtherCompany: true);
        using var client = Client(factory, seed.Subject, seed.Email);
        var startResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding",
            new BeginMicrosoft365OnboardingCommand("/settings/document-repositories"));
        var start = (await startResponse.Content.ReadFromJsonAsync<Microsoft365OnboardingStartDto>())!;

        var wrongCompany = await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(start.SessionHandle)}");
        Assert.Equal(HttpStatusCode.NotFound, wrongCompany.StatusCode);
        var status = await client.GetFromJsonAsync<Microsoft365OnboardingStatusDto>($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(start.SessionHandle)}");
        Assert.Equal(DocumentRepositoryOnboardingStatuses.AwaitingAuthorization, status!.Status);

        var cancelled = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(start.SessionHandle)}/cancel",
            new { expectedConcurrencyVersion = status.ConcurrencyVersion });
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var session = await db.CompanyDocumentRepositoryOnboardingSessions.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(DocumentRepositoryOnboardingStatuses.Cancelled, session.Status);
            Assert.Null(session.ProtectedSetupMaterial);
        }

        var replay = await client.GetAsync($"/api/document-repositories/microsoft/callback?state={Uri.EscapeDataString(start.SessionHandle)}&code=forged");
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
    }

    [Fact]
    public async Task Missing_platform_configuration_is_specific_and_does_not_block_existing_manual_connections()
    {
        using var factory = new OnboardingFactory(configured: false);
        var seed = await Seed(factory, includeOtherCompany: false);
        using var client = Client(factory, seed.Subject, seed.Email);

        var onboarding = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding",
            new BeginMicrosoft365OnboardingCommand("/settings/document-repositories"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, onboarding.StatusCode);
        Assert.Contains("configuration_unavailable", await onboarding.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var manual = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories", new ConfigureDocumentRepositoryConnectionCommand(
            DocumentRepositoryProviderKinds.SharePointLibrary, Guid.NewGuid(), Guid.NewGuid(), "customer-secret", "drive", "root", "Manual", DocumentRepositoryAudiences.Company));
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        var dto = (await manual.Content.ReadFromJsonAsync<DocumentRepositoryConnectionDto>())!;
        Assert.Equal(DocumentRepositoryCredentialModes.CustomerManaged, dto.CredentialMode);
    }

    [Fact]
    public async Task Development_user_secret_is_seeded_into_the_platform_secret_store()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Microsoft365DocumentOnboardingDevelopmentSecretBootstrapper.DevelopmentClientSecretKey] = "development-client-secret"
        }).Build();
        var store = new RecordingSecretStore();
        var bootstrapper = new Microsoft365DocumentOnboardingDevelopmentSecretBootstrapper(
            new DevelopmentHostEnvironment(),
            configuration,
            Options.Create(new Microsoft365DocumentOnboardingOptions { CredentialReference = "platform/microsoft365/client-secret" }),
            store,
            NullLogger<Microsoft365DocumentOnboardingDevelopmentSecretBootstrapper>.Instance);

        await bootstrapper.StartAsync(CancellationToken.None);

        Assert.Equal("platform/microsoft365/client-secret", store.LastWrittenName);
        Assert.Equal("development-client-secret", store.LastWrittenValue);
    }

    [Fact]
    public async Task Production_ignores_the_development_client_secret()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Microsoft365DocumentOnboardingDevelopmentSecretBootstrapper.DevelopmentClientSecretKey] = "must-not-be-used"
        }).Build();
        var store = new RecordingSecretStore();
        var environment = new DevelopmentHostEnvironment { EnvironmentName = Environments.Production };
        var bootstrapper = new Microsoft365DocumentOnboardingDevelopmentSecretBootstrapper(
            environment,
            configuration,
            Options.Create(new Microsoft365DocumentOnboardingOptions { CredentialReference = "platform/microsoft365/client-secret" }),
            store,
            NullLogger<Microsoft365DocumentOnboardingDevelopmentSecretBootstrapper>.Instance);

        await bootstrapper.StartAsync(CancellationToken.None);

        Assert.Null(store.LastWrittenName);
        Assert.Null(store.LastWrittenValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://web.example")]
    [InlineData("http://localhost:5062")]
    public async Task Valid_callback_associates_verified_tenant_encrypts_tokens_and_is_single_use(string webOrigin)
    {
        using var factory = new OnboardingFactory(configured: true, webOrigin: webOrigin);
        var seed = await Seed(factory, includeOtherCompany: false);
        using var client = Client(factory, seed.Subject, seed.Email);
        var startResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding",
            new BeginMicrosoft365OnboardingCommand("/settings/document-repositories"));
        var start = (await startResponse.Content.ReadFromJsonAsync<Microsoft365OnboardingStartDto>())!;

        var callback = await client.GetAsync($"/api/document-repositories/microsoft/callback?state={Uri.EscapeDataString(start.SessionHandle)}&code=one-time-code");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal($"{webOrigin}/settings/document-repositories?microsoft365Session={Uri.EscapeDataString(start.SessionHandle)}", callback.Headers.Location!.OriginalString);
        var completed = (await client.GetFromJsonAsync<Microsoft365OnboardingStatusDto>($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(start.SessionHandle)}"))!;
        Assert.Equal(DocumentRepositoryOnboardingStatuses.Authorized, completed.Status);
        Assert.Equal(OnboardingFactory.ProviderTenantId, completed.ProviderTenantId);
        Assert.Equal(start.SessionHandle, completed.SessionHandle);
        using (var scope = factory.Services.CreateScope())
        {
            var session = await scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>().CompanyDocumentRepositoryOnboardingSessions.IgnoreQueryFilters().SingleAsync();
            Assert.NotNull(session.ProtectedSetupMaterial);
            Assert.DoesNotContain("delegated-access-token", session.ProtectedSetupMaterial!, StringComparison.Ordinal);
            Assert.DoesNotContain("delegated-refresh-token", session.ProtectedSetupMaterial!, StringComparison.Ordinal);
        }

        var replay = await client.GetAsync($"/api/document-repositories/microsoft/callback?state={Uri.EscapeDataString(start.SessionHandle)}&code=one-time-code");
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
    }

    [Fact]
    public async Task Consent_denial_redirects_back_to_the_opaque_session_for_safe_recovery()
    {
        using var factory = new OnboardingFactory(configured: true, webOrigin: "https://web.example");
        var seed = await Seed(factory, includeOtherCompany: false);
        using var client = Client(factory, seed.Subject, seed.Email);
        var start = (await (await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding", new BeginMicrosoft365OnboardingCommand($"/settings/document-repositories?companyId={seed.CompanyId:D}"))).Content.ReadFromJsonAsync<Microsoft365OnboardingStartDto>())!;

        var callback = await client.GetAsync($"/api/document-repositories/microsoft/callback?state={Uri.EscapeDataString(start.SessionHandle)}&error=access_denied");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("microsoft365Session=", callback.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Equal("web.example", callback.Headers.Location.Host);
        Assert.Contains($"companyId={seed.CompanyId:D}", callback.Headers.Location.Query, StringComparison.Ordinal);
        var status = await client.GetFromJsonAsync<Microsoft365OnboardingStatusDto>($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(start.SessionHandle)}");
        Assert.Equal(DocumentRepositoryOnboardingStatuses.Failed, status!.Status);
        Assert.Equal(DocumentRepositoryOnboardingFailureCodes.ConsentDenied, status.FailureCode);
    }

    [Theory]
    [InlineData("Files.ReadWrite Sites.Read.All")]
    [InlineData("https://graph.microsoft.com/Files.ReadWrite https://graph.microsoft.com/Sites.Read.All")]
    [InlineData("Files.ReadWrite https://graph.microsoft.com/Sites.Read.All")]
    public async Task Authorized_session_discovers_and_selects_folders_with_only_opaque_session_bound_handles(string grantedScopes)
    {
        using var factory = new OnboardingFactory(configured: true, grantedScopes: grantedScopes);
        var seed = await Seed(factory, includeOtherCompany: false);
        using var client = Client(factory, seed.Subject, seed.Email);
        var started = (await (await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding", new BeginMicrosoft365OnboardingCommand("/settings/document-repositories"))).Content.ReadFromJsonAsync<Microsoft365OnboardingStartDto>())!;
        var callback = await client.GetAsync($"/api/document-repositories/microsoft/callback?state={Uri.EscapeDataString(started.SessionHandle)}&code=one-time-code");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var authorized = (await client.GetFromJsonAsync<Microsoft365OnboardingStatusDto>($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}"))!;
        var kinds = await client.GetFromJsonAsync<List<Microsoft365SourceKindDto>>($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/source-kinds");
        Assert.All(kinds!, kind => Assert.True(kind.IsAvailable));
        var sitesResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/sources/sharepoint/sites?query=Finance");
        Assert.Equal(HttpStatusCode.OK, sitesResponse.StatusCode);
        var sources = await client.GetFromJsonAsync<Microsoft365SourcePageDto>($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/sources/onedrive");
        var source = Assert.Single(sources!.Items);
        Assert.DoesNotContain("drive-1", source.SelectionHandle, StringComparison.Ordinal);
        var folders = await client.GetFromJsonAsync<Microsoft365FolderPageDto>($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/folders?sourceHandle={Uri.EscapeDataString(source.SelectionHandle)}");
        var folder = Assert.Single(folders!.Items);
        Assert.DoesNotContain("folder-1", folder.SelectionHandle, StringComparison.Ordinal);
        var forged = await client.GetAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/folders?sourceHandle=forged");
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        var selectionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/selection", new SelectMicrosoft365RepositoryRootCommand(source.SelectionHandle, folder.SelectionHandle, authorized.ConcurrencyVersion));
        Assert.Equal(HttpStatusCode.OK, selectionResponse.StatusCode);
        var selection = (await selectionResponse.Content.ReadFromJsonAsync<Microsoft365RepositorySelectionDto>())!;
        Assert.Equal("Approved folder", selection.RootDisplayName);
        Assert.True(selection.CanAssignSelectedApplicationPermission);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Empty(await db.CompanyDocumentRepositoryConnections.IgnoreQueryFilters().ToListAsync());
        var session = await db.CompanyDocumentRepositoryOnboardingSessions.IgnoreQueryFilters().SingleAsync();
        var state = scope.ServiceProvider.GetRequiredService<IMicrosoft365OnboardingMaterialProtector>().UnprotectState(session.ProtectedSetupMaterial!);
        Assert.Equal("folder-1", state.Selection!.RootItemId);

        var accessResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/access",
            new ConfigureMicrosoft365RepositoryAccessCommand(false, null, [], selection.SelectionVersion));
        Assert.Equal(HttpStatusCode.OK, accessResponse.StatusCode);
        var access = (await accessResponse.Content.ReadFromJsonAsync<Microsoft365RepositoryAccessDraftDto>())!;
        Assert.False(access.EnableWrites);
        Assert.Empty(access.AgentIds);
        var review = await client.GetFromJsonAsync<Microsoft365RepositoryReviewDto>($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/review");
        Assert.Equal("Read-only", review!.AccessMode);
        Assert.Empty(review.Agents);
        Assert.Single(review.PermissionChanges);

        var finalized = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/finalize",
            new FinalizeMicrosoft365RepositoryCommand(access.DraftVersion, true));
        Assert.Equal(HttpStatusCode.Accepted, finalized.StatusCode);
        var firstOperation = (await finalized.Content.ReadFromJsonAsync<Microsoft365RepositoryProvisioningDto>())!;
        var duplicate = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(started.SessionHandle)}/finalize",
            new FinalizeMicrosoft365RepositoryCommand(access.DraftVersion, true));
        var duplicateOperation = (await duplicate.Content.ReadFromJsonAsync<Microsoft365RepositoryProvisioningDto>())!;
        Assert.Equal(firstOperation.Id, duplicateOperation.Id);
        Assert.Single(await db.CompanyDocumentRepositoryProvisionings.IgnoreQueryFilters().ToListAsync());
    }
    [Fact]
    public void Session_expiry_and_cancellation_make_protected_material_unusable()
    {
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var session = new CompanyDocumentRepositoryOnboardingSession(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), new string('b', 64), "ciphertext", "/settings/document-repositories", "correlation", now, now.AddMinutes(10));
        session.Expire(now.AddMinutes(11));
        Assert.Equal(DocumentRepositoryOnboardingStatuses.Expired, session.Status);
        Assert.Null(session.ProtectedSetupMaterial);
        Assert.Throws<InvalidOperationException>(() => session.CompleteAuthorization(Guid.NewGuid(), "token", now.AddMinutes(11)));
    }

    [Fact]
    public void Partial_managed_grant_cleanup_uses_a_durable_retryable_state_machine()
    {
        var now = new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);
        var operation = new CompanyDocumentRepositoryProvisioning(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DocumentRepositoryProviderKinds.OneDriveForBusiness, Guid.NewGuid(), null, "drive-1", "root-1",
            "Business OneDrive", "Approved root", false, null, null, "correlation", now);
        operation.RecordRootPermission("permission-1", true, now);
        operation.Fail("validation_failed", "Application-only validation failed.", false, now);

        Assert.True(operation.CanCleanup);
        operation.QueueCleanup(now.AddSeconds(1));
        Assert.Equal(DocumentRepositoryProvisioningStatuses.CleanupQueued, operation.Status);
        Assert.False(operation.CanCleanup);

        operation.FailCleanup("cleanup_outcome_ambiguous", "Cleanup requires reconciliation.", true, now.AddSeconds(2));
        Assert.True(operation.CanRetry);
        operation.QueueRetry(now.AddSeconds(3));
        Assert.Equal(DocumentRepositoryProvisioningStatuses.CleanupQueued, operation.Status);
    }

    private static async Task<SeedData> Seed(TestWebApplicationFactory factory, bool includeOtherCompany)
    {
        var userId = Guid.NewGuid(); var companyId = Guid.NewGuid(); var otherCompanyId = Guid.NewGuid();
        var subject = $"m365-onboarding-{Guid.NewGuid():N}"; var email = $"{subject}@example.com";
        await factory.SeedAsync(db =>
        {
            db.Users.Add(new User(userId, email, subject, "dev-header", subject));
            db.Companies.Add(new Company(companyId, "Onboarding company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            if (includeOtherCompany) { db.Companies.Add(new Company(otherCompanyId, "Other company")); db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active)); }
            return Task.CompletedTask;
        });
        return new(subject, email, companyId, otherCompanyId);
    }

    private static HttpClient Client(TestWebApplicationFactory factory, string subject, string email)
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed record SeedData(string Subject, string Email, Guid CompanyId, Guid OtherCompanyId);
    private sealed class DevelopmentHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "VirtualCompany.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingSecretStore : IPlatformSecretStore
    {
        public string BackendName => "test";
        public bool SupportsWrites => true;
        public string? LastWrittenName { get; private set; }
        public string? LastWrittenValue { get; private set; }

        public Task<PlatformSecretValue?> GetAsync(string name, string? version, CancellationToken cancellationToken) =>
            Task.FromResult<PlatformSecretValue?>(LastWrittenName == name && LastWrittenValue is not null
                ? new(LastWrittenValue, "v1", DateTime.UtcNow)
                : null);

        public Task<PlatformSecretWriteResult> SetAsync(string name, string value, CancellationToken cancellationToken)
        {
            LastWrittenName = name;
            LastWrittenValue = value;
            return Task.FromResult(new PlatformSecretWriteResult("v1", DateTime.UtcNow));
        }
    }

    [Theory]
    [InlineData("Files.ReadWrite Sites.Read.All", true)]
    [InlineData("https://graph.microsoft.com/Files.ReadWrite https://graph.microsoft.com/Sites.Read.All", true)]
    [InlineData("Files.ReadWrite https://graph.microsoft.com/Sites.Read.All", true)]
    [InlineData("https://graph.microsoft.com/Files.ReadWrite", false)]
    [InlineData("https://other.example/Files.ReadWrite https://other.example/Sites.Read.All", false)]
    public async Task Callback_checks_granted_graph_scopes_in_both_response_formats(string scopes, bool authorized)
    {
        using var factory = new OnboardingFactory(true, grantedScopes: scopes);
        var seed = await Seed(factory, includeOtherCompany: false);
        using var client = Client(factory, seed.Subject, seed.Email);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding",
            new BeginMicrosoft365OnboardingCommand("/settings/document-repositories"));
        var start = (await response.Content.ReadFromJsonAsync<Microsoft365OnboardingStartDto>())!;
        var callback = await client.GetAsync($"/api/document-repositories/microsoft/callback?state={Uri.EscapeDataString(start.SessionHandle)}&code=one-time-code");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var status = (await client.GetFromJsonAsync<Microsoft365OnboardingStatusDto>($"/api/companies/{seed.CompanyId}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(start.SessionHandle)}"))!;
        Assert.Equal(authorized ? DocumentRepositoryOnboardingStatuses.Authorized : DocumentRepositoryOnboardingStatuses.Failed, status.Status);
        if (!authorized) Assert.Equal(DocumentRepositoryOnboardingFailureCodes.ConsentDenied, status.FailureCode);
    }

    private sealed class OnboardingFactory(bool configured, string webOrigin = "", string grantedScopes = "Files.ReadWrite Sites.Read.All") : TestWebApplicationFactory
    {
        public static readonly Guid ProviderTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Microsoft365DocumentOnboarding:PlatformClientId"] = configured ? "11111111-1111-1111-1111-111111111111" : "",
                ["Microsoft365DocumentOnboarding:WebOrigin"] = webOrigin,
                ["Microsoft365DocumentOnboarding:CallbackUri"] = configured ? "https://localhost/api/document-repositories/microsoft/callback" : "",
                ["Microsoft365DocumentOnboarding:CredentialReference"] = configured ? "platform/microsoft365/client-secret" : ""
                ,["Microsoft365DocumentOnboarding:ProvisioningPollIntervalSeconds"] = "60"
            }));
            if (configured) builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPlatformSecretStore>();
                services.AddSingleton<IPlatformSecretStore, StubSecretStore>();
                services.RemoveAll<IMicrosoft365IdTokenValidator>();
                services.AddSingleton<IMicrosoft365IdTokenValidator, StubIdTokenValidator>();
                services.AddHttpClient(DocumentRepositoryMicrosoftOnboardingService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new TokenHandler(grantedScopes));
                services.AddHttpClient(MicrosoftGraphDocumentRepositorySetupAdapter.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new DiscoveryHandler());
            });
        }

        private sealed class StubSecretStore : IPlatformSecretStore
        {
            public string BackendName => "test";
            public bool SupportsWrites => true;
            public Task<PlatformSecretValue?> GetAsync(string name, string? version, CancellationToken cancellationToken) => Task.FromResult<PlatformSecretValue?>(new("client-secret", "v1", DateTime.UtcNow));
            public Task<PlatformSecretWriteResult> SetAsync(string name, string value, CancellationToken cancellationToken) => Task.FromResult(new PlatformSecretWriteResult("v1", DateTime.UtcNow));
        }
        private sealed class StubIdTokenValidator : IMicrosoft365IdTokenValidator
        {
            public Task<ClaimsPrincipal> ValidateAsync(Microsoft365DocumentOnboardingOptions options, string token, string nonce, CancellationToken cancellationToken) => Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("tid", ProviderTenantId.ToString("D")),
                new Claim("wids", "62e90394-69f5-4237-9190-012177145e10"),
                new Claim("nonce", nonce)
            ], "test")));
        }
        private sealed class DiscoveryHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.AbsolutePath;
                var json = path.EndsWith("/sites", StringComparison.Ordinal) ? """{"value":[]}"""
                    : path.EndsWith("/me/drive", StringComparison.Ordinal)
                    ? """{"id":"drive-1","name":"Business OneDrive","driveType":"business","owner":{"user":{"displayName":"Microsoft administrator"}},"root":{"id":"root-1","name":"Root","folder":{}}}"""
                    : path.EndsWith("/drives/drive-1/items/root-1/children", StringComparison.Ordinal)
                        ? """{"value":[{"id":"folder-1","name":"Approved folder","folder":{},"parentReference":{"driveId":"drive-1","id":"root-1"}}]}"""
                        : path.EndsWith("/drives/drive-1/items/folder-1", StringComparison.Ordinal)
                            ? """{"id":"folder-1","name":"Approved folder","folder":{},"parentReference":{"driveId":"drive-1","id":"root-1"}}"""
                            : """{"id":"root-1","name":"Root","folder":{},"parentReference":{"driveId":"drive-1"}}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
        }
        private sealed class TokenHandler(string grantedScopes) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { access_token = "delegated-access-token", refresh_token = "delegated-refresh-token", id_token = "signed-id-token", expires_in = 3600, scope = grantedScopes })
            });
        }
    }
}
