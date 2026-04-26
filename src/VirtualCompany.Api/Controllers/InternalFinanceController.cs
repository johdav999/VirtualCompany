using System.Globalization;
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

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed partial class InternalFinanceController : ControllerBase
{
    private const string FinanceSimulationCurrentRunKey = "financeSimulationLatestRun";
    private const string FinanceSimulationRunHistoryKey = "financeSimulationRunHistory";
    private const string FinanceRequestNotInitializedAction = "finance.request.not_initialized";
    private static readonly JsonSerializerOptions SandboxSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IFinanceReadService _financeReadService;
    private readonly IDashboardFinanceSnapshotService _dashboardFinanceSnapshotService;
    private readonly IFinanceCommandService _financeCommandService;
    private readonly IFinancePaymentReadService _financePaymentReadService;
    private readonly IFinancePaymentCommandService _financePaymentCommandService;
    private readonly IAuditQueryService _auditQueryService;
    private readonly IFinancePolicyConfigurationService _financePolicyConfigurationService;
    private readonly IFinanceSeedBootstrapService _financeSeedBootstrapService;
    private readonly IFinanceEntryService _financeEntryService;
    private readonly IInvoiceReviewWorkflowService _invoiceReviewWorkflowService;
    private readonly IFinanceSeedingStateService _financeSeedingStateService;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly IFinanceTransactionAnomalyDetectionService _anomalyDetectionService;
    private readonly IFinanceCashPositionWorkflowService _cashPositionWorkflowService;
    private readonly ICompanySimulationService _companySimulationService;
    private readonly ICompanyToolRegistry _toolRegistry;
    private readonly IFinanceToolProvider _financeToolProvider;
    private readonly ISimulationFeatureGate _simulationFeatureGate;
    private readonly IFinanceBootstrapRerunService _financeBootstrapRerunService;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IOptions<FinanceInitializationOptions> _financeInitializationOptions;
    private readonly ILogger<InternalFinanceController> _logger;

    public InternalFinanceController(
        IFinanceReadService financeReadService,
        IDashboardFinanceSnapshotService dashboardFinanceSnapshotService,
        IFinanceCommandService financeCommandService,
        IFinancePaymentReadService financePaymentReadService,
        IFinancePaymentCommandService financePaymentCommandService,
        IAuditQueryService auditQueryService,
        IFinancePolicyConfigurationService financePolicyConfigurationService,
        IFinanceEntryService financeEntryService,
        IFinanceSeedBootstrapService financeSeedBootstrapService,
        IFinanceSeedingStateService financeSeedingStateService,
        IInvoiceReviewWorkflowService invoiceReviewWorkflowService,
        IApprovalRequestService approvalRequestService,
        IFinanceTransactionAnomalyDetectionService anomalyDetectionService,
        IFinanceCashPositionWorkflowService cashPositionWorkflowService,
        ICompanyToolRegistry toolRegistry,
        IFinanceToolProvider financeToolProvider,
        VirtualCompanyDbContext dbContext,
        IFinanceBootstrapRerunService financeBootstrapRerunService,
        ISimulationFeatureGate simulationFeatureGate,
        ICompanySimulationService companySimulationService,
        IAuditEventWriter auditEventWriter,
        IOptions<FinanceInitializationOptions> financeInitializationOptions,
        ILogger<InternalFinanceController> logger)
    {
        _financeReadService = financeReadService;
        _dashboardFinanceSnapshotService = dashboardFinanceSnapshotService;
        _toolRegistry = toolRegistry;
        _financePaymentReadService = financePaymentReadService;
        _financePaymentCommandService = financePaymentCommandService;
        _financeToolProvider = financeToolProvider;
        _financeCommandService = financeCommandService;
        _auditQueryService = auditQueryService;
        _financePolicyConfigurationService = financePolicyConfigurationService;
        _financeEntryService = financeEntryService;
        _financeSeedBootstrapService = financeSeedBootstrapService;
        _financeSeedingStateService = financeSeedingStateService;
        _invoiceReviewWorkflowService = invoiceReviewWorkflowService;
        _approvalRequestService = approvalRequestService;
        _anomalyDetectionService = anomalyDetectionService;
        _dbContext = dbContext;
        _financeBootstrapRerunService = financeBootstrapRerunService;
        _simulationFeatureGate = simulationFeatureGate;
        _cashPositionWorkflowService = cashPositionWorkflowService;
        _companySimulationService = companySimulationService;
        _auditEventWriter = auditEventWriter;
        _financeInitializationOptions = financeInitializationOptions;
        _logger = logger;
    }

    [HttpGet("cash-balance")]
    public async Task<ActionResult<FinanceCashBalanceDto>> GetCashBalanceAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetCashBalanceAsync(
                new GetFinanceCashBalanceQuery(companyId, asOfUtc),
                cancellationToken));

    [HttpGet("cash-position")]
    public async Task<ActionResult<FinanceCashPositionDto>> GetCashPositionAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] decimal? averageMonthlyBurn,
        [FromQuery] int burnLookbackDays,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetCashPositionAsync(
                new GetFinanceCashPositionQuery(companyId, asOfUtc, averageMonthlyBurn, burnLookbackDays),
                cancellationToken));

    [HttpGet("balances")]
    public async Task<ActionResult<IReadOnlyList<FinanceAccountBalanceDto>>> GetBalancesAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetBalancesAsync(
                new GetFinanceBalancesQuery(companyId, asOfUtc),
                cancellationToken));

    [HttpPost("cash-position/evaluation")]
    public async Task<ActionResult<FinanceCashPositionDto>> EvaluateCashPositionAsync(
        Guid companyId,
        [FromBody] EvaluateFinanceCashPositionRequest? request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _cashPositionWorkflowService.EvaluateAsync(
                new EvaluateFinanceCashPositionWorkflowCommand(
                    companyId,
                    request?.WorkflowInstanceId,
                    request?.AgentId),
                cancellationToken));

    [HttpGet("dashboard/cash-metrics")]
    public async Task<ActionResult<DashboardFinanceSnapshotDto>> GetDashboardCashMetricsAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        await ExecuteReadAsync(
            () => _dashboardFinanceSnapshotService.GetAsync(
                companyId,
                asOfUtc,
                upcomingWindowDays,
                cancellationToken));

    [HttpGet("dashboard/current-cash-balance")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetCurrentCashBalanceAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("current_cash_balance", snapshot.CurrentCashBalance, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("dashboard/expected-incoming-cash")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetExpectedIncomingCashAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("expected_incoming_cash", snapshot.ExpectedIncomingCash, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("dashboard/expected-outgoing-cash")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetExpectedOutgoingCashAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("expected_outgoing_cash", snapshot.ExpectedOutgoingCash, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("dashboard/overdue-receivables")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetOverdueReceivablesAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("overdue_receivables", snapshot.OverdueReceivables, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("dashboard/upcoming-payables")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetUpcomingPayablesAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("upcoming_payables", snapshot.UpcomingPayables, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("seeding-state")]
    public async Task<ActionResult<FinanceSeedingStateResponse>> GetSeedingStateAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () =>
        {
            var result = await _financeSeedingStateService.GetCompanyFinanceSeedingStateAsync(companyId, cancellationToken);
            return new FinanceSeedingStateResponse
            {
                CompanyId = result.CompanyId,
                SeedingState = result.State.ToStorageValue(),
                DerivedFrom = result.DerivedFrom,
                CheckedAtUtc = result.CheckedAtUtc,
                Diagnostics = new FinanceSeedingStateDiagnosticsResponse
                {
                    PersistedState = result.Diagnostics.PersistedState?.ToStorageValue(),
                    MetadataState = result.Diagnostics.MetadataState?.ToStorageValue(),
                    MetadataPresent = result.Diagnostics.MetadataPresent,
                    MetadataIndicatesComplete = result.Diagnostics.MetadataIndicatesComplete,
                    UsedFastPath = result.Diagnostics.UsedFastPath,
                    Reason = result.Diagnostics.Reason
                }.WithRecordChecks(result.Diagnostics)
            };
        });

    [HttpGet("entry-state")]
    public async Task<ActionResult<FinanceEntryInitializationResponse>> GetEntryStateAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () =>
        {
            var result = await _financeEntryService.GetEntryStateAsync(
                new GetFinanceEntryStateQuery(companyId),
                cancellationToken);
            return InternalFinanceControllerMappings.MapFinanceEntryState(result);
        });

    [HttpPost("entry-state/request")]
    public async Task<ActionResult<FinanceEntryInitializationResponse>> RequestEntryStateAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(async () =>
        {
            var result = await _financeEntryService.RequestEntryStateAsync(
                new GetFinanceEntryStateQuery(companyId),
                cancellationToken);
            return InternalFinanceControllerMappings.MapFinanceEntryState(result);
        });

    [HttpPost("entry-state/retry")]
    public async Task<ActionResult<FinanceEntryInitializationResponse>> RetryEntryStateAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(async () =>
        {
            var result = await _financeEntryService.RequestEntryStateAsync(
                new GetFinanceEntryStateQuery(companyId, RetryOnFailure: true, Source: FinanceEntrySources.FinanceEntryRetry),
                cancellationToken);
            return InternalFinanceControllerMappings.MapFinanceEntryState(result);
        });

    [HttpPost("manual-seed")]
    public async Task<ActionResult<FinanceEntryInitializationResponse>> RequestManualSeedAsync(
        Guid companyId,
        [FromBody] FinanceManualSeedRequest? request,
        CancellationToken cancellationToken)
    {
        var normalizedMode = FinanceManualSeedModes.Normalize(request?.Mode);
        if (!FinanceManualSeedModes.IsSupported(normalizedMode))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(FinanceManualSeedRequest.Mode)] = ["Select a supported finance seed mode."]
            })
            {
                Title = "Finance validation failed",
                Detail = "Update the finance seed request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        if (!string.Equals(normalizedMode, FinanceManualSeedModes.Replace, StringComparison.OrdinalIgnoreCase))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(FinanceManualSeedRequest.Mode)] = ["Append mode is not available for manual finance seeding."]
            })
            {
                Title = "Finance validation failed",
                Detail = "Only replace mode is currently supported for manual finance seeding.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        return await ExecuteWriteAsync(async () =>
        {
            var result = await _financeEntryService.RequestEntryStateAsync(
                new GetFinanceEntryStateQuery(
                    companyId,
                    ForceSeed: true,
                    Source: FinanceEntrySources.ManualSeed,
                    SeedMode: normalizedMode,
                    ConfirmReplace: request?.ConfirmReplace ?? false),
                cancellationToken);
            return InternalFinanceControllerMappings.MapFinanceEntryState(result);
        });
    }

    [HttpGet("profit-and-loss/monthly")]
    public async Task<ActionResult<FinanceMonthlyProfitAndLossDto>> GetMonthlyProfitAndLossAsync(
        Guid companyId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetMonthlyProfitAndLossAsync(
                new GetFinanceMonthlyProfitAndLossQuery(companyId, year, month),
                cancellationToken));

    [HttpGet("reports/profit-loss")]
    public async Task<ActionResult<ProfitAndLossReportDto>> GetProfitAndLossReportAsync(
        Guid companyId,
        [FromQuery] Guid fiscalPeriodId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetProfitAndLossReportAsync(
                new GetFinanceProfitAndLossReportQuery(companyId, fiscalPeriodId),
                cancellationToken));

    [HttpGet("reports/balance-sheet")]
    public async Task<ActionResult<BalanceSheetReportDto>> GetBalanceSheetReportAsync(
        Guid companyId,
        [FromQuery] Guid fiscalPeriodId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetBalanceSheetReportAsync(
                new GetFinanceBalanceSheetReportQuery(companyId, fiscalPeriodId),
                cancellationToken));

    [HttpGet("reports/drilldown")]
    public async Task<ActionResult<FinancialStatementDrilldownDto>> GetFinancialStatementDrilldownAsync(
        Guid companyId,
        [FromQuery] Guid fiscalPeriodId,
        [FromQuery] string statementType,
        [FromQuery] string lineCode,
        [FromQuery] int? snapshotVersionNumber,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetFinancialStatementDrilldownAsync(
                new GetFinancialStatementDrilldownQuery(
                    companyId,
                    fiscalPeriodId,
                    FinancialStatementTypeValues.Parse(statementType),
                    lineCode,
                    snapshotVersionNumber),
                cancellationToken));

    [HttpGet("expense-breakdown")]
    public async Task<ActionResult<FinanceExpenseBreakdownDto>> GetExpenseBreakdownAsync(
        Guid companyId,
        [FromQuery] DateTime startUtc,
        [FromQuery] DateTime endUtc,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetExpenseBreakdownAsync(
                new GetFinanceExpenseBreakdownQuery(companyId, startUtc, endUtc),
                cancellationToken));

    [HttpGet("transactions")]
    public async Task<ActionResult<IReadOnlyList<FinanceTransactionDto>>> GetTransactionsAsync(
        Guid companyId,
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] string? category,
        [FromQuery] string? flagged,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetTransactionsAsync(
                new GetFinanceTransactionsQuery(companyId, startUtc, endUtc, limit, category, flagged),
                cancellationToken));

    [HttpGet("transactions/{transactionId:guid}")]
    public async Task<ActionResult<FinanceTransactionDetailDto>> GetTransactionDetailAsync(
        Guid companyId,
        Guid transactionId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financeReadService.GetTransactionDetailAsync(
                new GetFinanceTransactionDetailQuery(companyId, transactionId),
                cancellationToken),
            "Finance transaction was not found.");

    [HttpGet("payments")]
    public async Task<ActionResult<IReadOnlyList<FinancePaymentDto>>> GetPaymentsAsync(
        Guid companyId,
        [FromQuery(Name = "type")] string? paymentType,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financePaymentReadService.GetPaymentsAsync(
                new GetFinancePaymentsQuery(companyId, paymentType, limit),
                cancellationToken));

    [HttpGet("payments/{paymentId:guid}")]
    public async Task<ActionResult<FinancePaymentDto>> GetPaymentDetailAsync(
        Guid companyId,
        Guid paymentId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financePaymentReadService.GetPaymentDetailAsync(
                new GetFinancePaymentDetailQuery(companyId, paymentId),
                cancellationToken),
            "Finance payment was not found.");

    [HttpGet("payments/{paymentId:guid}/allocations")]
    public async Task<ActionResult<IReadOnlyList<FinancePaymentAllocationDto>>> GetPaymentAllocationsAsync(
        Guid companyId,
        Guid paymentId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financePaymentReadService.GetAllocationsByPaymentAsync(
                new GetFinancePaymentAllocationsByPaymentQuery(companyId, paymentId),
                cancellationToken),
            "Finance payment was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("payments")]
    public async Task<ActionResult<FinancePaymentDto>> CreatePaymentAsync(
        Guid companyId,
        [FromBody] CreateFinancePaymentRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financePaymentCommandService.CreatePaymentAsync(
                new CreateFinancePaymentCommand(companyId, request.ToDto()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("payments/{paymentId:guid}/allocations")]
    public async Task<ActionResult<FinancePaymentAllocationDto>> CreatePaymentAllocationAsync(
        Guid companyId,
        Guid paymentId,
        [FromBody] CreateFinancePaymentAllocationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financePaymentCommandService.CreateAllocationAsync(
                new CreateFinancePaymentAllocationCommand(companyId, request.ToDto(paymentId)),
                cancellationToken));

    [HttpGet("invoices")]
    public async Task<ActionResult<IReadOnlyList<FinanceInvoiceDto>>> GetInvoicesAsync(
        Guid companyId,
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetInvoicesAsync(
                new GetFinanceInvoicesQuery(companyId, startUtc, endUtc, limit),
                cancellationToken));

    [HttpGet("invoices/{invoiceId:guid}/allocations")]
    public async Task<ActionResult<IReadOnlyList<FinancePaymentAllocationDto>>> GetInvoiceAllocationsAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financePaymentReadService.GetAllocationsByInvoiceAsync(
                new GetFinanceInvoiceAllocationsQuery(companyId, invoiceId),
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
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedStatus = NormalizeReviewToken(status);
            var normalizedSupplier = NormalizeReviewText(supplier);
            var normalizedRiskLevel = NormalizeReviewToken(riskLevel);
            var normalizedOutcome = NormalizeReviewToken(recommendationOutcome);
            var normalizedLimit = NormalizeReviewLimit(limit);

            var invoices = await _financeReadService.GetInvoicesAsync(
                new GetFinanceInvoicesQuery(companyId, null, null, normalizedLimit),
                cancellationToken);

            var items = new List<FinanceInvoiceReviewListItemResponse>(invoices.Count);
            foreach (var invoice in invoices)
            {
                var review = await _invoiceReviewWorkflowService.GetLatestByInvoiceAsync(companyId, invoice.Id, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await BuildInvoiceReviewDetailResponseAsync(companyId, invoiceId, executeIfMissing: true, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _financeReadService.GetInvoiceDetailAsync(
                new GetFinanceInvoiceDetailQuery(companyId, invoiceId),
                cancellationToken);
            if (detail is null)
            {
                return NotFound(CreateProblemDetails("Finance invoice was not found.", "Finance record was not found.", StatusCodes.Status404NotFound));
            }

            var review = await _invoiceReviewWorkflowService.GetLatestByInvoiceAsync(companyId, invoiceId, cancellationToken);
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
                detail.AgentInsights));
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
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _invoiceReviewWorkflowService.ExecuteAsync(
                new ReviewFinanceInvoiceWorkflowCommand(
                    companyId,
                    invoiceId,
                    request?.WorkflowInstanceId,
                    request?.AgentId,
                    request?.Payload),
                cancellationToken));

    [HttpGet("anomalies")]
    public async Task<ActionResult<IReadOnlyList<FinanceSeedAnomalyDto>>> GetSeedAnomaliesAsync(
        Guid companyId,
        [FromQuery(Name = "type")] string? anomalyType,
        [FromQuery] Guid? affectedRecordId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetSeedAnomaliesAsync(
                new GetFinanceSeedAnomaliesQuery(
                    companyId, anomalyType, affectedRecordId, limit),
                cancellationToken));

    [HttpGet("anomalies/{anomalyId:guid}")]
    public async Task<ActionResult<FinanceSeedAnomalyDto>> GetSeedAnomalyAsync(
        Guid companyId,
        Guid anomalyId,
        CancellationToken cancellationToken)
    {
        var anomaly = await _financeReadService.GetSeedAnomalyByIdAsync(new GetFinanceSeedAnomalyByIdQuery(companyId, anomalyId), cancellationToken);
        return anomaly is null
            ? NotFound(CreateProblemDetails("Finance seed anomaly was not found.", "Finance record was not found.", StatusCodes.Status404NotFound))
            : Ok(anomaly);
    }

    [HttpGet("anomalies/workbench")]
    public async Task<ActionResult<FinanceAnomalyWorkbenchResultDto>> GetAnomalyWorkbenchAsync(
        Guid companyId,
        [FromQuery(Name = "type")] string? anomalyType,
        [FromQuery] string? status,
        [FromQuery] decimal? confidenceMin,
        [FromQuery] decimal? confidenceMax,
        [FromQuery] string? supplier,
        [FromQuery] DateTime? dateFromUtc,
        [FromQuery] DateTime? dateToUtc,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetAnomalyWorkbenchAsync(
                new GetFinanceAnomalyWorkbenchQuery(
                    companyId,
                    anomalyType,
                    status,
                    confidenceMin,
                    confidenceMax,
                    supplier,
                    dateFromUtc,
                    dateToUtc,
                    page,
                    pageSize),
                cancellationToken));

    [HttpGet("anomalies/workbench/{anomalyId:guid}")]
    public async Task<ActionResult<FinanceAnomalyDetailDto>> GetAnomalyDetailAsync(
        Guid companyId,
        Guid anomalyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financeReadService.GetAnomalyDetailAsync(new GetFinanceAnomalyDetailQuery(companyId, anomalyId), cancellationToken),
            "Finance anomaly was not found.");

    [HttpGet("bills")]
    public async Task<ActionResult<IReadOnlyList<FinanceBillDto>>> GetBillsAsync(
        Guid companyId,
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetBillsAsync(
                new GetFinanceBillsQuery(companyId, startUtc, endUtc, limit),
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

    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<FinanceCounterpartyDto>>> GetCustomersAsync(
        Guid companyId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetCounterpartiesAsync(
                new GetFinanceCounterpartiesQuery(companyId, "customer", Limit: limit),
                cancellationToken));

    [HttpGet("customers/{counterpartyId:guid}")]
    public async Task<ActionResult<FinanceCounterpartyDto>> GetCustomerAsync(
        Guid companyId,
        Guid counterpartyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financeReadService.GetCounterpartyAsync(
                new GetFinanceCounterpartyQuery(companyId, counterpartyId, "customer"),
                cancellationToken),
            "Finance customer was not found.");

    [HttpGet("suppliers")]
    public async Task<ActionResult<IReadOnlyList<FinanceCounterpartyDto>>> GetSuppliersAsync(
        Guid companyId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetCounterpartiesAsync(
                new GetFinanceCounterpartiesQuery(companyId, "supplier", Limit: limit),
                cancellationToken));

    [HttpGet("suppliers/{counterpartyId:guid}")]
    public async Task<ActionResult<FinanceCounterpartyDto>> GetSupplierAsync(
        Guid companyId,
        Guid counterpartyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financeReadService.GetCounterpartyAsync(
                new GetFinanceCounterpartyQuery(companyId, counterpartyId, "supplier"),
                cancellationToken),
            "Finance supplier was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("customers")]
    public async Task<ActionResult<FinanceCounterpartyDto>> CreateCustomerAsync(
        Guid companyId,
        [FromBody] UpsertFinanceCounterpartyRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.CreateCounterpartyAsync(
                new CreateFinanceCounterpartyCommand(companyId, "customer", request.ToDto()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPut("customers/{counterpartyId:guid}")]
    public async Task<ActionResult<FinanceCounterpartyDto>> UpdateCustomerAsync(
        Guid companyId,
        Guid counterpartyId,
        [FromBody] UpsertFinanceCounterpartyRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.UpdateCounterpartyAsync(
                new UpdateFinanceCounterpartyCommand(companyId, counterpartyId, "customer", request.ToDto()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("suppliers")]
    public async Task<ActionResult<FinanceCounterpartyDto>> CreateSupplierAsync(
        Guid companyId,
        [FromBody] UpsertFinanceCounterpartyRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.CreateCounterpartyAsync(
                new CreateFinanceCounterpartyCommand(companyId, "supplier", request.ToDto()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPut("suppliers/{counterpartyId:guid}")]
    public async Task<ActionResult<FinanceCounterpartyDto>> UpdateSupplierAsync(
        Guid companyId,
        Guid counterpartyId,
        [FromBody] UpsertFinanceCounterpartyRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.UpdateCounterpartyAsync(
                new UpdateFinanceCounterpartyCommand(companyId, counterpartyId, "supplier", request.ToDto()),
                cancellationToken));

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

    [HttpGet("policy-configuration")]
    public async Task<ActionResult<FinancePolicyConfigurationDto>> GetPolicyConfigurationAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financePolicyConfigurationService.GetPolicyConfigurationAsync(
                new GetFinancePolicyConfigurationQuery(companyId),
                cancellationToken));

    [HttpPut("policy-configuration")]
    public async Task<ActionResult<FinancePolicyConfigurationDto>> UpsertPolicyConfigurationAsync(
        Guid companyId,
        [FromBody] FinancePolicyConfigurationDto configuration,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financePolicyConfigurationService.UpsertPolicyConfigurationAsync(
                new UpsertFinancePolicyConfigurationCommand(companyId, configuration),
                cancellationToken));

    [HttpPost("bootstrap/seed")]
    public async Task<ActionResult<FinanceSeedBootstrapResultDto>> BootstrapSeedAsync(
        Guid companyId,
        [FromBody] BootstrapFinanceSeedRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeSeedBootstrapService.GenerateAsync(
                new FinanceSeedBootstrapCommand(
                    companyId,
                    request.SeedValue,
                    request.SeedAnchorUtc,
                    request.ReplaceExisting,
                    request.InjectAnomalies,
                    request.AnomalyScenarioProfile),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpPost("bootstrap/rerun")]
    public async Task<ActionResult<FinanceBootstrapRerunResultDto>> RerunBootstrapAsync(
        Guid companyId,
        [FromBody] RerunFinanceBootstrapRequest? request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request is not null && request.BatchSize <= 0)
        {
            errors[nameof(RerunFinanceBootstrapRequest.BatchSize)] = ["Batch size must be greater than zero."];
        }

        if (request is not null && !request.RerunPlanningBackfill && !request.RerunApprovalBackfill)
        {
            errors[nameof(RerunFinanceBootstrapRequest.RerunPlanningBackfill)] = ["Enable at least one bootstrap rerun operation."];
        }

        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors)
            {
                Title = "Finance validation failed",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        return await ExecuteWriteAsync(() => _financeBootstrapRerunService.RerunAsync(new RerunFinanceBootstrapCommand(companyId, request?.RerunPlanningBackfill ?? true, request?.RerunApprovalBackfill ?? true, request?.BatchSize ?? 250, request?.CorrelationId), cancellationToken));
    }

    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpPost("insights/refresh")]
    public async Task<ActionResult<FinanceInsightsSnapshotRefreshResultDto>> RefreshInsightsSnapshotAsync(
        Guid companyId,
        [FromBody] RefreshFinanceInsightsSnapshotRequest? request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () =>
            {
                var snapshotKey = FinanceInsightSnapshotKeys.Normalize(request?.SnapshotKey);
                if (request?.RunInBackground == true)
                {
                    return _financeReadService.QueueInsightsSnapshotRefreshAsync(
                        new QueueFinanceInsightsSnapshotRefreshCommand(
                            companyId,
                            request.AsOfUtc,
                            request.ExpenseWindowDays,
                            request.TrendWindowDays,
                            request.PayableWindowDays,
                            snapshotKey,
                            request.RetentionMinutes,
                            request.ResetAttempts,
                            request.CorrelationId),
                        cancellationToken);
                }

                return _financeReadService.RefreshInsightsSnapshotAsync(
                    new RefreshFinanceInsightsSnapshotCommand(
                        companyId,
                        request?.AsOfUtc,
                        request?.ExpenseWindowDays ?? 90,
                        request?.TrendWindowDays ?? 30,
                        request?.PayableWindowDays ?? 14,
                        snapshotKey,
                        TimeSpan.FromMinutes(request?.RetentionMinutes ?? 360)),
                    cancellationToken);
            });

    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpPost("sandbox-admin/seed-generation")]
    public async Task<ActionResult<FinanceSandboxSeedGenerationResponse>> GenerateSandboxSeedDatasetAsync(
        Guid companyId,
        [FromBody] FinanceSandboxSeedGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateSandboxSeedGenerationRequest(companyId, request);
        if (validationErrors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors)
            {
                Title = "Finance validation failed",
                Detail = "Update the seed generation request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        var normalizedMode = FinanceSandboxSeedGenerationModes.Normalize(request.GenerationMode);
        var command = normalizedMode switch
        {
            FinanceSandboxSeedGenerationModes.Refresh => new FinanceSeedBootstrapCommand(
                companyId,
                request.SeedValue,
                request.AnchorDateUtc,
                ReplaceExisting: true,
                InjectAnomalies: false),
            FinanceSandboxSeedGenerationModes.RefreshWithAnomalies => new FinanceSeedBootstrapCommand(
                companyId,
                request.SeedValue,
                request.AnchorDateUtc,
                ReplaceExisting: true,
                InjectAnomalies: true,
                AnomalyScenarioProfile: "baseline"),
            _ => throw new InvalidOperationException("Unsupported sandbox seed generation mode.")
        };

        return await ExecuteWriteAsync(async () => BuildSandboxSeedGenerationResponse(await _financeSeedBootstrapService.GenerateAsync(command, cancellationToken), normalizedMode));
    }

    [HttpGet("simulation/clock")]
    public async Task<ActionResult<CompanySimulationClockDto>> GetSimulationClockAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _companySimulationService.GetClockAsync(
                new GetCompanySimulationClockQuery(companyId),
                cancellationToken));

    [HttpPost("simulation/advance")]
    public Task<ActionResult<AdvanceCompanySimulationTimeResultDto>> AdvanceSimulationAsync(
        Guid companyId,
        [FromBody] AdvanceCompanySimulationTimeRequest request,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(
            () => _companySimulationService.AdvanceAsync(
                new AdvanceCompanySimulationTimeCommand(
                    companyId,
                    request.TotalHours,
                    request.ExecutionStepHours,
                    request.Accelerated),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/dataset-generation")]
    public Task<ActionResult<FinanceSandboxDatasetGenerationResponse>> GetSandboxDatasetGenerationAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxDatasetGenerationAsync(companyId, cancellationToken),
            "Finance sandbox dataset generation data was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/anomaly-injection")]
    public Task<ActionResult<FinanceSandboxAnomalyInjectionResponse>> GetSandboxAnomalyInjectionAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxAnomalyInjectionAsync(companyId, cancellationToken),
            "Finance sandbox anomaly injection data was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/anomaly-injection/{anomalyId:guid}")]
    public Task<ActionResult<FinanceSandboxAnomalyDetailResponse>> GetSandboxAnomalyDetailAsync(
        Guid companyId,
        Guid anomalyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxAnomalyDetailAsync(companyId, anomalyId, cancellationToken),
            "Finance sandbox anomaly detail was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpPost("sandbox-admin/anomaly-injection")]
    public async Task<ActionResult<FinanceSandboxAnomalyDetailResponse>> InjectSandboxAnomalyAsync(
        Guid companyId,
        [FromBody] FinanceSandboxAnomalyInjectionRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateSandboxAnomalyInjectionRequest(companyId, request);
        if (validationErrors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors)
            {
                Title = "Finance validation failed",
                Detail = "Update the anomaly injection request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        var profile = SandboxScenarioProfiles.First(x => string.Equals(x.Code, request.ScenarioProfileCode.Trim(), StringComparison.OrdinalIgnoreCase));
        var affectedRecord = await ResolveSandboxFinanceRecordAsync(companyId, [], cancellationToken);
        if (affectedRecord is null)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [nameof(FinanceSandboxAnomalyInjectionRequest.ScenarioProfileCode)] = ["No finance records are available yet for anomaly injection. Generate a sandbox dataset first."] })
            {
                Title = "Finance validation failed",
                Detail = "Generate sandbox data before injecting anomalies.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        var anomaly = new FinanceSeedAnomaly(Guid.NewGuid(), companyId, MapScenarioProfileToAnomalyType(profile.Code), profile.Code, [affectedRecord.RecordId], BuildExpectedDetectionMetadataJson(profile, affectedRecord));
        _dbContext.FinanceSeedAnomalies.Add(anomaly);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await BuildSandboxAnomalyDetailAsync(companyId, anomaly.Id, cancellationToken));
    }

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/simulation-controls")]
    public Task<ActionResult<FinanceSandboxSimulationControlsResponse>> GetSandboxSimulationControlsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxSimulationControlsAsync(companyId, cancellationToken),
            "Finance sandbox simulation controls were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpPost("sandbox-admin/simulation-controls/advance")]
    public async Task<ActionResult<FinanceSandboxProgressionRunSummaryResponse>> AdvanceSandboxSimulationAsync(
        Guid companyId,
        [FromBody] FinanceSandboxSimulationAdvanceRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateSandboxSimulationAdvanceRequest(companyId, request);
        if (validationErrors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors)
            {
                Title = "Finance validation failed",
                Detail = "Update the simulation control request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        return await ExecuteWriteAsync(async () =>
        {
            var result = await _companySimulationService.AdvanceAsync(
                new AdvanceCompanySimulationTimeCommand(
                    companyId,
                    request.IncrementHours,
                    request.ExecutionStepHours,
                    request.Accelerated),
                cancellationToken);

            return BuildSandboxProgressionRunSummary("advance", result);
        });
    }

    private static readonly FinanceSandboxAnomalyScenarioProfileResponse[] SandboxScenarioProfiles =
    [
        new() { Code = "baseline", Name = "Baseline threshold breach", Description = "Registers a threshold-breach anomaly against an existing sandbox record." },
        new() { Code = "missing_receipt", Name = "Missing receipt", Description = "Registers a missing-receipt scenario for finance validation coverage." },
        new() { Code = "duplicate_vendor_charge", Name = "Duplicate vendor charge", Description = "Registers a duplicate-charge anomaly for accounts payable review flows." },
        new() { Code = "historical_baseline_deviation", Name = "Historical baseline deviation", Description = "Registers a historical-drift scenario against a representative sandbox record." }
    ];

    private sealed record SandboxFinanceRecordCandidate(
        Guid RecordId,
        string RecordType,
        string Reference);

    private static Dictionary<string, string[]> ValidateSandboxAnomalyInjectionRequest(Guid companyId, FinanceSandboxAnomalyInjectionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.CompanyId != companyId) errors[nameof(FinanceSandboxAnomalyInjectionRequest.CompanyId)] = ["The request company does not match the active company context."];
        if (string.IsNullOrWhiteSpace(request.ScenarioProfileCode)) errors[nameof(FinanceSandboxAnomalyInjectionRequest.ScenarioProfileCode)] = ["Select a scenario profile."];
        else if (!SandboxScenarioProfiles.Any(x => string.Equals(x.Code, request.ScenarioProfileCode.Trim(), StringComparison.OrdinalIgnoreCase))) errors[nameof(FinanceSandboxAnomalyInjectionRequest.ScenarioProfileCode)] = ["Select a supported scenario profile."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateSandboxSimulationAdvanceRequest(Guid companyId, FinanceSandboxSimulationAdvanceRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.CompanyId != companyId) errors[nameof(FinanceSandboxSimulationAdvanceRequest.CompanyId)] = ["The request company does not match the active company context."];
        if (request.IncrementHours <= 0) errors[nameof(FinanceSandboxSimulationAdvanceRequest.IncrementHours)] = ["Enter a positive hour increment."];
        if (request.ExecutionStepHours is <= 0) errors[nameof(FinanceSandboxSimulationAdvanceRequest.ExecutionStepHours)] = ["Enter a positive execution step size."];
        return errors;
    }

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpPost("sandbox-admin/simulation-controls/progression-run")]
    public async Task<ActionResult<FinanceSandboxProgressionRunSummaryResponse>> StartSandboxProgressionRunAsync(
        Guid companyId,
        [FromBody] FinanceSandboxSimulationAdvanceRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateSandboxSimulationAdvanceRequest(companyId, request);
        if (validationErrors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors)
            {
                Title = "Finance validation failed",
                Detail = "Update the simulation control request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        return await ExecuteWriteAsync(async () =>
        {
            var result = await _companySimulationService.AdvanceAsync(
                new AdvanceCompanySimulationTimeCommand(
                    companyId,
                    request.IncrementHours,
                    request.ExecutionStepHours,
                    request.Accelerated),
                cancellationToken);
            return BuildSandboxProgressionRunSummary("progression_run", result);
        });
    }

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/tool-execution-visibility")]
    public Task<ActionResult<FinanceSandboxToolExecutionVisibilityResponse>> GetSandboxToolExecutionVisibilityAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxToolExecutionVisibilityAsync(companyId, cancellationToken),
            "Finance sandbox tool execution visibility was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/domain-events")]
    public Task<ActionResult<FinanceSandboxDomainEventsResponse>> GetSandboxDomainEventsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxDomainEventsAsync(companyId, cancellationToken),
            "Finance sandbox domain events were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/tool-manifests")]
    public Task<ActionResult<FinanceTransparencyToolManifestListResponse>> GetTransparencyToolManifestsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyToolManifestsAsync(companyId, cancellationToken),
            "Finance transparency tool manifests were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/tool-executions")]
    public Task<ActionResult<FinanceTransparencyToolExecutionHistoryResponse>> GetTransparencyToolExecutionsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyToolExecutionsAsync(companyId, cancellationToken),
            "Finance transparency tool executions were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/tool-executions/{executionId:guid}")]
    public Task<ActionResult<FinanceTransparencyToolExecutionDetailResponse>> GetTransparencyToolExecutionDetailAsync(
        Guid companyId,
        Guid executionId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyToolExecutionDetailAsync(companyId, executionId, cancellationToken),
            "Finance transparency tool execution detail was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/events")]
    public Task<ActionResult<FinanceTransparencyEventStreamResponse>> GetTransparencyEventsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyEventsAsync(companyId, cancellationToken),
            "Finance transparency events were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/events/{eventId:guid}")]
    public Task<ActionResult<FinanceTransparencyEventDetailResponse>> GetTransparencyEventDetailAsync(
        Guid companyId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyEventDetailAsync(companyId, eventId, cancellationToken),
            "Finance transparency event detail was not found.");

    private static FinanceSandboxSeedGenerationResponse BuildSandboxSeedGenerationResponse(
        FinanceSeedBootstrapResultDto result,
        string generationMode)
    {
        var referentialIntegrityErrors = result.ValidationErrors
            .Where(x => IsReferentialIntegrityCode(x.Code))
            .Select(MapSeedGenerationIssue)
            .ToArray();
        var validationErrors = result.ValidationErrors
            .Where(x => !IsReferentialIntegrityCode(x.Code))
            .Select(MapSeedGenerationIssue)
            .ToArray();
        var warnings = result.Anomalies
            .Select(MapSeedGenerationWarning)
            .ToArray();
        var succeeded = result.ValidationErrors.Count == 0;

        return new FinanceSandboxSeedGenerationResponse
        {
            CompanyId = result.CompanyId,
            SeedValue = result.SeedValue,
            AnchorDateUtc = result.WindowEndUtc,
            GenerationMode = generationMode,
            Succeeded = succeeded,
            CreatedCount = succeeded ? CountCreatedSeedRecords(result) : 0,
            UpdatedCount = 0,
            Message = succeeded
                ? "Seed dataset generated successfully. Review the summary and validation results below."
                : "Seed dataset generation returned validation issues. Resolve the reported problems and retry the request.",
            Errors = validationErrors,
            Warnings = warnings,
            ReferentialIntegrityErrors = referentialIntegrityErrors
        };
    }

    private static Dictionary<string, string[]> ValidateSandboxSeedGenerationRequest(
        Guid companyId,
        FinanceSandboxSeedGenerationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (request.CompanyId == Guid.Empty)
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.CompanyId)] = ["Select a company before generating a seed dataset."];
        }
        else if (request.CompanyId != companyId)
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.CompanyId)] = ["The selected company does not match the active company context."];
        }

        if (request.SeedValue <= 0)
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.SeedValue)] = ["Enter a positive seed value."];
        }

        if (request.AnchorDateUtc == default)
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.AnchorDateUtc)] = ["Select an anchor date for the generated dataset."];
        }

        if (!FinanceSandboxSeedGenerationModes.IsSupported(request.GenerationMode))
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.GenerationMode)] = ["Select a supported generation mode."];
        }

        return errors;
    }

    private static int CountCreatedSeedRecords(FinanceSeedBootstrapResultDto result) =>
        result.AccountCount +
        result.CounterpartyCount +
        result.InvoiceCount +
        result.BillCount +
        result.RecurringExpenseCount +
        result.TransactionCount +
        result.BalanceCount +
        result.PaymentCount +
        result.DocumentCount +
        result.Anomalies.Count +
        1;

    private static FinanceSandboxSeedGenerationIssueResponse MapSeedGenerationIssue(FinanceSeedValidationErrorDto error) =>
        new()
        {
            Code = error.Code,
            Message = error.Message
        };

    private static FinanceSandboxSeedGenerationIssueResponse MapSeedGenerationWarning(FinanceSeedAnomalyDto anomaly) =>
        new()
        {
            Code = $"anomaly.{anomaly.AnomalyType}",
            Message = $"Injected validation scenario '{HumanizeReviewToken(anomaly.AnomalyType)}' affecting {anomaly.AffectedRecordIds.Count} record(s)."
        };

    private async Task<FinanceSandboxDatasetGenerationResponse?> BuildSandboxDatasetGenerationAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var companyExists = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return null;
        }

        var transactionCount = await _dbContext.FinanceTransactions
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);
        var invoiceCount = await _dbContext.FinanceInvoices
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);
        var billCount = await _dbContext.FinanceBills
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);
        var balanceCount = await _dbContext.FinanceBalances
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);
        var anomalyCount = await _dbContext.FinanceSeedAnomalies
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);

        var lastGeneratedUtc = await _dbContext.AuditEvents
            .Where(x => x.CompanyId == companyId && x.Action == "finance.sandbox.dataset.generated")
            .OrderByDescending(x => x.OccurredUtc)
            .Select(x => (DateTime?)x.OccurredUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.FinanceTransactions
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => (DateTime?)x.CreatedUtc)
                .FirstOrDefaultAsync(cancellationToken)
            ?? DateTime.UtcNow;

        return new FinanceSandboxDatasetGenerationResponse
        {
            ProfileName = "Tenant sandbox dataset",
            LastGeneratedUtc = lastGeneratedUtc,
            CoverageSummary = $"{transactionCount} transactions, {invoiceCount} invoices, {billCount} bills, {balanceCount} balances, and {anomalyCount} anomalies are available for sandbox validation.",
            AvailableProfiles =
            [
                "Tenant sandbox dataset",
                "Tenant sandbox dataset with anomaly validation"
            ]
        };
    }

    private async Task<FinanceSandboxAnomalyInjectionResponse?> BuildSandboxAnomalyInjectionAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var companyExists = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return null;
        }

        var anomalies = await _dbContext.FinanceSeedAnomalies
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        var registryEntries = new List<FinanceSandboxAnomalyRegistryItemResponse>(anomalies.Count);
        foreach (var anomaly in anomalies)
        {
            registryEntries.Add(await BuildSandboxAnomalyRegistryItemAsync(anomaly, cancellationToken));
        }

        var lastInjectedUtc = anomalies.FirstOrDefault()?.CreatedUtc
            ?? await _dbContext.AuditEvents
                .Where(x => x.CompanyId == companyId && x.Action == "finance.sandbox.dataset.generated")
                .OrderByDescending(x => x.OccurredUtc)
                .Select(x => (DateTime?)x.OccurredUtc)
                .FirstOrDefaultAsync(cancellationToken)
            ?? DateTime.UtcNow;

        return new FinanceSandboxAnomalyInjectionResponse
        {
            Mode = "Seed anomaly scenarios",
            LastInjectedUtc = lastInjectedUtc,
            Observation = anomalies.Count == 0
                ? "No anomaly injections have been registered yet. Inject a scenario profile to populate the registry."
                : $"Tracking {anomalies.Count} sandbox anomaly registration(s) for the active company.",
            ActiveScenarios = anomalies
                .Select(x => ResolveSandboxScenarioProfile(x.ScenarioProfile).Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AvailableScenarioProfiles = SandboxScenarioProfiles,
            RegistryEntries = registryEntries
        };
    }

    private async Task<FinanceSandboxAnomalyDetailResponse?> BuildSandboxAnomalyDetailAsync(
        Guid companyId,
        Guid anomalyId,
        CancellationToken cancellationToken)
    {
        var anomaly = await _dbContext.FinanceSeedAnomalies
            .Where(x => x.CompanyId == companyId && x.Id == anomalyId)
            .SingleOrDefaultAsync(cancellationToken);

        return anomaly is null
            ? null
            : await BuildSandboxAnomalyDetailAsync(anomaly, cancellationToken);
    }

    private async Task<FinanceSandboxAnomalyRegistryItemResponse> BuildSandboxAnomalyRegistryItemAsync(
        FinanceSeedAnomaly anomaly,
        CancellationToken cancellationToken)
    {
        var profile = ResolveSandboxScenarioProfile(anomaly.ScenarioProfile);
        var affectedRecord = await ResolveSandboxFinanceRecordAsync(anomaly.CompanyId, anomaly.GetAffectedRecordIds(), cancellationToken);

        return new FinanceSandboxAnomalyRegistryItemResponse
        {
            Id = anomaly.Id,
            Type = anomaly.AnomalyType,
            Status = "registered",
            ScenarioProfileCode = profile.Code,
            ScenarioProfileName = profile.Name,
            AffectedRecordType = affectedRecord?.RecordType ?? "unknown",
            AffectedRecordId = affectedRecord?.RecordId,
            AffectedRecordReference = affectedRecord?.Reference ?? "Unavailable",
            CreatedUtc = anomaly.CreatedUtc,
            Messages = BuildSandboxAnomalyMessages(profile, affectedRecord)
        };
    }

    private async Task<FinanceSandboxAnomalyDetailResponse> BuildSandboxAnomalyDetailAsync(
        FinanceSeedAnomaly anomaly,
        CancellationToken cancellationToken)
    {
        var profile = ResolveSandboxScenarioProfile(anomaly.ScenarioProfile);
        var affectedRecord = await ResolveSandboxFinanceRecordAsync(anomaly.CompanyId, anomaly.GetAffectedRecordIds(), cancellationToken);

        return new FinanceSandboxAnomalyDetailResponse
        {
            Id = anomaly.Id,
            Type = anomaly.AnomalyType,
            Status = "registered",
            ScenarioProfileCode = profile.Code,
            ScenarioProfileName = profile.Name,
            AffectedRecordType = affectedRecord?.RecordType ?? "unknown",
            AffectedRecordId = affectedRecord?.RecordId,
            AffectedRecordReference = affectedRecord?.Reference ?? "Unavailable",
            CreatedUtc = anomaly.CreatedUtc,
            ExpectedDetectionMetadataJson = anomaly.ExpectedDetectionMetadataJson,
            Messages = BuildSandboxAnomalyMessages(profile, affectedRecord)
        };
    }

    private static FinanceSandboxAnomalyScenarioProfileResponse ResolveSandboxScenarioProfile(string? code) =>
        SandboxScenarioProfiles.FirstOrDefault(x => string.Equals(x.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? new FinanceSandboxAnomalyScenarioProfileResponse
        {
            Code = NormalizeReviewToken(code) ?? "custom",
            Name = HumanizeReviewToken(code),
            Description = "Registers a sandbox anomaly scenario against an existing finance record."
        };

    private static IReadOnlyList<FinanceSandboxBackendMessageResponse> BuildSandboxAnomalyMessages(
        FinanceSandboxAnomalyScenarioProfileResponse profile,
        SandboxFinanceRecordCandidate? affectedRecord)
    {
        var messages = new List<FinanceSandboxBackendMessageResponse>
        {
            new()
            {
                Severity = "info",
                Code = "sandbox.anomaly.registered",
                Message = $"Scenario '{profile.Name}' is registered for sandbox review."
            }
        };

        if (affectedRecord is null)
        {
            messages.Add(new FinanceSandboxBackendMessageResponse
            {
                Severity = "warning",
                Code = "sandbox.anomaly.record_unresolved",
                Message = "The related finance record could not be resolved from the sandbox registry."
            });
        }

        return messages;
    }

    private async Task<FinanceSandboxSimulationControlsResponse?> BuildSandboxSimulationControlsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var companyExists = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return null;
        }

        var clock = await _companySimulationService.GetClockAsync(
            new GetCompanySimulationClockQuery(companyId),
            cancellationToken);
        var runHistory = await BuildSandboxRunHistoryAsync(companyId, cancellationToken);
        var currentRun = runHistory.FirstOrDefault();

        return new FinanceSandboxSimulationControlsResponse
        {
            ClockMode = clock.Enabled ? "Simulated clock enabled" : "Live clock fallback",
            ReferenceUtc = clock.CurrentUtc,
            CheckpointLabel = currentRun is null
                ? "No simulation run has been recorded for the current sandbox."
                : $"{HumanizeReviewToken(currentRun.RunType)} completed {currentRun.AdvancedHours}h with {currentRun.Steps.Count} step(s).",
            Observation = currentRun is null
                ? "Advance simulation time or start a progression run to populate backend status and history."
                : currentRun.Messages.FirstOrDefault()?.Message
                    ?? "The latest simulation run completed without backend messages.",
            CurrentRun = currentRun,
            RunHistory = runHistory
        };
    }

    private async Task<IReadOnlyList<FinanceSandboxProgressionRunSummaryResponse>> BuildSandboxRunHistoryAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.FinanceSimulationStepLogs
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new SandboxSimulationRunStepRow(
                x.RunId,
                x.StepNumber,
                x.WindowStartUtc,
                x.WindowEndUtc,
                x.ExecutionStepHours,
                x.TotalHoursProcessed,
                x.IsAccelerated,
                x.TransactionsGenerated,
                x.InvoicesGenerated,
                x.BillsGenerated,
                x.RecurringExpenseInstancesGenerated,
                x.EventsEmitted,
                x.CreatedUtc))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.RunId)
            .OrderByDescending(group => group.Max(step => step.CreatedUtc))
            .Take(10)
            .Select(BuildSandboxProgressionRunSummary)
            .ToArray();
    }

    private static FinanceSandboxProgressionRunSummaryResponse BuildSandboxProgressionRunSummary(IGrouping<Guid, SandboxSimulationRunStepRow> run)
    {
        var orderedSteps = run.OrderBy(step => step.StepNumber).ToArray();
        var firstStep = orderedSteps[0];
        var lastStep = orderedSteps[^1];
        var transactionsGenerated = orderedSteps.Sum(step => step.TransactionsGenerated);
        var invoicesGenerated = orderedSteps.Sum(step => step.InvoicesGenerated);
        var billsGenerated = orderedSteps.Sum(step => step.BillsGenerated);
        var recurringExpenseInstancesGenerated = orderedSteps.Sum(step => step.RecurringExpenseInstancesGenerated);
        var eventsEmitted = orderedSteps.Sum(step => step.EventsEmitted);
        var generatedRecordCount = transactionsGenerated + invoicesGenerated + billsGenerated + recurringExpenseInstancesGenerated;

        return new FinanceSandboxProgressionRunSummaryResponse
        {
            RunType = firstStep.IsAccelerated ? "progression_run" : "advance",
            Status = "completed",
            StartedUtc = firstStep.WindowStartUtc,
            CompletedUtc = lastStep.WindowEndUtc,
            AdvancedHours = firstStep.TotalHoursProcessed,
            ExecutionStepHours = firstStep.ExecutionStepHours,
            TransactionsGenerated = transactionsGenerated,
            InvoicesGenerated = invoicesGenerated,
            BillsGenerated = billsGenerated,
            RecurringExpenseInstancesGenerated = recurringExpenseInstancesGenerated,
            EventsEmitted = eventsEmitted,
            Messages = generatedRecordCount == 0
                ? [new FinanceSandboxBackendMessageResponse { Severity = "warning", Code = "sandbox.progression.no_output", Message = "The simulation run completed without generating new finance records." }]
                : [new FinanceSandboxBackendMessageResponse { Severity = "info", Code = "sandbox.progression.completed", Message = $"The simulation run completed and generated {generatedRecordCount} finance record(s)." }],
            Steps = orderedSteps.Select(step => new FinanceSandboxProgressionRunStepResponse
            {
                WindowStartUtc = step.WindowStartUtc,
                WindowEndUtc = step.WindowEndUtc,
                TransactionsGenerated = step.TransactionsGenerated,
                InvoicesGenerated = step.InvoicesGenerated,
                BillsGenerated = step.BillsGenerated,
                RecurringExpenseInstancesGenerated = step.RecurringExpenseInstancesGenerated,
                EventsEmitted = step.EventsEmitted
            }).ToArray()
        };
    }

    private sealed record SandboxSimulationRunStepRow(
        Guid RunId,
        int StepNumber,
        DateTime WindowStartUtc,
        DateTime WindowEndUtc,
        int ExecutionStepHours,
        int TotalHoursProcessed,
        bool IsAccelerated,
        int TransactionsGenerated,
        int InvoicesGenerated,
        int BillsGenerated,
        int RecurringExpenseInstancesGenerated,
        int EventsEmitted,
        DateTime CreatedUtc);

    private async Task<SandboxFinanceRecordCandidate?> ResolveSandboxFinanceRecordAsync(
        Guid companyId,
        IReadOnlyList<Guid> preferredIds,
        CancellationToken cancellationToken)
    {
        foreach (var recordId in preferredIds.Where(x => x != Guid.Empty))
        {
            var transaction = await _dbContext.FinanceTransactions
                .Where(x => x.CompanyId == companyId && x.Id == recordId)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "transaction", string.IsNullOrWhiteSpace(x.ExternalReference) ? $"Transaction {x.Id:D}" : x.ExternalReference))
                .FirstOrDefaultAsync(cancellationToken);
            if (transaction is not null) return transaction;

            var invoice = await _dbContext.FinanceInvoices
                .Where(x => x.CompanyId == companyId && x.Id == recordId)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "invoice", x.InvoiceNumber))
                .FirstOrDefaultAsync(cancellationToken);
            if (invoice is not null) return invoice;

            var bill = await _dbContext.FinanceBills
                .Where(x => x.CompanyId == companyId && x.Id == recordId)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "bill", x.BillNumber))
                .FirstOrDefaultAsync(cancellationToken);
            if (bill is not null) return bill;
        }

        return await _dbContext.FinanceTransactions
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new SandboxFinanceRecordCandidate(x.Id, "transaction", string.IsNullOrWhiteSpace(x.ExternalReference) ? $"Transaction {x.Id:D}" : x.ExternalReference))
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.FinanceInvoices
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "invoice", x.InvoiceNumber))
                .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.FinanceBills
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "bill", x.BillNumber))
                .FirstOrDefaultAsync(cancellationToken);
    }

    private static string MapScenarioProfileToAnomalyType(string code) =>
        code.Trim().ToLowerInvariant() switch
        {
            "missing_receipt" => "missing_receipt",
            "duplicate_vendor_charge" => "duplicate_vendor_charge",
            "historical_baseline_deviation" => "historical_baseline_deviation",
            _ => "threshold_breach"
        };

    private static string BuildExpectedDetectionMetadataJson(FinanceSandboxAnomalyScenarioProfileResponse profile, SandboxFinanceRecordCandidate affectedRecord) =>
        new JsonObject
        {
            ["scenarioProfileCode"] = profile.Code,
            ["scenarioProfileName"] = profile.Name,
            ["affectedRecordType"] = affectedRecord.RecordType,
            ["affectedRecordReference"] = affectedRecord.Reference
        }.ToJsonString();

    private static FinanceSandboxProgressionRunSummaryResponse BuildSandboxProgressionRunSummary(string runType, AdvanceCompanySimulationTimeResultDto result)
    {
        var generatedRecordCount = result.TransactionsGenerated + result.InvoicesGenerated + result.BillsGenerated + result.RecurringExpenseInstancesGenerated;
        return new FinanceSandboxProgressionRunSummaryResponse
        {
            RunType = runType,
            Status = "completed",
            StartedUtc = result.PreviousUtc,
            CompletedUtc = result.CurrentUtc,
            AdvancedHours = result.TotalHoursProcessed,
            ExecutionStepHours = result.ExecutionStepHours,
            TransactionsGenerated = result.TransactionsGenerated,
            InvoicesGenerated = result.InvoicesGenerated,
            BillsGenerated = result.BillsGenerated,
            RecurringExpenseInstancesGenerated = result.RecurringExpenseInstancesGenerated,
            EventsEmitted = result.EventsEmitted,
            Messages = generatedRecordCount == 0
                ? [new FinanceSandboxBackendMessageResponse { Severity = "warning", Code = "sandbox.progression.no_output", Message = "The progression run completed without generating new finance records." }]
                : [new FinanceSandboxBackendMessageResponse { Severity = "info", Code = "sandbox.progression.completed", Message = $"The progression run completed and generated {generatedRecordCount} finance record(s)." }],
            Steps = result.Logs.Select(log => new FinanceSandboxProgressionRunStepResponse
            {
                WindowStartUtc = log.WindowStartUtc,
                WindowEndUtc = log.WindowEndUtc,
                TransactionsGenerated = log.TransactionsGenerated,
                InvoicesGenerated = log.InvoicesGenerated,
                BillsGenerated = log.BillsGenerated,
                RecurringExpenseInstancesGenerated = log.RecurringExpenseInstancesGenerated,
                EventsEmitted = log.EventsEmitted
            }).ToArray()
        };
    }

    private async Task<FinanceSandboxToolExecutionVisibilityResponse?> BuildSandboxToolExecutionVisibilityAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var attempts = await _dbContext.ToolExecutionAttempts
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CompletedUtc ?? x.StartedUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (attempts.Count == 0)
        {
            return null;
        }

        return new FinanceSandboxToolExecutionVisibilityResponse
        {
            Summary = $"Observed {attempts.Count} recent sandbox tool execution(s) for the active company.",
            Items = attempts
                .Select(x => new FinanceSandboxToolExecutionItemResponse
                {
                    Name = x.ToolName,
                    Visibility = x.ResultPayload.Count > 0 || x.PolicyDecision.Count > 0
                        ? "Visible in admin timeline"
                        : "Visible with minimal telemetry",
                    LastStatus = x.Status.ToString()
                })
                .ToArray()
        };
    }

    private async Task<FinanceSandboxDomainEventsResponse?> BuildSandboxDomainEventsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var events = await _dbContext.AuditEvents
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (events.Count == 0)
        {
            return null;
        }

        return new FinanceSandboxDomainEventsResponse
        {
            Summary = $"Showing the {events.Count} most recent sandbox-relevant audit event(s) for the active company.",
            Items = events
                .Select(x => new FinanceSandboxDomainEventItemResponse
                {
                    EventType = x.Action,
                    Status = x.Outcome,
                    OccurredAtUtc = x.OccurredUtc
                })
                .ToArray()
        };
    }

    private Task<FinanceTransparencyToolManifestListResponse?> BuildFinanceTransparencyToolManifestsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var financeRegistrations = _toolRegistry.ListTools()
            .Where(registration => registration.Scopes.Contains("finance"))
            .ToDictionary(registration => registration.ToolName, StringComparer.OrdinalIgnoreCase);

        var definitions = _toolRegistry.ListToolDefinitions()
            .Where(definition => financeRegistrations.ContainsKey(definition.ToolName))
            .OrderBy(definition => definition.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (definitions.Length == 0)
        {
            return Task.FromResult<FinanceTransparencyToolManifestListResponse?>(null);
        }

        var providerAdapterIdentity = _financeToolProvider.GetType().Name;
        return Task.FromResult<FinanceTransparencyToolManifestListResponse?>(new FinanceTransparencyToolManifestListResponse
        {
            Summary = $"Registered {definitions.Length} finance tool manifest(s) for provider adapter {providerAdapterIdentity}.",
            Items = definitions
                .Select(definition =>
                {
                    var contractSummary = BuildContractSummary(definition.InputSchema, definition.OutputSchema);
                    var schemaSummary = BuildSchemaSummary(definition.InputSchema, definition.OutputSchema);
                    return new FinanceTransparencyToolManifestItemResponse
                    {
                        ToolName = definition.ToolName,
                        Version = definition.Version,
                        VersionMetadata = BuildManifestVersionMetadata(definition.Version),
                        ContractSummary = contractSummary,
                        SchemaSummary = schemaSummary,
                        ManifestSource = "runtime_registry",
                        ProviderAdapterId = providerAdapterIdentity,
                        ProviderAdapterName = providerAdapterIdentity,
                        ProviderAdapterIdentity = providerAdapterIdentity
                    };
                })
                .ToArray()
        });
    }

    private async Task<FinanceTransparencyToolExecutionHistoryResponse?> BuildFinanceTransparencyToolExecutionsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var financeToolNames = BuildFinanceToolNameSet();
        var attempts = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.ExecutedUtc ?? x.CompletedUtc ?? x.StartedUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var items = attempts
            .Where(attempt => IsFinanceToolExecution(attempt, financeToolNames))
            .Take(25)
            .Select(BuildToolExecutionListItem)
            .ToArray();

        if (items.Length == 0)
        {
            return null;
        }

        return new FinanceTransparencyToolExecutionHistoryResponse
        {
            Summary = $"Showing {items.Length} recent finance tool execution(s) for the active company.",
            Items = items
        };
    }

    private async Task<FinanceTransparencyToolExecutionDetailResponse?> BuildFinanceTransparencyToolExecutionDetailAsync(
        Guid companyId,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        var financeToolNames = BuildFinanceToolNameSet();
        var attempt = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == executionId, cancellationToken);

        if (attempt is null || !IsFinanceToolExecution(attempt, financeToolNames))
        {
            return null;
        }

        var relatedRecords = await BuildExecutionRelatedRecordsAsync(companyId, attempt, cancellationToken);

        var (originatingEntityType, originatingEntityId, originatingEntityReference) = ResolveOriginatingEntity(attempt);
        return new FinanceTransparencyToolExecutionDetailResponse
        {
            ExecutionId = attempt.Id,
            ToolName = attempt.ToolName,
            ToolVersion = attempt.ToolVersion,
            LifecycleState = attempt.Status.ToString().ToLowerInvariant(),
            RequestSummary = BuildToolExecutionRequestSummary(attempt),
            ResponseSummary = BuildToolExecutionResponseSummary(attempt),
            ExecutionTimestampUtc = attempt.ExecutedUtc ?? attempt.CompletedUtc ?? attempt.StartedUtc,
            CorrelationId = attempt.CorrelationId ?? string.Empty,
            ApprovalRequestDisplay = BuildApprovalRequestDisplay(attempt.ApprovalRequestId),
            ApprovalRequestId = attempt.ApprovalRequestId,
            OriginatingEntityType = originatingEntityType,
            OriginatingFinanceActionDisplay = BuildOriginatingFinanceActionDisplay(originatingEntityReference),
            OriginatingEntityId = originatingEntityId,
            OriginatingEntityReference = originatingEntityReference,
            TaskId = attempt.TaskId,
            WorkflowInstanceId = attempt.WorkflowInstanceId,
            RelatedRecords = relatedRecords
        };
    }

    private async Task<FinanceTransparencyEventStreamResponse?> BuildFinanceTransparencyEventsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var auditEvents = await _dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var items = auditEvents
            .Where(IsFinanceTransparencyEvent)
            .Take(25)
            .Select(auditEvent =>
            {
                var triggerTrace = BuildTriggerConsumptionTrace(auditEvent);
                return new FinanceTransparencyEventListItemResponse
                {
                    Id = auditEvent.Id,
                    EventType = auditEvent.Action,
                    OccurredAtUtc = auditEvent.OccurredUtc,
                    CorrelationId = auditEvent.CorrelationId ?? string.Empty,
                    AffectedEntityType = auditEvent.TargetType,
                    AffectedEntityId = auditEvent.TargetId,
                    EntityReference = BuildAuditEntityReference(auditEvent),
                    PayloadSummary = BuildAuditPayloadSummary(auditEvent),
                    HasTriggerTrace = triggerTrace.Count > 0
                };
            })
            .ToArray();

        if (items.Length == 0)
        {
            return null;
        }

        return new FinanceTransparencyEventStreamResponse
        {
            Summary = $"Showing {items.Length} recent finance event(s) for the active company.",
            Items = items
        };
    }

    private async Task<FinanceTransparencyEventDetailResponse?> BuildFinanceTransparencyEventDetailAsync(
        Guid companyId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var auditEvent = await _dbContext.AuditEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == eventId, cancellationToken);

        if (auditEvent is null || !IsFinanceTransparencyEvent(auditEvent))
        {
            return null;
        }

        var relatedRecords = await BuildEventRelatedRecordsAsync(companyId, auditEvent, cancellationToken);

        return new FinanceTransparencyEventDetailResponse
        {
            Id = auditEvent.Id,
            EventType = auditEvent.Action,
            OccurredAtUtc = auditEvent.OccurredUtc,
            CorrelationId = auditEvent.CorrelationId ?? string.Empty,
            EntityType = auditEvent.TargetType,
            EntityId = auditEvent.TargetId,
            EntityReference = BuildAuditEntityReference(auditEvent),
            PayloadSummary = BuildAuditPayloadSummary(auditEvent),
            RelatedRecords = relatedRecords,
            TriggerConsumptionTrace = BuildTriggerConsumptionTrace(auditEvent)
        };
    }

    private HashSet<string> BuildFinanceToolNameSet() =>
        _toolRegistry.ListTools()
            .Where(registration => registration.Scopes.Contains("finance"))
            .Select(registration => registration.ToolName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<FinanceTransparencyRelatedRecordResponse>> BuildExecutionRelatedRecordsAsync(
        Guid companyId,
        ToolExecutionAttempt attempt,
        CancellationToken cancellationToken)
    {
        var records = new List<FinanceTransparencyRelatedRecordResponse>();

        AddRelatedRecord(
            records,
            "approval_request",
            "approval_request",
            attempt.ApprovalRequestId?.ToString("D"),
            BuildApprovalRequestDisplay(attempt.ApprovalRequestId),
            attempt.ApprovalRequestId is Guid approvalRequestId ? $"Approval request {approvalRequestId:D}" : string.Empty,
            "explicit_link");

        if (attempt.TaskId is Guid taskId)
        {
            AddRelatedRecord(
                records,
                "task",
                "work_task",
                taskId.ToString("D"),
                $"Task {taskId:D}",
                "Execution task context",
                "explicit_link");
        }

        if (attempt.WorkflowInstanceId is Guid workflowInstanceId)
        {
            AddRelatedRecord(
                records,
                "workflow",
                "workflow_instance",
                workflowInstanceId.ToString("D"),
                $"Workflow {workflowInstanceId:D}",
                "Workflow context",
                "explicit_link");
        }

        var (originatingEntityType, originatingEntityId, originatingEntityReference) = ResolveOriginatingEntity(attempt);
        AddRelatedRecord(
            records,
            "finance_action",
            originatingEntityType,
            originatingEntityId?.ToString("D"),
            BuildOriginatingFinanceActionDisplay(originatingEntityReference),
            originatingEntityReference,
            "payload_reference");

        var relatedEvents = await _dbContext.AuditEvents
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (x.RelatedToolExecutionAttemptId == attempt.Id ||
                 (!string.IsNullOrWhiteSpace(attempt.CorrelationId) &&
                  x.CorrelationId != null &&
                  x.CorrelationId == attempt.CorrelationId)))
            .OrderByDescending(x => x.OccurredUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        foreach (var auditEvent in relatedEvents)
        {
            AddRelatedRecord(
                records,
                "event",
                "audit_event",
                auditEvent.Id.ToString("D"),
                auditEvent.Action,
                BuildAuditEntityReference(auditEvent),
                auditEvent.RelatedToolExecutionAttemptId == attempt.Id ? "audit_link" : "correlation");

            AddRelatedRecord(
                records,
                "approval_request",
                "approval_request",
                auditEvent.RelatedApprovalRequestId?.ToString("D"),
                BuildApprovalRequestDisplay(auditEvent.RelatedApprovalRequestId),
                "Approval observed in related event",
                "audit_link");

            AddRelatedRecord(
                records,
                "finance_action",
                auditEvent.TargetType,
                auditEvent.TargetId,
                BuildAuditEntityReference(auditEvent),
                BuildAuditEntityReference(auditEvent),
                string.IsNullOrWhiteSpace(auditEvent.CorrelationId) ? "audit_target" : "correlation");
        }

        return OrderRelatedRecords(records);
    }

    private async Task<IReadOnlyList<FinanceTransparencyRelatedRecordResponse>> BuildEventRelatedRecordsAsync(
        Guid companyId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var records = new List<FinanceTransparencyRelatedRecordResponse>();

        AddRelatedRecord(
            records,
            "affected_entity",
            auditEvent.TargetType,
            auditEvent.TargetId,
            BuildAuditEntityReference(auditEvent),
            BuildAuditEntityReference(auditEvent),
            "audit_target");

        AddRelatedRecord(
            records,
            "approval_request",
            "approval_request",
            auditEvent.RelatedApprovalRequestId?.ToString("D"),
            BuildApprovalRequestDisplay(auditEvent.RelatedApprovalRequestId),
            "Approval recorded directly on the event",
            "explicit_link");

        if (auditEvent.RelatedTaskId is Guid taskId)
        {
            AddRelatedRecord(
                records,
                "task",
                "work_task",
                taskId.ToString("D"),
                $"Task {taskId:D}",
                "Task context recorded on the event",
                "explicit_link");
        }

        if (auditEvent.RelatedWorkflowInstanceId is Guid workflowInstanceId)
        {
            AddRelatedRecord(
                records,
                "workflow",
                "workflow_instance",
                workflowInstanceId.ToString("D"),
                $"Workflow {workflowInstanceId:D}",
                "Workflow context recorded on the event",
                "explicit_link");
        }

        var relatedAttempts = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (x.Id == auditEvent.RelatedToolExecutionAttemptId ||
                 (!string.IsNullOrWhiteSpace(auditEvent.CorrelationId) &&
                  x.CorrelationId != null &&
                  x.CorrelationId == auditEvent.CorrelationId)))
            .OrderByDescending(x => x.ExecutedUtc ?? x.CompletedUtc ?? x.StartedUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        foreach (var attempt in relatedAttempts)
        {
            AddRelatedRecord(
                records,
                "tool_execution",
                "tool_execution",
                attempt.Id.ToString("D"),
                BuildToolExecutionDisplay(attempt),
                BuildToolExecutionReference(attempt),
                auditEvent.RelatedToolExecutionAttemptId == attempt.Id ? "explicit_link" : "correlation");

            AddRelatedRecord(
                records,
                "approval_request",
                "approval_request",
                attempt.ApprovalRequestId?.ToString("D"),
                BuildApprovalRequestDisplay(attempt.ApprovalRequestId),
                "Approval attached to related tool execution",
                "execution_link");

            var (originatingEntityType, originatingEntityId, originatingEntityReference) = ResolveOriginatingEntity(attempt);
            AddRelatedRecord(
                records,
                "finance_action",
                originatingEntityType,
                originatingEntityId?.ToString("D"),
                BuildOriginatingFinanceActionDisplay(originatingEntityReference),
                originatingEntityReference,
                "execution_payload");
        }

        return OrderRelatedRecords(records);
    }

    private static void AddRelatedRecord(
        ICollection<FinanceTransparencyRelatedRecordResponse> records,
        string relationshipType,
        string? targetType,
        string? targetId,
        string? displayText,
        string? reference,
        string resolutionSource)
    {
        if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(targetId))
        {
            return;
        }

        var normalizedTargetType = targetType.Trim();
        var normalizedTargetId = targetId.Trim();
        if (records.Any(x =>
                string.Equals(x.RelationshipType, relationshipType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TargetType, normalizedTargetType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TargetId, normalizedTargetId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        records.Add(new FinanceTransparencyRelatedRecordResponse
        {
            RelationshipType = relationshipType,
            TargetType = normalizedTargetType,
            TargetId = normalizedTargetId,
            DisplayText = string.IsNullOrWhiteSpace(displayText) ? $"{NormalizeTransparencyToken(normalizedTargetType)} {normalizedTargetId}" : displayText.Trim(),
            Reference = string.IsNullOrWhiteSpace(reference) ? string.Empty : TruncateSummary(reference.Trim(), 180),
            ResolutionSource = resolutionSource
        });
    }

    private static bool IsFinanceToolExecution(ToolExecutionAttempt attempt, HashSet<string> financeToolNames) =>
        string.Equals(attempt.Scope, "finance", StringComparison.OrdinalIgnoreCase) ||
        financeToolNames.Contains(attempt.ToolName);

    private static bool IsFinanceTransparencyEvent(AuditEvent auditEvent)
    {
        if (auditEvent.Action.StartsWith("finance", StringComparison.OrdinalIgnoreCase) ||
            auditEvent.TargetType.StartsWith("finance", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return auditEvent.Metadata.TryGetValue("requestedDomain", out var requestedDomain) &&
               string.Equals(requestedDomain, "finance", StringComparison.OrdinalIgnoreCase) ||
               auditEvent.Metadata.TryGetValue("responsibilityDomain", out var responsibilityDomain) &&
               string.Equals(responsibilityDomain, "finance", StringComparison.OrdinalIgnoreCase);
    }

    private static FinanceTransparencyToolExecutionListItemResponse BuildToolExecutionListItem(ToolExecutionAttempt attempt) =>
        new()
        {
            ExecutionId = attempt.Id,
            ToolName = attempt.ToolName,
            ToolVersion = attempt.ToolVersion,
            LifecycleState = attempt.Status.ToString().ToLowerInvariant(),
            RequestSummary = BuildToolExecutionRequestSummary(attempt),
            ResponseSummary = BuildToolExecutionResponseSummary(attempt),
            ExecutionTimestampUtc = attempt.ExecutedUtc ?? attempt.CompletedUtc ?? attempt.StartedUtc,
            CorrelationId = attempt.CorrelationId ?? string.Empty
        };

    private static string BuildContractSummary(JsonObject inputSchema, JsonObject outputSchema) =>
        $"Requests {DescribeSchema(inputSchema)}; returns {DescribeSchema(outputSchema)}.";

    private static string BuildSchemaSummary(JsonObject inputSchema, JsonObject outputSchema) =>
        $"Input schema exposes {CountSchemaProperties(inputSchema)} field(s); output schema exposes {CountSchemaProperties(outputSchema)} field(s).";

    private static int CountSchemaProperties(JsonObject schema) =>
        (schema["properties"] as JsonObject)?.Count ?? 0;

    private static string BuildManifestVersionMetadata(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "Version metadata not available." : $"Manifest version {version.Trim()} from the active runtime registry.";

    private static string DescribeSchema(JsonObject schema)
    {
        var properties = schema["properties"] as JsonObject;
        if (properties is null || properties.Count == 0)
        {
            return "has no declared fields";
        }

        var required = (schema["required"] as JsonArray)?
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray() ?? [];

        if (required.Length > 0)
        {
            return $"requires {string.Join(", ", required.Take(3))}";
        }

        return $"declares {string.Join(", ", properties.Select(pair => pair.Key).Take(3))}";
    }

    private static string BuildToolExecutionRequestSummary(ToolExecutionAttempt attempt) =>
        SummarizePayload(
            attempt.RequestPayload,
            ["transactionId", "invoiceId", "category", "status", "candidateCategory", "candidateStatus", "limit", "asOfUtc"]);

    private static string BuildToolExecutionResponseSummary(ToolExecutionAttempt attempt)
    {
        var safeSummary = TryReadSummaryScalar(attempt.ResultPayload, "userSafeSummary");
        if (!string.IsNullOrWhiteSpace(safeSummary))
        {
            return safeSummary;
        }

        if (!string.IsNullOrWhiteSpace(attempt.DenialReason))
        {
            return attempt.DenialReason;
        }

        return SummarizePayload(
            attempt.ResultPayload,
            ["status", "errorCode", "toolName", "actionType", "success", "recommendedCategory", "recommendedStatus", "confidence"]);
    }

    private static string BuildApprovalRequestDisplay(Guid? approvalRequestId) =>
        approvalRequestId is Guid resolvedApprovalRequestId ? $"Approval request {resolvedApprovalRequestId:D}" : "Not available";

    private static string BuildOriginatingFinanceActionDisplay(string? originatingEntityReference) =>
        string.IsNullOrWhiteSpace(originatingEntityReference) ? "Not available" : originatingEntityReference.Trim();

    private static string BuildToolExecutionDisplay(ToolExecutionAttempt attempt)
    {
        var version = string.IsNullOrWhiteSpace(attempt.ToolVersion) ? "unversioned" : attempt.ToolVersion;
        return $"{attempt.ToolName} ({version})";
    }

    private static string BuildToolExecutionReference(ToolExecutionAttempt attempt) =>
        TruncateSummary(
            $"{NormalizeTransparencyToken(attempt.Status.ToString())} at {(attempt.ExecutedUtc ?? attempt.CompletedUtc ?? attempt.StartedUtc):u}",
            120);

    private static IReadOnlyList<FinanceTransparencyRelatedRecordResponse> OrderRelatedRecords(
        IEnumerable<FinanceTransparencyRelatedRecordResponse> records) =>
        records
            .OrderBy(record => record.RelationshipType switch
            {
                "affected_entity" => 0,
                "finance_action" => 1,
                "approval_request" => 2,
                "tool_execution" => 3,
                "event" => 4,
                "workflow" => 5,
                "task" => 6,
                _ => 99
            })
            .ThenBy(record => record.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string SummarizePayload(IReadOnlyDictionary<string, JsonNode?> payload, IReadOnlyList<string> preferredKeys)
    {
        if (payload.Count == 0)
        {
            return "No payload recorded.";
        }

        var parts = new List<string>();
        foreach (var key in preferredKeys)
        {
            var value = TryReadSummaryScalar(payload, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            parts.Add($"{key}={value}");
            if (parts.Count == 3)
            {
                break;
            }
        }

        if (parts.Count == 0 && payload.TryGetValue("data", out var dataNode) && dataNode is JsonObject dataObject)
        {
            foreach (var pair in dataObject)
            {
                var value = NodeToSummaryText(pair.Value);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                parts.Add($"{pair.Key}={value}");
                if (parts.Count == 3)
                {
                    break;
                }
            }
        }

        if (parts.Count == 0)
        {
            foreach (var pair in payload)
            {
                var value = NodeToSummaryText(pair.Value);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                parts.Add($"{pair.Key}={value}");
                if (parts.Count == 3)
                {
                    break;
                }
            }
        }

        return parts.Count == 0
            ? "Payload captured without scalar summary."
            : TruncateSummary(string.Join(" | ", parts), 240);
    }

    private Task<ActionResult<FinanceDashboardMetricDto>> GetDashboardMetricAsync(
        Guid companyId,
        DateTime? asOfUtc,
        int upcomingWindowDays,
        Func<DashboardFinanceSnapshotDto, FinanceDashboardMetricDto> selector,
        CancellationToken cancellationToken) =>
        ExecuteReadAsync(async () =>
        {
            var snapshot = await _dashboardFinanceSnapshotService.GetAsync(
                companyId,
                asOfUtc,
                upcomingWindowDays,
                cancellationToken);

            return selector(snapshot);
        });

    private static bool IsReferentialIntegrityCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        return code.StartsWith("accounts.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("counterparties.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("documents.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("invoices.counterparty", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("invoices.document", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("bills.counterparty", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("bills.document", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("recurring.supplier", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("recurring.category", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.account", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.counterparty", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.invoice", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.bill", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.document", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("balances.account", StringComparison.OrdinalIgnoreCase);
    }

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
            .Select(item => item with
            {
                EventId = NormalizeWorkflowHistoryEventId(item.EventId) ?? string.Empty,
                EventType = string.IsNullOrWhiteSpace(item.EventType) ? "Review" : item.EventType.Trim(),
                ActorOrSourceDisplayName = string.IsNullOrWhiteSpace(item.ActorOrSourceDisplayName) ? "System" : item.ActorOrSourceDisplayName.Trim()
            })
            .OrderBy(item => item.OccurredAtUtc == default ? 1 : 0)
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenBy(item => item.EventType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ActorOrSourceDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EventId, StringComparer.OrdinalIgnoreCase)
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

    private static string? NormalizeReviewToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace(" ", "_", StringComparison.Ordinal).Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();

    private async Task<FinanceInvoiceReviewDetailResponse?> BuildInvoiceReviewDetailResponseAsync(
        Guid companyId,
        Guid invoiceId,
        bool executeIfMissing,
        CancellationToken cancellationToken)
    {
        var invoice = await _financeReadService.GetInvoiceDetailAsync(
            new GetFinanceInvoiceDetailQuery(companyId, invoiceId),
            cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var review = await _invoiceReviewWorkflowService.GetLatestByInvoiceAsync(companyId, invoiceId, cancellationToken);
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
                    }),
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

    private async Task<ActionResult<T>> ExecuteReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return Ok(await read());
        }
        catch (UnauthorizedAccessException)
        {
            LogHandledFinanceException("read_forbidden", null);
            return Forbid();
        }
        catch (FinanceNotInitializedException ex)
        {
            LogHandledFinanceException("read_not_initialized", ex);
            return await CreateFinanceNotInitializedResultAsync<T>(ex);
        }
        catch (KeyNotFoundException ex)
        {
            LogHandledFinanceException("read_not_found", ex);
            return NotFound(CreateProblemDetails(ex.Message, "Finance record was not found.", StatusCodes.Status404NotFound));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            LogHandledFinanceException("read_argument_out_of_range", ex);
            return BadRequest(CreateProblemDetails(ex.Message));
        }
        catch (ArgumentException ex)
        {
            LogHandledFinanceException("read_argument", ex);
            return BadRequest(CreateProblemDetails(ex.Message));
        }
    }

    private async Task<ActionResult<T>> ExecuteReadOptionalAsync<T>(Func<Task<T?>> read, string notFoundDetail)
        where T : class
    {
        try
        {
            var result = await read();
            return result is null
                ? NotFound(CreateProblemDetails(notFoundDetail, "Finance record was not found.", StatusCodes.Status404NotFound))
                : Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            LogHandledFinanceException("read_optional_forbidden", null);
            return Forbid();
        }
        catch (FinanceNotInitializedException ex)
        {
            LogHandledFinanceException("read_optional_not_initialized", ex);
            return await CreateFinanceNotInitializedResultAsync<T>(ex);
        }
        catch (KeyNotFoundException ex)
        {
            LogHandledFinanceException("read_optional_not_found", ex);
            return NotFound(CreateProblemDetails(ex.Message, "Finance record was not found.", StatusCodes.Status404NotFound));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            LogHandledFinanceException("read_optional_argument_out_of_range", ex);
            return BadRequest(CreateProblemDetails(ex.Message));
        }
        catch (ArgumentException ex)
        {
            LogHandledFinanceException("read_optional_argument", ex);
            return BadRequest(CreateProblemDetails(ex.Message));
        }
    }

    private async Task<ActionResult<T>> ExecuteWriteAsync<T>(Func<Task<T>> write)
    {
        try
        {
            return Ok(await write());
        }
        catch (UnauthorizedAccessException)
        {
            LogHandledFinanceException("write_forbidden", null);
            return Forbid();
        }
        catch (FinanceNotInitializedException ex)
        {
            LogHandledFinanceException("write_not_initialized", ex);
            return await CreateFinanceNotInitializedResultAsync<T>(ex);
        }
        catch (SimulationBackendDisabledException ex)
        {
            LogHandledFinanceException("simulation_execution_disabled", ex);
            return Conflict(CreateSimulationExecutionDisabledProblemDetails(ex.Message));
        }
        catch (FinanceValidationException ex)
        {
            LogHandledFinanceException("write_validation", ex);
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
            LogHandledFinanceException("write_not_found", ex);
            return NotFound(CreateProblemDetails(ex.Message, "Finance record was not found.", StatusCodes.Status404NotFound));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            LogHandledFinanceException("write_argument_out_of_range", ex);
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
        catch (ArgumentException ex)
        {
            LogHandledFinanceException("write_argument", ex);
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
        catch (InvalidOperationException ex)
        {
            LogHandledFinanceException("write_invalid_operation", ex);
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
    }

    private void LogHandledFinanceException(string category, Exception? exception)
    {
        if (exception is null)
        {
            _logger.LogWarning(
                "Finance request {Category} for HTTP {Method} {Path}.",
                category,
                HttpContext.Request.Method,
                HttpContext.Request.Path);
            return;
        }

        _logger.LogWarning(
            exception,
            "Finance request {Category} for HTTP {Method} {Path}: {Message}",
            category,
            HttpContext.Request.Method,
            HttpContext.Request.Path,
            exception.Message);
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

    private static string HumanizeReviewToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "System"
            : string.Join(" ", value
                .Trim()
                .Replace("-", "_", StringComparison.Ordinal)
                .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private ProblemDetails CreateProblemDetails(string detail) =>
        CreateProblemDetails(detail, "Invalid finance read request.", StatusCodes.Status400BadRequest);

    private ProblemDetails CreateSimulationExecutionDisabledProblemDetails(string detail) =>
        CreateProblemDetails(detail, "Simulation execution is disabled.", StatusCodes.Status409Conflict);

    private async Task<ActionResult<T>> CreateFinanceNotInitializedResultAsync<T>(FinanceNotInitializedException exception)
    {
        var shouldTriggerFallback = exception.CanTriggerSeed && _financeInitializationOptions.Value.ShouldTriggerSeedFallback();
        var entryState = shouldTriggerFallback
            ? await _financeEntryService.RequestEntryStateAsync(
                new GetFinanceEntryStateQuery(
                    exception.CompanyId,
                    Source: FinanceEntrySources.FallbackRead,
                    SeedMode: FinanceSeedRequestModes.Replace),
                HttpContext.RequestAborted)
            : await _financeEntryService.GetEntryStateAsync(
                new GetFinanceEntryStateQuery(exception.CompanyId, Source: FinanceEntrySources.FallbackRead),
                HttpContext.RequestAborted);

        var response = new FinanceInitializationProblemResponse
        {
            Title = "Finance data is not initialized.",
            Detail = exception.Message,
            Status = StatusCodes.Status409Conflict,
            Code = FinanceInitializationProblemCodeValues.NotInitialized,
            Message = entryState.Message,
            Domain = exception.Domain,
            Module = FinanceInitializationDomainValues.Finance,
            CompanyId = exception.CompanyId,
            CanTriggerSeed = exception.CanTriggerSeed,
            CanGenerate = entryState.CanGenerate,
            RecommendedAction = entryState.RecommendedAction,
            SupportedModes = entryState.SupportedModes,
            FallbackTriggered = shouldTriggerFallback,
            SeedRequested = entryState.SeedJobEnqueued,
            SeedJobActive = entryState.SeedJobActive,
            ProgressState = entryState.ProgressState,
            SeedingState = entryState.SeedingState.ToStorageValue(),
            InitializationStatus = entryState.InitializationStatus,
            JobStatus = entryState.JobStatus,
            CorrelationId = entryState.CorrelationId ?? ResolveCorrelationId(),
            StatusEndpoint = entryState.StatusEndpoint,
            SeedEndpoint = entryState.SeedEndpoint,
            ConfirmationRequired = entryState.ConfirmationRequired,
            ConfirmationMessage = entryState.ConfirmationMessage
        };

        _logger.LogInformation(
            "Finance request for company {CompanyId} returned not_initialized on {RequestPath}. TriggerSource={TriggerSource}, CorrelationId={CorrelationId}, FallbackTriggered={FallbackTriggered}, SeedRequested={SeedRequested}, ProgressState={ProgressState}.",
            exception.CompanyId,
            HttpContext.Request.Path,
            response.FallbackTriggered ? FinanceEntrySources.FallbackRead : FinanceEntrySources.FinanceEntry,
            response.CorrelationId,
            response.FallbackTriggered,
            response.SeedRequested,
            response.ProgressState);

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                exception.CompanyId,
                AuditActorTypes.System,
                null,
                FinanceRequestNotInitializedAction,
                BackgroundExecutionRelatedEntityTypes.FinanceSeed,
                exception.CompanyId.ToString("D"),
                AuditEventOutcomes.Failed,
                "A finance API request returned a structured not_initialized response because the finance dataset is unavailable.",
                Metadata: new Dictionary<string, string?>
                {
                    ["triggerSource"] = response.FallbackTriggered ? FinanceEntrySources.FallbackRead : FinanceEntrySources.FinanceEntry,
                    ["requestPath"] = HttpContext.Request.Path,
                    ["requestMethod"] = HttpContext.Request.Method,
                    ["code"] = response.Code,
                    ["domain"] = response.Domain,
                    ["module"] = response.Module,
                    ["fallbackTriggered"] = response.FallbackTriggered ? "true" : "false",
                    ["seedRequested"] = response.SeedRequested ? "true" : "false",
                    ["seedJobActive"] = response.SeedJobActive ? "true" : "false",
                    ["progressState"] = response.ProgressState,
                    ["seedingState"] = response.SeedingState,
                    ["recommendedAction"] = response.RecommendedAction,
                    ["statusEndpoint"] = response.StatusEndpoint,
                    ["seedEndpoint"] = response.SeedEndpoint
                },
                CorrelationId: response.CorrelationId,
                OccurredUtc: DateTime.UtcNow),
            HttpContext.RequestAborted);

        return new ObjectResult(response) { StatusCode = response.Status };
    }

    private string ResolveCorrelationId()
    {
        if (Request.Headers.TryGetValue("X-Correlation-ID", out var headerValue) && !string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.ToString();
        }

        return HttpContext.TraceIdentifier;
    }

    private ProblemDetails CreateProblemDetails(string detail, string title, int status) =>
        new()
        {
            Title = title,
            Detail = detail,
            Status = status,
            Instance = HttpContext.Request.Path
        };

    private static (string EntityType, Guid? EntityId, string EntityReference) ResolveOriginatingEntity(ToolExecutionAttempt attempt)
    {
        var transactionId = TryReadGuid(attempt.RequestPayload, "transactionId") ?? TryReadGuid(attempt.ResultPayload, "transactionId");
        if (transactionId is Guid resolvedTransactionId)
        {
            return ("finance_transaction", resolvedTransactionId, $"Finance transaction {resolvedTransactionId:D}");
        }

        var invoiceId = TryReadGuid(attempt.RequestPayload, "invoiceId") ?? TryReadGuid(attempt.ResultPayload, "invoiceId");
        if (invoiceId is Guid resolvedInvoiceId)
        {
            return ("finance_invoice", resolvedInvoiceId, $"Finance invoice {resolvedInvoiceId:D}");
        }

        return (string.Empty, null, string.Empty);
    }

    private static string BuildAuditEntityReference(AuditEvent auditEvent)
    {
        if (auditEvent.Metadata.TryGetValue("recordReference", out var recordReference) && !string.IsNullOrWhiteSpace(recordReference))
        {
            return recordReference;
        }

        if (auditEvent.Metadata.TryGetValue("affectedRecordReference", out var affectedReference) && !string.IsNullOrWhiteSpace(affectedReference))
        {
            return affectedReference;
        }

        return string.IsNullOrWhiteSpace(auditEvent.TargetId)
            ? NormalizeTransparencyToken(auditEvent.TargetType)
            : $"{NormalizeTransparencyToken(auditEvent.TargetType)} {auditEvent.TargetId}";
    }

    private static string BuildAuditPayloadSummary(AuditEvent auditEvent)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(auditEvent.RationaleSummary))
        {
            parts.Add(auditEvent.RationaleSummary);
        }

        var metadataSummary = auditEvent.Metadata
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Take(3)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToArray();

        if (metadataSummary.Length > 0)
        {
            parts.Add(string.Join(" | ", metadataSummary));
        }

        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(auditEvent.PayloadDiffJson))
        {
            parts.Add(auditEvent.PayloadDiffJson);
        }

        return parts.Count == 0
            ? "No payload summary recorded."
            : TruncateSummary(string.Join(" ", parts), 320);
    }

    private static IReadOnlyList<FinanceTransparencyTriggerTraceItemResponse> BuildTriggerConsumptionTrace(AuditEvent auditEvent)
    {
        var items = auditEvent.DataSourcesUsed
            .Select(dataSource => new FinanceTransparencyTriggerTraceItemResponse
            {
                SourceType = dataSource.SourceType,
                SourceId = dataSource.SourceId ?? string.Empty,
                DisplayName = dataSource.DisplayName ?? dataSource.SourceType,
                Reference = dataSource.Reference ?? string.Empty
            })
            .ToList();

        if (auditEvent.RelatedWorkflowInstanceId is Guid workflowInstanceId)
        {
            items.Add(new FinanceTransparencyTriggerTraceItemResponse
            {
                SourceType = "workflow_instance",
                SourceId = workflowInstanceId.ToString("D"),
                DisplayName = "Workflow instance",
                Reference = workflowInstanceId.ToString("D")
            });
        }

        if (auditEvent.RelatedToolExecutionAttemptId is Guid toolExecutionAttemptId)
        {
            items.Add(new FinanceTransparencyTriggerTraceItemResponse
            {
                SourceType = "tool_execution",
                SourceId = toolExecutionAttemptId.ToString("D"),
                DisplayName = "Tool execution",
                Reference = toolExecutionAttemptId.ToString("D")
            });
        }

        return items;
    }

    private static Guid? TryReadGuid(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node))
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty)
        {
            return guid;
        }

        return node is JsonValue stringValue &&
               stringValue.TryGetValue<string>(out var text) &&
               Guid.TryParse(text, out guid) &&
               guid != Guid.Empty
            ? guid
            : null;
    }

    private static string? TryReadSummaryScalar(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (payload.TryGetValue(key, out var node))
        {
            return NodeToSummaryText(node);
        }

        if (payload.TryGetValue("data", out var dataNode) && dataNode is JsonObject dataObject)
        {
            return NodeToSummaryText(dataObject[key]);
        }

        return null;
    }

    private static string? NodeToSummaryText(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return TruncateSummary(text, 96);
            }

            if (value.TryGetValue<Guid>(out var guid))
            {
                return guid.ToString("D");
            }

            if (value.TryGetValue<DateTime>(out var dateTime))
            {
                return dateTime.ToString("u");
            }

            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue ? "true" : "false";
            }
        }

        return TruncateSummary(node.ToJsonString(), 96);
    }

    private static string NormalizeTransparencyToken(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Replace("_", " ", StringComparison.Ordinal);

    private static string TruncateSummary(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..Math.Max(0, maxLength - 3)]}...";
    }
}

public sealed record FinanceInvoiceDetailResponse(
    Guid Id,
    Guid CounterpartyId,
    string CounterpartyName,
    string InvoiceNumber,
    DateTime IssuedUtc,
    DateTime DueUtc,
    decimal Amount,
    string Currency,
    string Status,
    FinanceInvoiceWorkflowContextDto? WorkflowContext,
    FinanceActionPermissionsDto Permissions,
    FinanceLinkedDocumentAccessDto LinkedDocument,
    FinanceInvoiceRecommendationDetailsResponse? RecommendationDetails = null,
    IReadOnlyList<FinanceInvoiceWorkflowHistoryItemResponse>? WorkflowHistory = null,
    IReadOnlyList<NormalizedFinanceInsightDto>? AgentInsights = null);

public sealed record FinanceInvoiceReviewListItemResponse(
    Guid Id,
    string InvoiceNumber,
    string SupplierName,
    decimal Amount,
    string Currency,
    string Status,
    string RiskLevel,
    string RecommendationStatus,
    string RecommendationOutcome,
    decimal Confidence,
    DateTime LastUpdatedUtc);

public sealed record FinanceInvoiceReviewDetailResponse(
    Guid Id,
    string InvoiceNumber,
    string SupplierName,
    decimal Amount,
    string Currency,
    string Status,
    string RiskLevel,
    string RecommendationStatus,
    string RecommendationSummary,
    string RecommendedAction,
    decimal Confidence,
    DateTime LastUpdatedUtc,
    Guid SourceInvoiceId,
    Guid? RelatedApprovalId,
    FinanceInvoiceReviewActionAvailabilityResponse Actions,
    FinanceInvoiceRecommendationDetailsResponse? RecommendationDetails = null,
    IReadOnlyList<FinanceInvoiceWorkflowHistoryItemResponse>? WorkflowHistory = null);

public sealed record FinanceInvoiceRecommendationDetailsResponse(
    string Classification,
    string Risk,
    string RationaleSummary,
    decimal Confidence,
    string RecommendedAction,
    string CurrentWorkflowStatus);

public sealed record FinanceInvoiceReviewActionAvailabilityResponse(
    bool IsActionable,
    bool CanApprove,
    bool CanReject,
    bool CanSendForFollowUp);

public sealed record FinanceInvoiceWorkflowHistoryItemResponse(
    string EventId,
    string EventType,
    string ActorOrSourceDisplayName,
    DateTime OccurredAtUtc,
    Guid? RelatedAuditId,
    Guid? RelatedApprovalId);

public sealed record UpdateFinanceInvoiceApprovalStatusRequest(string Status);

public sealed record ReviewFinanceInvoiceWorkflowRequest(
    Guid? WorkflowInstanceId,
    Guid? AgentId,
    Dictionary<string, System.Text.Json.Nodes.JsonNode?>? Payload);

public sealed record EvaluateFinanceTransactionAnomalyRequest(
    Guid? WorkflowInstanceId,
    Guid? AgentId);

public sealed record EvaluateFinanceCashPositionRequest(
    Guid? WorkflowInstanceId,
    Guid? AgentId);

public sealed record UpdateFinanceTransactionCategoryRequest(string Category);

public sealed record CreateFinancePaymentRequest(
    string PaymentType,
    decimal Amount,
    string Currency,
    DateTime PaymentDate,
    string Method,
    string Status,
    string CounterpartyReference)
{
    public CreateFinancePaymentDto ToDto() =>
        new(PaymentType, Amount, Currency, PaymentDate, Method, Status, CounterpartyReference);
}

public sealed record BootstrapFinanceSeedRequest(
    int SeedValue,
    DateTime? SeedAnchorUtc = null,
    bool ReplaceExisting = true,
    bool InjectAnomalies = false,
    string? AnomalyScenarioProfile = null);
public sealed record UpsertFinanceCounterpartyRequest(
    string Name,
    string? Email,
    string? PaymentTerms,
    string? TaxId,
    decimal? CreditLimit,
    string? PreferredPaymentMethod,
    string? DefaultAccountMapping)
{
    public FinanceCounterpartyUpsertDto ToDto() =>
        new(Name, Email, PaymentTerms, TaxId, CreditLimit, PreferredPaymentMethod, DefaultAccountMapping);
}

public sealed record RerunFinanceBootstrapRequest(
    int BatchSize = 250,
    bool RerunPlanningBackfill = true,
    bool RerunApprovalBackfill = true,
    string? CorrelationId = null);

public sealed record RefreshFinanceInsightsSnapshotRequest(
    DateTime? AsOfUtc = null,
    int ExpenseWindowDays = 90,
    int TrendWindowDays = 30,
    int PayableWindowDays = 14,
    string? SnapshotKey = null,
    int RetentionMinutes = 360,
    bool RunInBackground = false,
    bool ResetAttempts = false,
    string? CorrelationId = null);

public sealed record AdvanceCompanySimulationTimeRequest(
    int TotalHours,
    int? ExecutionStepHours = null,
    bool Accelerated = false);

internal static class FinanceSeedingStateResponseMappings
{
    public static FinanceSeedingStateDiagnosticsResponse WithRecordChecks(
        this FinanceSeedingStateDiagnosticsResponse response,
        FinanceSeedingStateDiagnosticsDto diagnostics)
    {
        response.HasAccounts = diagnostics.HasAccounts;
        response.HasCounterparties = diagnostics.HasCounterparties;
        response.HasTransactions = diagnostics.HasTransactions;
        response.HasBalances = diagnostics.HasBalances;
        response.HasPolicyConfiguration = diagnostics.HasPolicyConfiguration;
        response.HasInvoices = diagnostics.HasInvoices;
        response.HasBills = diagnostics.HasBills;

        return response;
    }
}

public sealed record RetryFinanceEntryStateRequest;

internal static partial class InternalFinanceControllerMappings
{
    public static FinanceEntryInitializationResponse MapFinanceEntryState(FinanceEntryStateDto result) =>
        new()
        {
            CompanyId = result.CompanyId,
            InitializationStatus = result.InitializationStatus,
            ProgressState = result.ProgressState,
            SeedingState = result.SeedingState.ToStorageValue(),
            SeedJobEnqueued = result.SeedJobEnqueued,
            SeedJobActive = result.SeedJobActive,
            CanRetry = result.CanRetry,
            CanRefresh = result.CanRefresh,
            Message = result.Message,
            CheckedAtUtc = result.CheckedAtUtc,
            SeededAtUtc = result.SeededAtUtc,
            DataAlreadyExists = result.DataAlreadyExists,
            SeedMode = result.SeedMode,
            SeedOperation = result.SeedOperation,
            ConfirmationRequired = result.ConfirmationRequired,
            FallbackTriggered = result.FallbackTriggered,
            StatusEndpoint = result.StatusEndpoint,
            SeedEndpoint = result.SeedEndpoint,
            IdempotencyKey = result.IdempotencyKey,
            ConfirmationMessage = result.ConfirmationMessage,
            LastAttemptedUtc = result.LastAttemptedUtc,
            LastCompletedUtc = result.LastCompletedUtc,
            LastErrorCode = result.LastErrorCode,
            LastErrorMessage = result.LastErrorMessage,
            JobStatus = result.JobStatus,
            CorrelationId = result.CorrelationId,
            CanGenerate = result.CanGenerate,
            RecommendedAction = result.RecommendedAction,
            SupportedModes = result.SupportedModes
        };
}
