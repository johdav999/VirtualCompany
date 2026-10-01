using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/accounting/statement-workspace")]
[Authorize(Policy = CompanyPolicies.AccountingView)]
[RequireCompanyContext]
public sealed class FinancialStatementWorkspaceController(IFinancialStatementWorkspaceService service) : ControllerBase
{
    [HttpGet("{reportKind}")]
    public async Task<ActionResult<StatementWorkspaceReport>> GetAsync(Guid companyId, string reportKind,
        [FromQuery] Guid fiscalPeriodId, [FromQuery] Guid? comparisonFiscalPeriodId,
        [FromQuery] Guid? snapshotId, [FromQuery] Guid? comparisonSnapshotId, CancellationToken cancellationToken)
    {
        try { return Ok(await service.GetAsync(new(companyId, fiscalPeriodId, reportKind, comparisonFiscalPeriodId,
            snapshotId, comparisonSnapshotId), cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException e) { return BadRequest(new ProblemDetails { Title = "Invalid statement scope", Detail = e.Message, Status = 400 }); }
        catch (InvalidOperationException e) { return Conflict(new ProblemDetails { Title = "Statement requires review", Detail = e.Message, Status = 409 }); }
    }
}
