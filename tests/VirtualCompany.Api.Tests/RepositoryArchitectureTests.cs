using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class RepositoryArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string, string[]> InwardDependencyRules => new()
    {
        {
            "src/VirtualCompany.Domain/VirtualCompany.Domain.csproj",
            [
                "VirtualCompany.Application",
                "VirtualCompany.Infrastructure",
                "VirtualCompany.Api",
                "VirtualCompany.Web",
                "VirtualCompany.Mobile"
            ]
        },
        {
            "src/VirtualCompany.Application/VirtualCompany.Application.csproj",
            [
                "VirtualCompany.Infrastructure",
                "VirtualCompany.Api",
                "VirtualCompany.Web",
                "VirtualCompany.Mobile"
            ]
        }
    };

    [Theory]
    [MemberData(nameof(InwardDependencyRules))]
    public void Projects_do_not_reference_prohibited_outward_projects(
        string projectPath,
        string[] prohibitedProjects)
    {
        var absolutePath = Path.Combine(RepositoryRoot, projectPath.Replace('/', Path.DirectorySeparatorChar));
        var references = ReadProjectReferences(absolutePath);
        var violations = references
            .Where(reference => prohibitedProjects.Contains(reference, StringComparer.OrdinalIgnoreCase))
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{projectPath} must point inward and cannot reference: {string.Join(", ", violations)}.");
    }

    public static TheoryData<string, string[]> ExtractedProjectDependencyRules => new()
    {
        {
            "src/VirtualCompany.Persistence/VirtualCompany.Persistence.csproj",
            [
                "VirtualCompany.Infrastructure.Platform",
                "VirtualCompany.Infrastructure.Mailbox",
                "VirtualCompany.Infrastructure.Finance",
                "VirtualCompany.Infrastructure.Sales",
                "VirtualCompany.Infrastructure.Support",
                "VirtualCompany.Infrastructure.Operations"
            ]
        },
        {
            "src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj",
            [
                "VirtualCompany.Infrastructure.Platform",
                "VirtualCompany.Infrastructure.Mailbox",
                "VirtualCompany.Infrastructure.Finance",
                "VirtualCompany.Infrastructure.Sales",
                "VirtualCompany.Infrastructure.Support",
                "VirtualCompany.Infrastructure.Operations"
            ]
        },
        {
            "src/VirtualCompany.Infrastructure.Platform/VirtualCompany.Infrastructure.Platform.csproj",
            [
                "VirtualCompany.Infrastructure.Mailbox",
                "VirtualCompany.Infrastructure.Finance",
                "VirtualCompany.Infrastructure.Sales",
                "VirtualCompany.Infrastructure.Support",
                "VirtualCompany.Infrastructure.Operations"
            ]
        },
        {
            "src/VirtualCompany.Infrastructure.Operations/VirtualCompany.Infrastructure.Operations.csproj",
            [
                "VirtualCompany.Infrastructure.Mailbox",
                "VirtualCompany.Infrastructure.Finance",
                "VirtualCompany.Infrastructure.Sales",
                "VirtualCompany.Infrastructure.Support"
            ]
        },
        {
            "src/VirtualCompany.Infrastructure.Finance/VirtualCompany.Infrastructure.Finance.csproj",
            [
                "VirtualCompany.Infrastructure.Mailbox",
                "VirtualCompany.Infrastructure.Sales",
                "VirtualCompany.Infrastructure.Support",
                "VirtualCompany.Infrastructure.Operations"
            ]
        },
        {
            "src/VirtualCompany.Infrastructure.Sales/VirtualCompany.Infrastructure.Sales.csproj",
            [
                "VirtualCompany.Infrastructure.Mailbox",
                "VirtualCompany.Infrastructure.Finance",
                "VirtualCompany.Infrastructure.Support",
                "VirtualCompany.Infrastructure.Operations"
            ]
        },
        {
            "src/VirtualCompany.Infrastructure.Support/VirtualCompany.Infrastructure.Support.csproj",
            [
                "VirtualCompany.Infrastructure.Mailbox",
                "VirtualCompany.Infrastructure.Finance",
                "VirtualCompany.Infrastructure.Sales",
                "VirtualCompany.Infrastructure.Operations"
            ]
        }
    };

    [Theory]
    [MemberData(nameof(ExtractedProjectDependencyRules))]
    public void Extracted_projects_do_not_reference_prohibited_sibling_implementations(
        string projectPath,
        string[] prohibitedProjects)
    {
        Projects_do_not_reference_prohibited_outward_projects(projectPath, prohibitedProjects);
    }

    [Fact]
    public void Sql_server_migrations_remain_discoverable_from_the_extracted_assembly()
    {
        var migrationsAssembly = typeof(VirtualCompanyDbContextFactory).Assembly;
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=virtualcompany-architecture-test;Trusted_Connection=True;TrustServerCertificate=True",
                sqlServer => sqlServer.MigrationsAssembly(migrationsAssembly.GetName().Name))
            .Options;

        using var context = new VirtualCompanyDbContext(options);
        var migrations = context.Database.GetMigrations().ToArray();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;

        Assert.Equal("VirtualCompany.Persistence.Migrations", migrationsAssembly.GetName().Name);
        Assert.Contains("20260330170139_InitialSqlServerBaseline", migrations);
        Assert.Contains("20260720113000_AddWorkflowProgressionPollingIndex", migrations);
        Assert.Contains("20260726170000_CancelInvalidDailyBriefingBacklog", migrations);
        Assert.Contains("20260726173000_PurgeInvalidDailyBriefingBacklog", migrations);
        Assert.NotNull(snapshot);
        Assert.Equal(migrationsAssembly, snapshot!.GetType().Assembly);
    }

    [Fact]
    public void Capability_boundary_rules_have_an_explicit_extensible_matrix()
    {
        var rules = CapabilityDependencyRules.Current;

        Assert.Contains("Finance", rules.Keys);
        Assert.Contains("Sales", rules.Keys);
        Assert.Contains("Support", rules.Keys);
        Assert.Contains("Mailbox", rules.Keys);
        Assert.Contains("Operations", rules.Keys);
        Assert.All(rules, rule => Assert.DoesNotContain(rule.Key, rule.Value));
    }

    [Theory]
    [InlineData("Finance", "VirtualCompany.Infrastructure.Finance")]
    [InlineData("Sales", "VirtualCompany.Infrastructure.Sales")]
    [InlineData("Support", "VirtualCompany.Infrastructure.Support")]
    [InlineData("Mailbox", "VirtualCompany.Infrastructure.Mailbox")]
    [InlineData("Companies", "VirtualCompany.Infrastructure.Operations")]
    public void Capability_implementations_do_not_import_other_capability_implementations(
        string capability,
        string projectDirectory)
    {
        var capabilityDirectory = Path.Combine(
            RepositoryRoot,
            "src",
            projectDirectory);
        var otherCapabilities = new[] { "Finance", "Sales", "Support", "Mailbox", "Companies" }
            .Where(candidate => !candidate.Equals(capability, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var violations = Directory
            .EnumerateFiles(capabilityDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).EndsWith("ModuleRegistration.cs", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = line, Number = index + 1 }))
            .Where(item => otherCapabilities.Any(other =>
                item.Line.Contains($"VirtualCompany.Infrastructure.{other}", StringComparison.Ordinal)))
            .Select(item => $"{Path.GetRelativePath(RepositoryRoot, item.Path)}:{item.Number}")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{capability} implementation code must use Application contracts instead of concrete capability namespaces: {string.Join(", ", violations)}.");
    }

    private static IReadOnlyList<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value!))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VirtualCompany.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Virtual Company repository root.");
    }
}

internal static class CapabilityDependencyRules
{
    // This matrix becomes enforceable namespace/project rules as capability assemblies are extracted.
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Current { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Finance"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ["Sales"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ["Support"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ["Mailbox"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ["Operations"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
}
