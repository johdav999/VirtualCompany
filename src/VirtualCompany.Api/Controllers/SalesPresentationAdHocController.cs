using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController,Route("api/sales/presentation-runs/ad-hoc"),Authorize(Policy=CompanyPolicies.CompanyMember),RequireCompanyContext]
public sealed class SalesPresentationAdHocController(ISalesPresentationAdHocService service,ICompanyContextAccessor context):ControllerBase
{
    [HttpGet("context-options")]public async Task<ActionResult<SalesPresentationAdHocOptionsDto>>Options(CancellationToken ct)=>Ok(await service.GetOptionsAsync(Company(),Actor(),ct));
    [HttpGet("{runId:guid}")]public async Task<ActionResult<SalesPresentationAdHocRunDto>>Get(Guid runId,CancellationToken ct){var result=await service.GetAsync(Company(),Actor(),runId,ct);return result is null?NotFound():Ok(result);}
    [HttpPost]public async Task<ActionResult<SalesPresentationAdHocRunDto>>Create([FromBody]CreateAdHocSalesPresentationRequest request,CancellationToken ct)
    {
        try{var result=await service.CreateAsync(Company(),Actor(),request.ToCommand(),HttpContext.TraceIdentifier,ct);return CreatedAtAction(nameof(Get),new{runId=result.Id},result);}
        catch(SalesPresentationPresetConflictException ex){return Conflict(StableProblemDetails.Create(HttpContext,StatusCodes.Status409Conflict,ex.Code,"Ad-hoc presentation unavailable",ex.Message));}
    }
    private Guid Company()=>context.CompanyId is{}id&&id!=Guid.Empty?id:throw new UnauthorizedAccessException("A resolved company is required.");private Guid Actor()=>context.UserId is{}id&&id!=Guid.Empty?id:throw new UnauthorizedAccessException("A resolved user is required.");
}
public sealed record CreateAdHocSalesPresentationRequest(Guid ClientRequestId,Guid PresetVersionId,Guid? CustomerCompanyId,Guid? ContactId,Guid? LeadId,Guid? DealId,Guid? PresenterAgentId,string? Goal,string? Audience,int? DurationMinutes,string? DemoScenario,string? Language,string? ControlMode,string RuntimeStrategy)
{public CreateAdHocSalesPresentationCommand ToCommand()=>new(ClientRequestId,PresetVersionId,CustomerCompanyId,ContactId,LeadId,DealId,PresenterAgentId,Goal,Audience,DurationMinutes,DemoScenario,Language,ControlMode,RuntimeStrategy);}
