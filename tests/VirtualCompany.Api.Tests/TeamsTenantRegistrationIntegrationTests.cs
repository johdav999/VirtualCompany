using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsTenantRegistrationIntegrationTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private readonly TeamsIdentityFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Platform_admin_completes_single_use_consent_and_disable_blocks_callbacks_immediately()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Companies.AddRange(new Company(companyId, "Teams company"), new Company(otherCompanyId, "Other company"));
            return Task.CompletedTask;
        });
        using var client = Client("alice");

        var associationResponse = await client.PutAsJsonAsync(
            $"/api/platform/teams-presenter/companies/{companyId:D}",
            new { entraTenantId = TenantId, approvedMediaRoute = "teams_application_hosted" });
        var association = await associationResponse.Content.ReadFromJsonAsync<TeamsTenantRegistrationDto>();
        Assert.Equal(HttpStatusCode.OK, associationResponse.StatusCode);
        Assert.Equal(TeamsTenantRegistrationStates.PendingConsent, association!.Status);

        var duplicateResponse = await client.PutAsJsonAsync(
            $"/api/platform/teams-presenter/companies/{otherCompanyId:D}",
            new { entraTenantId = TenantId, approvedMediaRoute = "teams_application_hosted" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        var startResponse = await client.PostAsync(
            $"/api/platform/teams-presenter/companies/{companyId:D}/admin-consent", null);
        var start = await startResponse.Content.ReadFromJsonAsync<TeamsAdminConsentStartResult>();
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.NotNull(start);
        Assert.Equal(TenantId.ToString("D"), start.AuthorizationUrl.AbsolutePath.Split('/')[1]);
        var state = QueryHelpers.ParseQuery(start.AuthorizationUrl.Query)["state"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(state));

        var callbackUrl = QueryHelpers.AddQueryString(
            "/api/platform/teams-presenter/admin-consent/callback",
            new Dictionary<string, string?>
            {
                ["state"] = state,
                ["tenant"] = TenantId.ToString("D"),
                ["admin_consent"] = "True"
            });
        var callbackResponse = await client.GetAsync(callbackUrl);
        var verified = await callbackResponse.Content.ReadFromJsonAsync<TeamsTenantRegistrationDto>();
        Assert.Equal(HttpStatusCode.OK, callbackResponse.StatusCode);
        Assert.Equal(TeamsTenantRegistrationStates.PendingPolicy, verified!.Status);
        Assert.Equal(TeamsTenantVerificationStates.Verified, verified.PermissionStatus);

        var replayResponse = await client.GetAsync(callbackUrl);
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);
        Assert.Contains(TeamsIdentityFailureCodes.ConsentStateReplayed,
            await replayResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var policyResponse = await client.PostAsJsonAsync(
            $"/api/platform/teams-presenter/companies/{companyId:D}/policy-attestation",
            new { approved = true });
        var ready = await policyResponse.Content.ReadFromJsonAsync<TeamsTenantRegistrationDto>();
        Assert.Equal(TeamsTenantRegistrationStates.Ready, ready!.Status);

        var readinessResponse = await client.GetAsync($"/api/platform/teams-presenter/readiness?companyId={companyId:D}");
        var readinessBody = await readinessResponse.Content.ReadAsStringAsync();
        var readiness = await readinessResponse.Content.ReadFromJsonAsync<TeamsPresenterReadinessDto>();
        Assert.True(readinessResponse.IsSuccessStatusCode, readinessBody);
        Assert.Contains(readiness!.Checks, item => item.Name == TeamsPresenterReadinessCheckNames.CallbackAuthentication && item.Ready);
        Assert.False(readiness.LiveCallingReady);
        Assert.Contains(TenantId.ToString("D"), readinessBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.TokenProvider.AccessToken, readinessBody, StringComparison.Ordinal);

        using var authenticatedCallback = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/teams/calls");
        authenticatedCallback.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "signed-callback-token");
        using var callbackAcceptedButUnavailable = await client.SendAsync(authenticatedCallback);
        Assert.Equal(HttpStatusCode.NoContent, callbackAcceptedButUnavailable.StatusCode);

        var disableResponse = await client.PostAsJsonAsync(
            $"/api/platform/teams-presenter/companies/{companyId:D}/disable",
            new { consentRevoked = true });
        var disabled = await disableResponse.Content.ReadFromJsonAsync<TeamsTenantRegistrationDto>();
        Assert.Equal(TeamsTenantRegistrationStates.Revoked, disabled!.Status);

        using var blockedCallback = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/teams/calls");
        blockedCallback.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "signed-callback-token");
        using var blockedResponse = await client.SendAsync(blockedCallback);
        Assert.Equal(HttpStatusCode.Unauthorized, blockedResponse.StatusCode);

        await factory.ExecuteDbContextAsync(async db =>
        {
            var persisted = await db.TeamsTenantRegistrations.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
            Assert.DoesNotContain(factory.TokenProvider.AccessToken, persisted.GrantedPermissions, StringComparison.Ordinal);
            Assert.Equal(TeamsTenantVerificationStates.Revoked, persisted.PermissionStatus);
            Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x =>
                x.CompanyId == companyId && x.Action == "sales.teams_admin_consent.verified"));
        });
    }

    [Fact]
    public async Task Ordinary_user_cannot_associate_a_tenant()
    {
        var companyId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Companies.Add(new Company(companyId, "Protected company"));
            return Task.CompletedTask;
        });
        using var client = Client("bob");

        var response = await client.PutAsJsonAsync(
            $"/api/platform/teams-presenter/companies/{companyId:D}",
            new { entraTenantId = TenantId, approvedMediaRoute = "teams_application_hosted" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_callback_from_an_unassociated_tenant_is_rejected_without_disclosure()
    {
        using var client = Client("alice");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/teams/calls");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", "signed-callback-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TenantId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("company", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_tenant_and_expired_consent_callbacks_remain_fail_closed()
    {
        var companyId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Companies.Add(new Company(companyId, "Consent boundary company"));
            return Task.CompletedTask;
        });
        using var client = Client("alice");
        using var association = await client.PutAsJsonAsync(
            $"/api/platform/teams-presenter/companies/{companyId:D}",
            new { entraTenantId = TenantId, approvedMediaRoute = "teams_application_hosted" });
        association.EnsureSuccessStatusCode();

        var wrongTenantState = await StartStateAsync(client, companyId);
        var wrongTenantUrl = QueryHelpers.AddQueryString(
            "/api/platform/teams-presenter/admin-consent/callback",
            new Dictionary<string, string?>
            {
                ["state"] = wrongTenantState,
                ["tenant"] = Guid.NewGuid().ToString("D"),
                ["admin_consent"] = "True"
            });
        using var wrongTenant = await client.GetAsync(wrongTenantUrl);
        Assert.Equal(HttpStatusCode.BadRequest, wrongTenant.StatusCode);
        Assert.Contains(TeamsIdentityFailureCodes.ConsentTenantMismatch,
            await wrongTenant.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var unchangedResponse = await client.GetAsync($"/api/platform/teams-presenter/companies/{companyId:D}");
        var unchanged = await unchangedResponse.Content.ReadFromJsonAsync<TeamsTenantRegistrationDto>();
        Assert.Equal(TeamsTenantRegistrationStates.PendingConsent, unchanged!.Status);
        Assert.Null(unchanged.FailureCode);

        var expiredState = await StartStateAsync(client, companyId);
        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        var expiredUrl = QueryHelpers.AddQueryString(
            "/api/platform/teams-presenter/admin-consent/callback",
            new Dictionary<string, string?>
            {
                ["state"] = expiredState,
                ["tenant"] = TenantId.ToString("D"),
                ["admin_consent"] = "True"
            });
        using var expired = await client.GetAsync(expiredUrl);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Contains(TeamsIdentityFailureCodes.ConsentStateExpired,
            await expired.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, factory.TokenProvider.AcquisitionCount);
    }

    private static async Task<string> StartStateAsync(HttpClient client, Guid companyId)
    {
        using var response = await client.PostAsync(
            $"/api/platform/teams-presenter/companies/{companyId:D}/admin-consent", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TeamsAdminConsentStartResult>();
        return QueryHelpers.ParseQuery(result!.AuthorizationUrl.Query)["state"].ToString();
    }

    private HttpClient Client(string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, $"{subject}@example.test");
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed class TeamsIdentityFactory : TestWebApplicationFactory
    {
        public TeamsIdentityFactory() : this(new MutableTimeProvider(
            new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero))) { }

        private TeamsIdentityFactory(MutableTimeProvider clock) : base(clock, Configuration(), false)
        {
            Clock = clock;
        }

        public MutableTimeProvider Clock { get; }
        public FakeAppTokenProvider TokenProvider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITeamsAppOnlyTokenProvider>();
                services.AddSingleton<ITeamsAppOnlyTokenProvider>(TokenProvider);
                services.RemoveAll<ITeamsCallbackTokenValidator>();
                services.AddSingleton<ITeamsCallbackTokenValidator>(new FixedCallbackTokenValidator(TenantId));
            });
        }

        private static IReadOnlyDictionary<string, string?> Configuration()
        {
            var options = TeamsPresenterPackageBuilderTests.ValidOptions();
            return new Dictionary<string, string?>
            {
                ["PlatformAdministration:AdministratorIdentities:0"] = "dev-header:alice",
                ["TeamsPresenter:Enabled"] = "true",
                ["TeamsPresenter:TeamsAppId"] = options.TeamsAppId,
                ["TeamsPresenter:BotApplicationId"] = options.BotApplicationId,
                ["TeamsPresenter:WebApplicationId"] = options.WebApplicationId,
                ["TeamsPresenter:WebApplicationResource"] = options.WebApplicationResource,
                ["TeamsPresenter:TenantMode"] = options.TenantMode,
                ["TeamsPresenter:AllowedTenantIds:0"] = TenantId.ToString("D"),
                ["TeamsPresenter:PublicApiOrigin"] = options.PublicApiOrigin,
                ["TeamsPresenter:BotNotificationUrl"] = options.BotNotificationUrl,
                ["TeamsPresenter:BotCallingCallbackUrl"] = options.BotCallingCallbackUrl,
                ["TeamsPresenter:AdminConsentRedirectUrl"] = options.AdminConsentRedirectUrl,
                ["TeamsPresenter:WebOrigin"] = options.WebOrigin,
                ["TeamsPresenter:ConfigurationUrl"] = options.ConfigurationUrl,
                ["TeamsPresenter:SidePanelUrl"] = options.SidePanelUrl,
                ["TeamsPresenter:StageUrl"] = options.StageUrl,
                ["TeamsPresenter:MediaRoute"] = "teams_application_hosted",
                ["TeamsPresenter:MediaRouteApproved"] = "true",
                ["TeamsPresenter:CredentialMode"] = "certificate",
                ["TeamsPresenter:CertificateReference"] = options.CertificateReference,
                ["TeamsPresenter:PackageVersion"] = options.PackageVersion,
                ["TeamsPresenter:CallControlEnabled"] = "true",
                ["TeamsPresenter:SharedStageEnabled"] = "true"
            };
        }
    }

    private sealed class FakeAppTokenProvider : ITeamsAppOnlyTokenProvider
    {
        public string AccessToken { get; } = "super-secret-access-token";
        public int AcquisitionCount { get; private set; }

        public Task<TeamsAppOnlyAccessToken> AcquireAsync(TeamsAppOnlyTokenRequest request, CancellationToken cancellationToken)
        {
            AcquisitionCount++;
            return Task.FromResult(new TeamsAppOnlyAccessToken(
                AccessToken,
                DateTime.UtcNow.AddHours(1),
                request.EntraTenantId,
                request.RequiredPermissions.OrderBy(value => value, StringComparer.Ordinal).ToArray()));
        }
    }

    private sealed class FixedCallbackTokenValidator(Guid tenantId) : ITeamsCallbackTokenValidator
    {
        public Task<Guid> ValidateAsync(string token, CancellationToken cancellationToken) => Task.FromResult(tenantId);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan amount) => current = current.Add(amount);
    }
}
