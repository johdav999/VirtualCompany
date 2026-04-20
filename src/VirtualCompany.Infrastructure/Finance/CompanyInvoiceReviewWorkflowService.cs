using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyInvoiceReviewWorkflowService : IInvoiceReviewWorkflowService
{
    private const string ReviewTaskType = "invoice_approval_review";
    private const string ReviewCorrelationPrefix = "invoice-review";
    private const string FinanceApproverRole = "finance_approver";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceReadService _financeReadService;
    private readonly IFinancePolicyConfigurationService _policyConfigurationService;
    private readonly IApprovalRequestService _approvalRequestService;

    public CompanyInvoiceReviewWorkflowService(
        VirtualCompanyDbContext dbContext,
        IFinanceReadService financeReadService,
        IFinancePolicyConfigurationService policyConfigurationService,
        IApprovalRequestService approvalRequestService)
    {
        _dbContext = dbContext;
        _financeReadService = financeReadService;
        _policyConfigurationService = policyConfigurationService;
        _approvalRequestService = approvalRequestService;
    }

    public async Task<FinanceInvoiceReviewWorkflowResultDto> ExecuteAsync(
        ReviewFinanceInvoiceWorkflowCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.InvoiceId);

        var invoice = await LoadInvoiceAsync(command.CompanyId, command.InvoiceId, cancellationToken);
        var policy = await _policyConfigurationService.GetPolicyConfigurationAsync(
            new GetFinancePolicyConfigurationQuery(command.CompanyId),
            cancellationToken);
        var paymentContext = await LoadRelatedPaymentContextAsync(command.CompanyId, command.InvoiceId, cancellationToken);
        var review = BuildReview(invoice, policy, paymentContext);
        var reviewAgentId = command.AgentId ?? await ResolveLauraAgentIdAsync(command.CompanyId, cancellationToken);
        var correlationId = BuildInvoiceReviewCorrelationId(command.CompanyId, invoice.Id);

        var task = await FindExistingReviewTaskAsync(command.CompanyId, command.InvoiceId, command.WorkflowInstanceId, cancellationToken);
        if (task is null)
        {
            task = new WorkTask(
                Guid.NewGuid(),
                command.CompanyId,
                ReviewTaskType,
                $"Review invoice {invoice.InvoiceNumber}",
                $"Review invoice {invoice.InvoiceNumber} from {invoice.CounterpartyName} for {invoice.Amount:0.##} {invoice.Currency}.",
                review.RequiresHumanApproval ? WorkTaskPriority.High : WorkTaskPriority.Normal,
                command.AgentId ?? await ResolveLauraAgentIdAsync(command.CompanyId, cancellationToken),
                null,
                "agent",
                reviewAgentId,
                BuildTaskInput(command, invoice, policy, paymentContext),
                command.WorkflowInstanceId,
                null,
                null,
                null,
                correlationId,
                WorkTaskSourceTypes.Agent,
                reviewAgentId,
                "workflow",
                "Invoice entered awaiting approval and needs a finance review.",
                command.WorkflowInstanceId?.ToString("N"),
                review.RequiresHumanApproval ? WorkTaskStatus.AwaitingApproval : WorkTaskStatus.Completed);
            task.SetDueDate(invoice.DueUtc);
            _dbContext.WorkTasks.Add(task);
        }

        var outputPayload = BuildOutputPayload(command.CompanyId, invoice, policy, paymentContext, review, task.Id, null, command.WorkflowInstanceId);
        ApprovalRequestDto? approval = null;
        var existingApprovalId = TryGetGuid(task.OutputPayload, "approvalRequestId");

        if (review.RequiresHumanApproval)
        {
            approval = existingApprovalId.HasValue
                ? await TryLoadApprovalAsync(command.CompanyId, existingApprovalId.Value, cancellationToken)
                : await CreateApprovalRequestAsync(command, invoice, policy, paymentContext, review, task.Id, reviewAgentId, cancellationToken);
            if (approval is not null)
            {
                outputPayload["approvalRequestId"] = JsonValue.Create(approval.Id);
            }
        }

        var targetStatus = review.RequiresHumanApproval
            ? WorkTaskStatus.AwaitingApproval
            : WorkTaskStatus.Completed;
        task.UpdateStatus(targetStatus, outputPayload, review.Rationale, review.ConfidenceScore);
        await PersistWorkflowOutputAsync(command.CompanyId, command.WorkflowInstanceId, outputPayload, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceInvoiceReviewWorkflowResultDto(
            command.CompanyId,
            invoice.Id,
            command.WorkflowInstanceId,
            task.Id,
            approval?.Id ?? existingApprovalId,
            review.InvoiceClassification,
            review.RiskLevel,
            review.RecommendedAction,
            review.Rationale,
            review.ConfidenceScore,
            review.RequiresHumanApproval,
            invoice,
            policy,
            CloneNodes(outputPayload))
        {
            LastUpdatedUtc = task.UpdatedUtc,
            ReviewTaskStatus = task.Status.ToStorageValue(),
            WorkflowOutput = FinanceWorkflowOutputSchemas.Create(
                review.InvoiceClassification,
                review.RiskLevel,
                review.RecommendedAction,
                review.Rationale,
                review.ConfidenceScore,
                "invoice_review")
        };
    }

    public async Task<FinanceInvoiceReviewWorkflowResultDto?> GetLatestByInvoiceAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        Validate(companyId, invoiceId);

        var correlationId = BuildInvoiceReviewCorrelationId(companyId, invoiceId);
        var task = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Type == ReviewTaskType && x.CorrelationId == correlationId)
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null)
        {
            var tasks = await _dbContext.WorkTasks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Type == ReviewTaskType)
                .OrderByDescending(x => x.UpdatedUtc)
                .Take(250)
                .ToListAsync(cancellationToken);

            task = tasks.FirstOrDefault(x =>
                TryGetGuid(x.InputPayload, "invoiceId") == invoiceId ||
                TryGetGuid(x.OutputPayload, "invoiceId") == invoiceId);
        }
        if (task is null)
        {
            return null;
        }

        var invoice = await LoadInvoiceAsync(companyId, invoiceId, cancellationToken);
        var policy = await _policyConfigurationService.GetPolicyConfigurationAsync(
            new GetFinancePolicyConfigurationQuery(companyId),
            cancellationToken);
        var paymentContext = BuildPaymentContextFromTask(task) ?? await LoadRelatedPaymentContextAsync(companyId, invoiceId, cancellationToken);
        var review = BuildReviewFromTask(task) ?? BuildReview(invoice, policy, paymentContext);

        return new FinanceInvoiceReviewWorkflowResultDto(
            companyId,
            invoiceId,
            task.WorkflowInstanceId,
            task.Id,
            TryGetGuid(task.OutputPayload, "approvalRequestId"),
            review.InvoiceClassification,
            review.RiskLevel,
            review.RecommendedAction,
            review.Rationale,
            review.ConfidenceScore,
            review.RequiresHumanApproval,
            invoice,
            policy,
            CloneNodes(task.OutputPayload))
        {
            LastUpdatedUtc = task.UpdatedUtc,
            ReviewTaskStatus = task.Status.ToStorageValue(),
            WorkflowOutput = FinanceWorkflowOutputSchemas.Create(
                review.InvoiceClassification,
                review.RiskLevel,
                review.RecommendedAction,
                review.Rationale,
                review.ConfidenceScore,
                "invoice_review")
        };
    }

    private async Task<FinanceInvoiceDto> LoadInvoiceAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var invoices = await _financeReadService.GetInvoicesAsync(
            new GetFinanceInvoicesQuery(companyId, null, null, 500),
            cancellationToken);
        var invoice = invoices.FirstOrDefault(x => x.Id == invoiceId);
        return invoice ?? throw new KeyNotFoundException("Finance invoice was not found.");
    }

    private static InvoiceReviewDecision BuildReview(
        FinanceInvoiceDto invoice,
        FinancePolicyConfigurationDto policy,
        InvoicePaymentContext paymentContext)
    {
        var statusIsAwaitingApproval =
            string.Equals(invoice.Status, "awaiting_approval", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invoice.Status, "pending_approval", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invoice.Status, "pending", StringComparison.OrdinalIgnoreCase);
        var currencyMismatch = !string.Equals(invoice.Currency, policy.ApprovalCurrency, StringComparison.OrdinalIgnoreCase);
        var exceedsThreshold = invoice.Amount > policy.InvoiceApprovalThreshold;
        var overdue = invoice.DueUtc.Date < DateTime.UtcNow.Date;
        var dueSoon = !overdue && invoice.DueUtc.Date <= DateTime.UtcNow.Date.AddDays(7);
        var hasPaymentActivity = paymentContext.TransactionCount > 0;

        var riskLevel = currencyMismatch || exceedsThreshold || paymentContext.TotalPaidAmount >= invoice.Amount
            ? "high"
            : overdue || dueSoon || hasPaymentActivity
                ? "medium"
                : "low";
        var classification = currencyMismatch
            ? "policy_currency_exception"
            : exceedsThreshold
                ? "over_threshold_invoice"
                : overdue
                    ? "overdue_invoice"
                    : hasPaymentActivity
                        ? "invoice_with_payment_activity"
                    : dueSoon
                        ? "due_soon_invoice"
                        : "standard_invoice";

        var requiresHumanApproval = statusIsAwaitingApproval && riskLevel is "high" or "medium";
        var recommendedAction = !statusIsAwaitingApproval
            ? "no_action"
            : requiresHumanApproval
                ? "request_human_approval"
                : "approve";
        var confidence = riskLevel switch
        {
            "high" => 0.88m,
            "medium" => 0.76m,
            _ => 0.84m
        };

        var rationaleParts = new List<string>
        {
            $"Invoice status is '{invoice.Status}'.",
            $"Invoice amount is {invoice.Amount:0.##} {invoice.Currency}; policy threshold is {policy.InvoiceApprovalThreshold:0.##} {policy.ApprovalCurrency}."
        };
        if (currencyMismatch)
        {
            rationaleParts.Add("Invoice currency does not match the approval policy currency.");
        }
        if (exceedsThreshold)
        {
            rationaleParts.Add("Invoice exceeds the configured approval threshold.");
        }
        if (overdue)
        {
            rationaleParts.Add("Invoice is overdue.");
        }
        else if (dueSoon)
        {
            rationaleParts.Add("Invoice is due within seven days.");
        }
        if (hasPaymentActivity)
        {
            rationaleParts.Add($"Related payment context contains {paymentContext.TransactionCount} transaction(s) totaling {paymentContext.TotalPaidAmount:0.##} {paymentContext.Currency}.");
        }
        if (!statusIsAwaitingApproval)
        {
            rationaleParts.Add("Invoice is not currently awaiting approval, so no approval action is recommended.");
        }

        return new InvoiceReviewDecision(
            classification,
            riskLevel,
            recommendedAction,
            string.Join(" ", rationaleParts),
            confidence,
            requiresHumanApproval);
    }

    private async Task<ApprovalRequestDto?> CreateApprovalRequestAsync(
        ReviewFinanceInvoiceWorkflowCommand command,
        FinanceInvoiceDto invoice,
        FinancePolicyConfigurationDto policy,
        InvoicePaymentContext paymentContext,
        InvoiceReviewDecision review,
        Guid taskId,
        Guid? reviewAgentId,
        CancellationToken cancellationToken)
    {
        var thresholdContext = BuildOutputPayload(command.CompanyId, invoice, policy, paymentContext, review, taskId, null, command.WorkflowInstanceId);
        thresholdContext["workflowRunId"] = command.WorkflowInstanceId.HasValue ? JsonValue.Create(command.WorkflowInstanceId.Value) : null;
        thresholdContext["approvalLinkReason"] = JsonValue.Create("invoice_review_requires_human_approval");

        return await _approvalRequestService.CreateAsync(
            command.CompanyId,
            new CreateApprovalRequestCommand(
                "task",
                taskId,
                "agent",
                reviewAgentId ?? Guid.NewGuid(),
                "invoice_review",
                thresholdContext,
                FinanceApproverRole,
                null,
                [new CreateApprovalStepInput(1, "role", FinanceApproverRole)]),
            cancellationToken);
    }

    private async Task<ApprovalRequestDto?> TryLoadApprovalAsync(
        Guid companyId,
        Guid approvalRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _approvalRequestService.GetAsync(companyId, approvalRequestId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private async Task<WorkTask?> FindExistingReviewTaskAsync(
        Guid companyId,
        Guid invoiceId,
        Guid? workflowInstanceId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Type == ReviewTaskType);

        if (workflowInstanceId.HasValue)
        {
            query = query.Where(x => x.WorkflowInstanceId == workflowInstanceId.Value);
        }

        var candidates = await query
            .Where(x => x.CorrelationId == BuildInvoiceReviewCorrelationId(companyId, invoiceId))
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(1)
            .ToListAsync(cancellationToken);

        candidates.AddRange(await query
            .Where(x => x.CorrelationId != BuildInvoiceReviewCorrelationId(companyId, invoiceId))
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(100)
            .ToListAsync(cancellationToken));

        return candidates.FirstOrDefault(x =>
            TryGetGuid(x.InputPayload, "invoiceId") == invoiceId ||
            TryGetGuid(x.OutputPayload, "invoiceId") == invoiceId);
    }

    private async Task<Guid?> ResolveLauraAgentIdAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.CanReceiveAssignments &&
                (x.TemplateId == LauraFinanceAgentSeedData.TemplateId ||
                 x.DisplayName.Contains("Laura") ||
                 x.Department == "Finance"))
            .OrderByDescending(x => x.TemplateId == LauraFinanceAgentSeedData.TemplateId)
            .ThenByDescending(x => x.DisplayName.Contains("Laura"))
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static Dictionary<string, JsonNode?> BuildTaskInput(
        ReviewFinanceInvoiceWorkflowCommand command,
        FinanceInvoiceDto invoice,
        FinancePolicyConfigurationDto policy,
        InvoicePaymentContext paymentContext) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["companyId"] = JsonValue.Create(command.CompanyId),
            ["invoiceId"] = JsonValue.Create(invoice.Id),
            ["workflowInstanceId"] = command.WorkflowInstanceId.HasValue ? JsonValue.Create(command.WorkflowInstanceId.Value) : null,
            ["agentId"] = command.AgentId.HasValue ? JsonValue.Create(command.AgentId.Value) : null,
            ["invoiceNumber"] = JsonValue.Create(invoice.InvoiceNumber),
            ["counterpartyId"] = JsonValue.Create(invoice.CounterpartyId),
            ["counterpartyName"] = JsonValue.Create(invoice.CounterpartyName),
            ["amount"] = JsonValue.Create(invoice.Amount),
            ["currency"] = JsonValue.Create(invoice.Currency),
            ["status"] = JsonValue.Create(invoice.Status),
            ["approvalThreshold"] = JsonValue.Create(policy.InvoiceApprovalThreshold),
            ["approvalCurrency"] = JsonValue.Create(policy.ApprovalCurrency),
            ["linkedDocumentId"] = invoice.LinkedDocument is null ? null : JsonValue.Create(invoice.LinkedDocument.Id),
            ["linkedDocumentTitle"] = invoice.LinkedDocument is null ? null : JsonValue.Create(invoice.LinkedDocument.Title),
            ["relatedPaymentContext"] = BuildRelatedPaymentNode(paymentContext)
        };

    private static Dictionary<string, JsonNode?> BuildOutputPayload(
        Guid companyId,
        FinanceInvoiceDto invoice,
        FinancePolicyConfigurationDto policy,
        InvoicePaymentContext paymentContext,
        InvoiceReviewDecision review,
        Guid taskId,
        Guid? approvalRequestId,
        Guid? workflowInstanceId)
    {
        var workflowOutput = FinanceWorkflowOutputSchemas.Create(
            review.InvoiceClassification,
            review.RiskLevel,
            review.RecommendedAction,
            review.Rationale,
            review.ConfidenceScore,
            "invoice_review");
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["companyId"] = JsonValue.Create(companyId),
            ["invoiceId"] = JsonValue.Create(invoice.Id),
            ["workflowInstanceId"] = workflowInstanceId.HasValue ? JsonValue.Create(workflowInstanceId.Value) : null,
            ["workflowRunId"] = workflowInstanceId.HasValue ? JsonValue.Create(workflowInstanceId.Value) : null,
            ["taskId"] = JsonValue.Create(taskId),
            ["approvalRequestId"] = approvalRequestId.HasValue ? JsonValue.Create(approvalRequestId.Value) : null,
            ["classification"] = JsonValue.Create(review.InvoiceClassification),
            ["invoiceClassification"] = JsonValue.Create(review.InvoiceClassification),
            ["riskLevel"] = JsonValue.Create(review.RiskLevel),
            ["confidenceScore"] = JsonValue.Create(review.ConfidenceScore),
            ["requiresHumanApproval"] = JsonValue.Create(review.RequiresHumanApproval),
            ["invoiceAmount"] = JsonValue.Create(invoice.Amount),
            ["invoiceCurrency"] = JsonValue.Create(invoice.Currency),
            ["invoiceStatus"] = JsonValue.Create(invoice.Status),
            ["invoiceDueUtc"] = JsonValue.Create(invoice.DueUtc),
            ["vendorId"] = JsonValue.Create(invoice.CounterpartyId),
            ["vendorName"] = JsonValue.Create(invoice.CounterpartyName),
            ["policyInvoiceApprovalThreshold"] = JsonValue.Create(policy.InvoiceApprovalThreshold),
            ["policyApprovalCurrency"] = JsonValue.Create(policy.ApprovalCurrency),
            ["relatedPaymentContext"] = BuildRelatedPaymentNode(paymentContext)
        };
        FinanceWorkflowOutputSchemas.CopyToPayload(payload, workflowOutput);
        payload["invoiceClassification"] = JsonValue.Create(workflowOutput.Classification);
        payload["confidenceScore"] = JsonValue.Create(workflowOutput.Confidence);
        return payload;
    }

    private async Task<InvoicePaymentContext> LoadRelatedPaymentContextAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var transactions = await _financeReadService.GetTransactionsAsync(
            new GetFinanceTransactionsQuery(companyId, null, null, 500),
            cancellationToken);

        var relatedTransactions = transactions
            .Where(x => x.InvoiceId == invoiceId)
            .ToList();

        if (relatedTransactions.Count == 0)
        {
            return new InvoicePaymentContext(0, 0m, "UNKNOWN", null);
        }

        var currencies = relatedTransactions
            .Select(x => x.Currency)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InvoicePaymentContext(
            relatedTransactions.Count,
            relatedTransactions.Sum(x => Math.Abs(x.Amount)),
            currencies.Count == 1 ? currencies[0] : "MIXED",
            relatedTransactions.Max(x => x.TransactionUtc));
    }

    private async Task PersistWorkflowOutputAsync(
        Guid companyId,
        Guid? workflowInstanceId,
        IReadOnlyDictionary<string, JsonNode?> outputPayload,
        CancellationToken cancellationToken)
    {
        if (!workflowInstanceId.HasValue)
        {
            return;
        }

        var workflow = await _dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == workflowInstanceId.Value, cancellationToken);
        if (workflow is null)
        {
            return;
        }

        workflow.UpdateState(workflow.State, workflow.CurrentStep, CloneNodes(outputPayload));
    }

    private static JsonObject BuildRelatedPaymentNode(InvoicePaymentContext paymentContext) =>
        new()
        {
            ["transactionCount"] = JsonValue.Create(paymentContext.TransactionCount),
            ["totalPaidAmount"] = JsonValue.Create(paymentContext.TotalPaidAmount),
            ["currency"] = JsonValue.Create(paymentContext.Currency),
            ["latestPaymentUtc"] = paymentContext.LatestPaymentUtc.HasValue ? JsonValue.Create(paymentContext.LatestPaymentUtc.Value) : null
        };

    private static InvoiceReviewDecision? BuildReviewFromTask(WorkTask task)
    {
        var classification = TryGetString(task.OutputPayload, "invoiceClassification");
        var riskLevel = TryGetString(task.OutputPayload, "riskLevel");
        var recommendedAction = TryGetString(task.OutputPayload, "recommendedAction");
        var rationale = TryGetString(task.OutputPayload, "rationale");
        var confidenceScore = TryGetDecimal(task.OutputPayload, "confidenceScore");
        var requiresHumanApproval = TryGetBoolean(task.OutputPayload, "requiresHumanApproval");

        return string.IsNullOrWhiteSpace(classification) ||
            string.IsNullOrWhiteSpace(riskLevel) ||
            string.IsNullOrWhiteSpace(recommendedAction) ||
            string.IsNullOrWhiteSpace(rationale) ||
            !confidenceScore.HasValue ||
            !requiresHumanApproval.HasValue
                ? null
                : new InvoiceReviewDecision(classification, riskLevel, recommendedAction, rationale, confidenceScore.Value, requiresHumanApproval.Value);
    }

    private static InvoicePaymentContext? BuildPaymentContextFromTask(WorkTask task)
    {
        if (!task.OutputPayload.TryGetValue("relatedPaymentContext", out var paymentNode) ||
            paymentNode is not JsonObject paymentObject)
        {
            return null;
        }

        var paymentPayload = paymentObject.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var transactionCount = TryGetInt(paymentPayload, "transactionCount");
        var totalPaidAmount = TryGetDecimal(paymentPayload, "totalPaidAmount");
        var currency = TryGetString(paymentPayload, "currency");
        var latestPaymentUtc = TryGetDateTime(paymentPayload, "latestPaymentUtc");

        return transactionCount.HasValue && totalPaidAmount.HasValue && !string.IsNullOrWhiteSpace(currency)
            ? new InvoicePaymentContext(transactionCount.Value, totalPaidAmount.Value, currency, latestPaymentUtc)
            : null;
    }

    private static string BuildInvoiceReviewCorrelationId(Guid companyId, Guid invoiceId) =>
        $"{ReviewCorrelationPrefix}:{companyId:N}:{invoiceId:N}";

    private static void Validate(Guid companyId, Guid invoiceId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        }
    }

    private static string? TryGetString(IReadOnlyDictionary<string, JsonNode?>? payload, string key)
    {
        if (payload is null ||
            !payload.TryGetValue(key, out var node) ||
            node is not JsonValue value ||
            !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static Guid? TryGetGuid(IReadOnlyDictionary<string, JsonNode?>? payload, string key)
    {
        if (payload is null ||
            !payload.TryGetValue(key, out var node) ||
            node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty)
        {
            return guid;
        }

        return value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid) && guid != Guid.Empty
            ? guid
            : null;
    }

    private static decimal? TryGetDecimal(IReadOnlyDictionary<string, JsonNode?>? payload, string key)
    {
        if (payload is null ||
            !payload.TryGetValue(key, out var node) ||
            node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && decimal.TryParse(text, out number)
            ? number
            : null;
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, JsonNode?>? payload, string key)
    {
        if (payload is null ||
            !payload.TryGetValue(key, out var node) ||
            node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out number)
            ? number
            : null;
    }

    private static DateTime? TryGetDateTime(IReadOnlyDictionary<string, JsonNode?>? payload, string key)
    {
        if (payload is null ||
            !payload.TryGetValue(key, out var node) ||
            node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<DateTime>(out var dateTime)
            ? dateTime
            : value.TryGetValue<string>(out var text) && DateTime.TryParse(text, out dateTime)
                ? dateTime
                : null;
    }

    private static bool? TryGetBoolean(IReadOnlyDictionary<string, JsonNode?>? payload, string key)
    {
        if (payload is null ||
            !payload.TryGetValue(key, out var node) ||
            node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            return flag;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out flag)
            ? flag
            : null;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private sealed record InvoicePaymentContext(
        int TransactionCount,
        decimal TotalPaidAmount,
        string Currency,
        DateTime? LatestPaymentUtc);

    private sealed record InvoiceReviewDecision(
        string InvoiceClassification,
        string RiskLevel,
        string RecommendedAction,
        string Rationale,
        decimal ConfidenceScore,
        bool RequiresHumanApproval);
}
