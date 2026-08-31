namespace VirtualCompany.Application.Finance;

public sealed record PostCashSettlementCommand(
    Guid CompanyId,
    string SourceType,
    string SourceId,
    Guid PaymentId,
    decimal SettledAmount,
    DateTime SettledAtUtc,
    CashSettlementAccountingFacts? AccountingFacts = null,
    Guid ActorUserId = default,
    string? CorrelationId = null);

public sealed record CashSettlementAccountingFacts(
    Guid AllocationId,
    string ControlAccountRole,
    string DocumentCurrency,
    string FunctionalCurrency,
    decimal AllocatedDocumentAmount,
    decimal WriteOffDocumentAmount,
    decimal FeeDocumentAmount,
    decimal AllocatedPaymentAmount,
    decimal AllocatedFunctionalAmount,
    decimal SettlementFunctionalAmount,
    decimal BankFunctionalAmount,
    decimal FeeFunctionalAmount,
    decimal WriteOffFunctionalAmount,
    decimal RealizedGainLossAmount,
    decimal RoundingFunctionalAmount,
    DateOnly SettlementRateDate,
    decimal SettlementRate,
    Guid? SettlementExchangeRateConversionId,
    string SettlementRateIdentity,
    decimal SettlementConversionRoundingResidual,
    decimal JournalDocumentTotal,
    bool IsFinalSettlement);

public sealed record CashSettlementPostingResultDto(
    Guid CompanyId,
    Guid LedgerEntryId,
    string SourceType,
    string SourceId,
    decimal PostedAmount,
    DateTime PostedAtUtc,
    bool Created,
    decimal RealizedGainLossAmount = 0m);

public interface IFinanceCashSettlementPostingService
{
    Task<CashSettlementPostingResultDto> PostCashSettlementAsync(
        PostCashSettlementCommand command,
        CancellationToken cancellationToken);
}

public static class FinanceCashPostingSourceTypes
{
    public const string PaymentAllocation = "payment_allocation";
    public const string PaymentSettlement = "payment_settlement";
    public const string BankTransaction = "bank_transaction";

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Source type is required.", nameof(value));
        }

        return value.Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToLowerInvariant();
    }
}
