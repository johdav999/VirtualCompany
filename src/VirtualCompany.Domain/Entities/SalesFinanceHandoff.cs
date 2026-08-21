namespace VirtualCompany.Domain.Entities;
public sealed class SalesFinanceHandoff : ICompanyOwnedEntity
{
    private SalesFinanceHandoff() { }

    public SalesFinanceHandoff(
        Guid id,
        Guid companyId,
        Guid dealId,
        string summary,
        string documentType,
        string dedupeKey,
        string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId))!.Value;
        Summary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 1000);
        DocumentType = NormalizeDocumentType(documentType);
        DedupeKey = SalesEntityText.NormalizeRequired(dedupeKey, nameof(dedupeKey), 256);
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 256);
        ExternalSystem = "virtual_company";
        Status = SalesStatuses.WaitingForApproval;
        ApprovalStatus = SalesStatuses.WaitingForApproval;
        ExecutionStatus = SalesStatuses.Pending;
        RequestedUtc = DateTime.UtcNow;
        CreatedUtc = RequestedUtc;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DealId { get; private set; }
    public string Status { get; private set; } = null!;
    public string ApprovalStatus { get; private set; } = null!;
    public string ExecutionStatus { get; private set; } = null!;
    public string DocumentType { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string DedupeKey { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public Guid? ApprovalId { get; private set; }
    public Guid? WriteRequestId { get; private set; }
    public string ExternalSystem { get; private set; } = null!;
    public string? ExternalDocumentId { get; private set; }
    public string? ExternalDocumentNumber { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public int ExecutionAttemptCount { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? ExecutionStartedUtc { get; private set; }
    public DateTime? ExecutedUtc { get; private set; }
    public DateTime? FailedUtc { get; private set; }
    public DateTime? RetriedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Deal Deal { get; private set; } = null!;

    public bool CanRetry => ExecutionStatus == SalesStatuses.RetryableFailed || ExecutionStatus == SalesStatuses.Failed;
    public bool HasExternalDocument => !string.IsNullOrWhiteSpace(ExternalDocumentId);

    public void AttachApproval(Guid approvalId, Guid writeRequestId)
    {
        if (approvalId == Guid.Empty)
        {
            throw new ArgumentException("ApprovalId is required.", nameof(approvalId));
        }

        if (writeRequestId == Guid.Empty)
        {
            throw new ArgumentException("WriteRequestId is required.", nameof(writeRequestId));
        }

        ApprovalId = approvalId;
        WriteRequestId = writeRequestId;
        Status = SalesStatuses.WaitingForApproval;
        ApprovalStatus = SalesStatuses.WaitingForApproval;
        ExecutionStatus = SalesStatuses.Pending;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetDestination(string destinationKey)
    {
        ExternalSystem = SalesEntityText.NormalizeRequired(destinationKey, nameof(destinationKey), 64).ToLowerInvariant();
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkFinanceReviewRequired(string destinationKey, string summary)
    {
        SetDestination(destinationKey);
        Summary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 1000);
        Status = SalesStatuses.Open;
        ApprovalStatus = "not_required";
        ExecutionStatus = SalesStatuses.Pending;
        ApprovalId = null;
        WriteRequestId = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkApproved()
    {
        if (HasExternalDocument)
        {
            return;
        }

        ApprovalStatus = SalesStatuses.Approved;
        Status = SalesStatuses.Approved;
        ApprovedUtc ??= DateTime.UtcNow;
        FailureSummary = null;
        LastErrorCode = null;
        UpdatedUtc = ApprovedUtc.Value;
    }

    public void MarkExecutionStarted()
    {
        if (HasExternalDocument)
        {
            return;
        }

        ExecutionStatus = SalesStatuses.InProgress;
        Status = SalesStatuses.InProgress;
        ExecutionAttemptCount++;
        ExecutionStartedUtc = DateTime.UtcNow;
        FailureSummary = null;
        LastErrorCode = null;
        UpdatedUtc = ExecutionStartedUtc.Value;
    }

    public void MarkCompleted(string externalDocumentId, string? externalDocumentNumber)
    {
        if (HasExternalDocument)
        {
            return;
        }

        ExternalDocumentId = SalesEntityText.NormalizeRequired(externalDocumentId, nameof(externalDocumentId), 256);
        ExternalDocumentNumber = SalesEntityText.NormalizeOptional(externalDocumentNumber, nameof(externalDocumentNumber), 128);
        Status = SalesStatuses.Completed;
        ApprovalStatus = SalesStatuses.Approved;
        ExecutionStatus = SalesStatuses.Completed;
        FailureSummary = null;
        LastErrorCode = null;
        ExecutedUtc = DateTime.UtcNow;
        UpdatedUtc = ExecutedUtc.Value;
    }

    public void MarkFailed(string errorCode, string failureSummary, bool retryable)
    {
        if (HasExternalDocument)
        {
            return;
        }

        ExecutionStatus = retryable ? SalesStatuses.RetryableFailed : SalesStatuses.Failed;
        Status = SalesStatuses.Failed;
        LastErrorCode = SalesEntityText.NormalizeOptional(errorCode, nameof(errorCode), 120);
        FailureSummary = SalesEntityText.NormalizeOptional(failureSummary, nameof(failureSummary), 1000);
        FailedUtc = DateTime.UtcNow;
        UpdatedUtc = FailedUtc.Value;
    }

    public void MarkRetrying()
    {
        if (!CanRetry)
        {
            throw new InvalidOperationException("Only failed finance handoffs can be retried.");
        }

        ExecutionStatus = SalesStatuses.InProgress;
        Status = SalesStatuses.InProgress;
        RetriedUtc = DateTime.UtcNow;
        FailureSummary = null;
        LastErrorCode = null;
        UpdatedUtc = RetriedUtc.Value;
    }

    private static string NormalizeDocumentType(string documentType)
    {
        var value = SalesEntityText.NormalizeRequired(documentType, nameof(documentType), 32).ToLowerInvariant();
        return value is "quote" or "invoice"
            ? value
            : throw new ArgumentException("Finance handoff document type must be quote or invoice.", nameof(documentType));
    }
}

