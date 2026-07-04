using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public static class SupplierInvoicePaymentProposalStatuses
{
    public const string Draft = "draft";
    public const string AwaitingApproval = "awaiting_approval";
    public const string ReadyForPayment = "ready_for_payment";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Exported = "exported";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Draft,
        AwaitingApproval,
        ReadyForPayment,
        Rejected,
        Cancelled,
        Exported
    };

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Payment proposal status is required.", nameof(value));
        }

        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return Allowed.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported payment proposal status.");
    }
}

public static class SupplierInvoicePaymentExportStatuses
{
    public const string NotExported = "not_exported";
    public const string ExportRequested = "export_requested";
    public const string Exported = "exported";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        NotExported,
        ExportRequested,
        Exported,
        Failed,
        Cancelled
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NotExported;
        }

        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return Allowed.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported payment export status.");
    }
}

public static class SupplierInvoicePaymentExportModes
{
    public const string RegisterPayment = "register_payment";
    public const string PreparePaymentFile = "prepare_payment_file";
    public const string ManualExport = "manual_export";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RegisterPayment,
        PreparePaymentFile,
        ManualExport
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RegisterPayment;
        }

        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return Allowed.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported payment export mode.");
    }
}

public static class SupplierInvoiceSourceDocumentAttachmentStatuses
{
    public const string NotAttached = "not_attached";
    public const string AttachmentRequested = "attachment_requested";
    public const string Attached = "attached";
    public const string Failed = "failed";
    public const string NotAvailable = "not_available";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        NotAttached,
        AttachmentRequested,
        Attached,
        Failed,
        NotAvailable
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NotAttached;
        }

        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return Allowed.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported source document attachment status.");
    }
}

public static class SupplierInvoiceDraftActionStatuses
{
    public const string Draft = "draft";
    public const string UpdatePending = "update_pending";
    public const string Updated = "updated";
    public const string BookkeepingRequested = "bookkeeping_requested";
    public const string Booked = "booked";
    public const string Failed = "failed";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Draft,
        UpdatePending,
        Updated,
        BookkeepingRequested,
        Booked,
        Failed
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Draft;
        }

        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return Allowed.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported supplier invoice draft action status.");
    }
}

public static class SupplierInvoiceCorrectionActionTypes
{
    public const string Cancellation = "cancellation";
    public const string CreditNote = "credit_note";
}

public static class SupplierInvoiceCorrectionActionStatuses
{
    public const string CancellationRequested = "cancellation_requested";
    public const string Cancelled = "cancelled";
    public const string CancellationFailed = "cancellation_failed";
    public const string CreditNoteRequested = "credit_note_requested";
    public const string CreditNoteCreated = "credit_note_created";
    public const string CreditNoteFailed = "credit_note_failed";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CancellationRequested,
        Cancelled,
        CancellationFailed,
        CreditNoteRequested,
        CreditNoteCreated,
        CreditNoteFailed
    };

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Supplier invoice correction status is required.", nameof(value));
        }

        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return Allowed.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported supplier invoice correction status.");
    }
}

public static class SupplierInvoiceEnrichmentActionStatuses
{
    public const string NotSuggested = "not_suggested";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Approved = "approved";
    public const string SyncRequested = "sync_requested";
    public const string Synced = "synced";
    public const string Failed = "failed";
    public const string ReconciliationWarning = "reconciliation_warning";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        NotSuggested,
        AwaitingApproval,
        Approved,
        SyncRequested,
        Synced,
        Failed,
        ReconciliationWarning
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NotSuggested;
        }

        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return Allowed.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported supplier invoice enrichment status.");
    }
}

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

public sealed class SupplierInvoicePaymentProposal : ICompanyOwnedEntity
{
    private SupplierInvoicePaymentProposal()
    {
    }

    public SupplierInvoicePaymentProposal(
        Guid id,
        Guid companyId,
        Guid billId,
        Guid supplierId,
        string supplierName,
        decimal amount,
        string currency,
        DateTime dueUtc,
        string paymentReference,
        Guid? requestedByUserId,
        DateTime createdUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (billId == Guid.Empty)
        {
            throw new ArgumentException("BillId is required.", nameof(billId));
        }

        if (supplierId == Guid.Empty)
        {
            throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BillId = billId;
        SupplierId = supplierId;
        SupplierName = NormalizeRequired(supplierName, nameof(supplierName), 200);
        Amount = NormalizeAmount(amount);
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        DueUtc = EntityTimestampNormalizer.NormalizeUtc(dueUtc, nameof(dueUtc));
        PaymentReference = NormalizeRequired(paymentReference, nameof(paymentReference), 128);
        Status = SupplierInvoicePaymentProposalStatuses.Draft;
        ExportStatus = SupplierInvoicePaymentExportStatuses.NotExported;
        ExportMode = SupplierInvoicePaymentExportModes.RegisterPayment;
        ExportProviderMetadata = [];
        RequestedByUserId = requestedByUserId == Guid.Empty ? null : requestedByUserId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        AppendAudit("created", null, Status, RequestedByUserId, "Payment proposal was created.");
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BillId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string SupplierName { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime DueUtc { get; private set; }
    public string PaymentReference { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid? TaskId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedUtc { get; private set; }
    public string ExportMode { get; private set; } = SupplierInvoicePaymentExportModes.RegisterPayment;
    public string ExportStatus { get; private set; } = SupplierInvoicePaymentExportStatuses.NotExported;
    public string? ExportProviderKey { get; private set; }
    public Guid? ExportConnectionId { get; private set; }
    public Guid? ExportRequestedByUserId { get; private set; }
    public DateTime? ExportRequestedUtc { get; private set; }
    public DateTime? ExportedUtc { get; private set; }
    public string? ExportResponseSummary { get; private set; }
    public JsonObject ExportProviderMetadata { get; private set; } = [];
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Dictionary<string, JsonNode?> AuditTrail { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Company Company { get; private set; } = null!;
    public FinanceBill Bill { get; private set; } = null!;
    public FinanceCounterparty Supplier { get; private set; } = null!;
    public WorkTask? Task { get; private set; }
    public ApprovalRequest? ApprovalRequest { get; private set; }

    public void AttachApprovalWorkflow(Guid taskId, Guid approvalRequestId, DateTime occurredUtc)
    {
        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("TaskId is required.", nameof(taskId));
        }

        if (approvalRequestId == Guid.Empty)
        {
            throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId));
        }

        TaskId = taskId;
        ApprovalRequestId = approvalRequestId;
        TransitionTo(SupplierInvoicePaymentProposalStatuses.AwaitingApproval, null, "Payment proposal was sent for approval.", occurredUtc);
    }

    public void MarkReadyForPayment(Guid? decidedByUserId, DateTime occurredUtc, string? rationale = null) =>
        TransitionTo(SupplierInvoicePaymentProposalStatuses.ReadyForPayment, decidedByUserId, string.IsNullOrWhiteSpace(rationale) ? "Payment proposal was approved and is ready for payment/export." : rationale, occurredUtc);

    public void MarkRejected(Guid? decidedByUserId, DateTime occurredUtc, string? rationale = null) =>
        TransitionTo(SupplierInvoicePaymentProposalStatuses.Rejected, decidedByUserId, string.IsNullOrWhiteSpace(rationale) ? "Payment proposal was rejected." : rationale, occurredUtc);

    public void MarkCancelled(Guid? actorUserId, DateTime occurredUtc, string? rationale = null) =>
        TransitionTo(SupplierInvoicePaymentProposalStatuses.Cancelled, actorUserId, string.IsNullOrWhiteSpace(rationale) ? "Payment proposal was cancelled." : rationale, occurredUtc);

    public void MarkPaymentExport(
        string exportMode,
        string exportStatus,
        string providerKey,
        Guid? connectionId,
        Guid? actorUserId,
        string responseSummary,
        JsonObject? providerMetadata,
        DateTime occurredUtc)
    {
        if (!string.Equals(Status, SupplierInvoicePaymentProposalStatuses.ReadyForPayment, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Status, SupplierInvoicePaymentProposalStatuses.Exported, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only approved payment proposals can be exported.");
        }

        var normalizedStatus = SupplierInvoicePaymentExportStatuses.Normalize(exportStatus);
        var normalizedMode = SupplierInvoicePaymentExportModes.Normalize(exportMode);
        var priorExportStatus = ExportStatus;
        ExportMode = normalizedMode;
        ExportStatus = normalizedStatus;
        ExportProviderKey = NormalizeRequired(providerKey, nameof(providerKey), 64).ToLowerInvariant();
        ExportConnectionId = connectionId == Guid.Empty ? null : connectionId;
        ExportRequestedByUserId = actorUserId == Guid.Empty ? null : actorUserId;
        ExportRequestedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        ExportedUtc = normalizedStatus == SupplierInvoicePaymentExportStatuses.Exported
            ? ExportRequestedUtc
            : ExportedUtc;
        ExportResponseSummary = string.IsNullOrWhiteSpace(responseSummary) ? null : responseSummary.Trim();
        ExportProviderMetadata = providerMetadata ?? [];
        UpdatedUtc = ExportRequestedUtc.Value;

        if (normalizedStatus == SupplierInvoicePaymentExportStatuses.Exported)
        {
            Status = SupplierInvoicePaymentProposalStatuses.Exported;
        }

        AppendAudit($"payment_export:{normalizedMode}", priorExportStatus, normalizedStatus, actorUserId, ExportResponseSummary ?? "Payment export state was updated.");
    }

    private void TransitionTo(string status, Guid? actorUserId, string rationale, DateTime occurredUtc)
    {
        var normalized = SupplierInvoicePaymentProposalStatuses.Normalize(status);
        var prior = Status;
        Status = normalized;
        DecidedByUserId = normalized is SupplierInvoicePaymentProposalStatuses.ReadyForPayment or SupplierInvoicePaymentProposalStatuses.Rejected
            ? actorUserId == Guid.Empty ? null : actorUserId
            : DecidedByUserId;
        DecidedUtc = normalized is SupplierInvoicePaymentProposalStatuses.ReadyForPayment or SupplierInvoicePaymentProposalStatuses.Rejected
            ? EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc))
            : DecidedUtc;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        AppendAudit("status_changed", prior, normalized, actorUserId, rationale);
    }

    private void AppendAudit(string action, string? priorStatus, string newStatus, Guid? actorUserId, string rationale)
    {
        var events = AuditTrail.TryGetValue("events", out var existing) && existing is JsonArray existingArray
            ? existingArray
            : [];
        events.Add(new JsonObject
        {
            ["action"] = action,
            ["priorStatus"] = priorStatus,
            ["newStatus"] = newStatus,
            ["actorUserId"] = actorUserId?.ToString("D"),
            ["rationale"] = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim(),
            ["occurredUtc"] = UpdatedUtc.ToString("O")
        });
        AuditTrail["events"] = events;
    }

    private static decimal NormalizeAmount(decimal amount)
    {
        var normalized = decimal.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        if (normalized <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Payment proposal amount must be greater than zero.");
        }

        return normalized;
    }

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

public sealed class SupplierInvoiceSourceDocumentAttachment : ICompanyOwnedEntity
{
    private SupplierInvoiceSourceDocumentAttachment()
    {
    }

    public SupplierInvoiceSourceDocumentAttachment(
        Guid id,
        Guid companyId,
        Guid billId,
        Guid? documentId,
        DateTime createdUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (billId == Guid.Empty)
        {
            throw new ArgumentException("BillId is required.", nameof(billId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BillId = billId;
        DocumentId = documentId == Guid.Empty ? null : documentId;
        Status = SupplierInvoiceSourceDocumentAttachmentStatuses.NotAttached;
        ProviderMetadata = [];
        AuditTrail = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        AppendAudit("created", null, Status, null, "Source document attachment tracking was created.");
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BillId { get; private set; }
    public Guid? DocumentId { get; private set; }
    public string Status { get; private set; } = SupplierInvoiceSourceDocumentAttachmentStatuses.NotAttached;
    public string? ProviderKey { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public DateTime? RequestedUtc { get; private set; }
    public DateTime? AttachedUtc { get; private set; }
    public string? ResponseSummary { get; private set; }
    public JsonObject ProviderMetadata { get; private set; } = [];
    public Dictionary<string, JsonNode?> AuditTrail { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceBill Bill { get; private set; } = null!;
    public CompanyKnowledgeDocument? Document { get; private set; }

    public void Mark(
        string status,
        string? providerKey,
        Guid? connectionId,
        Guid? requestedByUserId,
        string? responseSummary,
        JsonObject? providerMetadata,
        DateTime occurredUtc)
    {
        var normalizedStatus = SupplierInvoiceSourceDocumentAttachmentStatuses.Normalize(status);
        var priorStatus = Status;
        Status = normalizedStatus;
        ProviderKey = string.IsNullOrWhiteSpace(providerKey) ? ProviderKey : providerKey.Trim().ToLowerInvariant();
        ConnectionId = connectionId == Guid.Empty ? null : connectionId ?? ConnectionId;
        RequestedByUserId = requestedByUserId == Guid.Empty ? null : requestedByUserId;
        RequestedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        AttachedUtc = normalizedStatus == SupplierInvoiceSourceDocumentAttachmentStatuses.Attached
            ? RequestedUtc
            : AttachedUtc;
        ResponseSummary = string.IsNullOrWhiteSpace(responseSummary) ? null : responseSummary.Trim();
        ProviderMetadata = providerMetadata ?? [];
        UpdatedUtc = RequestedUtc.Value;
        AppendAudit("source_document_attachment", priorStatus, normalizedStatus, RequestedByUserId, ResponseSummary ?? "Source document attachment status was updated.");
    }

    public void UpdateDocument(Guid? documentId, DateTime occurredUtc)
    {
        DocumentId = documentId == Guid.Empty ? null : documentId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }

    private void AppendAudit(string action, string? priorStatus, string newStatus, Guid? actorUserId, string rationale)
    {
        var events = AuditTrail.TryGetValue("events", out var existing) && existing is JsonArray existingArray
            ? existingArray
            : [];
        events.Add(new JsonObject
        {
            ["action"] = action,
            ["priorStatus"] = priorStatus,
            ["newStatus"] = newStatus,
            ["actorUserId"] = actorUserId?.ToString("D"),
            ["rationale"] = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim(),
            ["occurredUtc"] = UpdatedUtc.ToString("O")
        });
        AuditTrail["events"] = events;
    }
}

public sealed class SupplierInvoiceDraftAction : ICompanyOwnedEntity
{
    private SupplierInvoiceDraftAction()
    {
    }

    public SupplierInvoiceDraftAction(Guid id, Guid companyId, Guid billId, DateTime createdUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (billId == Guid.Empty)
        {
            throw new ArgumentException("BillId is required.", nameof(billId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BillId = billId;
        Status = SupplierInvoiceDraftActionStatuses.Draft;
        ProviderMetadata = [];
        AuditTrail = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        AppendAudit("created", null, Status, null, "Supplier invoice draft action tracking was created.");
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BillId { get; private set; }
    public string Status { get; private set; } = SupplierInvoiceDraftActionStatuses.Draft;
    public string? ProviderKey { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public DateTime? RequestedUtc { get; private set; }
    public DateTime? UpdatedInProviderUtc { get; private set; }
    public DateTime? BookedUtc { get; private set; }
    public string? ResponseSummary { get; private set; }
    public JsonObject ProviderMetadata { get; private set; } = [];
    public Dictionary<string, JsonNode?> AuditTrail { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceBill Bill { get; private set; } = null!;

    public void Mark(
        string status,
        string? providerKey,
        Guid? connectionId,
        Guid? requestedByUserId,
        string? responseSummary,
        JsonObject? providerMetadata,
        DateTime occurredUtc)
    {
        var normalizedStatus = SupplierInvoiceDraftActionStatuses.Normalize(status);
        var priorStatus = Status;
        Status = normalizedStatus;
        ProviderKey = string.IsNullOrWhiteSpace(providerKey) ? ProviderKey : providerKey.Trim().ToLowerInvariant();
        ConnectionId = connectionId == Guid.Empty ? null : connectionId ?? ConnectionId;
        RequestedByUserId = requestedByUserId == Guid.Empty ? null : requestedByUserId;
        RequestedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        UpdatedInProviderUtc = normalizedStatus == SupplierInvoiceDraftActionStatuses.Updated
            ? RequestedUtc
            : UpdatedInProviderUtc;
        BookedUtc = normalizedStatus == SupplierInvoiceDraftActionStatuses.Booked
            ? RequestedUtc
            : BookedUtc;
        ResponseSummary = string.IsNullOrWhiteSpace(responseSummary) ? null : responseSummary.Trim();
        ProviderMetadata = providerMetadata ?? [];
        UpdatedUtc = RequestedUtc.Value;
        AppendAudit("supplier_invoice_draft_action", priorStatus, normalizedStatus, RequestedByUserId, ResponseSummary ?? "Supplier invoice draft action status was updated.");
    }

    private void AppendAudit(string action, string? priorStatus, string newStatus, Guid? actorUserId, string rationale)
    {
        var events = AuditTrail.TryGetValue("events", out var existing) && existing is JsonArray existingArray
            ? existingArray
            : [];
        events.Add(new JsonObject
        {
            ["action"] = action,
            ["priorStatus"] = priorStatus,
            ["newStatus"] = newStatus,
            ["actorUserId"] = actorUserId?.ToString("D"),
            ["rationale"] = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim(),
            ["occurredUtc"] = UpdatedUtc.ToString("O")
        });
        AuditTrail["events"] = events;
    }
}

public sealed class SupplierInvoiceCorrectionAction : ICompanyOwnedEntity
{
    private SupplierInvoiceCorrectionAction()
    {
    }

    public SupplierInvoiceCorrectionAction(
        Guid id,
        Guid companyId,
        Guid billId,
        string actionType,
        string status,
        DateTime createdUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (billId == Guid.Empty)
        {
            throw new ArgumentException("BillId is required.", nameof(billId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BillId = billId;
        ActionType = NormalizeActionType(actionType);
        Status = SupplierInvoiceCorrectionActionStatuses.Normalize(status);
        ProviderMetadata = [];
        AuditTrail = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        AppendAudit("created", null, Status, null, "Supplier invoice correction action tracking was created.");
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BillId { get; private set; }
    public string ActionType { get; private set; } = SupplierInvoiceCorrectionActionTypes.Cancellation;
    public string Status { get; private set; } = SupplierInvoiceCorrectionActionStatuses.CancellationRequested;
    public string? ProviderKey { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public DateTime? RequestedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public Guid? CreditNoteBillId { get; private set; }
    public string? ProviderCreditNoteNumber { get; private set; }
    public string? ResponseSummary { get; private set; }
    public JsonObject ProviderMetadata { get; private set; } = [];
    public Dictionary<string, JsonNode?> AuditTrail { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceBill Bill { get; private set; } = null!;
    public FinanceBill? CreditNoteBill { get; private set; }
    public WorkTask? Task { get; private set; }
    public ApprovalRequest? ApprovalRequest { get; private set; }

    public void AttachApprovalWorkflow(Guid taskId, Guid approvalRequestId, DateTime occurredUtc)
    {
        TaskId = taskId == Guid.Empty ? null : taskId;
        ApprovalRequestId = approvalRequestId == Guid.Empty ? null : approvalRequestId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        AppendAudit("supplier_invoice_correction_approval_requested", Status, Status, RequestedByUserId, "Supplier invoice correction approval was requested.");
    }

    public void MarkApproved(Guid? approvedByUserId, DateTime occurredUtc)
    {
        ApprovedByUserId = approvedByUserId == Guid.Empty ? null : approvedByUserId;
        ApprovedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        UpdatedUtc = ApprovedUtc.Value;
        AppendAudit("supplier_invoice_correction_approved", Status, Status, ApprovedByUserId, "Supplier invoice correction was approved for Fortnox sync.");
    }

    public void Mark(
        string status,
        string? providerKey,
        Guid? connectionId,
        Guid? requestedByUserId,
        string? responseSummary,
        JsonObject? providerMetadata,
        DateTime occurredUtc,
        Guid? creditNoteBillId = null,
        string? providerCreditNoteNumber = null)
    {
        var normalizedStatus = SupplierInvoiceCorrectionActionStatuses.Normalize(status);
        var priorStatus = Status;
        Status = normalizedStatus;
        ProviderKey = string.IsNullOrWhiteSpace(providerKey) ? ProviderKey : providerKey.Trim().ToLowerInvariant();
        ConnectionId = connectionId == Guid.Empty ? null : connectionId ?? ConnectionId;
        RequestedByUserId = requestedByUserId == Guid.Empty ? null : requestedByUserId;
        RequestedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        CompletedUtc = normalizedStatus is SupplierInvoiceCorrectionActionStatuses.Cancelled or SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated
            ? RequestedUtc
            : CompletedUtc;
        CreditNoteBillId = creditNoteBillId == Guid.Empty ? null : creditNoteBillId ?? CreditNoteBillId;
        ProviderCreditNoteNumber = string.IsNullOrWhiteSpace(providerCreditNoteNumber) ? ProviderCreditNoteNumber : providerCreditNoteNumber.Trim();
        ResponseSummary = string.IsNullOrWhiteSpace(responseSummary) ? null : responseSummary.Trim();
        ProviderMetadata = providerMetadata ?? [];
        UpdatedUtc = RequestedUtc.Value;
        AppendAudit("supplier_invoice_correction_action", priorStatus, normalizedStatus, RequestedByUserId, ResponseSummary ?? "Supplier invoice correction status was updated.");
    }

    private static string NormalizeActionType(string value)
    {
        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return normalized is SupplierInvoiceCorrectionActionTypes.Cancellation or SupplierInvoiceCorrectionActionTypes.CreditNote
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported supplier invoice correction action type.");
    }

    private void AppendAudit(string action, string? priorStatus, string newStatus, Guid? actorUserId, string rationale)
    {
        var events = AuditTrail.TryGetValue("events", out var existing) && existing is JsonArray existingArray
            ? existingArray
            : [];
        events.Add(new JsonObject
        {
            ["action"] = action,
            ["priorStatus"] = priorStatus,
            ["newStatus"] = newStatus,
            ["actorUserId"] = actorUserId?.ToString("D"),
            ["rationale"] = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim(),
            ["occurredUtc"] = UpdatedUtc.ToString("O")
        });
        AuditTrail["events"] = events;
    }
}

public sealed class SupplierInvoiceEnrichmentAction : ICompanyOwnedEntity
{
    private SupplierInvoiceEnrichmentAction()
    {
    }

    public SupplierInvoiceEnrichmentAction(Guid id, Guid companyId, Guid billId, DateTime createdUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (billId == Guid.Empty)
        {
            throw new ArgumentException("BillId is required.", nameof(billId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BillId = billId;
        Status = SupplierInvoiceEnrichmentActionStatuses.NotSuggested;
        SuggestionPayload = [];
        ReconciliationWarnings = [];
        ProviderMetadata = [];
        AuditTrail = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        AppendAudit("created", null, Status, null, "Supplier invoice enrichment tracking was created.");
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BillId { get; private set; }
    public string Status { get; private set; } = SupplierInvoiceEnrichmentActionStatuses.NotSuggested;
    public string? ProviderKey { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public DateTime? RequestedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? SyncedUtc { get; private set; }
    public string? ResponseSummary { get; private set; }
    public JsonObject SuggestionPayload { get; private set; } = [];
    public JsonArray ReconciliationWarnings { get; private set; } = [];
    public JsonObject ProviderMetadata { get; private set; } = [];
    public Dictionary<string, JsonNode?> AuditTrail { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceBill Bill { get; private set; } = null!;
    public WorkTask? Task { get; private set; }
    public ApprovalRequest? ApprovalRequest { get; private set; }

    public void MarkSuggested(
        JsonObject suggestionPayload,
        JsonArray reconciliationWarnings,
        Guid? requestedByUserId,
        string responseSummary,
        DateTime occurredUtc)
    {
        var priorStatus = Status;
        Status = SupplierInvoiceEnrichmentActionStatuses.AwaitingApproval;
        RequestedByUserId = requestedByUserId == Guid.Empty ? null : requestedByUserId;
        RequestedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        SuggestionPayload = suggestionPayload ?? [];
        ReconciliationWarnings = reconciliationWarnings ?? [];
        ResponseSummary = string.IsNullOrWhiteSpace(responseSummary) ? null : responseSummary.Trim();
        UpdatedUtc = RequestedUtc.Value;
        AppendAudit("supplier_invoice_enrichment_suggested", priorStatus, Status, RequestedByUserId, ResponseSummary ?? "Laura suggested supplier invoice enrichment changes.");
    }

    public void AttachApprovalWorkflow(Guid taskId, Guid approvalRequestId, DateTime occurredUtc)
    {
        TaskId = taskId == Guid.Empty ? null : taskId;
        ApprovalRequestId = approvalRequestId == Guid.Empty ? null : approvalRequestId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }

    public void MarkApproved(Guid? approvedByUserId, DateTime occurredUtc)
    {
        var priorStatus = Status;
        Status = SupplierInvoiceEnrichmentActionStatuses.Approved;
        ApprovedByUserId = approvedByUserId == Guid.Empty ? null : approvedByUserId;
        ApprovedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        UpdatedUtc = ApprovedUtc.Value;
        AppendAudit("supplier_invoice_enrichment_approved", priorStatus, Status, ApprovedByUserId, "Supplier invoice enrichment was approved for Fortnox sync.");
    }

    public void MarkProviderResult(
        string status,
        string? providerKey,
        Guid? connectionId,
        Guid? actorUserId,
        string responseSummary,
        JsonObject providerMetadata,
        DateTime occurredUtc)
    {
        var normalizedStatus = SupplierInvoiceEnrichmentActionStatuses.Normalize(status);
        var priorStatus = Status;
        Status = normalizedStatus;
        ProviderKey = string.IsNullOrWhiteSpace(providerKey) ? ProviderKey : providerKey.Trim().ToLowerInvariant();
        ConnectionId = connectionId == Guid.Empty ? null : connectionId ?? ConnectionId;
        SyncedUtc = normalizedStatus == SupplierInvoiceEnrichmentActionStatuses.Synced
            ? EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc))
            : SyncedUtc;
        ResponseSummary = string.IsNullOrWhiteSpace(responseSummary) ? null : responseSummary.Trim();
        ProviderMetadata = providerMetadata ?? [];
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        AppendAudit("supplier_invoice_enrichment_provider_sync", priorStatus, normalizedStatus, actorUserId, ResponseSummary ?? "Supplier invoice enrichment provider sync was updated.");
    }

    public void MarkReconciliation(JsonArray reconciliationWarnings, DateTime occurredUtc)
    {
        var priorStatus = Status;
        ReconciliationWarnings = reconciliationWarnings ?? [];
        if (ReconciliationWarnings.Count > 0 && Status != SupplierInvoiceEnrichmentActionStatuses.Failed)
        {
            Status = SupplierInvoiceEnrichmentActionStatuses.ReconciliationWarning;
        }

        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        AppendAudit("supplier_invoice_reconciled", priorStatus, Status, null, ReconciliationWarnings.Count == 0
            ? "Supplier invoice reconciliation found no warnings."
            : "Supplier invoice reconciliation found warnings.");
    }

    private void AppendAudit(string action, string? priorStatus, string newStatus, Guid? actorUserId, string rationale)
    {
        var events = AuditTrail.TryGetValue("events", out var existing) && existing is JsonArray existingArray
            ? existingArray
            : [];
        events.Add(new JsonObject
        {
            ["action"] = action,
            ["priorStatus"] = priorStatus,
            ["newStatus"] = newStatus,
            ["actorUserId"] = actorUserId?.ToString("D"),
            ["rationale"] = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim(),
            ["occurredUtc"] = UpdatedUtc.ToString("O")
        });
        AuditTrail["events"] = events;
    }
}
