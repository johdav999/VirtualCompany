namespace VirtualCompany.Web.Tests;

public sealed class MailboxProviderSetupSurfaceTests
{
    [Theory]
    [InlineData("TeamMailboxConnection.razor")]
    [InlineData("Agents.razor")]
    public void Agent_workflows_do_not_collect_shared_oauth_application_secrets(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "VirtualCompany.Web",
            "Pages",
            fileName));

        Assert.DoesNotContain("OAuthClientSecret", source, StringComparison.Ordinal);
        Assert.DoesNotContain("client-secret", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UpdateEmailSettingsAsync", source, StringComparison.Ordinal);
        Assert.Contains("FinanceRoutes.EmailProviderSettings", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Central_email_settings_remain_the_only_web_surface_for_oauth_application_secrets()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "VirtualCompany.Web",
            "Pages",
            "Finance",
            "SettingsPage.razor"));

        Assert.Contains("gmail-client-secret", source, StringComparison.Ordinal);
        Assert.Contains("microsoft-client-secret", source, StringComparison.Ordinal);
        Assert.Contains(
            "@page \"/system/admin/integrations/email-providers\"",
            source,
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
