using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class FortnoxFinanceIntegrationApplicationDefinition
    : IFinanceIntegrationApplicationDefinition
{
    public FinanceIntegrationApplicationDefinition Definition { get; } = new(
        FinanceIntegrationProviderKeys.Fortnox,
        "Fortnox",
        FortnoxOptions.SectionName,
        "/finance/integrations/fortnox/callback",
        FortnoxScopeDefaults.ImportSync,
        FortnoxScopeDefaults.ImportSync);
}
