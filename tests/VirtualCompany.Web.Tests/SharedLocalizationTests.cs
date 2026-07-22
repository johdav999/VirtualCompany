using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using VirtualCompany.Web.Localization.Shared;
using VirtualCompany.Web.Localization.Agents;
using VirtualCompany.Web.Localization.Finance;
using VirtualCompany.Web.Localization.Sales;
using VirtualCompany.Web.Localization.Support;

namespace VirtualCompany.Web.Tests;

public sealed class SharedLocalizationTests
{
    public static TheoryData<Type> ResourceFamilies => new()
    {
        typeof(CommonResources),
        typeof(NavigationResources),
        typeof(ValidationResources),
        typeof(AgentsResources),
        typeof(FinanceResources),
        typeof(SalesResources),
        typeof(SupportResources)
    };

    [Theory]
    [MemberData(nameof(ResourceFamilies))]
    public void EnglishAndSwedishResources_HaveMatchingKeysAndPlaceholders(Type markerType)
    {
        var manager = new ResourceManager(markerType);
        var english = Read(manager, CultureInfo.InvariantCulture);
        var swedish = Read(manager, CultureInfo.GetCultureInfo("sv-SE"));

        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), swedish.Keys.Order());
        foreach (var key in english.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(english[key]));
            Assert.False(string.IsNullOrWhiteSpace(swedish[key]));
            Assert.Equal(Placeholders(english[key]), Placeholders(swedish[key]));
        }
    }

    [Theory]
    [InlineData("pending_review", "StatusPendingReview")]
    [InlineData("in-progress", "StatusInProgress")]
    [InlineData("APPROVED", "StatusApproved")]
    public void SharedStatusPresenter_MapsStableCodes(string code, string expectedKey)
    {
        var presentation = SharedStatusPresenter.Resolve(code);

        Assert.True(presentation.IsKnown);
        Assert.Equal(expectedKey, presentation.ResourceKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("future_internal_state")]
    public void SharedStatusPresenter_UsesSafeFallbackForUnknownCodes(string? code)
    {
        var presentation = SharedStatusPresenter.Resolve(code);

        Assert.False(presentation.IsKnown);
        Assert.Equal("StatusUnknown", presentation.ResourceKey);
    }

    [Theory]
    [InlineData("critical", "StatusCritical", false)]
    [InlineData("manual-review", "StatusManualReview", false)]
    [InlineData("approved", "StatusApproved", true)]
    public void FinanceStatusPresenter_MapsStableCodes(string code, string expectedKey, bool isShared)
    {
        var presentation = FinanceStatusPresenter.Resolve(code);

        Assert.True(presentation.IsKnown);
        Assert.Equal(expectedKey, presentation.ResourceKey);
        Assert.Equal(isShared, presentation.IsShared);
    }

    [Fact]
    public void FinanceStatusPresenter_UsesSafeFallbackForUnknownCode()
    {
        var presentation = FinanceStatusPresenter.Resolve("future_finance_state");

        Assert.False(presentation.IsKnown);
        Assert.Equal("StatusUnknown", presentation.ResourceKey);
    }

    [Fact]
    public void SwedishNavigationResource_ProvidesExpandedLocalizedLabel()
    {
        var manager = new ResourceManager(typeof(NavigationResources));

        Assert.Equal("Användarinställningar", manager.GetString("UserPreferences", CultureInfo.GetCultureInfo("sv-SE")));
        Assert.Equal("Meddelandegranskning", manager.GetString("MessageReview", CultureInfo.GetCultureInfo("sv-SE")));
    }

    [Fact]
    public void RuntimeLocalizer_ResolvesMarkerNamespaceResources()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        using var provider = services.BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<AgentsResources>>();
        var previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            var value = localizer["AgentCapabilities"];

            Assert.False(value.ResourceNotFound);
            Assert.Equal("Agent capabilities", value.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(typeof(FinanceResources), "OverviewCashPosition", "Likviditet")]
    [InlineData(typeof(FinanceResources), "OverviewRoleFinanceManager", "Ekonomiansvarig")]
    [InlineData(typeof(CommonResources), "OnboardingPageTitle", "Skapa företagsyta")]
    [InlineData(typeof(CommonResources), "DashboardLatestBriefing", "Senaste briefing")]
    public void SwedishOverviewAndOnboardingResources_AreLocalized(Type markerType, string key, string expected)
    {
        var manager = new ResourceManager(markerType);

        Assert.Equal(expected, manager.GetString(key, CultureInfo.GetCultureInfo("sv-SE")));
    }

    private static Dictionary<string, string> Read(ResourceManager manager, CultureInfo culture)
    {
        var set = manager.GetResourceSet(culture, true, true) ?? throw new InvalidOperationException($"No resources for {manager.BaseName} and {culture.Name}.");
        return set.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, "\\{[0-9]+(?:[^}]*)?\\}")
            .Select(match => match.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
