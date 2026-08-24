using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/reports/general-ledger")]
    public async Task<ActionResult<GeneralLedgerReportDto>> GetAccountingGeneralLedgerAsync(
        Guid companyId, [FromQuery] Guid fiscalPeriodId, [FromQuery] Guid? accountId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 200, CancellationToken cancellationToken = default) =>
        await ExecuteReadAsync(() => _accountingReportingService.GetGeneralLedgerAsync(
            new GetGeneralLedgerQuery(companyId, fiscalPeriodId, accountId, page, pageSize), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/reports/trial-balance")]
    public async Task<ActionResult<TrialBalanceReportDto>> GetAccountingTrialBalanceAsync(
        Guid companyId, [FromQuery] Guid fiscalPeriodId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingReportingService.GetTrialBalanceAsync(
            new GetTrialBalanceQuery(companyId, fiscalPeriodId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/reports/tax-summary")]
    public async Task<ActionResult<AccountingTaxSummaryDto>> GetAccountingTaxSummaryAsync(
        Guid companyId, [FromQuery] Guid fiscalPeriodId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingReportingService.GetTaxSummaryAsync(
            new GetAccountingTaxSummaryQuery(companyId, fiscalPeriodId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/reports/tax-summary/review")]
    public async Task<ActionResult<AccountingTaxSummaryDto>> ReviewAccountingTaxSummaryAsync(
        Guid companyId, [FromBody] AccountingPeriodRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingReportingService.ReviewTaxSummaryAsync(
            new ReviewAccountingTaxSummaryCommand(companyId, request.FiscalPeriodId,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required.")), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/reports/control-reconciliation")]
    public async Task<ActionResult<ControlAccountReconciliationDto>> GetAccountingControlReconciliationAsync(
        Guid companyId, [FromQuery] Guid fiscalPeriodId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingReportingService.GetControlAccountReconciliationAsync(
            new GetControlAccountReconciliationQuery(companyId, fiscalPeriodId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/periods/{fiscalPeriodId:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<AccountingPeriodHistoryDto>>> GetAccountingPeriodHistoryAsync(
        Guid companyId, Guid fiscalPeriodId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingReportingService.GetPeriodHistoryAsync(companyId, fiscalPeriodId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/exports")]
    public async Task<ActionResult<AccountingExportJobDto>> RequestAccountingExportAsync(
        Guid companyId, [FromBody] RequestAccountingExportRequest request, CancellationToken cancellationToken)
    {
        var result = await ExecuteWriteAsync(() => _accountingReportingService.RequestExportAsync(
            new RequestAccountingExportCommand(companyId, request.FiscalPeriodId,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                request.IdempotencyKey), cancellationToken));
        return result.Result is null ? Accepted(result.Value) : result;
    }

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/exports")]
    public async Task<ActionResult<IReadOnlyList<AccountingExportJobDto>>> ListAccountingExportsAsync(
        Guid companyId, [FromQuery] Guid? fiscalPeriodId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100, CancellationToken cancellationToken = default) =>
        await ExecuteReadAsync(() => _accountingReportingService.ListExportsAsync(
            new ListAccountingExportsQuery(companyId, fiscalPeriodId, page, pageSize), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/exports/{exportId:guid}/download")]
    public async Task<IActionResult> DownloadAccountingExportAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken)
    {
        try
        {
            var export = await _accountingReportingService.DownloadExportAsync(new GetAccountingExportQuery(companyId, exportId), cancellationToken);
            Response.Headers.ETag = $"\"{export.Checksum}\"";
            return File(export.Content, export.MediaType, export.FileName);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception) { return Conflict(new ProblemDetails { Title = "Export is not available", Detail = exception.Message, Status = 409 }); }
    }
}

public class AccountingPeriodRequest
{
    public Guid FiscalPeriodId { get; set; }
}

public sealed class RequestAccountingExportRequest : AccountingPeriodRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}
