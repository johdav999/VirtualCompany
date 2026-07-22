using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public sealed class SupportRefundRequest : ICompanyOwnedEntity
{
    private SupportRefundRequest()
    {
    }

    public SupportRefundRequest(Guid id, Guid companyId, Guid supportCaseId, decimal amount, string currency, string reasonCode, string explanation, Guid? invoiceId, Guid? paymentId, Guid? requestedByAgentId, Guid? requestedByUserId)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        Amount = amount <= 0 ? throw new ArgumentOutOfRangeException(nameof(amount)) : amount;
        Currency = SupportEntityText.NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        ReasonCode = SupportEntityText.NormalizeRequired(reasonCode, nameof(reasonCode), 80);
        Explanation = SupportEntityText.NormalizeRequired(explanation, nameof(explanation), 2000);
        InvoiceId = SupportEntityText.NormalizeOptionalId(invoiceId, nameof(invoiceId));
        PaymentId = SupportEntityText.NormalizeOptionalId(paymentId, nameof(paymentId));
        RequestedByAgentId = SupportEntityText.NormalizeOptionalId(requestedByAgentId, nameof(requestedByAgentId));
        RequestedByUserId = SupportEntityText.NormalizeOptionalId(requestedByUserId, nameof(requestedByUserId));
        Status = SupportRefundRequestStatuses.PendingApproval;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public Guid? InvoiceId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? RequestedByAgentId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? FinanceActionReferenceId { get; private set; }
    public Guid? ProviderWriteRequestId { get; private set; }
    public Guid? ProviderApprovalRequestId { get; private set; }
    public string Status { get; private set; } = null!;
    public string? LastFailureSummary { get; private set; }
    public DateTime? ExecutionRequestedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;

    public void LinkApproval(Guid approvalRequestId)
    {
        if (ApprovalRequestId.HasValue && ApprovalRequestId.Value != approvalRequestId)
        {
            throw new InvalidOperationException("The refund request is already linked to another approval.");
        }

        ApprovalRequestId = approvalRequestId == Guid.Empty ? throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId)) : approvalRequestId;
        UpdatedUtc = DateTime.UtcNow;
    }

    public bool ApplyApprovalOutcome(string approvalStatus)
    {
        var next = approvalStatus?.Trim().ToLowerInvariant() switch
        {
            "approved" => SupportRefundRequestStatuses.Approved,
            "rejected" => SupportRefundRequestStatuses.Rejected,
            "expired" => SupportRefundRequestStatuses.Expired,
            "cancelled" => SupportRefundRequestStatuses.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(approvalStatus), approvalStatus, "Unsupported refund approval outcome.")
        };

        if (string.Equals(Status, next, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(next, SupportRefundRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase) &&
            Status is (SupportRefundRequestStatuses.Queued or
                SupportRefundRequestStatuses.Executing or
                SupportRefundRequestStatuses.ReconciliationRequired or
                SupportRefundRequestStatuses.Completed or
                SupportRefundRequestStatuses.Executed))
        {
            return false;
        }

        if (!string.Equals(Status, SupportRefundRequestStatuses.PendingApproval, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refund approval cannot transition from '{Status}' to '{next}'.");
        }

        Status = next;
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public bool LinkFinanceAction(Guid financeActionReferenceId)
    {
        if (financeActionReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Finance action reference is required.", nameof(financeActionReferenceId));
        }

        if (FinanceActionReferenceId.HasValue)
        {
            if (FinanceActionReferenceId.Value == financeActionReferenceId)
            {
                return false;
            }

            throw new InvalidOperationException("The refund request is already linked to another finance action.");
        }

        if (!string.Equals(Status, SupportRefundRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only approved refund requests can create finance actions.");
        }

        FinanceActionReferenceId = financeActionReferenceId;
        Status = SupportRefundRequestStatuses.Queued;
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkPendingFinanceApproval(Guid? providerWriteRequestId = null, Guid? providerApprovalRequestId = null)
    {
        if (Status == SupportRefundRequestStatuses.PendingFinanceApproval)
        {
            return false;
        }

        EnsureExecutionState(SupportRefundRequestStatuses.Queued, SupportRefundRequestStatuses.Failed);
        Status = SupportRefundRequestStatuses.PendingFinanceApproval;
        ProviderWriteRequestId = providerWriteRequestId == Guid.Empty ? null : providerWriteRequestId ?? ProviderWriteRequestId;
        ProviderApprovalRequestId = providerApprovalRequestId == Guid.Empty ? null : providerApprovalRequestId ?? ProviderApprovalRequestId;
        ExecutionRequestedUtc ??= DateTime.UtcNow;
        LastFailureSummary = null;
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public bool ApplyFinanceExecutionStatus(string financeStatus, string? safeFailureSummary = null)
    {
        var next = financeStatus?.Trim().ToLowerInvariant() switch
        {
            "awaiting_approval" => SupportRefundRequestStatuses.PendingFinanceApproval,
            "approved" or "executing" => SupportRefundRequestStatuses.Executing,
            "executed" => SupportRefundRequestStatuses.Completed,
            "failed" => SupportRefundRequestStatuses.Failed,
            "rejected" or "expired" or "cancelled" => SupportRefundRequestStatuses.Cancelled,
            _ => SupportRefundRequestStatuses.ReconciliationRequired
        };
        if (Status == next)
        {
            return false;
        }

        if (Status is SupportRefundRequestStatuses.Completed or SupportRefundRequestStatuses.Cancelled)
        {
            throw new InvalidOperationException($"Completed or cancelled refunds cannot transition to '{next}'.");
        }

        Status = next;
        LastFailureSummary = next is SupportRefundRequestStatuses.Failed or SupportRefundRequestStatuses.ReconciliationRequired
            ? SupportEntityText.NormalizeOptional(safeFailureSummary, nameof(safeFailureSummary), 1000) ?? "The accounting-system action needs review."
            : null;
        CompletedUtc = next == SupportRefundRequestStatuses.Completed ? DateTime.UtcNow : null;
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public bool CancelBeforeExecution()
    {
        if (Status == SupportRefundRequestStatuses.Cancelled) return false;
        EnsureExecutionState(SupportRefundRequestStatuses.PendingApproval, SupportRefundRequestStatuses.Approved, SupportRefundRequestStatuses.Queued, SupportRefundRequestStatuses.Failed);
        if (ProviderWriteRequestId.HasValue && Status != SupportRefundRequestStatuses.Failed)
            throw new InvalidOperationException("Reconcile the accounting-system request before cancelling it.");
        Status = SupportRefundRequestStatuses.Cancelled;
        LastFailureSummary = null;
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkReconciliationRequired(string? safeSummary)
    {
        if (Status == SupportRefundRequestStatuses.Completed) return false;
        if (Status == SupportRefundRequestStatuses.Cancelled) throw new InvalidOperationException("Cancelled refunds cannot be reconciled.");
        Status = SupportRefundRequestStatuses.ReconciliationRequired;
        LastFailureSummary = SupportEntityText.NormalizeOptional(safeSummary, nameof(safeSummary), 1000) ?? "The accounting-system outcome could not be confirmed.";
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    private void EnsureExecutionState(params string[] allowed)
    {
        if (!allowed.Contains(Status, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refund action cannot continue from '{Status}'.");
        }
    }
}

