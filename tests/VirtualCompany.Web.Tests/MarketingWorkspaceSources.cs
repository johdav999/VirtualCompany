namespace VirtualCompany.Web.Tests;

internal static class MarketingWorkspaceSources
{
    public static string Read()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "src", "VirtualCompany.Web");
        var files = new[]
        {
            Path.Combine(web, "Pages", "Marketing", "MarketingDashboard.razor"),
            Path.Combine(web, "Pages", "Marketing", "MarketingDashboard.razor.cs")
        }.Concat(Directory.EnumerateFiles(Path.Combine(web, "Components", "Marketing"), "Marketing*Workspace.razor"))
         .Concat(Directory.EnumerateFiles(Path.Combine(web, "Features", "Marketing"), "*.cs"));
        return string.Join(Environment.NewLine, files.Order(StringComparer.Ordinal).Select(File.ReadAllText));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "VirtualCompany.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
