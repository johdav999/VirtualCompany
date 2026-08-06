namespace VirtualCompany.Domain.Entities;

public static class SupplierSubscriptionStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public static bool IsSupported(string? value) =>
        value is Draft or Active or Paused or Cancelled or Expired;
}

public static class SupplierSubscriptionCadences
{
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Yearly = "yearly";

    public static bool IsSupported(string? value) =>
        value is Monthly or Quarterly or Yearly;

    public static int Months(string value) => value switch
    {
        Monthly => 1,
        Quarterly => 3,
        Yearly => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported subscription cadence.")
    };
}

public sealed class SupplierSubscription : ICompanyOwnedEntity
{
    private SupplierSubscription() { }

    public SupplierSubscription(
        Guid id,
        Guid companyId,
        Guid counterpartyId,
        string name,
        string currency,
        decimal expectedAmount,
        string cadence,
        int billingDay,
        DateTime startDateUtc,
        DateTime nextExpectedBillDateUtc,
        decimal amountTolerance = 0m,
        int dateToleranceDays = 5,
        DateTime? endDateUtc = null,
        string? contractReference = null,
        string? description = null,
        int noticePeriodDays = 30,
        bool autoRenews = false,
        Guid? contractDocumentId = null,
        string status = SupplierSubscriptionStatuses.Draft,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (counterpartyId == Guid.Empty) throw new ArgumentException("CounterpartyId is required.", nameof(counterpartyId));
        if (contractDocumentId == Guid.Empty) throw new ArgumentException("ContractDocumentId cannot be empty.", nameof(contractDocumentId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CounterpartyId = counterpartyId;
        SetTerms(name, currency, expectedAmount, cadence, billingDay, startDateUtc, nextExpectedBillDateUtc,
            amountTolerance, dateToleranceDays, endDateUtc, contractReference, description, noticePeriodDays,
            autoRenews, contractDocumentId);
        Status = NormalizeStatus(status);
        CreatedUtc = NormalizeUtc(createdUtc ?? DateTime.UtcNow);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CounterpartyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? ContractReference { get; private set; }
    public string? Description { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal ExpectedAmount { get; private set; }
    public decimal AmountTolerance { get; private set; }
    public string Cadence { get; private set; } = null!;
    public int BillingDay { get; private set; }
    public DateTime StartDateUtc { get; private set; }
    public DateTime? EndDateUtc { get; private set; }
    public DateTime NextExpectedBillDateUtc { get; private set; }
    public int DateToleranceDays { get; private set; }
    public int NoticePeriodDays { get; private set; }
    public bool AutoRenews { get; private set; }
    public string Status { get; private set; } = null!;
    public Guid? ContractDocumentId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public FinanceCounterparty Counterparty { get; private set; } = null!;
    public CompanyKnowledgeDocument? ContractDocument { get; private set; }
    public ICollection<SupplierSubscriptionBillMatch> BillMatches { get; } = new List<SupplierSubscriptionBillMatch>();

    public void UpdateTerms(
        string name,
        string currency,
        decimal expectedAmount,
        string cadence,
        int billingDay,
        DateTime startDateUtc,
        DateTime nextExpectedBillDateUtc,
        decimal amountTolerance,
        int dateToleranceDays,
        DateTime? endDateUtc,
        string? contractReference,
        string? description,
        int noticePeriodDays,
        bool autoRenews,
        Guid? contractDocumentId)
    {
        if (Status == SupplierSubscriptionStatuses.Cancelled)
            throw new InvalidOperationException("A cancelled subscription cannot be edited.");

        SetTerms(name, currency, expectedAmount, cadence, billingDay, startDateUtc, nextExpectedBillDateUtc,
            amountTolerance, dateToleranceDays, endDateUtc, contractReference, description, noticePeriodDays,
            autoRenews, contractDocumentId);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Activate() => ChangeStatus(SupplierSubscriptionStatuses.Active);
    public void Pause() => ChangeStatus(SupplierSubscriptionStatuses.Paused);
    public void Resume() => ChangeStatus(SupplierSubscriptionStatuses.Active);
    public void Cancel() => ChangeStatus(SupplierSubscriptionStatuses.Cancelled);
    public void Expire() => ChangeStatus(SupplierSubscriptionStatuses.Expired);

    public void AdvanceAfterConfirmedBill(DateTime fulfilledExpectedDateUtc)
    {
        var expected = NormalizeDate(fulfilledExpectedDateUtc);
        if (expected != NextExpectedBillDateUtc)
            return;

        var months = SupplierSubscriptionCadences.Months(Cadence);
        var anchor = new DateTime(expected.Year, expected.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(months);
        var day = Math.Min(BillingDay, DateTime.DaysInMonth(anchor.Year, anchor.Month));
        NextExpectedBillDateUtc = new DateTime(anchor.Year, anchor.Month, day, 0, 0, 0, DateTimeKind.Utc);
        if (EndDateUtc.HasValue && NextExpectedBillDateUtc > EndDateUtc.Value)
            Status = SupplierSubscriptionStatuses.Expired;
        UpdatedUtc = DateTime.UtcNow;
    }

    private void ChangeStatus(string status)
    {
        var normalized = NormalizeStatus(status);
        if (Status == SupplierSubscriptionStatuses.Cancelled && normalized != SupplierSubscriptionStatuses.Cancelled)
            throw new InvalidOperationException("A cancelled subscription cannot be reactivated.");
        Status = normalized;
        UpdatedUtc = DateTime.UtcNow;
    }

    private void SetTerms(
        string name, string currency, decimal expectedAmount, string cadence, int billingDay,
        DateTime startDateUtc, DateTime nextExpectedBillDateUtc, decimal amountTolerance,
        int dateToleranceDays, DateTime? endDateUtc, string? contractReference, string? description,
        int noticePeriodDays, bool autoRenews, Guid? contractDocumentId)
    {
        if (expectedAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(expectedAmount), "Expected amount must be greater than zero.");
        if (amountTolerance < 0m) throw new ArgumentOutOfRangeException(nameof(amountTolerance));
        if (billingDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(billingDay));
        if (dateToleranceDays is < 0 or > 90) throw new ArgumentOutOfRangeException(nameof(dateToleranceDays));
        if (noticePeriodDays is < 0 or > 730) throw new ArgumentOutOfRangeException(nameof(noticePeriodDays));
        if (contractDocumentId == Guid.Empty) throw new ArgumentException("ContractDocumentId cannot be empty.", nameof(contractDocumentId));

        var normalizedCadence = NormalizeToken(cadence);
        if (!SupplierSubscriptionCadences.IsSupported(normalizedCadence))
            throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unsupported subscription cadence.");

        var start = NormalizeDate(startDateUtc);
        var next = NormalizeDate(nextExpectedBillDateUtc);
        var end = endDateUtc.HasValue ? NormalizeDate(endDateUtc.Value) : null;
        if (end.HasValue && end.Value < start) throw new ArgumentException("End date cannot be before start date.", nameof(endDateUtc));
        if (next < start) throw new ArgumentException("Next expected bill date cannot be before start date.", nameof(nextExpectedBillDateUtc));

        Name = Required(name, nameof(name), 200);
        Currency = Required(currency, nameof(currency), 3).ToUpperInvariant();
        ExpectedAmount = decimal.Round(expectedAmount, 2, MidpointRounding.AwayFromZero);
        AmountTolerance = decimal.Round(amountTolerance, 2, MidpointRounding.AwayFromZero);
        Cadence = normalizedCadence;
        BillingDay = billingDay;
        StartDateUtc = start;
        EndDateUtc = end;
        NextExpectedBillDateUtc = next;
        DateToleranceDays = dateToleranceDays;
        NoticePeriodDays = noticePeriodDays;
        AutoRenews = autoRenews;
        ContractReference = Optional(contractReference, nameof(contractReference), 128);
        Description = Optional(description, nameof(description), 1000);
        ContractDocumentId = contractDocumentId;
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = NormalizeToken(value);
        return SupplierSubscriptionStatuses.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported subscription status.");
    }

    private static string NormalizeToken(string value) => Required(value, nameof(value), 32).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    private static DateTime NormalizeDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) :
        value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name) : value.Trim();
    private static string? Optional(string? value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name) : value.Trim();
}
