using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;


public sealed record SaveFinanceIntegrationApplicationConfigurationRequest(
    bool Enabled,
    string? ClientId,
    string? ClientSecret,
    string? RedirectUri,
    IReadOnlyCollection<string>? Scopes);
