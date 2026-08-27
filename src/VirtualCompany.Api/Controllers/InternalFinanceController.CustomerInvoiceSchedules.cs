using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-invoice-schedules")]
    public async Task<ActionResult<CustomerInvoiceScheduleListResult>> ListCustomerInvoiceSchedulesAsync(Guid companyId,
        [FromQuery] string? status = null, [FromQuery] Guid? customerId = null, [FromQuery] int skip = 0,
        [FromQuery] int take = 100, CancellationToken cancellationToken = default) => await ExecuteReadAsync(() =>
        _customerInvoiceScheduleService.ListAsync(new(companyId, status, customerId, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-invoice-schedules/{scheduleId:guid}")]
    public async Task<ActionResult<CustomerInvoiceScheduleDto>> GetCustomerInvoiceScheduleAsync(Guid companyId,
        Guid scheduleId, CancellationToken cancellationToken) => await ExecuteReadAsync(() =>
        _customerInvoiceScheduleService.GetAsync(new(companyId, scheduleId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/customer-invoice-schedules/{scheduleId:guid}/preview")]
    public async Task<ActionResult<CustomerInvoiceSchedulePreviewDto>> PreviewCustomerInvoiceScheduleAsync(Guid companyId,
        Guid scheduleId, [FromQuery] int count = 12, CancellationToken cancellationToken = default) => await ExecuteReadAsync(() =>
        _customerInvoiceScheduleService.PreviewAsync(new(companyId, scheduleId, count), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-schedules")]
    public async Task<ActionResult<CustomerInvoiceScheduleDto>> CreateCustomerInvoiceScheduleAsync(Guid companyId,
        [FromBody] SaveCustomerInvoiceScheduleRequest request, CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _customerInvoiceScheduleService.CreateAsync(new(companyId, Map(request), request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/customer-invoice-schedules/{scheduleId:guid}")]
    public async Task<ActionResult<CustomerInvoiceScheduleDto>> UpdateCustomerInvoiceScheduleAsync(Guid companyId,
        Guid scheduleId, [FromBody] SaveCustomerInvoiceScheduleRequest request, CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _customerInvoiceScheduleService.UpdateAsync(new(companyId, scheduleId, request.ExpectedVersion, Map(request), request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-schedules/{scheduleId:guid}/submit")]
    public async Task<ActionResult<CustomerInvoiceScheduleSubmissionResult>> SubmitCustomerInvoiceScheduleAsync(Guid companyId,
        Guid scheduleId, [FromBody] CustomerInvoiceScheduleActionRequest request, CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _customerInvoiceScheduleService.SubmitAsync(new(companyId, scheduleId, request.ExpectedVersion,
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-schedules/{scheduleId:guid}/activate")]
    public Task<ActionResult<CustomerInvoiceScheduleDto>> ActivateCustomerInvoiceScheduleAsync(Guid companyId, Guid scheduleId, [FromBody] CustomerInvoiceScheduleActionRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() => _customerInvoiceScheduleService.ActivateAsync(new(companyId, scheduleId, request.ExpectedVersion, request.IdempotencyKey, RequiredActor(), ResolveCorrelationId(), request.AllowBackdatedGeneration, request.RetryBlockedOccurrence), cancellationToken));
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-schedules/{scheduleId:guid}/pause")]
    public Task<ActionResult<CustomerInvoiceScheduleDto>> PauseCustomerInvoiceScheduleAsync(Guid companyId, Guid scheduleId, [FromBody] CustomerInvoiceScheduleActionRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() => _customerInvoiceScheduleService.PauseAsync(new(companyId, scheduleId, request.ExpectedVersion, request.IdempotencyKey, RequiredActor(), ResolveCorrelationId(), request.AllowBackdatedGeneration, request.RetryBlockedOccurrence), cancellationToken));
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-schedules/{scheduleId:guid}/resume")]
    public Task<ActionResult<CustomerInvoiceScheduleDto>> ResumeCustomerInvoiceScheduleAsync(Guid companyId, Guid scheduleId, [FromBody] CustomerInvoiceScheduleActionRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() => _customerInvoiceScheduleService.ResumeAsync(new(companyId, scheduleId, request.ExpectedVersion, request.IdempotencyKey, RequiredActor(), ResolveCorrelationId(), request.AllowBackdatedGeneration, request.RetryBlockedOccurrence), cancellationToken));
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/customer-invoice-schedules/{scheduleId:guid}/end")]
    public Task<ActionResult<CustomerInvoiceScheduleDto>> EndCustomerInvoiceScheduleAsync(Guid companyId, Guid scheduleId, [FromBody] CustomerInvoiceScheduleActionRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() => _customerInvoiceScheduleService.EndAsync(new(companyId, scheduleId, request.ExpectedVersion, request.IdempotencyKey, RequiredActor(), ResolveCorrelationId(), request.AllowBackdatedGeneration, request.RetryBlockedOccurrence), cancellationToken));

    private static CustomerInvoiceScheduleInput Map(SaveCustomerInvoiceScheduleRequest request) => new(request.CustomerId, request.Name, request.StartDate, request.EndDate, request.Cadence, request.BillingDay, request.TimeZoneId, request.BusinessDayConvention, request.ProrationRule, request.DueDateOffsetDays, request.DocumentType, request.Currency, request.PaymentTermKind, request.PaymentTermDays, request.BuyerReference, request.SellerReference, request.Notes, request.DeliveryIntent, request.AutoIssueEnabled, (request.Lines ?? []).Select(x => new CustomerInvoiceScheduleLineInput(x.Sequence, x.Description, x.Quantity, x.Unit, x.UnitPrice, x.DiscountPercent, x.TaxRuleKey, x.TaxClassification, (x.TaxEvidence ?? []).Select(y => new CustomerInvoiceDraftTaxEvidenceInput(y.Classification, y.SourceReference)).ToArray(), x.DimensionFacts, x.RevenueAccountRoleKey, x.SourceReference, x.OrderReference)).ToArray(), request.EvidenceDocumentIds ?? []);
}

public sealed class SaveCustomerInvoiceScheduleRequest
{
    public Guid CustomerId { get; set; } public string Name { get; set; } = string.Empty; public DateOnly StartDate { get; set; } public DateOnly? EndDate { get; set; } public string Cadence { get; set; } = "monthly"; public int BillingDay { get; set; } public string TimeZoneId { get; set; } = "Europe/Stockholm"; public string BusinessDayConvention { get; set; } = "calendar"; public string ProrationRule { get; set; } = "none"; public int DueDateOffsetDays { get; set; } public string DocumentType { get; set; } = "invoice"; public string Currency { get; set; } = string.Empty; public string PaymentTermKind { get; set; } = "net"; public int PaymentTermDays { get; set; } public string? BuyerReference { get; set; } public string? SellerReference { get; set; } public string? Notes { get; set; } public string DeliveryIntent { get; set; } = "email"; public bool AutoIssueEnabled { get; set; } public long ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; public List<CustomerInvoiceScheduleLineRequest>? Lines { get; set; } = []; public List<Guid>? EvidenceDocumentIds { get; set; } = [];
}
public sealed class CustomerInvoiceScheduleLineRequest
{ public int Sequence { get; set; } public string Description { get; set; } = string.Empty; public decimal Quantity { get; set; } public string Unit { get; set; } = string.Empty; public decimal UnitPrice { get; set; } public decimal DiscountPercent { get; set; } public string TaxRuleKey { get; set; } = string.Empty; public string TaxClassification { get; set; } = string.Empty; public List<CustomerInvoiceDraftTaxEvidenceRequest>? TaxEvidence { get; set; } = []; public Dictionary<string, string>? DimensionFacts { get; set; } public string? RevenueAccountRoleKey { get; set; } public string? SourceReference { get; set; } public string? OrderReference { get; set; } }
public sealed class CustomerInvoiceScheduleActionRequest { public long ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; public bool AllowBackdatedGeneration { get; set; } public bool RetryBlockedOccurrence { get; set; } }
