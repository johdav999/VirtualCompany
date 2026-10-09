using System.Net;
using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public interface IWeeklyWorkspaceApiClient
{
    Task<WeeklyWorkspaceViewModel?> GetAsync(Guid companyId, string? lens = null, DateOnly? week = null, CancellationToken token = default);
}
public sealed class WeeklyWorkspaceApiClient(ICompanyApiTransport transport, bool offline) : IWeeklyWorkspaceApiClient
{
    public async Task<WeeklyWorkspaceViewModel?> GetAsync(Guid companyId, string? lens = null, DateOnly? week = null, CancellationToken token = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company context is required.", nameof(companyId));
        var normalized = TodayWorkspaceLensValues.Normalize(lens);
        if (normalized.Length > 0 && !TodayWorkspaceLensValues.All.Contains(normalized)) throw new ArgumentException("Choose an available responsibility.", nameof(lens));
        if (week?.Year is < 2000 or > 2100) throw new ArgumentOutOfRangeException(nameof(week));
        if (offline) return null;
        var parameters = new List<string>();
        if (normalized.Length > 0) parameters.Add($"lens={Uri.EscapeDataString(normalized)}");
        if (week.HasValue) parameters.Add($"week={week.Value:yyyy-MM-dd}");
        using var response = await transport.SendAsync(companyId, HttpMethod.Get,
            $"api/companies/{companyId:D}/workspace/weekly{(parameters.Count > 0 ? "?" + string.Join("&", parameters) : "")}", null, token);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new TodayWorkspaceAccessException(response.StatusCode);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WeeklyWorkspaceViewModel>(cancellationToken: token);
        if (result is not null && (result.CompanyId != companyId || !TodayWorkspaceLensValues.All.Contains(result.ActiveLens) ||
                !result.AvailableLenses.Any(x => x.Value == result.ActiveLens) ||
                result.Contributions.Any(x => !result.AvailableLenses.Any(l => l.Value == x.Lens) || result.ActiveLens != "company" && x.Lens != result.ActiveLens) ||
                week.HasValue && (week.Value < result.Period.WeekStart || week.Value > result.Period.WeekEnd)))
            throw new HttpRequestException("Weekly response has an incompatible company, responsibility or period.");
        return result;
    }
}
