using System.Net;
using System.Net.Http.Json;
namespace VirtualCompany.Web.Services;
public sealed class SalesManagementApiClient(ICompanyApiTransport transport,bool offline)
{
    public async Task<SalesManagementReport> Report(Guid company,SalesManagementQuery query,CancellationToken ct=default)
    {
        var r=await Send<SalesManagementReport>(company,HttpMethod.Get,$"?year={query.Year}&month={query.Month}&currency={Uri.EscapeDataString(query.Currency??"")}",null,ct);
        Validate(r,company);if(r.Year!=query.Year || r.Month!=query.Month || r.Currency!=Normalize(query.Currency))throw new InvalidDataException("Report period or currency does not match.");return r;
    }
    public async Task<SalesCapacityProposal> Open(Guid company,Guid id,CancellationToken ct=default)
    {var r=await Send<SalesCapacityProposal>(company,HttpMethod.Get,$"/proposals/{id:D}",null,ct);Validate(r.Report,company);if(r.Summary is null || r.Summary.Id!=id)throw new InvalidDataException("Proposal identity does not match.");return r;}
    public async Task<SalesCapacityProposal> Save(Guid company,SaveSalesCapacityProposal command,CancellationToken ct=default)
    {var r=await Send<SalesCapacityProposal>(company,HttpMethod.Post,"/proposals",JsonContent.Create(command),ct);Validate(r.Report,company);
        if(r.Summary is null || r.Summary.PreviousId!=command.PreviousId || r.Report.Year!=command.Query.Year || r.Report.Month!=command.Query.Month || r.Report.Currency!=Normalize(command.Query.Currency))throw new InvalidDataException("Saved proposal context does not match.");return r;}
    public Task<IReadOnlyList<SalesCapacityProposalSummary>> History(Guid company,int skip,CancellationToken ct=default)=>Send<IReadOnlyList<SalesCapacityProposalSummary>>(company,HttpMethod.Get,$"/proposals?skip={skip}",null,ct);
    private static string? Normalize(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim().ToUpperInvariant();
    private static void Validate(SalesManagementReport r,Guid company){if(r is null || r.Selected is null || r.Prior is null || r.Opportunities is null || r.CurrentForecastOpportunities is null || r.CurrentForecastWindows is null || r.CurrentOpenOpportunityIds is null || r.ForecastHistory is null || r.CompanyId!=company || r.CalculationVersion!="sales-management.v1")throw new InvalidDataException("Sales report context cannot be verified.");}
    private async Task<T> Send<T>(Guid company,HttpMethod method,string route,HttpContent? content,CancellationToken ct)
    {
        if(company==Guid.Empty)throw new ArgumentException("Choose a company.");if(offline)throw new InvalidOperationException("Management reports require the connected API.");
        using var response=await transport.SendAsync(company,method,"api/sales/management"+route,content,ct);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)throw new TodayWorkspaceAccessException(response.StatusCode);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException(response.StatusCode switch{
            HttpStatusCode.BadRequest=>"Check the month, currency, hours and allocation percentages (total 100%).",
            HttpStatusCode.NotFound=>"This proposal is unavailable. Open your current history.",
            HttpStatusCode.Conflict=>"This proposal changed. Open latest before creating another revision.",
            HttpStatusCode.UnprocessableEntity=>"Retained results cannot be reproduced; return to the current report.",
            _=>"Sales request failed. Retry when the connection is available."});
        try {return await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct)??throw new InvalidDataException("Empty Sales response.");}catch(System.Text.Json.JsonException){throw new InvalidDataException("Sales response could not be read. Reload or retry.");}
    }
}

