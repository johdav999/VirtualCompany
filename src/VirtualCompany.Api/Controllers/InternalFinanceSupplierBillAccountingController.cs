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
public sealed class InternalFinanceSupplierBillAccountingController : InternalFinanceControllerBase
{
    private readonly ISupplierBillAccountingService _supplierBillAccountingService;

    public InternalFinanceSupplierBillAccountingController(
        ISupplierBillAccountingService supplierBillAccountingService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceSupplierBillAccountingController> logger)
        : base(initializationProblems, logger)
    {
        _supplierBillAccountingService = supplierBillAccountingService;
    }
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
