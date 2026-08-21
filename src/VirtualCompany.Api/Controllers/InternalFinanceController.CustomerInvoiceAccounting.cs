using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [HttpGet("invoices/{invoiceId:guid}/accounting/reference-data")]
    public async Task<ActionResult<CustomerInvoiceAccountingReferenceDataDto>> GetCustomerInvoiceAccountingReferenceDataAsync(
        Guid companyId, Guid invoiceId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _customerInvoiceAccountingService.GetReferenceDataAsync(new(companyId, invoiceId), cancellationToken));

    [HttpPost("invoices/{invoiceId:guid}/accounting/preview")]
    public async Task<ActionResult<CustomerInvoiceAccountingPreviewDto>> PreviewCustomerInvoiceAccountingAsync(
        Guid companyId, Guid invoiceId, [FromBody] CustomerInvoiceAccountingRequest request, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _customerInvoiceAccountingService.PreviewAsync(
            new(companyId, invoiceId, request.ToInput(), ResolveRequiredAccountingActorId()), cancellationToken));

    [HttpPost("invoices/{invoiceId:guid}/accounting/submit")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<CustomerInvoiceAccountingSubmissionResult>> SubmitCustomerInvoiceAccountingAsync(
        Guid companyId, Guid invoiceId, [FromBody] SubmitCustomerInvoiceAccountingRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _customerInvoiceAccountingService.SubmitAsync(
            new(companyId, invoiceId, request.ToInput(), request.ExpectedVersion, request.IdempotencyKey,
                ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpPost("invoices/{invoiceId:guid}/accounting/post")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<CustomerInvoiceAccountingPostingResult>> PostCustomerInvoiceAccountingAsync(
        Guid companyId, Guid invoiceId, [FromBody] PostCustomerInvoiceAccountingRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _customerInvoiceAccountingService.PostAsync(
            new(companyId, invoiceId, request.ExpectedVersion, request.IdempotencyKey,
                ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpGet("invoices/{invoiceId:guid}/accounting")]
    public async Task<ActionResult<CustomerInvoiceAccountingStateDto>> GetCustomerInvoiceAccountingAsync(
        Guid companyId, Guid invoiceId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _customerInvoiceAccountingService.GetAsync(
            new(companyId, invoiceId), cancellationToken));

    [HttpPost("invoices/{invoiceId:guid}/credit-notes")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<CustomerInvoiceAccountingStateDto>> CreateCustomerCreditNoteAsync(
        Guid companyId, Guid invoiceId, [FromBody] CreateCustomerCreditNoteRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _customerInvoiceAccountingService.CreateCreditNoteAsync(
            new(companyId, invoiceId, request.CreditNoteNumber, request.IssueDate, request.DueDate, request.Reason,
                request.Accounting.ToInput(), ResolveRequiredAccountingActorId(), request.IdempotencyKey, ResolveCorrelationId()), cancellationToken));

    [HttpGet("accounting/reconciliation/receivables")]
    public async Task<ActionResult<CustomerInvoiceReceivableReconciliationDto>> GetCustomerReceivableReconciliationAsync(
        Guid companyId, [FromQuery] DateOnly? throughDate, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _customerInvoiceAccountingService.ReconcileAsync(
            new(companyId, throughDate), cancellationToken));

    private Guid ResolveRequiredAccountingActorId() => ResolveActorId()
        ?? throw new UnauthorizedAccessException("An authenticated company member is required.");
}

public class CustomerInvoiceAccountingRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = "G";
    public decimal? ExchangeRate { get; set; }
    public List<CustomerInvoiceAccountingLineRequest> Lines { get; set; } = [];
    public CustomerInvoiceAccountingInput ToInput() => new(FiscalPeriodId, VoucherSeriesCode, ExchangeRate,
        Lines.Select(x => new CustomerInvoiceAccountingLineInput(x.Description, x.Amount, x.TaxRuleKey)).ToArray());
}

public sealed class SubmitCustomerInvoiceAccountingRequest : CustomerInvoiceAccountingRequest
{
    public long? ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class CustomerInvoiceAccountingLineRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string TaxRuleKey { get; set; } = string.Empty;
}

public sealed class PostCustomerInvoiceAccountingRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class CreateCustomerCreditNoteRequest
{
    public string CreditNoteNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public CustomerInvoiceAccountingRequest Accounting { get; set; } = new();
}
