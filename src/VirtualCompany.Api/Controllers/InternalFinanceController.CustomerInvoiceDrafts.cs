using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-invoice-drafts")]
    public async Task<ActionResult<CustomerInvoiceDraftListResult>> ListCustomerInvoiceDraftsAsync(Guid companyId,
        [FromQuery] string? status = null, [FromQuery] Guid? customerId = null,
        [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) => await ExecuteReadAsync(() =>
        _customerInvoiceDraftService.ListAsync(new(companyId, status, customerId, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-invoice-drafts/{draftId:guid}")]
    public async Task<ActionResult<CustomerInvoiceDraftDto>> GetCustomerInvoiceDraftAsync(Guid companyId,
        Guid draftId, CancellationToken cancellationToken) => await ExecuteReadAsync(() =>
        _customerInvoiceDraftService.GetAsync(new(companyId, draftId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-drafts")]
    public async Task<ActionResult<CustomerInvoiceDraftDto>> CreateCustomerInvoiceDraftAsync(Guid companyId,
        [FromBody] SaveCustomerInvoiceDraftRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _customerInvoiceDraftService.CreateAsync(new(companyId, MapInvoiceDraft(request),
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/customer-invoice-drafts/{draftId:guid}")]
    public async Task<ActionResult<CustomerInvoiceDraftDto>> UpdateCustomerInvoiceDraftAsync(Guid companyId,
        Guid draftId, [FromBody] SaveCustomerInvoiceDraftRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _customerInvoiceDraftService.UpdateAsync(new(companyId, draftId,
            request.ExpectedVersion, MapInvoiceDraft(request), request.IdempotencyKey, RequiredActor(),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-drafts/{draftId:guid}/copy")]
    public async Task<ActionResult<CustomerInvoiceDraftDto>> CopyCustomerInvoiceDraftAsync(Guid companyId,
        Guid draftId, [FromBody] CopyCustomerInvoiceDraftRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _customerInvoiceDraftService.CopyAsync(new(companyId, draftId,
            request.ExpectedVersion, request.IssueDate, request.IdempotencyKey, RequiredActor(),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-drafts/{draftId:guid}/discard")]
    public async Task<ActionResult<CustomerInvoiceDraftDto>> DiscardCustomerInvoiceDraftAsync(Guid companyId,
        Guid draftId, [FromBody] CustomerInvoiceDraftVersionedActionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _customerInvoiceDraftService.DiscardAsync(new(companyId, draftId, request.ExpectedVersion,
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpPost("accounting/customer-invoice-drafts/{draftId:guid}/preview")]
    public async Task<ActionResult<CustomerInvoiceDraftPreviewDto>> PreviewCustomerInvoiceDraftAsync(Guid companyId,
        Guid draftId, [FromBody] CustomerInvoiceDraftVersionRequest request,
        CancellationToken cancellationToken) => await ExecuteReadAsync(() =>
        _customerInvoiceDraftService.PreviewAsync(new(companyId, draftId, request.ExpectedVersion), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-invoice-drafts/{draftId:guid}/readiness")]
    public async Task<ActionResult<CustomerInvoiceDraftReadinessDto>> GetCustomerInvoiceDraftReadinessAsync(
        Guid companyId, Guid draftId, [FromQuery] long expectedVersion,
        CancellationToken cancellationToken) => await ExecuteReadAsync(() =>
        _customerInvoiceDraftService.GetReadinessAsync(new(companyId, draftId, expectedVersion), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-drafts/{draftId:guid}/submit")]
    public async Task<ActionResult<CustomerInvoiceDraftSubmissionResult>> SubmitCustomerInvoiceDraftAsync(
        Guid companyId, Guid draftId, [FromBody] CustomerInvoiceDraftVersionedActionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _customerInvoiceDraftService.SubmitAsync(new(companyId, draftId, request.ExpectedVersion,
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-drafts/{draftId:guid}/issue")]
    public async Task<ActionResult<CustomerInvoiceDraftIssueResult>> IssueCustomerInvoiceDraftAsync(
        Guid companyId, Guid draftId, [FromBody] IssueCustomerInvoiceDraftRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _customerInvoiceDraftService.IssueAsync(new(companyId, draftId, request.ExpectedVersion,
            request.ExpectedResultHash, request.SeriesId, request.FiscalPeriodId, request.AccountingDate,
            request.VoucherSeriesCode, request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    private static CustomerInvoiceDraftInput MapInvoiceDraft(SaveCustomerInvoiceDraftRequest request) => new(
        request.CustomerId, request.DocumentType, request.IssueDate, request.SupplyDate, request.DueDate,
        request.Currency, request.PaymentTermKind, request.PaymentTermDays, request.BuyerReference,
        request.SellerReference, request.Notes, request.DeliveryIntent, request.SourceKind,
        request.SourceReference, (request.Lines ?? []).Select(line => new CustomerInvoiceDraftLineInput(
            line.Sequence, line.Description, line.Quantity, line.Unit, line.UnitPrice, line.DiscountPercent,
            line.TaxRuleKey, line.TaxClassification, (line.TaxEvidence ?? []).Select(evidence =>
                new CustomerInvoiceDraftTaxEvidenceInput(evidence.Classification, evidence.SourceReference)).ToArray(),
            line.DimensionFacts, line.RevenueAccountRoleKey, line.SourceReference, line.OrderReference)).ToArray(),
        request.EvidenceDocumentIds ?? [], request.OriginalInvoiceId);
}

public sealed class SaveCustomerInvoiceDraftRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public DateOnly SupplyDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentTermKind { get; set; } = string.Empty;
    public int PaymentTermDays { get; set; }
    public string? BuyerReference { get; set; }
    public string? SellerReference { get; set; }
    public string? Notes { get; set; }
    public string DeliveryIntent { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string? SourceReference { get; set; }
    public Guid? OriginalInvoiceId { get; set; }
    public List<CustomerInvoiceDraftLineRequest>? Lines { get; set; } = [];
    public List<Guid>? EvidenceDocumentIds { get; set; } = [];
}

public sealed class CustomerInvoiceDraftLineRequest
{
    public int Sequence { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public string TaxRuleKey { get; set; } = string.Empty;
    public string TaxClassification { get; set; } = string.Empty;
    public List<CustomerInvoiceDraftTaxEvidenceRequest>? TaxEvidence { get; set; } = [];
    public Dictionary<string, string>? DimensionFacts { get; set; }
    public string? RevenueAccountRoleKey { get; set; }
    public string? SourceReference { get; set; }
    public string? OrderReference { get; set; }
}

public sealed class CustomerInvoiceDraftTaxEvidenceRequest
{
    public string Classification { get; set; } = string.Empty;
    public string? SourceReference { get; set; }
}

public class CustomerInvoiceDraftVersionRequest
{
    public long ExpectedVersion { get; set; }
}

public class CustomerInvoiceDraftVersionedActionRequest : CustomerInvoiceDraftVersionRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class CopyCustomerInvoiceDraftRequest : CustomerInvoiceDraftVersionedActionRequest
{
    public DateOnly IssueDate { get; set; }
}

public sealed class IssueCustomerInvoiceDraftRequest : CustomerInvoiceDraftVersionedActionRequest
{
    public string ExpectedResultHash { get; set; } = string.Empty;
    public Guid SeriesId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public DateOnly AccountingDate { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
}
