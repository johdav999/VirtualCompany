namespace VirtualCompany.Application.Finance;

public static class FinanceIntegrationProviderKeys
{
    public const string Fortnox = "fortnox";
}

public interface IFinanceIntegrationProvider
{
    string ProviderKey { get; }
}

public interface IFinanceIntegrationProviderResolver
{
    IFinanceIntegrationProvider GetRequired(string providerKey);
}

public interface IFinanceIntegrationProviderRegistry : IFinanceIntegrationProviderResolver
{
    IFinanceIntegrationProvider Resolve(string providerKey);
}

public sealed class FinanceIntegrationProviderNotFoundException(string providerKey)
    : InvalidOperationException($"Finance integration provider '{providerKey}' is not registered.")
{
    public string ProviderKey { get; } = providerKey;
}
