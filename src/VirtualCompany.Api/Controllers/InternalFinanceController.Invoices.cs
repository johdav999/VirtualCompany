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
    [HttpGet("invoices")]
    public async Task<ActionResult<IReadOnlyList<FinanceInvoiceDto>>> GetInvoicesAsync(
        Guid companyId,
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] int limit,
        CancellationToken cancellationToken,
        [FromQuery] string source = FinanceDataSources.Operational) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetInvoicesAsync(
                new GetFinanceInvoicesQuery(companyId, startUtc, endUtc, limit, source),
                cancellationToken));

    [HttpGet("invoices/{invoiceId:guid}/allocations")]
    public async Task<ActionResult<IReadOnlyList<FinancePaymentAllocationDto>>> GetInvoiceAllocationsAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken,
        [FromQuery] string source = FinanceDataSources.Operational) =>
        await ExecuteReadOptionalAsync(
            () => _financePaymentReadService.GetAllocationsByInvoiceAsync(
                new GetFinanceInvoiceAllocationsQuery(companyId, invoiceId, source),
                cancellationToken),
            "Finance invoice was not found.");

    [HttpGet("reviews")]
    public async Task<ActionResult<IReadOnlyList<FinanceInvoiceReviewListItemResponse>>> GetInvoiceReviewsAsync(
        Guid companyId,
        [FromQuery] string? status,
        [FromQuery] string? supplier,
        [FromQuery] string? riskLevel,
        [FromQuery(Name = "outcome")] string? recommendationOutcome,
        [FromQuery] int limit,
        CancellationToken cancellationToken,
        [FromQuery] string source = FinanceDataSources.Operational)
    {
        try
        {
            var normalizedStatus = NormalizeReviewToken(status);
            var normalizedSupplier = NormalizeReviewText(supplier);
            var normalizedRiskLevel = NormalizeReviewToken(riskLevel);
            var normalizedOutcome = NormalizeReviewToken(recommendationOutcome);
            var normalizedLimit = NormalizeReviewLimit(limit);

            var invoices = await _financeReadService.GetInvoicesAsync(
                new GetFinanceInvoicesQuery(companyId, null, null, normalizedLimit, source),
                cancellationToken);

            var items = new List<FinanceInvoiceReviewListItemResponse>(invoices.Count);
            foreach (var invoice in invoices)
            {
                var review = await _invoiceReviewWorkflowService.GetLatestByInvoiceAsync(companyId, invoice.Id, cancellationToken, source);
                var item = MapInvoiceReviewListItem(invoice, review);
                if (MatchesReviewFilters(item, normalizedStatus, normalizedSupplier, normalizedRiskLevel, normalizedOutcome))
                {
                    items.Add(item);
                }
            }

            return Ok(items
                .OrderByDescending(x => x.LastUpdatedUtc)
                .ThenBy(x => x.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (FinanceNotInitializedException ex)
        {
            return await CreateFinanceNotInitializedResultAsync<IReadOnlyList<FinanceInvoiceReviewListItemResponse>>(ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message));
        }
    }

    [HttpGet("reviews/{invoiceId:guid}")]
    public async Task<ActionResult<FinanceInvoiceReviewDetailResponse>> GetInvoiceReviewDetailAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken,
        [FromQuery] string source = FinanceDataSources.Operational)
    {
        try
        {
            var detail = await BuildInvoiceReviewDetailResponseAsync(companyId, invoiceId, executeIfMissing: true, cancellationToken, source);
            return detail is null
                ? NotFound(CreateProblemDetails("Finance invoice review was not found.", "Finance record was not found.", StatusCodes.Status404NotFound))
                : Ok(detail);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (FinanceNotInitializedException ex)
        {
            return await CreateFinanceNotInitializedResultAsync<FinanceInvoiceReviewDetailResponse>(ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message));
        }
    }

    [HttpGet("invoices/{invoiceId:guid}")]
    public async Task<ActionResult<FinanceInvoiceDetailResponse>> GetInvoiceDetailAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken,
        [FromQuery] string source = FinanceDataSources.Operational)
    {
        try
        {
            var detail = await _financeReadService.GetInvoiceDetailAsync(
                new GetFinanceInvoiceDetailQuery(companyId, invoiceId, source),
                cancellationToken);
            if (detail is null)
            {
                return NotFound(CreateProblemDetails("Finance invoice was not found.", "Finance record was not found.", StatusCodes.Status404NotFound));
            }

            var review = await _invoiceReviewWorkflowService.GetLatestByInvoiceAsync(companyId, invoiceId, cancellationToken, source);
            var existingWorkflowContext = detail.WorkflowContext;
            var relatedApprovalId = review?.ApprovalRequestId ?? existingWorkflowContext?.ApprovalRequestId;
            var approval = await TryGetApprovalAsync(companyId, relatedApprovalId, cancellationToken);
            var workflowContext = review is null
                ? existingWorkflowContext
                : new FinanceInvoiceWorkflowContextDto(
                    review.WorkflowInstanceId,
                    review.TaskId,
                    "Invoice review workflow",
                    review.ReviewTaskStatus,
                    review.ApprovalRequestId,
                    review.InvoiceClassification,
                    review.RiskLevel,
                    review.RecommendedAction,
                    review.Rationale,
                    review.ConfidenceScore,
                    review.RequiresHumanApproval,
                    approval?.Status,
                    BuildApprovalAssigneeSummary(approval),
                    review.WorkflowInstanceId.HasValue,
                    approval is not null);

            var recommendationDetails = BuildRecommendationDetails(review, workflowContext);
            var workflowHistory = await BuildWorkflowHistoryAsync(
                companyId,
                review,
                workflowContext,
                relatedApprovalId,
                cancellationToken);
            var accounting = await _customerInvoiceAccountingService.GetAsync(
                new GetCustomerInvoiceAccountingQuery(companyId, invoiceId), cancellationToken);

            return Ok(new FinanceInvoiceDetailResponse(
                detail.Id,
                detail.CounterpartyId,
                detail.CounterpartyName,
                detail.InvoiceNumber,
                detail.IssuedUtc,
                detail.DueUtc,
                detail.Amount,
                detail.Currency,
                detail.Status,
                workflowContext,
                detail.Permissions,
                detail.LinkedDocument,
                recommendationDetails,
                workflowHistory,
                detail.AgentInsights,
                detail.PostingStatus,
                detail.SettlementStatus,
                detail.DueStatus,
                detail.DocumentKind,
                detail.ProviderStatus,
                accounting,
                detail.Source));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (FinanceNotInitializedException ex)
        {
            return await CreateFinanceNotInitializedResultAsync<FinanceInvoiceDetailResponse>(ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message));
        }
    }

    [HttpPost("invoices/{invoiceId:guid}/review-workflow")]
    public async Task<ActionResult<FinanceInvoiceReviewWorkflowResultDto>> ReviewInvoiceWorkflowAsync(
        Guid companyId,
        Guid invoiceId,
        [FromBody] ReviewFinanceInvoiceWorkflowRequest? request,
        CancellationToken cancellationToken,
        [FromQuery] string source = FinanceDataSources.Operational) =>
        await ExecuteWriteAsync(
            () => _invoiceReviewWorkflowService.ExecuteAsync(
                new ReviewFinanceInvoiceWorkflowCommand(
                    companyId,
                    invoiceId,
                    request?.WorkflowInstanceId,
                    request?.AgentId,
                    request?.Payload,
                    source),
                cancellationToken));

    [HttpPost("invoices/{invoiceId:guid}/fortnox-export")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<CustomerInvoiceFortnoxActionDto>> RequestCustomerInvoiceFortnoxExportAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Customer invoice Fortnox export API request received. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            invoiceId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _customerInvoiceFortnoxActionService.RequestExportAsync(
                new RequestCustomerInvoiceFortnoxExportCommand(companyId, invoiceId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("invoices/{invoiceId:guid}/fortnox-export/execute")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<CustomerInvoiceFortnoxActionDto>> ExecuteCustomerInvoiceFortnoxExportAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Customer invoice Fortnox export execution API request received. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}. ActorUserId: {ActorUserId}.",
            companyId,
            invoiceId,
            ResolveActorId());

        return await ExecuteWriteAsync(
            () => _customerInvoiceFortnoxActionService.ExecuteExportAsync(
                new ExecuteCustomerInvoiceFortnoxExportCommand(companyId, invoiceId, ResolveActorId()),
                cancellationToken));
    }

    [HttpPost("invoices/{invoiceId:guid}/fortnox-bookkeep")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<CustomerInvoiceFortnoxActionDto>> RequestCustomerInvoiceFortnoxBookkeepAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Customer invoice Fortnox bookkeeping API request received. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}. ActorUserId: {ActorUserId}. ActorDisplayName: {ActorDisplayName}.",
            companyId,
            invoiceId,
            ResolveActorId(),
            ResolveActorDisplayName());

        return await ExecuteWriteAsync(
            () => _customerInvoiceFortnoxActionService.RequestBookkeepAsync(
                new RequestCustomerInvoiceFortnoxBookkeepCommand(companyId, invoiceId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    }

    [HttpPost("invoices/{invoiceId:guid}/fortnox-bookkeep/execute")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<CustomerInvoiceFortnoxActionDto>> ExecuteCustomerInvoiceFortnoxBookkeepAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Customer invoice Fortnox bookkeeping execution API request received. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}. ActorUserId: {ActorUserId}.",
            companyId,
            invoiceId,
            ResolveActorId());

        return await ExecuteWriteAsync(
            () => _customerInvoiceFortnoxActionService.ExecuteBookkeepAsync(
                new ExecuteCustomerInvoiceFortnoxBookkeepCommand(companyId, invoiceId, ResolveActorId()),
                cancellationToken));
    }

}

