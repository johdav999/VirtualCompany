using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace VirtualCompany.Infrastructure.Persistence;

#pragma warning disable EF1001 // MigrationsAssembly is the supported replacement point behind IMigrationsAssembly.
public sealed class CompositeMigrationsAssembly : MigrationsAssembly
{
    private static readonly string[] CatalogAssemblyNames =
    [
        "VirtualCompany.Persistence.Migrations",
        "VirtualCompany.Persistence.Migrations.History1",
        "VirtualCompany.Persistence.Migrations.History2",
        "VirtualCompany.Persistence.Migrations.History3"
    ];

    private readonly Type _contextType;
    private IReadOnlyDictionary<string, TypeInfo>? _migrations;

    public CompositeMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
        : base(currentContext, options, idGenerator, logger)
    {
        _contextType = currentContext.Context.GetType();
    }

    public override IReadOnlyDictionary<string, TypeInfo> Migrations
        => _migrations ??= LoadMigrations();

    public override ModelSnapshot? ModelSnapshot
        => base.ModelSnapshot ?? LoadRootModelSnapshot();

    private IReadOnlyDictionary<string, TypeInfo> LoadMigrations()
    {
        var migrations = new SortedDictionary<string, TypeInfo>(StringComparer.Ordinal);
        foreach (var migration in base.Migrations)
        {
            migrations.Add(migration.Key, migration.Value);
        }

        var baseAssemblyName = base.Assembly.GetName().Name;
        foreach (var assemblyName in CatalogAssemblyNames.Where(name => name != baseAssemblyName))
        {
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            foreach (var type in assembly.DefinedTypes.Where(IsMigrationForCurrentContext))
            {
                var migrationId = type.GetCustomAttribute<MigrationAttribute>()!.Id;
                if (!migrations.TryAdd(migrationId, type))
                {
                    throw new InvalidOperationException($"Duplicate migration id '{migrationId}' was found in the composite migration history.");
                }
            }
        }

        return migrations;
    }

    private ModelSnapshot? LoadRootModelSnapshot()
    {
        var rootAssembly = Assembly.Load(new AssemblyName(CatalogAssemblyNames[0]));
        var snapshotType = rootAssembly.DefinedTypes.SingleOrDefault(type =>
            !type.IsAbstract
            && typeof(ModelSnapshot).IsAssignableFrom(type)
            && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == _contextType);

        return snapshotType is null ? null : (ModelSnapshot)Activator.CreateInstance(snapshotType)!;
    }

    private bool IsMigrationForCurrentContext(TypeInfo type)
        => !type.IsAbstract
           && typeof(Migration).IsAssignableFrom(type)
           && type.GetCustomAttribute<MigrationAttribute>() is not null
           && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == _contextType;
}
#pragma warning restore EF1001
