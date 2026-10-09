using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace VirtualCompany.Web.Services;
public sealed class ExecutionControlApiClient(ICompanyApiTransport transport,bool offline=false)
{
    public async Task<ExecutionControlView> Get(Guid company,Guid? agent,CancellationToken ct=default)=>Validate(await Send<ExecutionControlView>(company,agent.HasValue?$"?agentId={agent:D}":"",HttpMethod.Get,null,ct),company,agent);
    public async Task<ExecutionControlPreview> Preview(Guid company,ExecutionControlChange change,CancellationToken ct=default)
    {
        var result=await Send<ExecutionControlPreview>(company,"/preview",HttpMethod.Post,change,ct);
        Validate(result.Before,company,change.AgentId);
        if(result.Change!=change||string.IsNullOrWhiteSpace(result.PreviewHash)||string.IsNullOrWhiteSpace(result.Explanation))throw new OnboardingApiException("The preview is incomplete. Refresh and review again.");
        return result;
    }
    public async Task<ExecutionControlView> Apply(Guid company,ExecutionControlApply apply,CancellationToken ct=default)=>Validate(await Send<ExecutionControlView>(company,"/apply",HttpMethod.Post,apply,ct),company,apply.Change.AgentId);
    private static ExecutionControlView Validate(ExecutionControlView result,Guid company,Guid? agent)
    {
        if(result.CompanyId!=company||result.AgentId!=agent||result.Agents is null||result.Work is null||result.History is null)throw new OnboardingApiException("The execution scope is incomplete or changed. Refresh current work.");
        return result;
    }
    private async Task<T> Send<T>(Guid company,string suffix,HttpMethod method,object? body,CancellationToken ct)
    {
        if(company==Guid.Empty)throw new ArgumentException("Choose a company.");
        if(offline)throw new OnboardingApiException("Execution controls require a backend connection.");
        try {
            using var response=await transport.SendAsync(company,method,$"api/companies/{company:D}/execution-controls{suffix}",body is null?null:JsonContent.Create(body),ct);
            if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)throw new OnboardingApiException("You do not have permission for this execution scope.");
            if(!response.IsSuccessStatusCode) {var problem=await response.Content.ReadFromJsonAsync<Problem>(cancellationToken:ct);throw new OnboardingApiException(problem?.Detail??"Execution controls are unavailable. Refresh before retrying.");}
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct)??throw new OnboardingApiException("No execution state was returned.");
        }catch(Exception ex)when(ex is HttpRequestException or JsonException || ex is TaskCanceledException&&!ct.IsCancellationRequested){throw new OnboardingApiException("Execution controls could not be reached. Refresh to check whether your last command applied.");}
    }
    private sealed record Problem(string? Detail);
}
