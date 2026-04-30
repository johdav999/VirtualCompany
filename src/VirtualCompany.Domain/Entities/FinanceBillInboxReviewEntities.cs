namespace VirtualCompany.Domain.Entities;

public static class FinanceBillInboxStatuses
{
    public const string Detected = "detected";
    public const string Extracted = "extracted";
    public const string NeedsReview = "needs_review";
    public const string ProposedForApproval = "proposed_for_approval";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string SentToPaymentExported = "sent_to_payment_exported";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Detected,
        Extracted,
        NeedsReview,
        ProposedForApproval,
        Approved,
        Rejected,
        SentToPaymentExported
    };

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Finance bill inbox status is required.", nameof(value));
        }

        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return Allowed.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported finance bill inbox status.");
    }
}

public sealed class FinanceBillReviewState : ICompanyOwnedEntity
{
    private static readonly IReadOnlySet<string> ActiveReviewStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FinanceBillInboxStatuses.Detected, FinanceBillInboxStatuses.Extracted, FinanceBillInboxStatuses.NeedsReview, FinanceBillInboxStatuses.ProposedForApproval
    };

    private readonly List<FinanceBillReviewAction> _actions = [];

    private FinanceBillReviewState()
    {
    }

    public FinanceBillReviewState(
        Guid id,
        Guid companyId,
        Guid detectedBillId,
        string status,
        string proposalSummary,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (detectedBillId == Guid.Empty)
        {
            throw new ArgumentException("DetectedBillId is required.", nameof(detectedBillId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DetectedBillId = detectedBillId;
        Status = FinanceBillInboxStatuses.Normalize(status);
        ProposalSummary = NormalizeProposalSummary(proposalSummary);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DetectedBillId { get; private set; }
    public string Status { get; private set; } = FinanceBillInboxStatuses.Detected;
    public string ProposalSummary { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public DetectedBill DetectedBill { get; private set; } = null!;
    public IReadOnlyCollection<FinanceBillReviewAction> Actions => _actions;

    public FinanceBillReviewAction Approve(Guid? actorUserId, string actorDisplayName, string rationale, DateTime occurredUtc, bool hasUnresolvedValidationFailures)
    {
        if (hasUnresolvedValidationFailures)
        {
            throw new InvalidOperationException("Finance bill approval is blocked while validation failures are unresolved.");
        }

        EnsureActiveReviewStatus("approve");
        return Transition("approve", FinanceBillInboxStatuses.Approved, actorUserId, actorDisplayName, rationale, occurredUtc);
    }

    public FinanceBillReviewAction Reject(Guid? actorUserId, string actorDisplayName, string rationale, DateTime occurredUtc)
    {
        EnsureActiveReviewStatus("reject");
        return Transition("reject", FinanceBillInboxStatuses.Rejected, actorUserId, actorDisplayName, rationale, occurredUtc);
    }

    public FinanceBillReviewAction RequestClarification(Guid? actorUserId, string actorDisplayName, string rationale, DateTime occurredUtc)
    {
        EnsureActiveReviewStatus("request clarification for");
        return Transition("clarification_requested", FinanceBillInboxStatuses.NeedsReview, actorUserId, actorDisplayName, rationale, occurredUtc);
    }

    private FinanceBillReviewAction Transition(string action, string newStatus, Guid? actorUserId, string actorDisplayName, string rationale, DateTime occurredUtc)
    {
        var priorStatus = FinanceBillInboxStatuses.Normalize(Status);
        Status = FinanceBillInboxStatuses.Normalize(newStatus);
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        var history = new FinanceBillReviewAction(Guid.NewGuid(), CompanyId, Id, DetectedBillId, action, actorUserId, actorDisplayName, priorStatus, Status, rationale, UpdatedUtc);
        _actions.Add(history);
        return history;
    }

    private void EnsureActiveReviewStatus(string action)
    {
        if (!ActiveReviewStatuses.Contains(Status))
        {
            throw new InvalidOperationException($"Cannot {action} a finance bill from status '{Status}'.");
        }
    }

    private static string NormalizeProposalSummary(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}

public sealed class FinanceBillReviewAction : ICompanyOwnedEntity
{
    private FinanceBillReviewAction()
    {
    }

    public FinanceBillReviewAction(
        Guid id,
        Guid companyId,
        Guid reviewStateId,
        Guid detectedBillId,
        string action,
        Guid? actorUserId,
        string actorDisplayName,
        string priorStatus,
        string newStatus,
        string rationale,
        DateTime occurredUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        ReviewStateId = reviewStateId == Guid.Empty ? throw new ArgumentException("ReviewStateId is required.", nameof(reviewStateId)) : reviewStateId;
        DetectedBillId = detectedBillId == Guid.Empty ? throw new ArgumentException("DetectedBillId is required.", nameof(detectedBillId)) : detectedBillId;
        Action = NormalizeRequired(action, nameof(action), 64);
        ActorUserId = actorUserId == Guid.Empty ? null : actorUserId;
        ActorDisplayName = NormalizeRequired(actorDisplayName, nameof(actorDisplayName), 200);
        PriorStatus = FinanceBillInboxStatuses.Normalize(priorStatus);
        NewStatus = FinanceBillInboxStatuses.Normalize(newStatus);
        Rationale = NormalizeRequired(rationale, nameof(rationale), 1000);
        OccurredUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ReviewStateId { get; private set; }
    public Guid DetectedBillId { get; private set; }
    public string Action { get; private set; } = null!;
    public Guid? ActorUserId { get; private set; }
    public string ActorDisplayName { get; private set; } = null!;
    public string PriorStatus { get; private set; } = null!;
    public string NewStatus { get; private set; } = null!;
    public string Rationale { get; private set; } = null!;
    public DateTime OccurredUtc { get; private set; }
    public FinanceBillReviewState ReviewState { get; private set; } = null!;
    public DetectedBill DetectedBill { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

public sealed class BillApprovalProposal : ICompanyOwnedEntity
{
    private BillApprovalProposal()
    {
    }

    public BillApprovalProposal(Guid id, Guid companyId, Guid detectedBillId, Guid reviewStateId, string summary, Guid? approvedByUserId, DateTime approvedUtc)
    {
        if (SuggestsPaymentExecution(summary))
        {
            throw new InvalidOperationException("Bill approval proposals cannot request or imply payment execution.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        DetectedBillId = detectedBillId == Guid.Empty ? throw new ArgumentException("DetectedBillId is required.", nameof(detectedBillId)) : detectedBillId;
        ReviewStateId = reviewStateId == Guid.Empty ? throw new ArgumentException("ReviewStateId is required.", nameof(reviewStateId)) : reviewStateId;
        Summary = string.IsNullOrWhiteSpace(summary) ? "Approval was requested for this bill. No payment has been initiated." : summary.Trim();
        ApprovedByUserId = approvedByUserId == Guid.Empty ? null : approvedByUserId;
        ApprovedUtc = EntityTimestampNormalizer.NormalizeUtc(approvedUtc, nameof(approvedUtc));
        PaymentExecutionRequested = false;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DetectedBillId { get; private set; }
    public Guid ReviewStateId { get; private set; }
    public string Summary { get; private set; } = null!;
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime ApprovedUtc { get; private set; }
    public bool PaymentExecutionRequested { get; private set; }
    public DetectedBill DetectedBill { get; private set; } = null!;
    public FinanceBillReviewState ReviewState { get; private set; } = null!;

    private static bool SuggestsPaymentExecution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("payment was initiated", StringComparison.Ordinal) ||
               normalized.Contains("payment has been initiated", StringComparison.Ordinal) ||
               normalized.Contains("payment will be initiated", StringComparison.Ordinal) ||
               normalized.Contains("payment will be sent", StringComparison.Ordinal) ||
               normalized.Contains("automatically pay", StringComparison.Ordinal) ||
               normalized.Contains("auto-pay", StringComparison.Ordinal) ||
               normalized.Contains("exported for payment", StringComparison.Ordinal);
    }
}