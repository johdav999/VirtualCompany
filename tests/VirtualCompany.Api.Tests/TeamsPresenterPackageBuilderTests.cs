using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsPresenterPackageBuilderTests
{
    [Fact]
    public async Task Valid_configuration_builds_reproducible_schema_versioned_package()
    {
        var builder = CreateBuilder(ValidOptions());

        Assert.Empty(builder.Validate());

        await using var first = new MemoryStream();
        await using var second = new MemoryStream();
        var firstResult = await builder.BuildAsync(first, CancellationToken.None);
        var secondResult = await builder.BuildAsync(second, CancellationToken.None);

        Assert.Equal(firstResult.Sha256, secondResult.Sha256);
        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal("1.29", firstResult.ManifestVersion);
        Assert.Equal(3, firstResult.EntryCount);

        first.Position = 0;
        using var archive = new ZipArchive(first, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal(["color.png", "manifest.json", "outline.png"],
            archive.Entries.Select(entry => entry.FullName).OrderBy(value => value).ToArray());
        Assert.Equal(192, ReadPngWidth(ReadEntry(archive, "color.png")));
        Assert.Equal(32, ReadPngWidth(ReadEntry(archive, "outline.png")));

        using var manifest = JsonDocument.Parse(ReadEntry(archive, "manifest.json"));
        var root = manifest.RootElement;
        Assert.Equal("1.29", root.GetProperty("manifestVersion").GetString());
        Assert.Equal("10000000-0000-0000-0000-000000000001", root.GetProperty("id").GetString());
        Assert.Equal("https://app.virtual.test/teams/meetings/configure",
            root.GetProperty("configurableTabs")[0].GetProperty("configurationUrl").GetString());
        Assert.False(root.GetProperty("bots")[0].GetProperty("supportsCalling").GetBoolean());
        Assert.False(root.GetProperty("bots")[0].GetProperty("supportsVideo").GetBoolean());
        Assert.Equal(["meetingChatTab", "meetingDetailsTab", "meetingSidePanel", "meetingStage"],
            root.GetProperty("configurableTabs")[0].GetProperty("context").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(["api.virtual.test", "app.virtual.test"],
            root.GetProperty("validDomains").EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    [Fact]
    public void Invalid_and_mismatched_values_fail_with_safe_actionable_codes()
    {
        var options = ValidOptions();
        options.TeamsAppId = "<teams-app-id>";
        options.BotNotificationUrl = "https://other.virtual.test/api/teams/notifications";
        options.VisualMediaEnabled = true;
        options.CertificateReference = "certificate-secret-value";
        options.AdminConsentRedirectUrl = "https://api.virtual.test/unexpected/callback?mode=unsafe";
        options.CallbackOpenIdConfigurationUrl = "https://metadata.untrusted.test/.well-known/openid-configuration";
        options.CallbackIssuer = "https://issuer.untrusted.test";

        var issues = CreateBuilder(options).Validate();

        Assert.Contains(issues, issue => issue.Code == "teams_app_id_invalid");
        Assert.Contains(issues, issue => issue.Code == "bot_notification_origin_mismatch");
        Assert.Contains(issues, issue => issue.Code == "visual_media_requires_audio");
        Assert.Contains(issues, issue => issue.Code == "admin_consent_redirect_path_invalid");
        Assert.Contains(issues, issue => issue.Code == "callback_openid_configuration_untrusted");
        Assert.Contains(issues, issue => issue.Code == "callback_issuer_invalid");
        Assert.DoesNotContain(issues, issue => issue.Message.Contains("certificate-secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Approved_calling_route_sets_calling_but_never_infers_video_from_audio()
    {
        var options = ValidOptions();
        options.MediaRoute = "teams_application_hosted";
        options.MediaRouteApproved = true;
        options.CallControlEnabled = true;
        options.AudioEnabled = true;
        options.VisualMediaEnabled = false;
        var builder = CreateBuilder(options);

        Assert.Empty(builder.Validate());
        await using var package = new MemoryStream();
        await builder.BuildAsync(package, CancellationToken.None);
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        using var manifest = JsonDocument.Parse(ReadEntry(archive, "manifest.json"));

        Assert.True(manifest.RootElement.GetProperty("bots")[0].GetProperty("supportsCalling").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("bots")[0].GetProperty("supportsVideo").GetBoolean());
    }

    internal static TeamsPresenterOptions ValidOptions() => new()
    {
        Enabled = true,
        TeamsAppId = "10000000-0000-0000-0000-000000000001",
        BotApplicationId = "20000000-0000-0000-0000-000000000002",
        WebApplicationId = "30000000-0000-0000-0000-000000000003",
        WebApplicationResource = "api://app.virtual.test/30000000-0000-0000-0000-000000000003",
        TenantMode = "single_tenant",
        AllowedTenantIds = ["40000000-0000-0000-0000-000000000004"],
        PublicApiOrigin = "https://api.virtual.test",
        BotNotificationUrl = "https://api.virtual.test/api/integrations/teams/notifications",
        BotCallingCallbackUrl = "https://api.virtual.test/api/integrations/teams/calls",
        AdminConsentRedirectUrl = "https://api.virtual.test/api/platform/teams-presenter/admin-consent/callback",
        WebOrigin = "https://app.virtual.test",
        ConfigurationUrl = "https://app.virtual.test/teams/meetings/configure",
        SidePanelUrl = "https://app.virtual.test/teams/meetings/side-panel",
        StageUrl = "https://app.virtual.test/teams/meetings/stage",
        MediaRoute = "disabled",
        MediaRouteApproved = false,
        UseManagedIdentity = false,
        CertificateReference = "keyvault://virtual-company/teams-presenter-certificate",
        PackageVersion = "1.2.3",
        SharedStageEnabled = true
    };

    private static TeamsPresenterPackageBuilder CreateBuilder(TeamsPresenterOptions options) =>
        new(Options.Create(options));

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static int ReadPngWidth(byte[] png) =>
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
}
