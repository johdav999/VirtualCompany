using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/treasury-workspace")]
public sealed class TreasuryWorkspaceController(
    ITreasuryWorkspaceQueryService service,
    IAuthorizationService authorizationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TreasuryWorkspaceDto>> GetAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] int horizonDays = 14,
        [FromQuery] int exceptionLimit = 12,
        [FromQuery] int taskLimit = 8,
        CancellationToken cancellationToken = default)
    {
        var canEdit = (await authorizationService.AuthorizeAsync(
            User,
            companyId,
            CompanyPolicies.FinanceEdit)).Succeeded;
        var canApprove = (await authorizationService.AuthorizeAsync(
            User,
            companyId,
            CompanyPolicies.FinanceApproval)).Succeeded;

        return Ok(await service.GetAsync(
            new GetTreasuryWorkspaceQuery(
                companyId,
                asOfUtc,
                horizonDays,
                exceptionLimit,
                taskLimit,
                canEdit,
                canApprove),
            cancellationToken));
    }
}
