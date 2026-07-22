using VirtualCompany.Web.Localization;

namespace VirtualCompany.Web.Tests;

public sealed class LocalizationRequestPolicyTests
{
    [Theory]
    [InlineData("en-GB", "en-GB")]
    [InlineData("SV-se", "sv-SE")]
    public void Registry_NormalizesOnlySupportedCultures(string input, string expected)
    {
        Assert.True(SupportedWebCultures.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("https://example.com", "/")]
    [InlineData("//example.com/path", "/")]
    [InlineData("/\\example.com", "/")]
    [InlineData("/agents?companyId=1", "/agents?companyId=1")]
    public void ReturnUrlPolicy_AllowsOnlyLocalPaths(string input, string expected)
    {
        Assert.Equal(expected, LocalizationRequestPolicy.NormalizeLocalReturnUrl(input));
    }

    [Fact]
    public void CookieValue_UsesAspNetCultureCookieFormat()
    {
        Assert.Equal("c=sv-SE|uic=sv-SE", LocalizationRequestPolicy.CreateCookieValue("sv-SE"));
    }

    [Theory]
    [InlineData(null, "sv-SE", false)]
    [InlineData("sv-SE", "sv-SE", false)]
    [InlineData("en-GB", "sv-SE", true)]
    [InlineData("de-DE", "sv-SE", false)]
    public void Synchronization_RequiresSavedSupportedCultureDifference(
        string? persisted,
        string active,
        bool expected)
    {
        Assert.Equal(expected, LocalizationRequestPolicy.ShouldSynchronize(persisted, active));
    }
}
