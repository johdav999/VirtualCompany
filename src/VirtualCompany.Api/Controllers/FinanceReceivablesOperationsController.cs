using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/receivables/readiness")]
[Authorize(Policy = CompanyPolicies.AccountingView)]
[RequireCompanyContext]
public sealed class FinanceReceivablesOperationsController(INativeReceivablesReadinessService readiness) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NativeReceivablesReadinessDto>> GetAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        Ok(await readiness.GetAsync(new GetNativeReceivablesReadinessQuery(companyId), cancellationToken));
}
