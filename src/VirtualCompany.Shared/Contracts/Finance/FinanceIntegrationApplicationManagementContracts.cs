

namespace VirtualCompany.Application.Finance;


public sealed record FinanceIntegrationApplicationAuditHistory(
    string ProviderKey,
    IReadOnlyList<FinanceIntegrationApplicationAuditItem> Items);


public sealed record FinanceIntegrationApplicationValidationCheck(
    string Key,
    string Label,
    bool Succeeded,
    string Message);


public sealed record FinanceIntegrationApplicationConfigurationDto(
    string ProviderKey,
    string DisplayName,
    bool Enabled,
    string Status,
    string StatusMessage,
    string RedirectUri,
    IReadOnlyCollection<string> SelectedScopes,
    IReadOnlyCollection<string> SupportedScopes,
    bool ClientIdConfigured,
    string? ClientIdHint,
    bool ClientSecretConfigured,
    string SecretBackend,
    bool SecretBackendSupportsWrites,
    string CallbackPath,
    DateTime? LastValidatedUtc,
    string ValidationStatus,
    string? ValidationSummary,
    DateTime? UpdatedUtc);


public sealed record FinanceIntegrationApplicationAuditItem(
    Guid Id,
    string ProviderKey,
    Guid ActorUserId,
    string Action,
    string Outcome,
    string Summary,
    IReadOnlyCollection<string> ChangedFields,
    DateTime OccurredUtc,
    string? CorrelationId);


public sealed record FinanceIntegrationApplicationConfigurationList(
    IReadOnlyList<FinanceIntegrationApplicationConfigurationDto> Providers);


public sealed record FinanceIntegrationApplicationValidationResult(
    string ProviderKey,
    bool Succeeded,
    string Summary,
    DateTime ValidatedUtc,
    IReadOnlyList<FinanceIntegrationApplicationValidationCheck> Checks);
