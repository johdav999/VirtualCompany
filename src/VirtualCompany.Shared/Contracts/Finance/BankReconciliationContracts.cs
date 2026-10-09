namespace VirtualCompany.Application.Finance;
public sealed record BankReconciliationFollowUpDto(Guid Id, string Status, string Reason, Guid LedgerEntryId, DateTime CreatedUtc, DateTime? ResolvedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Status { get; set; } = Status;
    public string Reason { get; set; } = Reason;
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? ResolvedUtc { get; set; } = ResolvedUtc;

    public BankReconciliationFollowUpDto() : this(default !, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record BankReconciliationWorkspaceDto(IReadOnlyList<BankReconciliationItemDto> Items, IReadOnlyDictionary<string, int> StateCounts)
{
    public IReadOnlyList<BankReconciliationItemDto> Items { get; set; } = Items;
    public IReadOnlyDictionary<string, int> StateCounts { get; set; } = StateCounts;

    public BankReconciliationWorkspaceDto() : this([], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
    {
    }
}

public sealed record BankReconciliationDetailDto(BankTransactionDetailDto Transaction, string State, decimal RemainingAmount, long SourceVersion, string? HandlingMode, string? ReviewReason, IReadOnlyList<BankReconciliationCandidatePaymentDto> CandidatePayments, IReadOnlyList<BankReconciliationJournalLinkDto> Journals, BankReconciliationFollowUpDto? FollowUp, bool CanPostToSuspense, bool CanReclassify, string? BlockingReason)
{
    public BankTransactionDetailDto Transaction { get; set; } = Transaction;
    public string State { get; set; } = State;
    public decimal RemainingAmount { get; set; } = RemainingAmount;
    public long SourceVersion { get; set; } = SourceVersion;
    public string? HandlingMode { get; set; } = HandlingMode;
    public string? ReviewReason { get; set; } = ReviewReason;
    public IReadOnlyList<BankReconciliationCandidatePaymentDto> CandidatePayments { get; set; } = CandidatePayments;
    public IReadOnlyList<BankReconciliationJournalLinkDto> Journals { get; set; } = Journals;
    public BankReconciliationFollowUpDto? FollowUp { get; set; } = FollowUp;
    public bool CanPostToSuspense { get; set; } = CanPostToSuspense;
    public bool CanReclassify { get; set; } = CanReclassify;
    public string? BlockingReason { get; set; } = BlockingReason;

    public BankReconciliationDetailDto() : this(new(), string.Empty, default !, default !, default !, default !, [], [], default !, default !, default !, default !)
    {
    }
}

public sealed record BankReconciliationJournalLinkDto(Guid LedgerEntryId, string EntryNumber, string PostingType, string Status, DateOnly? PostingDate, bool IsOriginalSuspense, bool IsCorrection)
{
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public string EntryNumber { get; set; } = EntryNumber;
    public string PostingType { get; set; } = PostingType;
    public string Status { get; set; } = Status;
    public DateOnly? PostingDate { get; set; } = PostingDate;
    public bool IsOriginalSuspense { get; set; } = IsOriginalSuspense;
    public bool IsCorrection { get; set; } = IsCorrection;

    public BankReconciliationJournalLinkDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record BankReconciliationCandidatePaymentDto(Guid PaymentId, string PaymentType, decimal Amount, decimal AlreadyLinkedAmount, decimal AvailableAmount, string Currency, DateTime PaymentDate, string CounterpartyReference, Guid? InvoiceId, string? InvoiceNumber, Guid? BillId, string? BillNumber)
{
    public Guid PaymentId { get; set; } = PaymentId;
    public string PaymentType { get; set; } = PaymentType;
    public decimal Amount { get; set; } = Amount;
    public decimal AlreadyLinkedAmount { get; set; } = AlreadyLinkedAmount;
    public decimal AvailableAmount { get; set; } = AvailableAmount;
    public string Currency { get; set; } = Currency;
    public DateTime PaymentDate { get; set; } = PaymentDate;
    public string CounterpartyReference { get; set; } = CounterpartyReference;
    public Guid? InvoiceId { get; set; } = InvoiceId;
    public string? InvoiceNumber { get; set; } = InvoiceNumber;
    public Guid? BillId { get; set; } = BillId;
    public string? BillNumber { get; set; } = BillNumber;

    public BankReconciliationCandidatePaymentDto() : this(default !, string.Empty, default !, default !, default !, string.Empty, default !, string.Empty, default !, default !, default !, default !)
    {
    }
}

public sealed record BankReconciliationItemDto(Guid BankTransactionId, DateTime BookingDate, decimal Amount, string Currency, string Counterparty, string ReferenceText, string BankAccountDisplayName, string State, decimal AllocatedAmount, decimal RemainingAmount, int LinkedPaymentCount, long SourceVersion, string? ConflictCode, string? ConflictExplanation, Guid? LedgerEntryId)
{
    public Guid BankTransactionId { get; set; } = BankTransactionId;
    public DateTime BookingDate { get; set; } = BookingDate;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Counterparty { get; set; } = Counterparty;
    public string ReferenceText { get; set; } = ReferenceText;
    public string BankAccountDisplayName { get; set; } = BankAccountDisplayName;
    public string State { get; set; } = State;
    public decimal AllocatedAmount { get; set; } = AllocatedAmount;
    public decimal RemainingAmount { get; set; } = RemainingAmount;
    public int LinkedPaymentCount { get; set; } = LinkedPaymentCount;
    public long SourceVersion { get; set; } = SourceVersion;
    public string? ConflictCode { get; set; } = ConflictCode;
    public string? ConflictExplanation { get; set; } = ConflictExplanation;
    public Guid? LedgerEntryId { get; set; } = LedgerEntryId;

    public BankReconciliationItemDto() : this(default !, default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record BankReconciliationAdjustmentDto(string Kind, decimal DebitAmount, decimal CreditAmount, string Explanation)
{
    public string Kind { get; set; } = Kind;
    public decimal DebitAmount { get; set; } = DebitAmount;
    public decimal CreditAmount { get; set; } = CreditAmount;
    public string Explanation { get; set; } = Explanation;

    public BankReconciliationAdjustmentDto() : this(string.Empty, default !, default !, string.Empty)
    {
    }
}
