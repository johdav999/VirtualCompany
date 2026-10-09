using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;

[ApiController, Route("api/companies/{companyId:guid}/execution-controls")]
[Authorize(Policy=CompanyPolicies.CompanyMember),RequireCompanyContext]
public sealed class ExecutionControlsController(IExecutionControlService controls) : ControllerBase
{
    [HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public Task<ActionResult<ExecutionControlView>> Get(Guid companyId,[FromQuery]Guid? agentId,CancellationToken ct)=>Run(()=>controls.GetAsync(companyId,agentId,ct));
    [HttpPost("preview")]
    public Task<ActionResult<ExecutionControlPreview>> Preview(Guid companyId,ExecutionControlChange change,CancellationToken ct)=>Run(()=>controls.PreviewAsync(companyId,change,ct));
    [HttpPost("apply")]
    public Task<ActionResult<ExecutionControlView>> Apply(Guid companyId,ExecutionControlApply apply,CancellationToken ct)=>Run(()=>controls.ApplyAsync(companyId,apply,ct));
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try{return Ok(await action());}
        catch(ExecutionControlConflictException ex){return Conflict(new ProblemDetails{Title="Controls changed",Detail=ex.Message});}
        catch(ArgumentException ex){return BadRequest(new ProblemDetails{Title="Review execution controls",Detail=ex.Message});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
    }
}
