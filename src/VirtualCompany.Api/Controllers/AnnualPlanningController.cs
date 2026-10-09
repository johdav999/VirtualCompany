using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;
[ApiController,Route("api/companies/{companyId:guid}/planning/years"),Authorize(Policy=CompanyPolicies.CompanyManager),RequireCompanyContext]
public sealed class AnnualPlanningController(IAnnualPlanningService service):ControllerBase
{
 [HttpGet("options")]public Task<ActionResult<AnnualPlanningOptions>> Options(Guid companyId,int fiscalYear,CancellationToken ct)=>Run(()=>service.OptionsAsync(companyId,fiscalYear,ct));
 [HttpGet("versions")]public Task<ActionResult<IReadOnlyList<AnnualPlanSummary>>> History(Guid companyId,int fiscalYear,CancellationToken ct)=>Run(()=>service.HistoryAsync(companyId,fiscalYear,ct));
 [HttpPost("preview")]public Task<ActionResult<AnnualPlanPreview>> Preview(Guid companyId,AnnualPlanInput input,CancellationToken ct)=>Run(()=>service.PreviewAsync(companyId,input,ct));
 [HttpPost("versions")]public Task<ActionResult<AnnualPlanDocument>> Save(Guid companyId,SaveAnnualPlan cmd,CancellationToken ct)=>Run(()=>service.SaveAsync(companyId,cmd,ct));
 [HttpPost("versions/{id:guid}/open")]public Task<ActionResult<AnnualPlanDocument>> Open(Guid companyId,Guid id,CancellationToken ct)=>Run(()=>service.OpenAsync(companyId,id,ct));
 [HttpPost("versions/{id:guid}/review")]public Task<ActionResult<AnnualPlanDocument>> Review(Guid companyId,Guid id,ReviewAnnualPlan cmd,CancellationToken ct)=>Run(()=>service.ReviewAsync(companyId,id,cmd,ct));
 private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action){Response.Headers.CacheControl="no-store";try{return Ok(await action());}catch(UnauthorizedAccessException){return Forbid();}catch(KeyNotFoundException){return NotFound();}catch(CompanyOperatingConcurrencyException e){return Problem(statusCode:409,detail:e.Message);}catch(Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException){return Problem(statusCode:409,detail:"Annual review changed. Reload.");}catch(ArgumentException e){return Problem(statusCode:400,detail:e.Message);}catch(InvalidDataException e){return Problem(statusCode:422,detail:e.Message);}}
}
