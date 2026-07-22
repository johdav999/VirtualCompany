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
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [HttpGet("bills")]
    public async Task<ActionResult<IReadOnlyList<FinanceBillDto>>> GetBillsAsync(
        Guid companyId,
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] int limit,
        CancellationToken cancellationToken,
        [FromQuery] string source = FinanceDataSources.All) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetBillsAsync(
                new GetFinanceBillsQuery(companyId, startUtc, endUtc, limit, source),
                cancellationToken));

    [HttpGet("bills/{billId:guid}")]
    public async Task<ActionResult<FinanceBillDetailDto>> GetBillDetailAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financeReadService.GetBillDetailAsync(
                new GetFinanceBillDetailQuery(companyId, billId),
                cancellationToken),
            "Finance bill was not found.");

    [HttpPost("bills/{billId:guid}/payment-proposal")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoicePaymentProposalDto>> RequestBillPaymentProposalAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill payment proposal API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierPaymentProposalService.RequestPaymentProposalAsync(
                new RequestSupplierInvoicePaymentProposalCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/payment-proposal/export")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoicePaymentProposalDto>> ExportBillPaymentInstructionAsync(
        Guid companyId,
        Guid billId,
        [FromBody] ExportSupplierInvoicePaymentInstructionRequest? request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill payment export API request received. CompanyId: {CompanyId}. BillId: {BillId}. ExportMode: {ExportMode}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            request?.ExportMode,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierPaymentProposalService.ExportPaymentInstructionAsync(
                new ExportSupplierInvoicePaymentInstructionCommand(
                    companyId,
                    billId,
                    ResolveActorId(),
                    ResolveActorDisplayName(),
                    request?.ExportMode ?? SupplierInvoicePaymentExportModes.RegisterPayment),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/source-document-attachment")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoiceSourceDocumentAttachmentDto>> AttachBillSourceDocumentAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill source document attachment API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierInvoiceSourceDocumentAttachmentService.RequestAttachmentAsync(
                new RequestSupplierInvoiceSourceDocumentAttachmentCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/fortnox-draft/update")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoiceDraftActionDto>> UpdateBillFortnoxDraftAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill Fortnox draft update API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierInvoiceDraftActionService.UpdateDraftAsync(
                new UpdateSupplierInvoiceDraftCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/fortnox-draft/bookkeep")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoiceDraftActionDto>> BookkeepBillFortnoxDraftAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill Fortnox bookkeeping API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierInvoiceDraftActionService.BookkeepAsync(
                new BookkeepSupplierInvoiceDraftCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/paid-expense-posting")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<PaidSupplierBillExpensePostingDto>> PostPaidSupplierBillExpenseAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Paid supplier bill expense posting API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _paidSupplierBillExpensePostingService.PostAsync(
                new PostPaidSupplierBillExpenseCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/corrections/cancel")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoiceCorrectionActionDto>> CancelSupplierInvoiceAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill cancellation API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierInvoiceCorrectionService.RequestCancellationAsync(
                new RequestSupplierInvoiceCancellationCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/corrections/credit-note")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoiceCorrectionActionDto>> CreateSupplierCreditNoteAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill credit note API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierInvoiceCorrectionService.RequestCreditNoteAsync(
                new RequestSupplierInvoiceCreditNoteCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/enrichment/suggest")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoiceEnrichmentActionDto>> SuggestSupplierInvoiceEnrichmentAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill enrichment suggestion API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierInvoiceEnrichmentService.SuggestAsync(
                new SuggestSupplierInvoiceEnrichmentCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/enrichment/sync")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoiceEnrichmentActionDto>> SyncSupplierInvoiceEnrichmentAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill enrichment sync API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierInvoiceEnrichmentService.SyncApprovedAsync(
                new SyncSupplierInvoiceEnrichmentCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("bills/{billId:guid}/enrichment/reconcile")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierInvoiceEnrichmentActionDto>> ReconcileSupplierInvoiceAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Supplier bill reconciliation API request received. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            billId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _supplierInvoiceEnrichmentService.ReconcileAsync(
                new ReconcileSupplierInvoiceCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpGet("bills/{billId:guid}/allocations")]
    public async Task<ActionResult<IReadOnlyList<FinancePaymentAllocationDto>>> GetBillAllocationsAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financePaymentReadService.GetAllocationsByBillAsync(
                new GetFinanceBillAllocationsQuery(companyId, billId),
                cancellationToken),
            "Finance bill was not found.");

}

