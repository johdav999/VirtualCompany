using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-invoices/{invoiceId:guid}/corrections/policy")]
    public Task<ActionResult<CustomerInvoiceCorrectionPolicyDecisionDto>> EvaluateCustomerInvoiceCorrectionAsync(
        Guid companyId, Guid invoiceId, [FromQuery] string correctionType, [FromQuery] decimal amount,
        [FromQuery] string currency, [FromQuery] string? providerKey,
        CancellationToken cancellationToken) => ExecuteReadAsync(() => _customerInvoiceCorrectionService.EvaluateAsync(
            new(companyId, invoiceId, correctionType, amount, currency, providerKey), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoices/{invoiceId:guid}/corrections")]
    public Task<ActionResult<CustomerInvoiceCorrectionDto>> ProposeCustomerInvoiceCorrectionAsync(
        Guid companyId, Guid invoiceId, [FromBody] ProposeCustomerInvoiceCorrectionRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => _customerInvoiceCorrectionService.ProposeAsync(
            new(companyId, invoiceId, request.CorrectionType, request.Amount, request.Currency, request.Reason,
                request.EvidenceReference, request.IdempotencyKey, RequiredActor(), ResolveCorrelationId(),
                request.BeneficiaryReference, request.PaymentEvidenceReference, request.ProviderKey,
                request.CreditDraft is null ? null : MapInvoiceDraft(request.CreditDraft)), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-invoice-corrections")]
    public Task<ActionResult<CustomerInvoiceCorrectionListResult>> ListCustomerInvoiceCorrectionsAsync(
        Guid companyId, [FromQuery] Guid? invoiceId, [FromQuery] string? status,
        [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) => ExecuteReadAsync(() =>
        _customerInvoiceCorrectionService.ListAsync(new(companyId, invoiceId, status, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-invoice-corrections/{correctionId:guid}")]
    public Task<ActionResult<CustomerInvoiceCorrectionDto>> GetCustomerInvoiceCorrectionAsync(
        Guid companyId, Guid correctionId, CancellationToken cancellationToken) => ExecuteReadAsync(() =>
        _customerInvoiceCorrectionService.GetAsync(companyId, correctionId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-corrections/{correctionId:guid}/execute")]
    public Task<ActionResult<CustomerInvoiceCorrectionDto>> ExecuteCustomerInvoiceCorrectionAsync(
        Guid companyId, Guid correctionId, [FromBody] ExecuteCustomerInvoiceCorrectionRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => _customerInvoiceCorrectionService.ExecuteAsync(
            new(companyId, correctionId, request.ExpectedVersion, request.ExpectedSourceHash,
                request.IdempotencyKey, RequiredActor(), request.SeriesId, request.FiscalPeriodId,
                request.AccountingDate, request.VoucherSeriesCode, request.ExpenseAccountId,
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-corrections/{correctionId:guid}/refund-reconciliation")]
    public Task<ActionResult<CustomerInvoiceCorrectionDto>> ReconcileCustomerInvoiceRefundAsync(
        Guid companyId, Guid correctionId, [FromBody] ReconcileCustomerInvoiceRefundRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _customerInvoiceCorrectionService.ReconcileRefundAsync(new(companyId, correctionId,
            request.ExpectedVersion, request.ProviderConfirmedSucceeded, request.ProviderConfirmedAbsent,
            request.EvidenceReference, request.ProviderReference, RequiredActor(), ResolveCorrelationId()), cancellationToken));
}

public sealed class ProposeCustomerInvoiceCorrectionRequest
{
    public string CorrectionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string EvidenceReference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? BeneficiaryReference { get; set; }
    public string? PaymentEvidenceReference { get; set; }
    public string? ProviderKey { get; set; }
    public SaveCustomerInvoiceDraftRequest? CreditDraft { get; set; }
}

public sealed class ExecuteCustomerInvoiceCorrectionRequest
{
    public long ExpectedVersion { get; set; }
    public string ExpectedSourceHash { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? SeriesId { get; set; }
    public Guid? FiscalPeriodId { get; set; }
    public DateOnly? AccountingDate { get; set; }
    public string? VoucherSeriesCode { get; set; }
    public Guid? ExpenseAccountId { get; set; }
}

public sealed class ReconcileCustomerInvoiceRefundRequest
{
    public long ExpectedVersion { get; set; }
    public bool ProviderConfirmedSucceeded { get; set; }
    public bool ProviderConfirmedAbsent { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
}
