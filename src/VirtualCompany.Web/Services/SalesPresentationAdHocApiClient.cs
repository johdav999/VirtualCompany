using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesPresentationAdHocApiClient(ICompanyApiTransport transport,bool offline,IApiProblemMessageResolver resolver)
{
    private const string Root="api/sales/presentation-runs/ad-hoc";private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public Task<SalesPresentationAdHocOptionsViewModel>GetOptionsAsync(Guid company,CancellationToken ct=default)=>Required<SalesPresentationAdHocOptionsViewModel>(company,HttpMethod.Get,$"{Root}/context-options",null,ct);
    public Task<SalesPresentationAdHocRunViewModel>CreateAsync(Guid company,CreateAdHocSalesPresentationRequest request,CancellationToken ct=default)=>Required<SalesPresentationAdHocRunViewModel>(company,HttpMethod.Post,Root,JsonContent.Create(request,options:Json),ct);
    public Task<SalesPresentationAdHocRunViewModel?>GetAsync(Guid company,Guid run,CancellationToken ct=default)=>Send<SalesPresentationAdHocRunViewModel>(company,HttpMethod.Get,$"{Root}/{run:D}",null,true,ct);
    private async Task<T>Required<T>(Guid c,HttpMethod m,string u,HttpContent? content,CancellationToken ct)=>await Send<T>(c,m,u,content,false,ct)??throw new SalesPresentationRunApiException("The ad-hoc presentation API returned an empty response.");
    private async Task<T?>Send<T>(Guid c,HttpMethod m,string u,HttpContent? content,bool allowNotFound,CancellationToken ct){if(offline)throw new SalesPresentationRunApiException("Ad-hoc presentation preparation needs the backend API.");try{using var response=await transport.SendAsync(c,m,u,content,ct);if(allowNotFound&&response.StatusCode==HttpStatusCode.NotFound)return default;if(!response.IsSuccessStatusCode){ApiProblemResponse? problem=null;if(response.Content.Headers.ContentType?.MediaType is "application/json" or "application/problem+json")problem=await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json,ct);throw new SalesPresentationRunApiException(resolver.Resolve(problem,problem?.Detail??problem?.Title??"The request failed."),response.StatusCode,problem?.Code);}return await response.Content.ReadFromJsonAsync<T>(Json,ct);}catch(HttpRequestException){throw new SalesPresentationRunApiException("The ad-hoc presentation service could not be reached.");}}
}
public sealed record SalesPresentationContextOptionViewModel(Guid Id,string Label,Guid? CustomerCompanyId=null,Guid? ContactId=null,Guid? LeadId=null);
public sealed record SalesPresentationAdHocOptionsViewModel(IReadOnlyList<SalesPresentationContextOptionViewModel> Accounts,IReadOnlyList<SalesPresentationContextOptionViewModel> Contacts,IReadOnlyList<SalesPresentationContextOptionViewModel> Leads,IReadOnlyList<SalesPresentationContextOptionViewModel> Deals,IReadOnlyList<SalesPresentationContextOptionViewModel> Presenters);
public sealed record CreateAdHocSalesPresentationRequest(Guid ClientRequestId,Guid PresetVersionId,Guid? CustomerCompanyId,Guid? ContactId,Guid? LeadId,Guid? DealId,Guid? PresenterAgentId,string? Goal,string? Audience,int? DurationMinutes,string? DemoScenario,string? Language,string? ControlMode,string RuntimeStrategy);
public sealed record SalesPresentationAdHocRunViewModel(Guid Id,Guid PresetId,string PresetName,Guid PresetVersionId,int PresetVersionNumber,Guid PresenterAgentId,string PresenterName,string Goal,string Audience,int DurationMinutes,string? DemoScenario,string Language,string ControlMode,string RuntimeStrategy,Guid? CustomerCompanyId,Guid? ContactId,Guid? LeadId,Guid? DealId,string PreparationStatus,IReadOnlyList<string> ReadinessBlockers,bool CanOpenPresenter,Guid? MeetingSessionId,long ConcurrencyVersion,IReadOnlyList<SalesPresentationRunArtifactViewModel> Artifacts);
