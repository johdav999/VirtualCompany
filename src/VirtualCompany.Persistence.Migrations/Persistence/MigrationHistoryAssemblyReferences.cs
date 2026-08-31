using History1Marker = VirtualCompany.Persistence.Migrations.History1.MigrationHistoryAssemblyMarker;
using History2Marker = VirtualCompany.Persistence.Migrations.History2.MigrationHistoryAssemblyMarker;
using History3Marker = VirtualCompany.Persistence.Migrations.History3.MigrationHistoryAssemblyMarker;

namespace VirtualCompany.Persistence.Migrations.Persistence;

public static class MigrationAssemblyMarker;

internal static class MigrationHistoryAssemblyReferences
{
    internal static readonly Type[] Markers =
    [
        typeof(History1Marker),
        typeof(History2Marker),
        typeof(History3Marker)
    ];
}
