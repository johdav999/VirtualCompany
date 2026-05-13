using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class FinanceIntegrationProviderRegistry : IFinanceIntegrationProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IFinanceIntegrationProvider> _providers;
    private readonly IReadOnlyCollection<IFinanceIntegrationProvider> _providerList;

    public FinanceIntegrationProviderRegistry(IEnumerable<IFinanceIntegrationProvider> providers)
    {
        _providers = BuildProviderMap(providers);
        _providerList = _providers.Values.ToArray();
    }

    public IReadOnlyCollection<IFinanceIntegrationProvider> Providers => _providerList;

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
    public FortnoxFinanceIntegrationProvider(
        FortnoxFinanceIntegrationOAuthService oauth,
        FortnoxFinanceIntegrationSyncService sync,
        FinanceIntegrationWriteApprovalService writeCommands,
        FortnoxFinanceIntegrationMapper mapper)
    {
        if (!IsFortnoxService(oauth.ProviderKey) ||
            !IsFortnoxService(sync.ProviderKey) ||
            !IsFortnoxService(writeCommands.ProviderKey) ||
            !IsFortnoxService(mapper.ProviderKey))
        {
            throw new InvalidOperationException("Fortnox provider dependencies must be registered with the Fortnox provider key.");
        }

        OAuth = oauth;
        Sync = sync;
        WriteCommands = writeCommands;
        Mapper = mapper;
    }

    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
    public string DisplayName => "Fortnox";
    public IReadOnlyCollection<string> Capabilities { get; } = ["accounts", "customers", "suppliers", "invoices", "bills", "payments", "vouchers", "write"];
    public IFinanceIntegrationOAuthService OAuth { get; }
    public IFinanceIntegrationSyncService Sync { get; }
    public IFinanceIntegrationWriteCommandService WriteCommands { get; }
    public IFinanceIntegrationMapper Mapper { get; }

    private static bool IsFortnoxService(string providerKey) =>
        string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase);
}
