using System.Xml.Linq;

namespace VirtualCompany.Domain.Tests;

public sealed class TestProjectArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("VirtualCompany.Domain.Tests", "VirtualCompany.Domain")]
    [InlineData("VirtualCompany.Web.Tests", "VirtualCompany.Web", "VirtualCompany.Application")]
    public void Isolated_test_projects_only_reference_their_own_layers(string project, params string[] allowed)
    {
        var references = Document(project).Descendants("ProjectReference")
            .Select(item => Path.GetFileNameWithoutExtension(item.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToArray();
        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.Contains(reference, allowed));
    }

    [Fact]
    public void Test_suites_use_default_source_ownership_without_cross_project_compile_links()
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "tests"), "*.csproj", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path);
            if (document.Descendants("IsTestProject").SingleOrDefault()?.Value != "true") continue;
            Assert.DoesNotContain(document.Descendants("EnableDefaultCompileItems"), item => item.Value == "false");
            Assert.DoesNotContain(document.Descendants("Compile"), item => item.Attribute("Remove") is not null);
            Assert.DoesNotContain(document.Descendants("Compile"), item =>
                item.Attribute("Include")?.Value.Replace('\\', '/').Split('/').Contains("..") == true);
        }
    }

    [Fact]
    public void Backend_test_projects_have_no_transitive_web_or_test_suite_dependencies()
    {
        foreach (var name in new[] { "VirtualCompany.Domain.Tests", "VirtualCompany.Api.Tests", "VirtualCompany.Finance.Tests",
            "VirtualCompany.Infrastructure.Mailbox.Tests", "VirtualCompany.Infrastructure.Platform.Tests",
            "VirtualCompany.SalesSource.Tests", "VirtualCompany.SupportGrounding.Tests" })
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Visit(Path.Combine(RepositoryRoot, "tests", name, name + ".csproj"), seen);
            Assert.DoesNotContain(seen, path => Path.GetFileNameWithoutExtension(path) == "VirtualCompany.Web");
            Assert.DoesNotContain(seen, path => path != Path.Combine(RepositoryRoot, "tests", name, name + ".csproj") &&
                XDocument.Load(path).Descendants("IsTestProject").SingleOrDefault()?.Value == "true");
        }
    }

    [Fact]
    public void Shared_integration_support_and_uat_host_do_not_reference_executable_test_projects()
    {
        foreach (var name in new[] { "VirtualCompany.TestSupport", "VirtualCompany.Workspace.Uat" })
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Visit(Path.Combine(RepositoryRoot, "tests", name, name + ".csproj"), seen);
            Assert.DoesNotContain(seen, path => XDocument.Load(path).Descendants("IsTestProject").SingleOrDefault()?.Value == "true");
        }
    }

    private static void Visit(string path, HashSet<string> seen)
    {
        path = Path.GetFullPath(path);
        if (!seen.Add(path)) return;
        foreach (var reference in XDocument.Load(path).Descendants("ProjectReference"))
            Visit(Path.Combine(Path.GetDirectoryName(path)!, reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)), seen);
    }

    private static XDocument Document(string project) =>
        XDocument.Load(Path.Combine(RepositoryRoot, "tests", project, project + ".csproj"));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "VirtualCompany.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Virtual Company repository root was not found.");
    }
}
