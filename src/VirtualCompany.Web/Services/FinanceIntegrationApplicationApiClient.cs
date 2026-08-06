using System.Net;
using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public sealed class FinanceIntegrationApplicationApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/platform/finance-integration-applications";

    public async Task<FinanceIntegrationApplicationConfigurationListResponse> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BasePath, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<FinanceIntegrationApplicationConfigurationListResponse>(cancellationToken)
            ?? new FinanceIntegrationApplicationConfigurationListResponse([]);
    }

    public async Task<FinanceIntegrationApplicationConfigurationResponse> SaveAsync(
        string providerKey,
        SaveFinanceIntegrationApplicationConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"{BasePath}/{Uri.EscapeDataString(providerKey)}",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<FinanceIntegrationApplicationConfigurationResponse>(cancellationToken)
            ?? throw new FinanceIntegrationApplicationApiException("The provider configuration response was empty.");
    }

    public async Task<FinanceIntegrationApplicationValidationResponse> ValidateAsync(
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            $"{BasePath}/{Uri.EscapeDataString(providerKey)}/validate",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<FinanceIntegrationApplicationValidationResponse>(cancellationToken)
            ?? throw new FinanceIntegrationApplicationApiException("The provider validation response was empty.");
    }

    public async Task<FinanceIntegrationApplicationAuditHistoryResponse> GetAuditHistoryAsync(
        string providerKey,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"{BasePath}/{Uri.EscapeDataString(providerKey)}/audit-history?limit={limit}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<FinanceIntegrationApplicationAuditHistoryResponse>(cancellationToken)
            ?? new FinanceIntegrationApplicationAuditHistoryResponse(providerKey, []);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new FinanceIntegrationApplicationApiException(
                "Platform administrator access is required.",
                response.StatusCode);
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(cancellationToken);
        throw new FinanceIntegrationApplicationApiException(
            problem?.Detail ?? problem?.Title ?? "The finance provider settings could not be loaded.",
            response.StatusCode);
    }

    private sealed record ApiProblemResponse(string? Title, string? Detail);
}

public sealed class FinanceIntegrationApplicationApiException(
    string message,
    HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed record FinanceIntegrationApplicationConfigurationListResponse(
    IReadOnlyList<FinanceIntegrationApplicationConfigurationResponse> Providers);

public sealed record FinanceIntegrationApplicationConfigurationResponse(
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

public sealed record SaveFinanceIntegrationApplicationConfigurationRequest(
    bool Enabled,
    string ClientId,
    string? ClientSecret,
    string RedirectUri,
    IReadOnlyCollection<string> Scopes);

public sealed record FinanceIntegrationApplicationValidationResponse(
    string ProviderKey,
    bool Succeeded,
    string Summary,
    DateTime ValidatedUtc,
    IReadOnlyList<FinanceIntegrationApplicationValidationCheckResponse> Checks);

public sealed record FinanceIntegrationApplicationValidationCheckResponse(
    string Key,
    string Label,
    bool Succeeded,
    string Message);

public sealed record FinanceIntegrationApplicationAuditHistoryResponse(
    string ProviderKey,
    IReadOnlyList<FinanceIntegrationApplicationAuditItemResponse> Items);

public sealed record FinanceIntegrationApplicationAuditItemResponse(
    Guid Id,
    string ProviderKey,
    Guid ActorUserId,
    string Action,
    string Outcome,
    string Summary,
    IReadOnlyCollection<string> ChangedFields,
    DateTime OccurredUtc,
    string? CorrelationId);
