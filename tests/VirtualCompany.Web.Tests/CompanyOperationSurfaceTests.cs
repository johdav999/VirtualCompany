namespace VirtualCompany.Web.Tests;

public sealed class CompanyOperationSurfaceTests
{
    [Fact]
    public void Operation_workspace_exposes_authoritative_controls_and_background_visibility()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "CompanyOperation.razor");

        Assert.Contains("Pause automatic work", source, StringComparison.Ordinal);
        Assert.Contains("Emergency stop", source, StringComparison.Ordinal);
        Assert.Contains("Automatic review activity", source, StringComparison.Ordinal);
        Assert.Contains("Agent work delivery", source, StringComparison.Ordinal);
        Assert.Contains("Company observations", source, StringComparison.Ordinal);
        Assert.Contains("Today's operating usage", source, StringComparison.Ordinal);
        Assert.Contains("Model calls", source, StringComparison.Ordinal);
        Assert.Contains("Scheduled and event-led company reviews continue while you are signed out.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Operation_client_uses_company_scoped_diagnostics_and_control_routes()
    {
        var source = Read("src", "VirtualCompany.Web", "Services", "CompanyOperationApiClient.cs");

        Assert.Contains("operating/dispatches?take=20", source, StringComparison.Ordinal);
        Assert.Contains("operating/events?take=20", source, StringComparison.Ordinal);
        Assert.Contains("operating/cycle-requests?take=20", source, StringComparison.Ordinal);
        Assert.Contains("operating/snapshots?take=10", source, StringComparison.Ordinal);
        Assert.Contains("operating/emergency-stop", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
