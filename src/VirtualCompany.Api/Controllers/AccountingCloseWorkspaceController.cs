using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.AccountingView)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/close-workspace")]
public sealed class AccountingCloseWorkspaceController(IAccountingCloseWorkspaceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AccountingCloseWorkspaceDto>> GetAsync(Guid companyId,
        [FromQuery] Guid? fiscalPeriodId, [FromQuery] Guid? closeInstanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetAsync(new(companyId, fiscalPeriodId, closeInstanceId), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (AccountingCloseException exception) when (exception.ReasonCode == AccountingCloseReasonCodes.NotFound)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Close workspace was not found",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound,
                Instance = Request.Path
            });
        }
    }
}
