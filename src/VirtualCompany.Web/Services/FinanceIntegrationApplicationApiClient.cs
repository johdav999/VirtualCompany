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
