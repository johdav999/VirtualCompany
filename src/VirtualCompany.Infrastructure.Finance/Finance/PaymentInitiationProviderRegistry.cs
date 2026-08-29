using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class PaymentInitiationProviderRegistry(IEnumerable<IPaymentInitiationProvider> providers)
    : IPaymentInitiationProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IPaymentInitiationProvider> _providers = providers
        .GroupBy(x => x.Descriptor.ProviderKey, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PaymentInitiationProviderDescriptor> GetProviders() =>
        _providers.Values.Select(x => x.Descriptor).OrderBy(x => x.DisplayName).ToArray();

    public IPaymentInitiationProvider GetRequired(string providerKey) =>
        _providers.TryGetValue(providerKey?.Trim() ?? string.Empty, out var provider)
            ? provider
            : throw new PaymentExecutionException(PaymentExecutionReasonCodes.ProviderUnsupported,
                "The selected bank connection does not support payment initiation.");
}
