using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MigrationDiscoveryTests
{
    private static readonly IReadOnlySet<string> LegacyUnregisteredMigrationTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "AddSupplierInvoiceDraftActions",
        "AddSupplierInvoiceCorrectionActions",
        "AddSupplierInvoiceEnrichmentActions",
        "AddSupplierInvoiceCorrectionApprovals",
        "RepairSupplierInvoiceActionTables"
    };

    [Fact]
    public void Every_migration_has_discovery_metadata_for_virtual_company_context()
    {
        var migrationTypes = typeof(VirtualCompany.Persistence.Migrations.Persistence.Migrations.PersistPreferredCompanySelection).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
            .OrderBy(type => type.Name)
            .ToArray();

        Assert.NotEmpty(migrationTypes);

        foreach (var migrationType in migrationTypes)
        {
            var migrationAttribute = migrationType
                .GetCustomAttributes(typeof(MigrationAttribute), inherit: false)
                .SingleOrDefault();
            Assert.True(migrationAttribute is not null, $"Migration {migrationType.FullName} is missing MigrationAttribute metadata.");

            var dbContextAttribute = migrationType
                .GetCustomAttributes(typeof(DbContextAttribute), inherit: false)
                .Cast<DbContextAttribute>()
                .SingleOrDefault();

            if (dbContextAttribute is null)
            {
                Assert.Contains(migrationType.Name, LegacyUnregisteredMigrationTypes);
                continue;
            }

            Assert.Equal(typeof(VirtualCompanyDbContext), dbContextAttribute.ContextType);
        }
    }
}
