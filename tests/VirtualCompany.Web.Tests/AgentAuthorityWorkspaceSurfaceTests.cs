namespace VirtualCompany.Web.Tests;

public sealed class AgentAuthorityWorkspaceSurfaceTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void AgentProfile_UsesAuthoritativeCapabilityAndApprovalClients()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "AgentProfile.razor");

        Assert.Contains("AgentApiClient.GetCapabilitiesAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApprovalApiClient.ListAsync", source, StringComparison.Ordinal);
        Assert.Contains("AgentAuthorityTransparencyPresenter.CreateApprovalPreview", source, StringComparison.Ordinal);
        Assert.Contains("currentProfile.Visibility.CanViewPermissions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentProfile_RendersAccessibleResponsiveStatesAndSafeEvidenceLinks()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "AgentProfile.razor");
        var css = Read("src", "VirtualCompany.Web", "wwwroot", "css", "app.css");

        Assert.Contains("aria-labelledby=\"agent-authority-title\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", source, StringComparison.Ordinal);
        Assert.Contains("BuildTargetHref", source, StringComparison.Ordinal);
        Assert.Contains("BuildApprovalHref", source, StringComparison.Ordinal);
        Assert.Contains("BuildAuditHref", source, StringComparison.Ordinal);
        Assert.Contains("CanViewOperatorTools", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ThresholdContext[", source, StringComparison.Ordinal);
        Assert.DoesNotContain("payloadHash", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".agent-authority-preview-links a:focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("content: attr(data-label)", css, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. parts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VirtualCompany.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
