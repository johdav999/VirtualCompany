namespace VirtualCompany.Domain.Entities;

public static class SupplierSubscriptionMatchStatuses
{
    public const string Suggested = "suggested";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";
    public const string Exception = "exception";
    public static bool IsSupported(string? value) => value is Suggested or Confirmed or Rejected or Exception;
}

public static class SupplierSubscriptionMatchMethods
{
    public const string Automatic = "automatic";
    public const string Manual = "manual";
    public const string ReceiptEvidence = "receipt_evidence";
    public static bool IsSupported(string? value) => value is Automatic or Manual or ReceiptEvidence;
}

public sealed class SupplierSubscriptionBillMatch : ICompanyOwnedEntity
{
    private SupplierSubscriptionBillMatch() { }

    public SupplierSubscriptionBillMatch(
        Guid id,
        Guid companyId,
        Guid subscriptionId,
        Guid billId,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        DateTime expectedBillDateUtc,
        decimal expectedAmount,
        decimal actualAmount,
        string status,
        string matchMethod,
        int confidenceScore,
        string evidenceSummary,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (subscriptionId == Guid.Empty) throw new ArgumentException("SubscriptionId is required.", nameof(subscriptionId));
        if (billId == Guid.Empty) throw new ArgumentException("BillId is required.", nameof(billId));
        if (confidenceScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(confidenceScore));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SubscriptionId = subscriptionId;
        BillId = billId;
        PeriodStartUtc = DateTime.SpecifyKind(periodStartUtc.Date, DateTimeKind.Utc);
        PeriodEndUtc = DateTime.SpecifyKind(periodEndUtc.Date, DateTimeKind.Utc);
        ExpectedBillDateUtc = DateTime.SpecifyKind(expectedBillDateUtc.Date, DateTimeKind.Utc);
        if (PeriodEndUtc < PeriodStartUtc) throw new ArgumentException("Period end cannot be before period start.", nameof(periodEndUtc));
        ExpectedAmount = decimal.Round(expectedAmount, 2, MidpointRounding.AwayFromZero);
        ActualAmount = decimal.Round(actualAmount, 2, MidpointRounding.AwayFromZero);
        AmountVariance = ActualAmount - ExpectedAmount;
        Status = NormalizeStatus(status);
        MatchMethod = NormalizeMethod(matchMethod);
        ConfidenceScore = confidenceScore;
        EvidenceSummary = Required(evidenceSummary, nameof(evidenceSummary), 600);
        CreatedUtc = NormalizeUtc(createdUtc ?? DateTime.UtcNow);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid BillId { get; private set; }
    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public DateTime ExpectedBillDateUtc { get; private set; }
    public decimal ExpectedAmount { get; private set; }
    public decimal ActualAmount { get; private set; }
    public decimal AmountVariance { get; private set; }
    public string Status { get; private set; } = null!;
    public string MatchMethod { get; private set; } = null!;
    public int ConfidenceScore { get; private set; }
    public string EvidenceSummary { get; private set; } = null!;
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SupplierSubscription Subscription { get; private set; } = null!;
    public FinanceBill Bill { get; private set; } = null!;

    public void Confirm(Guid? actorUserId, string method = SupplierSubscriptionMatchMethods.Manual)
    {
        Status = SupplierSubscriptionMatchStatuses.Confirmed;
        MatchMethod = NormalizeMethod(method);
        DecidedByUserId = actorUserId;
        DecidedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public void Reject(Guid? actorUserId)
    {
        Status = SupplierSubscriptionMatchStatuses.Rejected;
        DecidedByUserId = actorUserId;
        DecidedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = Required(value, nameof(value), 32).ToLowerInvariant();
        return SupplierSubscriptionMatchStatuses.IsSupported(normalized) ? normalized : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static string NormalizeMethod(string value)
    {
        var normalized = Required(value, nameof(value), 32).ToLowerInvariant();
        return SupplierSubscriptionMatchMethods.IsSupported(normalized) ? normalized : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) :
        value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name) : value.Trim();
}
