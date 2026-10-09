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
public sealed class InternalFinanceCustomerInvoiceDraftsController : InternalFinanceControllerBase
{
    private readonly ICustomerInvoiceDraftService _customerInvoiceDraftService;

    public InternalFinanceCustomerInvoiceDraftsController(
        ICustomerInvoiceDraftService customerInvoiceDraftService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceCustomerInvoiceDraftsController> logger)
        : base(initializationProblems, logger)
    {
        _customerInvoiceDraftService = customerInvoiceDraftService;
    }
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
        await ExecuteWriteAsync(() => _customerInvoiceDraftService.CreateAsync(new(companyId, CustomerInvoiceRequestMapping.MapInvoiceDraft(request),
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/customer-invoice-drafts/{draftId:guid}")]
    public async Task<ActionResult<CustomerInvoiceDraftDto>> UpdateCustomerInvoiceDraftAsync(Guid companyId,
        Guid draftId, [FromBody] SaveCustomerInvoiceDraftRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _customerInvoiceDraftService.UpdateAsync(new(companyId, draftId,
            request.ExpectedVersion, CustomerInvoiceRequestMapping.MapInvoiceDraft(request), request.IdempotencyKey, RequiredActor(),
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

}
