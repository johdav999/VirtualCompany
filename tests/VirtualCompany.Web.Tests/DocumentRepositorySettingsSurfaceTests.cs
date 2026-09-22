namespace VirtualCompany.Web.Tests;

public sealed class DocumentRepositorySettingsSurfaceTests
{
    [Fact]
    public void Settings_surface_exposes_real_workflow_failure_states_and_access_boundary()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "DocumentRepositoriesSettings.razor");
        Assert.Contains("Published to Virtual Company", page, StringComparison.Ordinal);
        Assert.Contains("SaveAndValidateAsync", page, StringComparison.Ordinal);
        Assert.Contains("StartImportAsync", page, StringComparison.Ordinal);
        Assert.Contains("StartSynchronizationAsync", page, StringComparison.Ordinal);
        Assert.Contains("missing_resource_grant", page, StringComparison.Ordinal);
        Assert.Contains("Credential repair needed", page, StringComparison.Ordinal);
        Assert.Contains("Administrator access required", page, StringComparison.Ordinal);
        Assert.Contains("No agents have access", page, StringComparison.Ordinal);
        Assert.Contains("Operator recovery", page, StringComparison.Ordinal);
        Assert.Contains("Pause retrieval", page, StringComparison.Ordinal);
        Assert.Contains("Retry failed items", page, StringComparison.Ordinal);
        Assert.Contains("Reconcile uncertain upload", page, StringComparison.Ordinal);
        Assert.Contains("Connect Microsoft 365", page, StringComparison.Ordinal);
        Assert.Contains("Advanced: use your own Entra application", page, StringComparison.Ordinal);
        var wizard = Read("src", "VirtualCompany.Web", "Pages", "DocumentRepositoryMicrosoftWizard.razor");
        Assert.Contains("Step @step of 8", wizard, StringComparison.Ordinal);
        Assert.Contains("No agents receive access yet", wizard, StringComparison.Ordinal);
        Assert.Contains("Retry safely", wizard, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant ID", wizard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mock", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Settings_surface_is_responsive_and_reference_is_persisted()
    {
        var css = Read("src", "VirtualCompany.Web", "Pages", "DocumentRepositoriesSettings.razor.css");
        Assert.Contains("@media(max-width:680px)", css, StringComparison.Ordinal);
        Assert.Contains("min-height:44px", css, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "document-repositories-settings-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "document-repositories-settings-reference-prompt.md")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "document-repository-operator-recovery-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "document-repository-operator-recovery-reference-prompt.md")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "microsoft-365-guided-connection-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "microsoft-365-guided-connection-reference-prompt.md")));
    }

    [Fact]
    public void Settings_and_agent_settings_link_to_repository_access_without_primary_navigation()
    {
        var settings = Read("src", "VirtualCompany.Web", "Pages", "SettingsHub.razor");
        var agents = Read("src", "VirtualCompany.Web", "Pages", "AgentSettingsHub.razor");
        Assert.Contains("/settings/document-repositories", settings, StringComparison.Ordinal);
        Assert.Contains("Document repository access", agents, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
