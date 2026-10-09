using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace VirtualCompany.Web.Services;

public sealed record AgentSupervisionQuery(Guid CompanyId, DateOnly? From = null, DateOnly? To = null,
    string? Responsibility = null, string? TaskType = null, Guid? AgentId = null,string View="work",string? Metric=null);
public sealed record SupervisionAgent(Guid Id,string Name,string Responsibility);
public sealed record SupervisionEvidence(Guid Id,string Source,DateTime OccurredUtc,string Description);
public sealed record SupervisionRow(string Key,string Metric,string WorkKind,Guid WorkId,string Title,string Responsibility,
    string TaskType,Guid? AgentId,string AgentName,string State,DateTime RecordedUtc,string EvidenceStage,string WorkRoute,
    Guid? ApprovalId,decimal? SecondsInPeriod,decimal? TotalSeconds,bool Ongoing,IReadOnlyList<SupervisionEvidence> Evidence);
public sealed record SupervisionMeasure(string Code,string View,string Name,int? Count,int Denominator,string DenominatorMeaning,
    string Definition,string Coverage,decimal? SecondsInPeriod=null,decimal? AverageTotalSeconds=null);
public sealed record AgentSupervisionReport(AgentSupervisionQuery Query,DateTime ObservedUtc,DateTime FromUtc,DateTime UntilUtc,
    string Timezone,bool Partial,IReadOnlyList<string> Coverage,IReadOnlyList<string> Responsibilities,IReadOnlyList<string> TaskTypes,
    IReadOnlyList<SupervisionAgent> Agents,IReadOnlyList<SupervisionMeasure> Measures,IReadOnlyList<SupervisionRow> Rows,string SnapshotHash);
public sealed record SupervisionCsv(string FileName,string Content,AgentSupervisionReport Report);

public sealed class AgentSupervisionApiClient(ICompanyApiTransport transport,bool offline=false)
{
    public async Task<AgentSupervisionReport> Get(AgentSupervisionQuery query,CancellationToken ct=default)=>Validate(await Send<AgentSupervisionReport>(query,false,ct),query);
    public async Task<SupervisionCsv> Export(AgentSupervisionQuery query,CancellationToken ct=default)
    {
        var csv=await Send<SupervisionCsv>(query,true,ct);Validate(csv.Report,query);
        if(string.IsNullOrWhiteSpace(csv.Content)||string.IsNullOrWhiteSpace(csv.FileName)||!csv.FileName.StartsWith("agent-supervision-",StringComparison.Ordinal)||!csv.FileName.EndsWith(".csv",StringComparison.Ordinal)||csv.FileName.IndexOfAny(['/', '\\'])>=0)
            throw new OnboardingApiException("The permitted export is unavailable.");
        return csv;
    }
    public static string Route(AgentSupervisionQuery q,bool export=false)=>DashboardRoutes.WithQuery($"api/companies/{q.CompanyId:D}/agent-supervision{(export?"/export":"")}",
        ("from",q.From?.ToString("yyyy-MM-dd")),("to",q.To?.ToString("yyyy-MM-dd")),("responsibility",q.Responsibility),("taskType",q.TaskType),("agentId",q.AgentId?.ToString("D")),("view",q.View),("metric",q.Metric));
    private static AgentSupervisionReport Validate(AgentSupervisionReport report,AgentSupervisionQuery q)
    {
        if(report is null||report.Query is null||report.Query.CompanyId!=q.CompanyId||report.Query.Responsibility!=q.Responsibility||report.Query.TaskType!=q.TaskType||report.Query.AgentId!=q.AgentId||report.Query.View!=q.View||report.Query.Metric!=q.Metric||
            q.From.HasValue&&report.Query.From!=q.From||q.To.HasValue&&report.Query.To!=q.To||report.Rows is null||report.Measures is null||report.Coverage is null||report.Responsibilities is null||report.TaskTypes is null||report.Agents is null||
            report.Rows.Any(x=>x.Evidence is null||AgentWorkRoutes.Local(x.WorkRoute,q.CompanyId) is null)||string.IsNullOrWhiteSpace(report.SnapshotHash))
            throw new OnboardingApiException("The supervision response does not match this company and its filters. Refresh before continuing.");
        return report;
    }
    private async Task<T> Send<T>(AgentSupervisionQuery q,bool export,CancellationToken ct)
    {
        if(offline)throw new OnboardingApiException("Agent supervision requires a backend connection.");
        try
        {
            using var response=await transport.SendAsync(q.CompanyId,HttpMethod.Get,Route(q,export),null,ct);
            if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)throw new OnboardingApiException("Manager access to this company and responsibility is required.");
            if(response.StatusCode==HttpStatusCode.NotFound)throw new OnboardingApiException("This supervision scope is unavailable.");
            if(!response.IsSuccessStatusCode){var error=await response.Content.ReadFromJsonAsync<Problem>(cancellationToken:ct);throw new OnboardingApiException(error?.Message??error?.Detail??"The supervision report is unavailable. Refresh to retry.");}
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct)??throw new OnboardingApiException("No supervision report was returned.");
        }
        catch(Exception ex)when(ex is HttpRequestException or JsonException || ex is TaskCanceledException&&!ct.IsCancellationRequested)
        {throw new OnboardingApiException("The supervision report could not be reached. Refresh to retry.");}
    }
    private sealed record Problem(string? Message,string? Detail);
}
