using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Support;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;

[ApiController,Route("api/support/quality"),Authorize(Policy=CompanyPolicies.CompanyMember),RequireCompanyContext]
[TypeFilter(typeof(SupportResponsibilityFilter))]
public sealed class SupportQualityController(ISupportQualityService service,ICompanyContextAccessor context):ControllerBase
{
    private Guid Company=>context.CompanyId??throw new UnauthorizedAccessException();
    [HttpGet]public Task<ActionResult<SupportQualityReport>> Report([FromQuery]SupportQualityQuery q,CancellationToken ct)=>Run(()=>service.ReportAsync(Company,q,ct));
    [HttpGet("export")]public Task<ActionResult<SupportQualityExport>> Export([FromQuery]SupportQualityQuery q,CancellationToken ct)=>Run(()=>service.ExportAsync(Company,q,ct));
    [HttpPost("groupings")]public Task<ActionResult<SupportIssueGroupingSummary>> Correct(CorrectSupportIssueGrouping command,CancellationToken ct)=>Run(()=>service.CorrectAsync(Company,command,ct));
    [HttpGet("groupings/{caseId:guid}")]public Task<ActionResult<IReadOnlyList<SupportIssueGroupingSummary>>> Groups(Guid caseId,CancellationToken ct)=>Run(()=>service.GroupHistoryAsync(Company,caseId,ct));
    [HttpPost("preview")]public Task<ActionResult<SupportCapacityPreview>> Preview(PreviewSupportCapacity input,CancellationToken ct)=>Run(()=>service.PreviewAsync(Company,input,ct));
    [HttpPost("proposals")]public Task<ActionResult<SupportCapacityProposal>> Save(SaveSupportCapacityProposal command,CancellationToken ct)=>Run(()=>service.SaveAsync(Company,command,ct));
    [HttpGet("proposals")]public Task<ActionResult<IReadOnlyList<SupportCapacityProposalSummary>>> History(int skip=0,CancellationToken ct=default)=>Run(()=>service.HistoryAsync(Company,skip,ct));
    [HttpGet("proposals/{id:guid}")]public Task<ActionResult<SupportCapacityProposal>> Open(Guid id,CancellationToken ct)=>Run(()=>service.OpenAsync(Company,id,ct));
    [HttpGet("proposals/{id:guid}/export")]public Task<ActionResult<SupportQualityExport>> ProposalExport(Guid id,CancellationToken ct)=>Run(()=>service.ProposalExportAsync(Company,id,ct));
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl="no-store";
        try{return Ok(await action());}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(SupportValidationException ex){return BadRequest(new{message=ex.Message,errors=ex.Errors});}
        catch(ArgumentException ex){return Problem(statusCode:400,title:"Check the cohort and capacity assumptions.",detail:ex.Message);}
        catch(InvalidDataException){return Problem(statusCode:422,title:"Original Support evidence cannot be reproduced.");}
        catch(InvalidOperationException ex){return Problem(statusCode:409,title:"Reload the latest Support evidence.",detail:ex.Message);}
    }
}
