using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalFinanceCustomerInvoiceSchedulesController : InternalFinanceControllerBase
{
    private readonly ICustomerInvoiceScheduleService _customerInvoiceScheduleService;

    public InternalFinanceCustomerInvoiceSchedulesController(
        ICustomerInvoiceScheduleService customerInvoiceScheduleService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceCustomerInvoiceSchedulesController> logger)
        : base(initializationProblems, logger)
    {
        _customerInvoiceScheduleService = customerInvoiceScheduleService;
    }
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
