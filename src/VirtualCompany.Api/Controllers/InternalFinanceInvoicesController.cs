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
public sealed class InternalFinanceInvoicesController : InternalFinanceControllerBase
{
    private readonly IFinanceTransactionAnomalyDetectionService _anomalyDetectionService;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly IAuditQueryService _auditQueryService;
    private readonly ICustomerInvoiceAccountingService _customerInvoiceAccountingService;
    private readonly IFinanceCustomerInvoiceFortnoxActionService _customerInvoiceFortnoxActionService;
    private readonly IFinanceCommandService _financeCommandService;
    private readonly IFinancePaymentReadService _financePaymentReadService;

    private readonly IFinanceReadService _financeReadService;
    private readonly IInvoiceReviewWorkflowService _invoiceReviewWorkflowService;

    public InternalFinanceInvoicesController(
        IFinanceTransactionAnomalyDetectionService anomalyDetectionService,
        IApprovalRequestService approvalRequestService,
        IAuditQueryService auditQueryService,
        ICustomerInvoiceAccountingService customerInvoiceAccountingService,
        IFinanceCustomerInvoiceFortnoxActionService customerInvoiceFortnoxActionService,
        IFinanceCommandService financeCommandService,
        IFinancePaymentReadService financePaymentReadService,
        IFinanceReadService financeReadService,
        IInvoiceReviewWorkflowService invoiceReviewWorkflowService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceInvoicesController> logger)
        : base(initializationProblems, logger)
    {
        _anomalyDetectionService = anomalyDetectionService;
        _approvalRequestService = approvalRequestService;
        _auditQueryService = auditQueryService;
        _customerInvoiceAccountingService = customerInvoiceAccountingService;
        _customerInvoiceFortnoxActionService = customerInvoiceFortnoxActionService;
        _financeCommandService = financeCommandService;
        _financePaymentReadService = financePaymentReadService;
        _financeReadService = financeReadService;
        _invoiceReviewWorkflowService = invoiceReviewWorkflowService;
    }
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

    private static FinanceInvoiceReviewListItemResponse MapInvoiceReviewListItem(
        FinanceInvoiceDto invoice,
        FinanceInvoiceReviewWorkflowResultDto? review) =>
        new(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.CounterpartyName,
            invoice.Amount,
            invoice.Currency,
            invoice.Status,
            review?.RiskLevel ?? "unknown",
            review?.ReviewTaskStatus ?? "not_started",
            review?.RecommendedAction ?? "pending_review",
            review?.ConfidenceScore ?? 0m,
            review?.LastUpdatedUtc ?? invoice.IssuedUtc);

    private static FinanceInvoiceReviewActionAvailabilityResponse BuildReviewActionAvailability(
        FinanceInvoiceDetailDto invoice,
        FinanceInvoiceReviewWorkflowResultDto? review)
    {
        // Default deny when the workflow state is unknown or no longer actionable.
        var isActionable = FinanceInvoiceReviewActionPolicy.CanPerformReviewAction(
            invoice.Permissions.CanChangeInvoiceApprovalStatus,
            review?.ReviewTaskStatus);

        return new FinanceInvoiceReviewActionAvailabilityResponse(
            isActionable,
            isActionable,
            isActionable,
            isActionable);
    }

    private static bool MatchesReviewFilters(
        FinanceInvoiceReviewListItemResponse item,
        string? status,
        string? supplier,
        string? riskLevel,
        string? outcome)
    {
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(NormalizeReviewToken(item.Status), status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(supplier) &&
            !item.SupplierName.Contains(supplier, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(riskLevel) && !string.Equals(NormalizeReviewToken(item.RiskLevel), riskLevel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(outcome) ||
               string.Equals(NormalizeReviewToken(item.RecommendationOutcome), outcome, StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizeReviewLimit(int limit) =>
        limit <= 0 ? 200 : Math.Min(limit, 500);

    private async Task<IReadOnlyList<FinanceInvoiceWorkflowHistoryItemResponse>> BuildWorkflowHistoryAsync(
        Guid companyId,
        FinanceInvoiceReviewWorkflowResultDto? review,
        FinanceInvoiceWorkflowContextDto? fallbackWorkflowContext,
        Guid? relatedApprovalId,
        CancellationToken cancellationToken)
    {
        var items = new List<FinanceInvoiceWorkflowHistoryItemResponse>();

        if (review is not null || fallbackWorkflowContext is not null)
        {
            var reviewTaskId = review?.TaskId ?? fallbackWorkflowContext?.TaskId ?? Guid.Empty;
            var workflowInstanceId = review?.WorkflowInstanceId ?? fallbackWorkflowContext?.WorkflowInstanceId;
            items.Add(new FinanceInvoiceWorkflowHistoryItemResponse(
                reviewTaskId != Guid.Empty ? $"review-task:{reviewTaskId:D}" : $"review-workflow:{workflowInstanceId:D}",
                "Review",
                "Invoice review workflow",
                review?.LastUpdatedUtc ?? DateTime.UtcNow,
                null,
                relatedApprovalId));

            if (reviewTaskId != Guid.Empty || workflowInstanceId.HasValue)
            {
                var auditHistory = await _auditQueryService.ListAsync(
                    companyId,
                    new AuditHistoryFilter(
                        TaskId: reviewTaskId != Guid.Empty ? reviewTaskId : null,
                        WorkflowInstanceId: workflowInstanceId,
                        Take: 100),
                    cancellationToken);

                items.AddRange(auditHistory.Items.Select(MapAuditHistoryItem));
            }
        }

        var approval = await TryGetApprovalAsync(companyId, relatedApprovalId, cancellationToken);
        if (approval is not null)
        {
            items.AddRange(MapApprovalHistory(approval));
        }

        return NormalizeWorkflowHistory(items);
    }

    private static FinanceInvoiceWorkflowHistoryItemResponse MapAuditHistoryItem(AuditHistoryListItem item) =>
        new(
            item.Id.ToString("D"),
            ResolveWorkflowHistoryEventType(item),
            ResolveWorkflowHistoryActor(item),
            item.OccurredAt,
            item.Id,
            item.TargetType.Equals(AuditTargetTypes.ApprovalRequest, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(item.TargetId, out var approvalId)
                ? approvalId
                : null);

    private static IEnumerable<FinanceInvoiceWorkflowHistoryItemResponse> MapApprovalHistory(ApprovalRequestDto approval)
    {
        yield return new FinanceInvoiceWorkflowHistoryItemResponse(
            $"approval:{approval.Id:D}:requested",
            "Approval requested",
            BuildApprovalRequesterDisplay(approval),
            approval.CreatedAt,
            null,
            approval.Id);

        foreach (var step in approval.Steps)
        {
            if (step.DecidedAt is not DateTime decidedAt)
            {
                continue;
            }

            var eventType = string.Equals(step.Status, "rejected", StringComparison.OrdinalIgnoreCase)
                ? "Rejection"
                : "Approval";

            yield return new FinanceInvoiceWorkflowHistoryItemResponse(
                $"approval-step:{step.Id:D}",
                eventType,
                BuildApprovalDecisionActorDisplay(step),
                decidedAt,
                null,
                approval.Id);
        }
    }

    private static IReadOnlyList<FinanceInvoiceWorkflowHistoryItemResponse> NormalizeWorkflowHistory(
        IEnumerable<FinanceInvoiceWorkflowHistoryItemResponse> items)
    {
        var orderedItems = items
            .Where(item => item is not null)
            .Select((item, index) => new { Item = item, Index = index })
            .OrderBy(entry => entry.Item.OccurredAtUtc == default ? 1 : 0)
            .ThenByDescending(entry => entry.Item.OccurredAtUtc)
            .ThenByDescending(entry => GetWorkflowHistoryItemCompleteness(entry.Item))
            .ThenBy(entry => NormalizeReviewText(entry.Item.EventType) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => NormalizeReviewText(entry.Item.ActorOrSourceDisplayName) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Item with
            {
                EventId = NormalizeWorkflowHistoryEventId(entry.Item.EventId) ?? string.Empty,
                EventType = NormalizeReviewText(entry.Item.EventType) ?? "Review",
                ActorOrSourceDisplayName = NormalizeReviewText(entry.Item.ActorOrSourceDisplayName) ?? "System"
            })
            .ToList();

        var uniqueItems = new List<FinanceInvoiceWorkflowHistoryItemResponse>(orderedItems.Count);
        var seenEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in orderedItems)
        {
            var normalizedEventId = NormalizeWorkflowHistoryEventId(item.EventId);
            if (normalizedEventId is not null)
            {
                if (!seenEventIds.Add(normalizedEventId))
                {
                    continue;
                }
            }

            uniqueItems.Add(item);
        }

        return uniqueItems;
    }

    private static int GetWorkflowHistoryItemCompleteness(FinanceInvoiceWorkflowHistoryItemResponse item) =>
        (item.OccurredAtUtc == default ? 0 : 1) +
        (string.IsNullOrWhiteSpace(item.EventType) ? 0 : 1) +
        (string.IsNullOrWhiteSpace(item.ActorOrSourceDisplayName) ? 0 : 1) +
        (item.RelatedAuditId.HasValue ? 1 : 0) +
        (item.RelatedApprovalId.HasValue ? 1 : 0);

    private static string? NormalizeWorkflowHistoryEventId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ResolveWorkflowHistoryEventType(AuditHistoryListItem item)
    {
        var action = item.Action.Trim().ToLowerInvariant();
        var outcome = item.Outcome.Trim().ToLowerInvariant();

        if (action.Contains("tool_execution", StringComparison.Ordinal))
        {
            return "Tool event";
        }

        if (outcome == AuditEventOutcomes.Rejected || action.Contains("rejected", StringComparison.Ordinal))
        {
            return "Rejection";
        }

        if (action.Contains("approval", StringComparison.Ordinal))
        {
            return outcome == AuditEventOutcomes.Requested || action.Contains("requested", StringComparison.Ordinal)
                ? "Approval requested"
                : "Approval";
        }

        if (item.TargetType.Equals(AuditTargetTypes.WorkTask, StringComparison.OrdinalIgnoreCase) || action.Contains("task", StringComparison.Ordinal))
        {
            return "Task execution";
        }

        return "Review";
    }

    private static string ResolveWorkflowHistoryActor(AuditHistoryListItem item) =>
        !string.IsNullOrWhiteSpace(item.ActorLabel) ? item.ActorLabel! :
        !string.IsNullOrWhiteSpace(item.AgentName) ? item.AgentName! :
        HumanizeReviewToken(item.ActorType);

    private static string BuildApprovalRequesterDisplay(ApprovalRequestDto approval) =>
        $"{HumanizeReviewToken(approval.RequestedByActorType)} {approval.RequestedByActorId:N}";

    private static string BuildApprovalDecisionActorDisplay(ApprovalStepDto step) =>
        step.DecidedByUserId is Guid decidedByUserId
            ? $"User {decidedByUserId:N}"
            : $"{HumanizeReviewToken(step.ApproverType)} {step.ApproverRef}";

    private static string? NormalizeReviewText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<FinanceInvoiceReviewDetailResponse?> BuildInvoiceReviewDetailResponseAsync(
        Guid companyId,
        Guid invoiceId,
        bool executeIfMissing,
        CancellationToken cancellationToken,
        string source = FinanceDataSources.Operational)
    {
        var invoice = await _financeReadService.GetInvoiceDetailAsync(
            new GetFinanceInvoiceDetailQuery(companyId, invoiceId, source),
            cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var review = await _invoiceReviewWorkflowService.GetLatestByInvoiceAsync(companyId, invoiceId, cancellationToken, source);
        if (review is null && executeIfMissing)
        {
            review = await _invoiceReviewWorkflowService.ExecuteAsync(
                new ReviewFinanceInvoiceWorkflowCommand(
                    companyId,
                    invoiceId,
                    null,
                    null,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["trigger"] = JsonValue.Create("review_detail_requested")
                    },
                    source),
                cancellationToken);
        }

        var existingWorkflowContext = invoice.WorkflowContext;
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

        return new FinanceInvoiceReviewDetailResponse(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.CounterpartyName,
            invoice.Amount,
            invoice.Currency,
            invoice.Status,
            review?.RiskLevel ?? workflowContext?.RiskLevel ?? "unknown",
            review?.ReviewTaskStatus ?? workflowContext?.ReviewTaskStatus ?? "not_started",
            review?.Rationale ?? workflowContext?.Rationale ?? "No invoice review workflow result is available.",
            review?.RecommendedAction ?? workflowContext?.RecommendedAction ?? "pending_review",
            review?.ConfidenceScore ?? workflowContext?.Confidence ?? 0m,
            review?.LastUpdatedUtc ?? invoice.IssuedUtc,
            invoice.Id,
            relatedApprovalId,
            BuildReviewActionAvailability(invoice, review),
            recommendationDetails,
            workflowHistory);
    }

    private static FinanceInvoiceRecommendationDetailsResponse? BuildRecommendationDetails(
        FinanceInvoiceReviewWorkflowResultDto? review,
        FinanceInvoiceWorkflowContextDto? workflowContext)
    {
        if (review is null && workflowContext is null)
        {
            return null;
        }

        return new FinanceInvoiceRecommendationDetailsResponse(
            review?.InvoiceClassification ?? workflowContext?.Classification ?? "unknown",
            review?.RiskLevel ?? workflowContext?.RiskLevel ?? "unknown",
            review?.Rationale ?? workflowContext?.Rationale ?? "No recommendation rationale is available.",
            review?.ConfidenceScore ?? workflowContext?.Confidence ?? 0m,
            review?.RecommendedAction ?? workflowContext?.RecommendedAction ?? "pending_review",
            review?.ReviewTaskStatus ?? workflowContext?.ReviewTaskStatus ?? "not_started");
    }

    private async Task<ActionResult<FinanceInvoiceReviewDetailResponse>> ExecuteInvoiceReviewActionAsync(
        Guid companyId,
        Guid invoiceId,
        string targetStatus,
        string actionName,
        CancellationToken cancellationToken)
    {
        try
        {
            await _financeCommandService.UpdateInvoiceApprovalStatusAsync(
                new UpdateFinanceInvoiceApprovalStatusCommand(companyId, invoiceId, targetStatus),
                cancellationToken);

            var detail = await BuildInvoiceReviewDetailResponseAsync(companyId, invoiceId, executeIfMissing: false, cancellationToken);
            return detail is null
                ? NotFound(CreateProblemDetails($"Finance invoice review for action '{actionName}' was not found.", "Finance record was not found.", StatusCodes.Status404NotFound))
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
        catch (FinanceValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Finance validation failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails(ex.Message, "Finance record was not found.", StatusCodes.Status404NotFound));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
    }

    private async Task<ApprovalRequestDto?> TryGetApprovalAsync(
        Guid companyId,
        Guid? approvalRequestId,
        CancellationToken cancellationToken)
    {
        if (!approvalRequestId.HasValue)
        {
            return null;
        }

        try { return await _approvalRequestService.GetAsync(companyId, approvalRequestId.Value, cancellationToken); }
        catch (KeyNotFoundException) { return null; }
    }

    private static string? BuildApprovalAssigneeSummary(ApprovalRequestDto? approval)
    {
        if (approval is null)
        {
            return null;
        }

        if (approval.CurrentStep is not null)
        {
            return approval.CurrentStep.ApproverType.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? $"Assigned to user {approval.CurrentStep.ApproverRef}."
                : $"Awaiting {approval.CurrentStep.ApproverRef} approval.";
        }

        if (approval.RequiredUserId is Guid requiredUserId)
        {
            return $"Assigned to user {requiredUserId:D}.";
        }

        return string.IsNullOrWhiteSpace(approval.RequiredRole)
            ? null
            : $"Awaiting {approval.RequiredRole} approval.";
    }

}
