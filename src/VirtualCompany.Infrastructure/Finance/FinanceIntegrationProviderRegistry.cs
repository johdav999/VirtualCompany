using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class FinanceIntegrationProviderRegistry(IEnumerable<IFinanceIntegrationProvider> providers)
    : IFinanceIntegrationProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IFinanceIntegrationProvider> _providers = BuildProviderMap(providers);

    public IFinanceIntegrationProvider Resolve(string providerKey) => GetRequired(providerKey);

    public IFinanceIntegrationProvider GetRequired(string providerKey)
    {
        var normalized = NormalizeProviderKey(providerKey);

        return _providers.TryGetValue(normalized, out var provider)
            ? provider
            : throw new FinanceIntegrationProviderNotFoundException(providerKey);
    }

    private static IReadOnlyDictionary<string, IFinanceIntegrationProvider> BuildProviderMap(IEnumerable<IFinanceIntegrationProvider> providers)
    {
        var map = new Dictionary<string, IFinanceIntegrationProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            var providerKey = NormalizeProviderKey(provider.ProviderKey);

            if (map.ContainsKey(providerKey))
            {
                throw new InvalidOperationException(
                    $"Multiple finance integration providers are registered for provider key '{providerKey}'.");
            }

            map.Add(providerKey, provider);
        }

        return map;
    }

    private static string NormalizeProviderKey(string providerKey) =>
        string.IsNullOrWhiteSpace(providerKey)
            ? string.Empty
            : providerKey.Trim();
}

public sealed class FortnoxFinanceIntegrationProvider : IFinanceIntegrationProvider
{
    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
}
