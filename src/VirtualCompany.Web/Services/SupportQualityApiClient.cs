using System.Net;
using System.Net.Http.Json;
namespace VirtualCompany.Web.Services;

public sealed class SupportQualityApiClient(ICompanyApiTransport transport,bool offline=false)
{
    private static readonly System.Text.Json.JsonSerializerOptions Options=new(System.Text.Json.JsonSerializerDefaults.Web){Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    public static string Parameters(SupportQualityQuery q)=>$"year={q.Year}&month={q.Month}&category={Uri.EscapeDataString(q.Category??"")}";
    public static void Validate(SupportQualityReport r,Guid company)
    {
        if(r is null||r.CompanyId!=company||r.Query is null||r.Period is null||r.Calendar is null||r.Metrics is null||r.Previous is null||r.Cases is null||r.Groups is null||r.Backlog is null||r.CalculationVersion!="support-quality.v1"||r.Fingerprint?.Length!=64)throw new InvalidDataException("Support report context cannot be verified.");
    }
    public async Task<SupportQualityReport> Report(Guid company,SupportQualityQuery q,CancellationToken ct)
    {var r=await Send<SupportQualityReport>(company,HttpMethod.Get,"?"+Parameters(q),null,ct);Validate(r,company);if(r.Query!=q)throw new InvalidDataException("Report filters differ from the current selection.");return r;}
    public Task<SupportQualityExport> Export(Guid company,SupportQualityQuery q,CancellationToken ct)=>Send<SupportQualityExport>(company,HttpMethod.Get,"/export?"+Parameters(q),null,ct);
    public async Task<SupportIssueGroupingSummary> Correct(Guid company,CorrectSupportIssueGrouping c,CancellationToken ct)
    {var r=await Send<SupportIssueGroupingSummary>(company,HttpMethod.Post,"/groupings",c,ct);if(r.CaseId!=c.CaseId||r.PreviousId!=c.PreviousId||r.Group!=c.Group.Trim())throw new InvalidDataException("Saved grouping differs from this request.");return r;}
    public Task<IReadOnlyList<SupportIssueGroupingSummary>> Groups(Guid company,Guid caseId,CancellationToken ct)=>Send<IReadOnlyList<SupportIssueGroupingSummary>>(company,HttpMethod.Get,$"/groupings/{caseId:D}",null,ct);
    public async Task<SupportCapacityPreview> Preview(Guid company,PreviewSupportCapacity c,CancellationToken ct)
    {var r=await Send<SupportCapacityPreview>(company,HttpMethod.Post,"/preview",c,ct);Validate(r.Report,company);if(r.Report.Query!=c.Query||r.Assumptions!=c.Assumptions||r.Result is null||r.Fingerprint?.Length!=64)throw new InvalidDataException("Preview differs from the current assumptions.");return r;}
    public async Task<SupportCapacityProposal> Save(Guid company,SaveSupportCapacityProposal c,CancellationToken ct)
    {var r=await Send<SupportCapacityProposal>(company,HttpMethod.Post,"/proposals",c,ct);ValidateProposal(r,company);if(r.Summary.Name!=c.Name.Trim()||r.Summary.PreviousId!=c.PreviousId||r.Preview.Fingerprint!=c.ExpectedFingerprint)throw new InvalidDataException("Saved proposal differs from this request.");return r;}
    public async Task<SupportCapacityProposal> Open(Guid company,Guid id,CancellationToken ct)
    {var r=await Send<SupportCapacityProposal>(company,HttpMethod.Get,$"/proposals/{id:D}",null,ct);ValidateProposal(r,company);if(r.Summary.Id!=id)throw new InvalidDataException("Proposal identity differs from this link.");return r;}
    public Task<IReadOnlyList<SupportCapacityProposalSummary>> History(Guid company,int skip,CancellationToken ct)=>Send<IReadOnlyList<SupportCapacityProposalSummary>>(company,HttpMethod.Get,$"/proposals?skip={skip}",null,ct);
    public Task<SupportQualityExport> ProposalExport(Guid company,Guid id,CancellationToken ct)=>Send<SupportQualityExport>(company,HttpMethod.Get,$"/proposals/{id:D}/export",null,ct);
    private static void ValidateProposal(SupportCapacityProposal r,Guid company)
    {if(r?.Summary is null||r.Preview?.Result is null||r.Preview.Assumptions is null||r.Checksum?.Length!=64)throw new InvalidDataException("Retained Support evidence is missing.");Validate(r.Preview.Report,company);}
    private async Task<T> Send<T>(Guid company,HttpMethod method,string path,object? body,CancellationToken ct)
    {
        if(company==Guid.Empty)throw new ArgumentException("Choose a company.");if(offline)throw new InvalidOperationException("Support quality requires the connected API.");
        using var response=await transport.SendAsync(company,method,"api/support/quality"+path,body is null?null:JsonContent.Create(body,options:Options),ct);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)throw new TodayWorkspaceAccessException(response.StatusCode);
        if(!response.IsSuccessStatusCode){
            string? detail=null;try{using var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));if(json.RootElement.TryGetProperty("detail",out var d))detail=d.GetString();}catch(System.Text.Json.JsonException){}
            throw new InvalidOperationException(detail??response.StatusCode switch{HttpStatusCode.NotFound=>"This case or proposal is unavailable in the current company.",HttpStatusCode.Conflict=>"Support sources changed. Reload or preview again before saving.",HttpStatusCode.UnprocessableEntity=>"Original Support evidence cannot be reproduced.",HttpStatusCode.BadRequest=>"Check the cohort and capacity assumptions.",_=>"Support request failed. Retry when the connection is available."});
        }
        try{return await response.Content.ReadFromJsonAsync<T>(Options,ct)??throw new InvalidDataException("Empty Support response.");}catch(System.Text.Json.JsonException ex){throw new InvalidDataException("Support response cannot be read.",ex);}
    }
}
