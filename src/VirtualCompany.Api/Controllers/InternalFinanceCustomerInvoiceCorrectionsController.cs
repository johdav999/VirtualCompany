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
public sealed class InternalFinanceCustomerInvoiceCorrectionsController : InternalFinanceControllerBase
{
    private readonly ICustomerInvoiceCorrectionService _customerInvoiceCorrectionService;

    public InternalFinanceCustomerInvoiceCorrectionsController(
        ICustomerInvoiceCorrectionService customerInvoiceCorrectionService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceCustomerInvoiceCorrectionsController> logger)
        : base(initializationProblems, logger)
    {
        _customerInvoiceCorrectionService = customerInvoiceCorrectionService;
    }
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
                request.CreditDraft is null ? null : CustomerInvoiceRequestMapping.MapInvoiceDraft(request.CreditDraft)), cancellationToken));

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
