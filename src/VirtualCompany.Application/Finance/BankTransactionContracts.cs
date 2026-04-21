namespace VirtualCompany.Application.Finance;

public sealed record ListBankTransactionsQuery(
    Guid CompanyId,
    Guid? BankAccountId = null,
    DateTime? BookingDateFromUtc = null,
    DateTime? BookingDateToUtc = null,
    string? Status = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    int Limit = 100);

public sealed record GetBankTransactionDetailQuery(
    Guid CompanyId,
    Guid BankTransactionId);

public sealed record ReconcileBankTransactionCommand(
    Guid CompanyId,
    Guid BankTransactionId,
    IReadOnlyList<BankTransactionPaymentMatchDto> Payments);

public sealed record BankTransactionPaymentMatchDto(
    Guid PaymentId,
    decimal AllocatedAmount);

public sealed record CompanyBankAccountDto(
    Guid Id,
    Guid CompanyId,
    Guid FinanceAccountId,
    string FinanceAccountName,
    string DisplayName,
    string BankName,
    string MaskedAccountNumber,
    string Currency,
    string? ExternalCode,
    bool IsPrimary,
    bool IsActive,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record BankTransactionDto(
    Guid Id,
    Guid CompanyId,
    Guid BankAccountId,
    string BankAccountDisplayName,
    string BankName,
    string MaskedAccountNumber,
    DateTime BookingDate,
    DateTime ValueDate,
    decimal Amount,
    string Currency,
    string ReferenceText,
    string Counterparty,
    string Status,
    decimal ReconciledAmount,
    string? ExternalReference,
    CompanyBankAccountDto BankAccount);

public sealed record BankTransactionPaymentLinkDto(
    Guid Id,
    Guid PaymentId,
    string PaymentType,
    DateTime PaymentDate,
    string CounterpartyReference,
    decimal AllocatedAmount,
    string Currency,
    DateTime CreatedUtc);

public sealed record BankTransactionDetailDto(
    Guid Id,
    Guid CompanyId,
    Guid BankAccountId,
    string BankAccountDisplayName,
    string BankName,
    string MaskedAccountNumber,
    DateTime BookingDate,
    DateTime ValueDate,
    decimal Amount,
    string Currency,
    string ReferenceText,
    string Counterparty,
    string Status,
    decimal ReconciledAmount,
    string? ExternalReference,
    Guid? CashLedgerEntryId,
    IReadOnlyList<BankTransactionPaymentLinkDto> LinkedPayments,
    CompanyBankAccountDto BankAccount);

public interface IBankTransactionReadService
{
    Task<IReadOnlyList<BankTransactionDto>> ListAsync(ListBankTransactionsQuery query, CancellationToken cancellationToken);
    Task<BankTransactionDetailDto?> GetDetailAsync(GetBankTransactionDetailQuery query, CancellationToken cancellationToken);
}

public interface IBankTransactionCommandService
{
    Task<BankTransactionDetailDto> ReconcileAsync(ReconcileBankTransactionCommand command, CancellationToken cancellationToken);
}