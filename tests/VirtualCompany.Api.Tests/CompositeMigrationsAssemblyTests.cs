using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompositeMigrationsAssemblyTests
{
    [Fact]
    public void Composite_catalog_discovers_every_checked_in_migration_once_and_in_order()
    {
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=CompositeMigrationCatalogTest;Integrated Security=true;TrustServerCertificate=true",
                sql => sql.MigrationsAssembly(typeof(AddAgentEffectiveAuthorityVersion).Assembly.GetName().Name))
            .Options;

        using var context = new VirtualCompanyDbContext(options);
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var discoveredIds = migrationsAssembly.Migrations.Keys.ToArray();
        var checkedInIds = CheckedInMigrationIds();

        Assert.Equal(checkedInIds, discoveredIds);
        Assert.Equal(discoveredIds.Length, discoveredIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [
                "VirtualCompany.Persistence.Migrations",
                "VirtualCompany.Persistence.Migrations.History1",
                "VirtualCompany.Persistence.Migrations.History2",
                "VirtualCompany.Persistence.Migrations.History3"
            ],
            migrationsAssembly.Migrations.Values
                .Select(type => type.Assembly.GetName().Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    private static string[] CheckedInMigrationIds()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VirtualCompany.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var migrationPath = Path.Combine(
            root!.FullName,
            "src",
            "VirtualCompany.Persistence.Migrations",
            "Persistence",
            "Migrations");

        return Directory.GetFiles(migrationPath, "*.cs")
            .Where(path => File.ReadAllText(path).Contains("DbContext(", StringComparison.Ordinal))
            .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    "\\[(?:Microsoft\\.EntityFrameworkCore\\.Migrations\\.)?Migration\\(\\\"([^\\\"]+)\\\"\\)\\]")
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
