namespace VirtualCompany.Application.Finance;
public sealed record CompanyBankAccountDto(Guid Id, Guid CompanyId, Guid FinanceAccountId, string FinanceAccountName, string DisplayName, string BankName, string MaskedAccountNumber, string Currency, string? ExternalCode, bool IsPrimary, bool IsActive, DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record BankTransactionPaymentLinkDto(Guid Id, Guid PaymentId, string PaymentType, DateTime PaymentDate, string CounterpartyReference, decimal AllocatedAmount, string Currency, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid PaymentId { get; set; } = PaymentId;
    public string PaymentType { get; set; } = PaymentType;
    public DateTime PaymentDate { get; set; } = PaymentDate;
    public string CounterpartyReference { get; set; } = CounterpartyReference;
    public decimal AllocatedAmount { get; set; } = AllocatedAmount;
    public string Currency { get; set; } = Currency;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public BankTransactionPaymentLinkDto() : this(default !, default !, default !, default !, string.Empty, default !, string.Empty, default !)
    {
    }
}

public sealed record BankTransactionDetailDto(Guid Id, Guid CompanyId, Guid BankAccountId, string BankAccountDisplayName, string BankName, string MaskedAccountNumber, DateTime BookingDate, DateTime ValueDate, decimal Amount, string Currency, string ReferenceText, string Counterparty, string Status, decimal ReconciledAmount, string? ExternalReference, Guid? CashLedgerEntryId, IReadOnlyList<BankTransactionPaymentLinkDto> LinkedPayments, CompanyBankAccountDto BankAccount)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid BankAccountId { get; set; } = BankAccountId;
    public string BankAccountDisplayName { get; set; } = BankAccountDisplayName;
    public string BankName { get; set; } = BankName;
    public string MaskedAccountNumber { get; set; } = MaskedAccountNumber;
    public DateTime BookingDate { get; set; } = BookingDate;
    public DateTime ValueDate { get; set; } = ValueDate;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string ReferenceText { get; set; } = ReferenceText;
    public string Counterparty { get; set; } = Counterparty;
    public string Status { get; set; } = Status;
    public decimal ReconciledAmount { get; set; } = ReconciledAmount;
    public string? ExternalReference { get; set; } = ExternalReference;
    public Guid? CashLedgerEntryId { get; set; } = CashLedgerEntryId;
    public IReadOnlyList<BankTransactionPaymentLinkDto> LinkedPayments { get; set; } = LinkedPayments;
    public CompanyBankAccountDto BankAccount { get; set; } = BankAccount;

    public BankTransactionDetailDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, [], default !)
    {
    }
}
