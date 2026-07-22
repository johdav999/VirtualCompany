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
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPatch("invoices/{invoiceId:guid}/approval-status")]
    public async Task<ActionResult<FinanceInvoiceDto>> UpdateInvoiceApprovalStatusAsync(
        Guid companyId,
        Guid invoiceId,
        [FromBody] UpdateFinanceInvoiceApprovalStatusRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.UpdateInvoiceApprovalStatusAsync(
                new UpdateFinanceInvoiceApprovalStatusCommand(companyId, invoiceId, request.Status),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("reviews/{invoiceId:guid}/approve")]
    public Task<ActionResult<FinanceInvoiceReviewDetailResponse>> ApproveInvoiceReviewAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken) =>
        ExecuteInvoiceReviewActionAsync(companyId, invoiceId, "approved", "approve", cancellationToken);

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("reviews/{invoiceId:guid}/reject")]
    public Task<ActionResult<FinanceInvoiceReviewDetailResponse>> RejectInvoiceReviewAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken) =>
        ExecuteInvoiceReviewActionAsync(companyId, invoiceId, "rejected", "reject", cancellationToken);

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("reviews/{invoiceId:guid}/follow-up")]
    public Task<ActionResult<FinanceInvoiceReviewDetailResponse>> SendInvoiceReviewForFollowUpAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken) =>
        ExecuteInvoiceReviewActionAsync(companyId, invoiceId, "open", "send_for_follow_up", cancellationToken);

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPatch("transactions/{transactionId:guid}/category")]
    public async Task<ActionResult<FinanceTransactionDto>> UpdateTransactionCategoryAsync(
        Guid companyId,
        Guid transactionId,
        [FromBody] UpdateFinanceTransactionCategoryRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.UpdateTransactionCategoryAsync(
                new UpdateFinanceTransactionCategoryCommand(companyId, transactionId, request.Category),
                cancellationToken));

    [HttpPost("transactions/{transactionId:guid}/anomaly-evaluation")]
    public async Task<ActionResult<FinanceTransactionAnomalyEvaluationDto>> EvaluateTransactionAnomalyAsync(
        Guid companyId,
        Guid transactionId,
        [FromBody] EvaluateFinanceTransactionAnomalyRequest? request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _anomalyDetectionService.EvaluateAsync(
                new EvaluateFinanceTransactionAnomalyCommand(
                    companyId,
                    transactionId,
                    request?.WorkflowInstanceId,
                    request?.AgentId),
                cancellationToken));

}
