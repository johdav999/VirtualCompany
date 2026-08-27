using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [HttpGet("bills/{billId:guid}/accounting/reference-data")]
    public async Task<ActionResult<SupplierBillAccountingReferenceDataDto>> GetSupplierBillAccountingReferenceDataAsync(
        Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _supplierBillAccountingService.GetReferenceDataAsync(new(companyId, billId), cancellationToken));

    [HttpPost("bills/{billId:guid}/accounting/preview")]
    public async Task<ActionResult<SupplierBillAccountingPreviewDto>> PreviewSupplierBillAccountingAsync(
        Guid companyId, Guid billId, [FromBody] SupplierBillAccountingRequest request, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _supplierBillAccountingService.PreviewAsync(
            new(companyId, billId, request.ToInput(), ResolveRequiredAccountingActorId()), cancellationToken));

    [HttpPost("bills/{billId:guid}/accounting/submit")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierBillAccountingSubmissionResult>> SubmitSupplierBillAccountingAsync(
        Guid companyId, Guid billId, [FromBody] SubmitSupplierBillAccountingRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _supplierBillAccountingService.SubmitAsync(
            new(companyId, billId, request.ToInput(), request.ExpectedVersion, request.IdempotencyKey,
                ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpPost("bills/{billId:guid}/accounting/post")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierBillAccountingPostingResult>> PostSupplierBillAccountingAsync(
        Guid companyId, Guid billId, [FromBody] PostSupplierBillAccountingRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _supplierBillAccountingService.PostAsync(
            new(companyId, billId, request.ExpectedVersion, request.IdempotencyKey,
                ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpGet("bills/{billId:guid}/accounting")]
    public async Task<ActionResult<SupplierBillAccountingStateDto>> GetSupplierBillAccountingAsync(
        Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _supplierBillAccountingService.GetAsync(new(companyId, billId), cancellationToken));

    [HttpPost("bills/{billId:guid}/native-credit-notes")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierBillAccountingStateDto>> CreateNativeSupplierCreditNoteAsync(
        Guid companyId, Guid billId, [FromBody] CreateNativeSupplierCreditNoteRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _supplierBillAccountingService.CreateCreditNoteAsync(
            new(companyId, billId, request.CreditNoteNumber, request.BillDate, request.DueDate,
                request.Reason, request.Accounting.ToInput(), ResolveRequiredAccountingActorId(),
                request.IdempotencyKey, ResolveCorrelationId()), cancellationToken));

    [HttpGet("accounting/reconciliation/payables")]
    public async Task<ActionResult<SupplierBillPayablesReconciliationDto>> GetSupplierPayablesReconciliationAsync(
        Guid companyId, [FromQuery] DateOnly? throughDate, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _supplierBillAccountingService.ReconcileAsync(new(companyId, throughDate), cancellationToken));
}

public class SupplierBillAccountingRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = "G";
    public decimal? ExchangeRate { get; set; }
    public List<SupplierBillAccountingLineRequest> Lines { get; set; } = [];
    public SupplierBillAccountingInput ToInput() => new(FiscalPeriodId, VoucherSeriesCode, ExchangeRate,
        Lines.Select(x => new SupplierBillAccountingLineInput(x.Description, x.Amount, x.CostAccountId, x.TaxRuleKey,
            x.LineClassification, x.CounterpartyJurisdiction, x.CounterpartyVatStatus,
            x.TaxEvidence.Select(evidence => new AccountingTaxEvidenceInput(
                evidence.Classification, evidence.SourceReference)).ToArray())).ToArray());
}

public sealed class SupplierBillAccountingLineRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid CostAccountId { get; set; }
    public string TaxRuleKey { get; set; } = string.Empty;
    public string? LineClassification { get; set; }
    public string? CounterpartyJurisdiction { get; set; }
    public string? CounterpartyVatStatus { get; set; }
    public List<AccountingTaxEvidenceRequest> TaxEvidence { get; set; } = [];
}

public sealed class SubmitSupplierBillAccountingRequest : SupplierBillAccountingRequest
{
    public long? ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class PostSupplierBillAccountingRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class CreateNativeSupplierCreditNoteRequest
{
    public string CreditNoteNumber { get; set; } = string.Empty;
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public SupplierBillAccountingRequest Accounting { get; set; } = new();
}
