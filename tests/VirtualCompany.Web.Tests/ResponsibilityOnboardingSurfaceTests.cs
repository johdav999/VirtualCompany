namespace VirtualCompany.Web.Tests;

public sealed class ResponsibilityOnboardingSurfaceTests
{
    [Fact]
    public void Onboarding_carries_company_size_through_ui_save_resume_complete_and_safe_routing()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Onboarding.razor");
        var client = Read("src", "VirtualCompany.Web", "Services", "OnboardingApiClient.cs");

        Assert.Contains("data-testid=\"onboarding-company-size\"", page, StringComparison.Ordinal);
        Assert.Contains("CompanySize = model.CompanySize", page, StringComparison.Ordinal);
        Assert.Contains("progress.CompanySize", page, StringComparison.Ordinal);
        Assert.Contains("result.ResponsibilitySetupRequired", page, StringComparison.Ordinal);
        Assert.Contains("ResponsibilitySettingsPath", page, StringComparison.Ordinal);
        Assert.Contains("public string CompanySize { get; set; } = \"micro\"", client, StringComparison.Ordinal);
        Assert.Contains("ResponsibilitySetupRequired", client, StringComparison.Ordinal);
    }

    [Fact]
    public void Narrow_layout_stacks_size_and_responsibility_controls_with_touch_targets()
    {
        var onboardingCss = Read("src", "VirtualCompany.Web", "Pages", "Onboarding.razor.css");
        var settingsCss = Read("src", "VirtualCompany.Web", "Pages", "ResponsibilitySettings.razor.css");

        Assert.Contains(".onboarding-size__options", onboardingCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr", onboardingCss, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:680px)", settingsCss, StringComparison.Ordinal);
        Assert.Contains("min-height:44px", settingsCss, StringComparison.Ordinal);
        Assert.DoesNotContain("overflow-x:auto", settingsCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Settings_and_today_mutations_use_canonical_context_and_cache_invalidation()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "ResponsibilitySettings.razor");
        var service = Read("src", "VirtualCompany.Infrastructure.Operations", "Companies", "CompanyResponsibilityService.cs");

        Assert.Contains("BuildResponsibilitySettingsPath", page, StringComparison.Ordinal);
        Assert.Contains("BuildTodayPath", page, StringComparison.Ordinal);
        Assert.Contains("await InvalidateTodayAsync(companyId", service, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
