namespace VirtualCompany.Domain.Entities;

public static class SupplierSubscriptionIntakeProposalClassifications
{
    public const string Agreement = "agreement";
    public const string Receipt = "receipt";
    public const string Unknown = "unknown";

    public static bool IsSupported(string? value) =>
        value is Agreement or Receipt or Unknown;
}

public static class SupplierSubscriptionIntakeProposalStatuses
{
    public const string Detected = "detected";
    public const string NeedsReview = "needs_review";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
    public const string Duplicate = "duplicate";

    public static bool IsSupported(string? value) =>
        value is Detected or NeedsReview or Accepted or Rejected or Failed or Duplicate;
}

public sealed class SupplierSubscriptionIntakeProposal : ICompanyOwnedEntity
{
    private SupplierSubscriptionIntakeProposal()
    {
    }

    public SupplierSubscriptionIntakeProposal(
        Guid id,
        Guid companyId,
        Guid sourceEmailMessageSnapshotId,
        Guid? sourceEmailAttachmentSnapshotId,
        Guid? sourceDocumentId,
        string sourceFingerprint,
        string classification,
        string status,
        int confidenceScore,
        string evidenceSummary,
        string? supplierName,
        string? supplierOrgNumber,
        Guid? matchedCounterpartyId,
        string? agreementName,
        string? currency,
        decimal? expectedAmount,
        string? cadence,
        int? billingDay,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        DateTime? nextExpectedBillDateUtc,
        decimal? amountTolerance,
        int? dateToleranceDays,
        int? noticePeriodDays,
        bool? autoRenews,
        string? contractReference,
        string? description,
        string? safeFailureSummary = null,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (sourceEmailMessageSnapshotId == Guid.Empty) throw new ArgumentException("SourceEmailMessageSnapshotId is required.", nameof(sourceEmailMessageSnapshotId));
        if (sourceEmailAttachmentSnapshotId == Guid.Empty) throw new ArgumentException("SourceEmailAttachmentSnapshotId cannot be empty.", nameof(sourceEmailAttachmentSnapshotId));
        if (sourceDocumentId == Guid.Empty) throw new ArgumentException("SourceDocumentId cannot be empty.", nameof(sourceDocumentId));
        if (matchedCounterpartyId == Guid.Empty) throw new ArgumentException("MatchedCounterpartyId cannot be empty.", nameof(matchedCounterpartyId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SourceEmailMessageSnapshotId = sourceEmailMessageSnapshotId;
        SourceEmailAttachmentSnapshotId = sourceEmailAttachmentSnapshotId;
        SourceDocumentId = sourceDocumentId;
        SourceFingerprint = Required(sourceFingerprint, nameof(sourceFingerprint), 256);
        Classification = NormalizeClassification(classification);
        Status = NormalizeStatus(status);
        ConfidenceScore = confidenceScore is < 0 or > 100 ? throw new ArgumentOutOfRangeException(nameof(confidenceScore)) : confidenceScore;
        EvidenceSummary = Required(evidenceSummary, nameof(evidenceSummary), 1000);
        SupplierName = Optional(supplierName, nameof(supplierName), 200);
        SupplierOrgNumber = Optional(supplierOrgNumber, nameof(supplierOrgNumber), 64);
        MatchedCounterpartyId = matchedCounterpartyId;
        AgreementName = Optional(agreementName, nameof(agreementName), 200);
        Currency = Optional(currency, nameof(currency), 3)?.ToUpperInvariant();
        ExpectedAmount = expectedAmount.HasValue ? NormalizeMoney(expectedAmount.Value, nameof(expectedAmount)) : null;
        Cadence = string.IsNullOrWhiteSpace(cadence) ? null : NormalizeCadence(cadence);
        BillingDay = billingDay.HasValue ? NormalizeBillingDay(billingDay.Value) : null;
        StartDateUtc = startDateUtc.HasValue ? NormalizeDate(startDateUtc.Value) : null;
        EndDateUtc = endDateUtc.HasValue ? NormalizeDate(endDateUtc.Value) : null;
        NextExpectedBillDateUtc = nextExpectedBillDateUtc.HasValue ? NormalizeDate(nextExpectedBillDateUtc.Value) : null;
        AmountTolerance = amountTolerance.HasValue ? NormalizeTolerance(amountTolerance.Value, nameof(amountTolerance)) : null;
        DateToleranceDays = dateToleranceDays.HasValue ? NormalizeDays(dateToleranceDays.Value, nameof(dateToleranceDays), 90) : null;
        NoticePeriodDays = noticePeriodDays.HasValue ? NormalizeDays(noticePeriodDays.Value, nameof(noticePeriodDays), 730) : null;
        AutoRenews = autoRenews;
        ContractReference = Optional(contractReference, nameof(contractReference), 128);
        Description = Optional(description, nameof(description), 1000);
        SafeFailureSummary = Optional(safeFailureSummary, nameof(safeFailureSummary), 1000);
        CreatedUtc = NormalizeUtc(createdUtc ?? DateTime.UtcNow);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SourceEmailMessageSnapshotId { get; private set; }
    public Guid? SourceEmailAttachmentSnapshotId { get; private set; }
    public Guid? SourceDocumentId { get; private set; }
    public string SourceFingerprint { get; private set; } = null!;
    public string Classification { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int ConfidenceScore { get; private set; }
    public string EvidenceSummary { get; private set; } = null!;
    public string? SupplierName { get; private set; }
    public string? SupplierOrgNumber { get; private set; }
    public Guid? MatchedCounterpartyId { get; private set; }
    public string? AgreementName { get; private set; }
    public string? Currency { get; private set; }
    public decimal? ExpectedAmount { get; private set; }
    public string? Cadence { get; private set; }
    public int? BillingDay { get; private set; }
    public DateTime? StartDateUtc { get; private set; }
    public DateTime? EndDateUtc { get; private set; }
    public DateTime? NextExpectedBillDateUtc { get; private set; }
    public decimal? AmountTolerance { get; private set; }
    public int? DateToleranceDays { get; private set; }
    public int? NoticePeriodDays { get; private set; }
    public bool? AutoRenews { get; private set; }
    public string? ContractReference { get; private set; }
    public string? Description { get; private set; }
    public string? SafeFailureSummary { get; private set; }
    public Guid? AcceptedSubscriptionId { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public string? DecisionReason { get; private set; }
    public DateTime? DecidedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public EmailMessageSnapshot SourceEmailMessageSnapshot { get; private set; } = null!;
    public EmailAttachmentSnapshot? SourceEmailAttachmentSnapshot { get; private set; }
    public CompanyKnowledgeDocument? SourceDocument { get; private set; }
    public FinanceCounterparty? MatchedCounterparty { get; private set; }
    public SupplierSubscription? AcceptedSubscription { get; private set; }

    public bool CanAccept => Status is SupplierSubscriptionIntakeProposalStatuses.Detected or SupplierSubscriptionIntakeProposalStatuses.NeedsReview or SupplierSubscriptionIntakeProposalStatuses.Failed;

    public void MarkNeedsReview(string evidenceSummary, int confidenceScore)
    {
        if (Status is SupplierSubscriptionIntakeProposalStatuses.Accepted or SupplierSubscriptionIntakeProposalStatuses.Rejected)
            throw new InvalidOperationException("A decided subscription proposal cannot be reopened by classification.");
        EvidenceSummary = Required(evidenceSummary, nameof(evidenceSummary), 1000);
        ConfidenceScore = confidenceScore is < 0 or > 100 ? throw new ArgumentOutOfRangeException(nameof(confidenceScore)) : confidenceScore;
        Status = SupplierSubscriptionIntakeProposalStatuses.NeedsReview;
        SafeFailureSummary = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string safeFailureSummary)
    {
        if (Status == SupplierSubscriptionIntakeProposalStatuses.Accepted)
            throw new InvalidOperationException("An accepted subscription proposal cannot be failed.");
        Status = SupplierSubscriptionIntakeProposalStatuses.Failed;
        SafeFailureSummary = Required(safeFailureSummary, nameof(safeFailureSummary), 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkDuplicate(string reason)
    {
        if (Status == SupplierSubscriptionIntakeProposalStatuses.Accepted)
            throw new InvalidOperationException("An accepted subscription proposal cannot be marked duplicate.");
        Status = SupplierSubscriptionIntakeProposalStatuses.Duplicate;
        DecisionReason = Required(reason, nameof(reason), 500);
        DecidedUtc = DateTime.UtcNow;
        UpdatedUtc = DecidedUtc.Value;
    }

    public void Accept(Guid subscriptionId, Guid? actorUserId, string? reason)
    {
        if (subscriptionId == Guid.Empty) throw new ArgumentException("SubscriptionId is required.", nameof(subscriptionId));
        if (!CanAccept) throw new InvalidOperationException("This subscription proposal cannot be accepted in its current state.");
        Status = SupplierSubscriptionIntakeProposalStatuses.Accepted;
        AcceptedSubscriptionId = subscriptionId;
        DecidedByUserId = actorUserId;
        DecisionReason = Optional(reason, nameof(reason), 500);
        DecidedUtc = DateTime.UtcNow;
        UpdatedUtc = DecidedUtc.Value;
    }

    public void Reject(Guid? actorUserId, string reason)
    {
        if (Status == SupplierSubscriptionIntakeProposalStatuses.Accepted)
            throw new InvalidOperationException("An accepted subscription proposal cannot be rejected.");
        Status = SupplierSubscriptionIntakeProposalStatuses.Rejected;
        DecidedByUserId = actorUserId;
        DecisionReason = Required(reason, nameof(reason), 500);
        DecidedUtc = DateTime.UtcNow;
        UpdatedUtc = DecidedUtc.Value;
    }

    public void UpdateReviewTerms(
        Guid? matchedCounterpartyId,
        string? agreementName,
        string? currency,
        decimal? expectedAmount,
        string? cadence,
        int? billingDay,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        DateTime? nextExpectedBillDateUtc,
        decimal? amountTolerance,
        int? dateToleranceDays,
        int? noticePeriodDays,
        bool? autoRenews,
        string? contractReference,
        string? description)
    {
        if (Status == SupplierSubscriptionIntakeProposalStatuses.Accepted)
            throw new InvalidOperationException("Accepted subscription proposal terms cannot be edited.");
        if (matchedCounterpartyId == Guid.Empty) throw new ArgumentException("MatchedCounterpartyId cannot be empty.", nameof(matchedCounterpartyId));
        MatchedCounterpartyId = matchedCounterpartyId;
        AgreementName = Optional(agreementName, nameof(agreementName), 200);
        Currency = Optional(currency, nameof(currency), 3)?.ToUpperInvariant();
        ExpectedAmount = expectedAmount.HasValue ? NormalizeMoney(expectedAmount.Value, nameof(expectedAmount)) : null;
        Cadence = string.IsNullOrWhiteSpace(cadence) ? null : NormalizeCadence(cadence);
        BillingDay = billingDay.HasValue ? NormalizeBillingDay(billingDay.Value) : null;
        StartDateUtc = startDateUtc.HasValue ? NormalizeDate(startDateUtc.Value) : null;
        EndDateUtc = endDateUtc.HasValue ? NormalizeDate(endDateUtc.Value) : null;
        NextExpectedBillDateUtc = nextExpectedBillDateUtc.HasValue ? NormalizeDate(nextExpectedBillDateUtc.Value) : null;
        AmountTolerance = amountTolerance.HasValue ? NormalizeTolerance(amountTolerance.Value, nameof(amountTolerance)) : null;
        DateToleranceDays = dateToleranceDays.HasValue ? NormalizeDays(dateToleranceDays.Value, nameof(dateToleranceDays), 90) : null;
        NoticePeriodDays = noticePeriodDays.HasValue ? NormalizeDays(noticePeriodDays.Value, nameof(noticePeriodDays), 730) : null;
        AutoRenews = autoRenews;
        ContractReference = Optional(contractReference, nameof(contractReference), 128);
        Description = Optional(description, nameof(description), 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeClassification(string value)
    {
        var normalized = NormalizeToken(value);
        return SupplierSubscriptionIntakeProposalClassifications.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported subscription proposal classification.");
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = NormalizeToken(value);
        return SupplierSubscriptionIntakeProposalStatuses.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported subscription proposal status.");
    }

    private static string NormalizeCadence(string value)
    {
        var normalized = NormalizeToken(value);
        return SupplierSubscriptionCadences.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported subscription cadence.");
    }

    private static int NormalizeBillingDay(int value) => value is < 1 or > 31 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    private static int NormalizeDays(int value, string name, int max) => value is < 0 ? throw new ArgumentOutOfRangeException(name) : value > max ? throw new ArgumentOutOfRangeException(name) : value;
    private static decimal NormalizeMoney(decimal value, string name) => value <= 0m ? throw new ArgumentOutOfRangeException(name) : decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal NormalizeTolerance(decimal value, string name) => value < 0m ? throw new ArgumentOutOfRangeException(name) : decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static DateTime NormalizeDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string NormalizeToken(string value) => Required(value, nameof(value), 32).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    private static string Required(string value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) :
        value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name) : value.Trim();
    private static string? Optional(string? value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}
