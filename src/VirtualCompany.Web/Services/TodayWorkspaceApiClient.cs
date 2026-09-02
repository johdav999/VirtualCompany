using System.Net;
using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public interface ITodayWorkspaceApiClient
{
    Task<TodayWorkspaceViewModel?> GetAsync(
        Guid companyId,
        string? lens = null,
        CancellationToken cancellationToken = default);
    Task<TodayWorkspaceManualReviewViewModel> RequestReviewAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);
}

public sealed class TodayWorkspaceAccessException(HttpStatusCode statusCode)
    : Exception("The Today workspace is not available for the current company membership.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class TodayWorkspaceApiClient(
    ICompanyApiTransport transport,
    bool offline) : ITodayWorkspaceApiClient
{
    public async Task<TodayWorkspaceManualReviewViewModel> RequestReviewAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company context is required for review.", nameof(companyId));
        if (offline) throw new InvalidOperationException("Company review is unavailable in offline mode.");
        using var response = await transport.SendAsync(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/operating/reviews/request", null, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new TodayWorkspaceAccessException(response.StatusCode);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TodayWorkspaceManualReviewViewModel>(cancellationToken: cancellationToken)
            ?? throw new HttpRequestException("The company review response was empty.");
    }

    public async Task<TodayWorkspaceViewModel?> GetAsync(
        Guid companyId,
        string? lens = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company context is required for the Today workspace.", nameof(companyId));
        }

        if (offline) return null;
        var normalizedLens = TodayWorkspaceLensValues.Normalize(lens);
        if (normalizedLens.Length > 0 && !TodayWorkspaceLensValues.All.Contains(normalizedLens))
        {
            throw new ArgumentException("The requested Today workspace lens is not supported.", nameof(lens));
        }

        var uri = $"api/companies/{companyId:D}/workspace/today";
        if (normalizedLens.Length > 0)
        {
            uri += $"?lens={Uri.EscapeDataString(normalizedLens)}";
        }

        using var response = await transport.SendAsync(
            companyId,
            HttpMethod.Get,
            uri,
            null,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new TodayWorkspaceAccessException(response.StatusCode);
        }
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TodayWorkspaceViewModel>(cancellationToken: cancellationToken);
    }
}
