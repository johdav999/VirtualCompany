using System.Net;
using System.Net.Http.Json;
namespace VirtualCompany.Web.Services;
public sealed record MonthlyReviewMeasureViewModel(string Key,string Label,decimal? Actual,decimal? Prior,string? Unit,
    string Definition,string Kind,string SourceType,string SourceLink,decimal? Target,Guid? TargetId,int? TargetVersion,
    string TargetAvailability,decimal? Change,decimal? TargetVariance,string Explanation);
public sealed record MonthlyReviewViewModel(string CalculationVersion,IReadOnlyList<MonthlyReviewMeasureViewModel> Measures,
    string Readiness,string CloseReadiness,string HistoryLimitation);
public sealed record MonthlyReviewSnapshotSummaryViewModel(Guid Id,Guid CompanyId,Guid SeriesId,int Revision,Guid? PreviousId,
    string Lens,int Year,int Month,DateTime AsOfUtc,DateTime SavedAtUtc,string Coverage,string CalculationVersion);
public sealed record MonthlyReviewSnapshotViewModel(MonthlyReviewSnapshotSummaryViewModel Summary,MonthlyWorkspaceViewModel Workspace,
    string ReviewNotes,IReadOnlyList<string> Changes,string Checksum,string Retention);
public sealed record MonthlyReviewHistoryViewModel(IReadOnlyList<MonthlyReviewSnapshotSummaryViewModel> Items,int Skip,int Take,bool HasMore);
public sealed record MonthlyReviewExportViewModel(string FileName,string Csv);
public interface IMonthlyReviewApiClient
{
    Task<MonthlyReviewHistoryViewModel> ListAsync(Guid company,string lens,int year,int month,int skip,CancellationToken ct);
    Task<MonthlyReviewSnapshotViewModel> SaveAsync(Guid company,string lens,int year,int month,Guid request,string notes,CancellationToken ct);
    Task<MonthlyReviewSnapshotViewModel> OpenAsync(Guid company,Guid id,CancellationToken ct);
    Task<MonthlyReviewSnapshotViewModel> RefreshAsync(Guid company,Guid id,int revision,Guid request,string notes,CancellationToken ct);
    Task<MonthlyReviewExportViewModel> ExportAsync(Guid company,Guid id,CancellationToken ct);
}
public sealed class MonthlyReviewApiClient(ICompanyApiTransport transport,bool offline) : IMonthlyReviewApiClient
{
    public async Task<MonthlyReviewHistoryViewModel> ListAsync(Guid company,string lens,int year,int month,int skip,CancellationToken ct)
    {
        var history=await Send<MonthlyReviewHistoryViewModel>(company,HttpMethod.Get,$"?lens={Uri.EscapeDataString(lens)}&year={year}&month={month}&skip={skip}&take=10",null,ct);
        if(history.Items.Any(x=>x.CompanyId!=company || x.Lens!=lens || x.Year!=year || x.Month!=month)) throw new InvalidDataException("Saved review history has inconsistent company or period context.");
        return history;
    }
    public async Task<MonthlyReviewSnapshotViewModel> SaveAsync(Guid company,string lens,int year,int month,Guid request,string notes,CancellationToken ct)
    {
        var value=Validate(await Send<MonthlyReviewSnapshotViewModel>(company,HttpMethod.Post,"",new {Lens=lens,Year=year,Month=month,RequestId=request,ReviewNotes=notes},ct),company);
        if(value.Summary.Lens!=lens || value.Summary.Year!=year || value.Summary.Month!=month) throw new InvalidDataException("Saved review does not match the requested responsibility and month.");
        return value;
    }
    public async Task<MonthlyReviewSnapshotViewModel> OpenAsync(Guid company,Guid id,CancellationToken ct)
        => Validate(await Send<MonthlyReviewSnapshotViewModel>(company,HttpMethod.Post,$"/{id:D}/open",null,ct),company,id);
    public async Task<MonthlyReviewSnapshotViewModel> RefreshAsync(Guid company,Guid id,int revision,Guid request,string notes,CancellationToken ct)
    {
        var value=Validate(await Send<MonthlyReviewSnapshotViewModel>(company,HttpMethod.Post,$"/{id:D}/refresh",new {ExpectedRevision=revision,RequestId=request,ReviewNotes=notes},ct),company);
        if(value.Summary.PreviousId!=id || value.Summary.Revision!=revision+1) throw new InvalidDataException("Refreshed review does not match its predecessor revision.");
        return value;
    }
    public Task<MonthlyReviewExportViewModel> ExportAsync(Guid company,Guid id,CancellationToken ct)=>Send<MonthlyReviewExportViewModel>(company,HttpMethod.Post,$"/{id:D}/export",null,ct);
    private async Task<T> Send<T>(Guid company,HttpMethod method,string suffix,object? body,CancellationToken ct)
    {
        if(company==Guid.Empty) throw new ArgumentException("A company context is required.");
        if(offline) throw new InvalidOperationException("Saved reviews are unavailable in offline mode.");
        using var response=await transport.SendAsync(company,method,$"api/companies/{company:D}/workspace/monthly/reviews{suffix}",body==null?null:JsonContent.Create(body),ct);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new TodayWorkspaceAccessException(response.StatusCode);
        if(response.StatusCode==HttpStatusCode.Conflict) throw new InvalidOperationException("This review has changed. Open the latest saved revision and retry.");
        if((int)response.StatusCode==422) throw new InvalidOperationException("The saved review could not be reproduced. Open another revision or contact your company administrator.");
        if(response.StatusCode==HttpStatusCode.NotFound) throw new InvalidOperationException("Saved review is missing or is not accessible to you.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct)??throw new InvalidDataException("Saved review response is empty.");
    }
    private static MonthlyReviewSnapshotViewModel Validate(MonthlyReviewSnapshotViewModel result,Guid company,Guid? id=null)
    {
        if(result.Summary.CompanyId!=company || result.Workspace.CompanyId!=company || id.HasValue && result.Summary.Id!=id ||
            result.Summary.Lens!=result.Workspace.ActiveLens || result.Summary.Year!=result.Workspace.Period.Year ||
            result.Summary.Month!=result.Workspace.Period.Month || result.Workspace.Review==null) throw new InvalidDataException("Saved review has inconsistent company or period context.");
        return result;
    }
}
