using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingChartCatalogResolver : IAccountingChartCatalogResolver
{
    private readonly IReadOnlyDictionary<(string Key, string Version), IAccountingChartCatalog> _catalogs;

    public AccountingChartCatalogResolver(IEnumerable<IAccountingChartCatalog> catalogs)
    {
        _catalogs = catalogs.ToDictionary(
            catalog => (catalog.CatalogKey.ToLowerInvariant(), catalog.CatalogVersion),
            IdentityComparer.Instance);
        if (_catalogs.Count == 0)
            throw new InvalidOperationException("At least one accounting chart catalogue must be registered.");
    }

    public IAccountingChartCatalog Resolve(string catalogKey, string catalogVersion)
    {
        if (string.IsNullOrWhiteSpace(catalogKey))
            throw new ArgumentException("Chart catalogue key is required.", nameof(catalogKey));
        if (string.IsNullOrWhiteSpace(catalogVersion))
            throw new ArgumentException("Chart catalogue version is required.", nameof(catalogVersion));
        var identity = (catalogKey.Trim().ToLowerInvariant(), catalogVersion.Trim());
        return _catalogs.TryGetValue(identity, out var catalog)
            ? catalog
            : throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.ChartCatalogNotFound,
                $"Accounting chart catalogue '{identity.Item1}' version '{identity.Item2}' is not available.");
    }

    public IReadOnlyList<IAccountingChartCatalog> GetAll() =>
        _catalogs.Values
            .OrderBy(catalog => catalog.CatalogKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(catalog => catalog.CatalogVersion, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed class IdentityComparer : IEqualityComparer<(string Key, string Version)>
    {
        public static IdentityComparer Instance { get; } = new();
        public bool Equals((string Key, string Version) x, (string Key, string Version) y) =>
            string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Version, y.Version, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Key, string Version) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Key),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Version));
    }
}
