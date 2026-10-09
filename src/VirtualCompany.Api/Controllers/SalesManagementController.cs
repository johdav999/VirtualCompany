using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;
[ApiController, Route("api/sales/management"), Authorize(Policy=CompanyPolicies.CompanyMember), RequireCompanyContext]
public sealed class SalesManagementController(ISalesManagementService service,ICompanyContextAccessor context) : ControllerBase
{
    private Guid Company=>context.CompanyId??throw new UnauthorizedAccessException();
    [HttpGet] public Task<ActionResult<SalesManagementReport>> Report(int year,int month,string? currency,CancellationToken ct)=>Run(()=>service.ReportAsync(Company,new(year,month,currency),ct));
    [HttpGet("proposals")] public Task<ActionResult<IReadOnlyList<SalesCapacityProposalSummary>>> History(int skip=0,CancellationToken ct=default)=>Run(()=>service.ListAsync(Company,skip,ct));
    [HttpPost("proposals")] public Task<ActionResult<SalesCapacityProposal>> Save(SaveSalesCapacityProposal command,CancellationToken ct)=>Run(()=>service.SaveAsync(Company,command,ct));
    [HttpGet("proposals/{id:guid}")] public Task<ActionResult<SalesCapacityProposal>> Open(Guid id,CancellationToken ct)=>Run(()=>service.OpenAsync(Company,id,ct));
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl="no-store";
        try{return Ok(await action());}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(ArgumentException ex){return Problem(statusCode:400,title:"Check the cohort and planning assumptions.",detail:ex.Message);}
        catch(InvalidDataException){return Problem(statusCode:422,title:"Saved results cannot be reproduced.");}
        catch(InvalidOperationException ex){return Problem(statusCode:409,title:"Reload the latest proposal.",detail:ex.Message);}
    }
}

