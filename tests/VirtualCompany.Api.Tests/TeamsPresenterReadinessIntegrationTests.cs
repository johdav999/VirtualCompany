using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Security;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsPresenterReadinessIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory factory = new(Configuration());

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Platform_administrator_receives_safe_fail_closed_readiness()
    {
        using var client = CreateClient("alice", "alice@example.com");

        using var response = await client.GetAsync("/api/platform/teams-presenter/readiness");
        var body = await response.Content.ReadAsStringAsync();
        var readiness = await response.Content.ReadFromJsonAsync<TeamsPresenterReadinessDto>();

        Assert.True(response.IsSuccessStatusCode, body);
        Assert.NotNull(readiness);
        Assert.True(readiness.Enabled);
        Assert.True(readiness.PackageReady);
        Assert.False(readiness.LiveCallingReady);
        Assert.False(readiness.EffectiveCapabilities.CallControl);
        Assert.Contains(readiness.Checks, check =>
            check.Name == TeamsPresenterReadinessCheckNames.TenantApproval &&
            check.ReasonCode == TeamsPresenterReadinessReasonCodes.TenantApprovalNotVerified);
        Assert.Contains(readiness.Checks, check =>
            check.Name == TeamsPresenterReadinessCheckNames.CallControl &&
            check.ReasonCode == TeamsPresenterReadinessReasonCodes.CallControlNotImplemented);
        Assert.DoesNotContain("keyvault://virtual-company/teams-presenter-certificate", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("10000000-0000-0000-0000-000000000001", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ordinary_authenticated_user_cannot_read_platform_readiness()
    {
        using var client = CreateClient("bob", "bob@example.com");

        using var response = await client.GetAsync("/api/platform/teams-presenter/readiness");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ordinary_authenticated_user_cannot_download_installable_package()
    {
        using var client = CreateClient("bob", "bob@example.com");

        using var response = await client.GetAsync("/api/platform/teams-presenter/package");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(string subject, string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private static IReadOnlyDictionary<string, string?> Configuration()
    {
        var options = TeamsPresenterPackageBuilderTests.ValidOptions();
        return new Dictionary<string, string?>
        {
            ["PlatformAdministration:AdministratorIdentities:0"] = "dev-header:alice",
            ["TeamsPresenter:Enabled"] = options.Enabled.ToString(),
            ["TeamsPresenter:TeamsAppId"] = options.TeamsAppId,
            ["TeamsPresenter:BotApplicationId"] = options.BotApplicationId,
            ["TeamsPresenter:WebApplicationId"] = options.WebApplicationId,
            ["TeamsPresenter:WebApplicationResource"] = options.WebApplicationResource,
            ["TeamsPresenter:TenantMode"] = options.TenantMode,
            ["TeamsPresenter:AllowedTenantIds:0"] = options.AllowedTenantIds[0],
            ["TeamsPresenter:PublicApiOrigin"] = options.PublicApiOrigin,
            ["TeamsPresenter:BotNotificationUrl"] = options.BotNotificationUrl,
            ["TeamsPresenter:BotCallingCallbackUrl"] = options.BotCallingCallbackUrl,
            ["TeamsPresenter:AdminConsentRedirectUrl"] = options.AdminConsentRedirectUrl,
            ["TeamsPresenter:WebOrigin"] = options.WebOrigin,
            ["TeamsPresenter:ConfigurationUrl"] = options.ConfigurationUrl,
            ["TeamsPresenter:SidePanelUrl"] = options.SidePanelUrl,
            ["TeamsPresenter:StageUrl"] = options.StageUrl,
            ["TeamsPresenter:MediaRoute"] = options.MediaRoute,
            ["TeamsPresenter:MediaRouteApproved"] = options.MediaRouteApproved.ToString(),
            ["TeamsPresenter:CertificateReference"] = options.CertificateReference,
            ["TeamsPresenter:PackageVersion"] = options.PackageVersion,
            ["TeamsPresenter:SharedStageEnabled"] = options.SharedStageEnabled.ToString()
        };
    }
}
