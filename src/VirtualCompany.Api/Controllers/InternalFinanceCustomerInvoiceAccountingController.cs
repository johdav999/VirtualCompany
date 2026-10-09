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
public sealed class InternalFinanceCustomerInvoiceAccountingController : InternalFinanceControllerBase
{
    private readonly ICustomerInvoiceAccountingService _customerInvoiceAccountingService;

    public InternalFinanceCustomerInvoiceAccountingController(
        ICustomerInvoiceAccountingService customerInvoiceAccountingService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceCustomerInvoiceAccountingController> logger)
        : base(initializationProblems, logger)
    {
        _customerInvoiceAccountingService = customerInvoiceAccountingService;
    }
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

}
