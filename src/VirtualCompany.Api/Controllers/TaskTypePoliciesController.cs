using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;

[ApiController,Route("api/companies/{companyId:guid}/task-policies")]
[Authorize(Policy=CompanyPolicies.CompanyMember),RequireCompanyContext]
public sealed class TaskTypePoliciesController(ITaskTypePolicyService policies):ControllerBase
{
    [HttpGet("catalogue")]
    public ActionResult<IReadOnlyList<TaskTypePolicyCatalogueEntry>> Catalogue()=>Ok(TaskTypePolicyCatalogue.All);
    [HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public Task<ActionResult<TaskPolicyView>> Get(Guid companyId,[FromQuery]Guid agentId,[FromQuery]string taskType,CancellationToken ct)=>Run(()=>policies.GetAsync(companyId,agentId,taskType,ct));
    [HttpGet("records"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public Task<ActionResult<TaskPolicyQueueContext>> Records(Guid companyId,[FromQuery]Guid agentId,[FromQuery]string taskType,CancellationToken ct)=>Run(()=>policies.WorkChoicesAsync(companyId,agentId,taskType,ct));
    [HttpPost("preview")]
    public Task<ActionResult<TaskPolicyPreview>> Preview(Guid companyId,TaskPolicyChange change,CancellationToken ct)=>Run(()=>policies.PreviewAsync(companyId,change,ct));
    [HttpPost("apply")]
    public Task<ActionResult<TaskPolicyView>> Apply(Guid companyId,TaskPolicyApply apply,CancellationToken ct)=>Run(()=>policies.ApplyAsync(companyId,apply,ct));
    [HttpPost("queue")]
    public Task<ActionResult<Guid>> Queue(Guid companyId,QueueTaskPolicyWork command,CancellationToken ct)=>Run(()=>policies.QueueAsync(companyId,command,ct));
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try{return Ok(await action());}
        catch(TaskPolicyConflictException ex){return Conflict(new ProblemDetails{Title="Policy changed",Detail=ex.Message});}
        catch(ArgumentException ex){return BadRequest(new ProblemDetails{Title="Invalid task policy",Detail=ex.Message});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
    }
}
