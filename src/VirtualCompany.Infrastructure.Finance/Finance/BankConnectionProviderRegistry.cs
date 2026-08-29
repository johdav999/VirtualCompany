using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BankConnectionProviderRegistry : IBankConnectionProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IBankConnectionProvider> _providers;
    public BankConnectionProviderRegistry(IEnumerable<IBankConnectionProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.Descriptor.ProviderKey.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
    }
    public IReadOnlyList<BankProviderDescriptor> GetProviders() => _providers.Values.Select(x => x.Descriptor).OrderBy(x => x.DisplayName).ToArray();
    public IBankConnectionProvider GetRequired(string providerKey)
    {
        if (!string.IsNullOrWhiteSpace(providerKey) && _providers.TryGetValue(providerKey.Trim(), out var provider) && provider.Descriptor.IsConfigured)
            return provider;
        throw new BankConnectionOperationException(BankConnectionReasonCodes.ProviderNotConfigured,
            "This bank provider is not configured. Ask a Virtual Company administrator to configure a supported provider.");
    }
}

public sealed class BankFeedProviderRegistry : IBankFeedProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IBankFeedProvider> _providers;
    public BankFeedProviderRegistry(IEnumerable<IBankFeedProvider> providers) =>
        _providers = providers.ToDictionary(x => x.ProviderKey.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
    public IBankFeedProvider GetRequired(string providerKey) => _providers.TryGetValue(providerKey?.Trim() ?? string.Empty, out var provider)
        ? provider
        : throw new BankConnectionOperationException(BankConnectionReasonCodes.ProviderNotConfigured,
            "The selected bank provider does not support continuous feeds in this environment.");
}
