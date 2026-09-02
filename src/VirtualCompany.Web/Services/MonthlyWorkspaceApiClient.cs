using System.Net;
using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public interface IMonthlyWorkspaceApiClient
{
    Task<MonthlyWorkspaceViewModel?> GetAsync(Guid companyId, string? lens = null, int? year = null, int? month = null,
        CancellationToken cancellationToken = default);
}

public sealed class MonthlyWorkspaceApiClient(ICompanyApiTransport transport, bool offline) : IMonthlyWorkspaceApiClient
{
    public async Task<MonthlyWorkspaceViewModel?> GetAsync(Guid companyId, string? lens = null, int? year = null, int? month = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company context is required for the Monthly workspace.", nameof(companyId));
        if (offline) return null;
        var normalizedLens = TodayWorkspaceLensValues.Normalize(lens);
        if (normalizedLens.Length > 0 && !TodayWorkspaceLensValues.All.Contains(normalizedLens))
            throw new ArgumentException("The requested Monthly workspace lens is not supported.", nameof(lens));
        if (year.HasValue != month.HasValue) throw new ArgumentException("Year and month must be supplied together.");
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        var parameters = new List<string>();
        if (normalizedLens.Length > 0) parameters.Add($"lens={Uri.EscapeDataString(normalizedLens)}");
        if (year.HasValue) parameters.Add($"year={year.Value}&month={month!.Value}");
        var uri = $"api/companies/{companyId:D}/workspace/monthly{(parameters.Count == 0 ? string.Empty : "?" + string.Join("&", parameters))}";
        using var response = await transport.SendAsync(companyId, HttpMethod.Get, uri, null, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new TodayWorkspaceAccessException(response.StatusCode);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MonthlyWorkspaceViewModel>(cancellationToken: cancellationToken);
    }
}
