using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingCreativeSafetyTests
{
    [Theory]
    [InlineData("passed", true)]
    [InlineData("pending", false)]
    [InlineData("failed", false)]
    [InlineData("error", false)]
    public void Only_authoritative_pass_allows_asset_use(string result, bool expected)
    {
        var scan = new MarketingCreativeAssetScan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "scanner",
            "scan-1", "2026.08", result, "policy", "{}", DateTime.UtcNow);

        Assert.Equal(expected, scan.AllowsUse);
    }

    [Fact]
    public void Request_changes_reopens_only_the_submitted_version()
    {
        var asset = Asset();
        asset.Submit();
        asset.RequestChanges();
        Assert.Equal("changes_requested", asset.Status);
        asset.UpdateMetadata("Revised", "en", "Accessible description");
        asset.Submit();
        asset.Review(true);
        Assert.Throws<InvalidOperationException>(() => asset.RequestChanges());
    }

    private static MarketingCreativeAsset Asset() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        "Creative", "image/png", "1200x630", "en", "Summary", "prompt-v1", "provider-1", "brand-v1",
        "provider safety accepted", "Accessible description", "companies/x/asset.png", "checksum", Guid.NewGuid(),
        "idempotency", provenanceJson: "{\"origin\":\"test\",\"copyrightStatus\":\"reviewed\"}");
}
