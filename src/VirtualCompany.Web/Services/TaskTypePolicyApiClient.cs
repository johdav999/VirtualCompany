using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace VirtualCompany.Web.Services;

public sealed class TaskTypePolicyApiClient(ICompanyApiTransport transport,bool offline=false)
{
    public Task<IReadOnlyList<TaskTypePolicyCatalogueEntry>> Catalogue(Guid company,CancellationToken ct=default)=>Send<IReadOnlyList<TaskTypePolicyCatalogueEntry>>(company,"catalogue",HttpMethod.Get,null,ct);
    public async Task<TaskPolicyView> Get(Guid company,Guid agent,string type,CancellationToken ct=default)
    {
        var result=await Send<TaskPolicyView>(company,$"?agentId={agent:D}&taskType={Uri.EscapeDataString(type)}",HttpMethod.Get,null,ct);
        if(result.CompanyId!=company||result.AgentId!=agent||result.TaskType!=type||result.History is null)throw new OnboardingApiException("Policy identity is incomplete or changed. Reload current settings.");
        return result;
    }
    public async Task<TaskPolicyPreview> Preview(Guid company,TaskPolicyChange change,CancellationToken ct=default)
    {
        var result=await Send<TaskPolicyPreview>(company,"preview",HttpMethod.Post,change,ct);
        if(result.Change!=change||result.Before.CompanyId!=company||result.Before.AgentId!=change.AgentId||result.Type.Code!=change.TaskType||string.IsNullOrWhiteSpace(result.PreviewHash)||result.Check is null)throw new OnboardingApiException("Preview identity changed. Reload and preview again.");
        return result;
    }
    public async Task<TaskPolicyView> Apply(Guid company,TaskPolicyApply command,CancellationToken ct=default)
    {
        var result=await Send<TaskPolicyView>(company,"apply",HttpMethod.Post,command,ct);
        if(result.CompanyId!=company||result.AgentId!=command.Change.AgentId||result.TaskType!=command.Change.TaskType||result.History is null)
            throw new OnboardingApiException("Applied policy identity changed. Reload current settings.");
        return result;
    }
    public async Task<Guid> Queue(Guid company,QueueTaskPolicyWork command,CancellationToken ct=default)
    {
        var result=await Send<Guid>(company,"queue",HttpMethod.Post,command,ct);
        if(result==Guid.Empty)throw new OnboardingApiException("No retained work identity was returned. Refresh current work before retrying.");
        return result;
    }
    public Task<TaskPolicyQueueContext> Records(Guid company,Guid agent,string type,CancellationToken ct=default)=>Send<TaskPolicyQueueContext>(company,$"records?agentId={agent:D}&taskType={Uri.EscapeDataString(type)}",HttpMethod.Get,null,ct);
    private async Task<T> Send<T>(Guid company,string route,HttpMethod method,object? body,CancellationToken ct)
    {
        if(company==Guid.Empty)throw new ArgumentException("Choose a company.");
        if(offline)throw new OnboardingApiException("Task policies require a backend connection.");
        try {
            using var response=await transport.SendAsync(company,method,$"api/companies/{company:D}/task-policies/{route}",body is null?null:JsonContent.Create(body),ct);
            if(!response.IsSuccessStatusCode){
                var problem=await response.Content.ReadFromJsonAsync<PolicyProblem>(cancellationToken:ct);
                throw new OnboardingApiException(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized?"You do not have policy-management permission.":problem?.Detail??"Policy settings are unavailable. Refresh and retry.");
            }
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct)??throw new OnboardingApiException("No policy result was returned.");
        }catch(Exception ex)when(ex is HttpRequestException or JsonException || ex is TaskCanceledException&&!ct.IsCancellationRequested){throw new OnboardingApiException("Policy settings could not be reached. Reconnect and retry.");}
    }
    private sealed record PolicyProblem(string? Detail);
}
