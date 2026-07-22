namespace VirtualCompany.Domain.Entities;
public sealed class SalesAgentRecommendation : ICompanyOwnedEntity
{
    private SalesAgentRecommendation()
    {
    }

    public SalesAgentRecommendation(
        Guid id,
        Guid companyId,
        string recommendation,
        string rationale,
        Guid? leadId = null,
        Guid? dealId = null,
        string status = SalesStatuses.Open,
        string category = "follow_up",
        string triggerCondition = "manual_review",
        string actionType = "create_draft_reply",
        string riskLevel = "medium",
        bool requiresApproval = true,
        string approvalStatus = SalesStatuses.WaitingForApproval,
        string executionStatus = SalesStatuses.Pending,
        string? dedupeKey = null,
        decimal? confidence = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        LeadId = SalesEntityText.NormalizeOptionalId(leadId, nameof(leadId));
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId));
        Recommendation = SalesEntityText.NormalizeRequired(recommendation, nameof(recommendation), 1000);
        Rationale = SalesEntityText.NormalizeRequired(rationale, nameof(rationale), 2000);
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        Category = SalesEntityText.NormalizeRequired(category, nameof(category), 64).ToLowerInvariant();
        TriggerCondition = SalesEntityText.NormalizeRequired(triggerCondition, nameof(triggerCondition), 80).ToLowerInvariant();
        ActionType = SalesEntityText.NormalizeRequired(actionType, nameof(actionType), 80).ToLowerInvariant();
        RiskLevel = SalesEntityText.NormalizeRequired(riskLevel, nameof(riskLevel), 32).ToLowerInvariant();
        RequiresApproval = requiresApproval;
        ApprovalStatus = SalesEntityText.NormalizeRequired(approvalStatus, nameof(approvalStatus), 32).ToLowerInvariant();
        ExecutionStatus = SalesEntityText.NormalizeRequired(executionStatus, nameof(executionStatus), 32).ToLowerInvariant();
        DedupeKey = SalesEntityText.NormalizeOptional(dedupeKey, nameof(dedupeKey), 256);
        Confidence = confidence;
        CreatedUtc = DateTime.UtcNow;
        ExecutionIdempotencyKey = $"sales-recommendation:{CompanyId:N}:{Id:N}:{ActionType}";
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? LeadId { get; private set; }
    public Guid? DealId { get; private set; }
    public string Recommendation { get; private set; } = null!;
    public string Rationale { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public string TriggerCondition { get; private set; } = null!;
    public string ActionType { get; private set; } = null!;
    public string RiskLevel { get; private set; } = null!;
    public bool RequiresApproval { get; private set; }
    public string ApprovalStatus { get; private set; } = null!;
    public string ExecutionStatus { get; private set; } = null!;
    public string? FailureSummary { get; private set; }
    public string? DedupeKey { get; private set; }
    public decimal? Confidence { get; private set; }
    public string Status { get; private set; } = null!;
    public int ExecutionAttemptCount { get; private set; }
    public string? LastExecutionErrorCode { get; private set; }
    public string? Provider { get; private set; }
    public Guid? MailboxConnectionId { get; private set; }
    public string? ProviderThreadId { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderDraftId { get; private set; }
    public Guid? ActivityId { get; private set; }
    public string ExecutionIdempotencyKey { get; private set; } = null!;
    public DateTime? ExecutedUtc { get; private set; }
    public bool CanRetryExecution => ExecutionStatus == SalesStatuses.RetryableFailed;
    public bool HasSucceeded => ExecutionStatus == SalesStatuses.Completed || ExecutionStatus == SalesStatuses.DraftCreated;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Lead? Lead { get; private set; }
    public Deal? Deal { get; private set; }
    public ICollection<SalesActionApproval> Approvals { get; } = new List<SalesActionApproval>();

    public void MarkApproved()
    {
        ApprovalStatus = SalesStatuses.Approved;
        Status = SalesStatuses.Approved;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkExecuting(Guid mailboxConnectionId, string provider, string? providerThreadId)
    {
        if (ApprovalStatus != SalesStatuses.Approved)
        {
            throw new InvalidOperationException("Recommendation must be approved before execution.");
        }

        if (HasSucceeded)
        {
            return;
        }

        ExecutionStatus = SalesStatuses.InProgress;
        ExecutionAttemptCount++;
        MailboxConnectionId = SalesEntityText.NormalizeOptionalId(mailboxConnectionId, nameof(mailboxConnectionId));
        Provider = SalesEntityText.NormalizeOptional(provider, nameof(provider), 64);
        ProviderThreadId = SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256) ?? ProviderThreadId;
        LastExecutionErrorCode = null;
        FailureSummary = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkDraftCreated(string providerDraftId, string? providerThreadId, Guid activityId)
    {
        if (HasSucceeded)
        {
            return;
        }

        ExecutionStatus = SalesStatuses.DraftCreated;
        Status = SalesStatuses.Completed;
        ProviderDraftId = SalesEntityText.NormalizeRequired(providerDraftId, nameof(providerDraftId), 256);
        ProviderThreadId = SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256) ?? ProviderThreadId;
        ActivityId = SalesEntityText.NormalizeOptionalId(activityId, nameof(activityId));
        ExecutedUtc = DateTime.UtcNow;
        LastExecutionErrorCode = null;
        FailureSummary = null;
        UpdatedUtc = ExecutedUtc.Value;
    }

    public void MarkSent(string providerMessageId, string? providerThreadId, Guid activityId)
    {
        if (HasSucceeded)
        {
            return;
        }

        ExecutionStatus = SalesStatuses.Completed;
        Status = SalesStatuses.Completed;
        ProviderMessageId = SalesEntityText.NormalizeRequired(providerMessageId, nameof(providerMessageId), 256);
        ProviderThreadId = SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256) ?? ProviderThreadId;
        ActivityId = SalesEntityText.NormalizeOptionalId(activityId, nameof(activityId));
        ExecutedUtc = DateTime.UtcNow;
        LastExecutionErrorCode = null;
        FailureSummary = null;
        UpdatedUtc = ExecutedUtc.Value;
    }

    public void MarkFailed(string errorCode, string failureSummary, bool retryable)
    {
        ExecutionStatus = retryable ? SalesStatuses.RetryableFailed : SalesStatuses.Failed;
        Status = SalesStatuses.Failed;
        LastExecutionErrorCode = SalesEntityText.NormalizeOptional(errorCode, nameof(errorCode), 120);
        FailureSummary = SalesEntityText.NormalizeOptional(failureSummary, nameof(failureSummary), 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkRetrying()
    {
        if (!CanRetryExecution)
        {
            throw new InvalidOperationException("Only retryable failed recommendation executions can be retried.");
        }

        ExecutionStatus = SalesStatuses.InProgress;
        Status = SalesStatuses.Open;
        LastExecutionErrorCode = null;
        FailureSummary = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void EnsureExecutionKey()
    {
        ExecutionIdempotencyKey = SalesEntityText.NormalizeOptional(ExecutionIdempotencyKey, nameof(ExecutionIdempotencyKey), 256)
            ?? $"sales-recommendation:{CompanyId:N}:{Id:N}:{ActionType}";
    }
}

