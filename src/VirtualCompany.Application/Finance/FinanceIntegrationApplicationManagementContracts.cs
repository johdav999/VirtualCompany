namespace VirtualCompany.Application.Finance;

public static class FinanceIntegrationApplicationConfigurationStatuses
{
    public const string Ready = "ready";
    public const string Incomplete = "incomplete";
    public const string Disabled = "disabled";
    public const string Invalid = "invalid";
}

public static class FinanceIntegrationApplicationValidationStatuses
{
    public const string NotChecked = "not_checked";
    public const string Valid = "valid";
    public const string Invalid = "invalid";
}

public sealed record FinanceIntegrationApplicationDefinition(
    string ProviderKey,
    string DisplayName,
    string ConfigurationSectionName,
    string CallbackPath,
    IReadOnlyCollection<string> SupportedScopes,
    IReadOnlyCollection<string> DefaultScopes);

public interface IFinanceIntegrationApplicationDefinition
{
    FinanceIntegrationApplicationDefinition Definition { get; }
}

public sealed record SaveFinanceIntegrationApplicationConfigurationCommand(
    string ProviderKey,
    bool Enabled,
    string ClientId,
    string? ClientSecret,
    string RedirectUri,
    IReadOnlyCollection<string> Scopes,
    Guid ActorUserId,
    string? CorrelationId);

public sealed record ValidateFinanceIntegrationApplicationConfigurationCommand(
    string ProviderKey,
    Guid ActorUserId,
    string? CorrelationId);

public interface IFinanceIntegrationApplicationManagementService
{
    Task<FinanceIntegrationApplicationConfigurationList> GetAllAsync(CancellationToken cancellationToken);
    Task<FinanceIntegrationApplicationConfigurationDto> GetAsync(string providerKey, CancellationToken cancellationToken);
    Task<FinanceIntegrationApplicationConfigurationDto> SaveAsync(
        SaveFinanceIntegrationApplicationConfigurationCommand command,
        CancellationToken cancellationToken);
    Task<FinanceIntegrationApplicationValidationResult> ValidateAsync(
        ValidateFinanceIntegrationApplicationConfigurationCommand command,
        CancellationToken cancellationToken);
    Task<FinanceIntegrationApplicationAuditHistory> GetAuditHistoryAsync(
        string providerKey,
        int limit,
        CancellationToken cancellationToken);
}
