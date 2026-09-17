using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController,Route("api/sales/presentation-legacy-migration"),Authorize(Policy=CompanyPolicies.CompanyMember),RequireCompanyContext]
public sealed class SalesPresentationLegacyMigrationController(ISalesPresentationLegacyMigrationService service,ICompanyContextAccessor context):ControllerBase
{
    [HttpGet]public async Task<ActionResult<SalesPresentationLegacyReconciliationDto>>Get(CancellationToken ct)=>Ok(await service.GetStatusAsync(Company(),Actor(),ct));
    [HttpPost("reconcile")]public async Task<ActionResult<SalesPresentationLegacyReconciliationDto>>Reconcile([FromBody]ReconcileLegacyPresentationsRequest request,CancellationToken ct)=>Ok(await service.ReconcileAsync(Company(),Actor(),request.BatchSize,HttpContext.TraceIdentifier,ct));
    [HttpPost("{recordId:guid}/retry")]public async Task<IActionResult>Retry(Guid recordId,CancellationToken ct)=>await service.RetryAsync(Company(),Actor(),recordId,ct)?Accepted():NotFound();
    private Guid Company()=>context.CompanyId is{}id&&id!=Guid.Empty?id:throw new UnauthorizedAccessException("A resolved company is required.");private Guid Actor()=>context.UserId is{}id&&id!=Guid.Empty?id:throw new UnauthorizedAccessException("A resolved user is required.");
}
public sealed record ReconcileLegacyPresentationsRequest(int BatchSize=50);
