using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Domain.Finance;

public sealed record ForeignCurrencySettlementInput(
    string PaymentType,
    decimal DocumentTotalAmount,
    decimal FunctionalDocumentTotalAmount,
    decimal PreviouslyAppliedDocumentAmount,
    decimal PreviouslyAppliedFunctionalAmount,
    decimal AllocatedDocumentAmount,
    decimal FeeDocumentAmount,
    decimal WriteOffDocumentAmount,
    decimal SettlementRate,
    int FunctionalPrecision,
    string RoundingMode);

public sealed record ForeignCurrencySettlementResult(
    decimal AppliedDocumentAmount,
    decimal AllocatedPaymentAmount,
    decimal AllocatedFunctionalAmount,
    decimal SettlementFunctionalAmount,
    decimal BankFunctionalAmount,
    decimal FeeFunctionalAmount,
    decimal WriteOffFunctionalAmount,
    decimal RealizedGainLossAmount,
    decimal RoundingFunctionalAmount,
    decimal DocumentOutstandingAfter,
    decimal FunctionalOutstandingAfter,
    decimal JournalDocumentTotal,
    bool IsFinalSettlement);

public static class ForeignCurrencySettlementPolicy
{
    public static ForeignCurrencySettlementResult Calculate(ForeignCurrencySettlementInput input)
    {
        var documentTotal = Positive(input.DocumentTotalAmount, nameof(input.DocumentTotalAmount));
        var functionalTotal = Positive(input.FunctionalDocumentTotalAmount, nameof(input.FunctionalDocumentTotalAmount));
        var previouslyAppliedDocument = NonNegative(input.PreviouslyAppliedDocumentAmount, nameof(input.PreviouslyAppliedDocumentAmount));
        var previouslyAppliedFunctional = NonNegative(input.PreviouslyAppliedFunctionalAmount, nameof(input.PreviouslyAppliedFunctionalAmount));
        var allocatedDocument = Positive(input.AllocatedDocumentAmount, nameof(input.AllocatedDocumentAmount));
        var feeDocument = NonNegative(input.FeeDocumentAmount, nameof(input.FeeDocumentAmount));
        var writeOffDocument = NonNegative(input.WriteOffDocumentAmount, nameof(input.WriteOffDocumentAmount));
        var rate = Positive(input.SettlementRate, nameof(input.SettlementRate));
        if (input.FunctionalPrecision is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(input.FunctionalPrecision));

        var incoming = string.Equals(PaymentTypes.Normalize(input.PaymentType), PaymentTypes.Incoming, StringComparison.Ordinal);
        var outgoing = string.Equals(PaymentTypes.Normalize(input.PaymentType), PaymentTypes.Outgoing, StringComparison.Ordinal);
        if (!incoming && !outgoing)
            throw new ArgumentOutOfRangeException(nameof(input.PaymentType), "Settlement requires an incoming or outgoing payment.");

        var appliedDocument = allocatedDocument + writeOffDocument;
        var remainingDocumentBefore = RoundDocument(documentTotal - previouslyAppliedDocument);
        if (remainingDocumentBefore <= 0m)
            throw new InvalidOperationException("The document has no remaining amount to settle.");
        if (appliedDocument > remainingDocumentBefore)
            throw new InvalidOperationException("The allocation and write-off exceed the remaining document amount.");
        if (writeOffDocument > 0m && appliedDocument < remainingDocumentBefore)
            throw new InvalidOperationException("A write-off is only permitted when it closes the remaining document balance.");
        if (incoming && feeDocument >= allocatedDocument)
            throw new InvalidOperationException("An incoming settlement fee must be smaller than the allocated document amount.");

        var allocatedPayment = incoming
            ? RoundDocument(allocatedDocument - feeDocument)
            : RoundDocument(allocatedDocument + feeDocument);
        if (allocatedPayment <= 0m)
            throw new InvalidOperationException("The settlement must consume a positive payment amount.");

        var isFinal = appliedDocument == remainingDocumentBefore;
        var remainingFunctionalBefore = Round(functionalTotal - previouslyAppliedFunctional, input);
        if (remainingFunctionalBefore < 0m)
            throw new InvalidOperationException("Prior settlement facts exceed the document's functional carrying amount.");
        var carryingApplied = isFinal
            ? remainingFunctionalBefore
            : Round(functionalTotal * (appliedDocument / documentTotal), input);
        if (carryingApplied > remainingFunctionalBefore)
            carryingApplied = remainingFunctionalBefore;

        var writeOffFunctional = writeOffDocument == 0m
            ? 0m
            : Round(carryingApplied * (writeOffDocument / appliedDocument), input);
        var carryingCash = Round(carryingApplied - writeOffFunctional, input);
        var settlementFunctional = Round(allocatedDocument * rate, input);
        var feeFunctional = Round(feeDocument * rate, input);
        var bankFunctional = incoming
            ? Round(settlementFunctional - feeFunctional, input)
            : Round(settlementFunctional + feeFunctional, input);
        var realized = incoming
            ? Round(settlementFunctional - carryingCash, input)
            : Round(carryingCash - settlementFunctional, input);
        var functionalOutstanding = isFinal
            ? 0m
            : Round(remainingFunctionalBefore - carryingApplied, input);
        var documentOutstanding = isFinal
            ? 0m
            : RoundDocument(remainingDocumentBefore - appliedDocument);
        var journalDocumentTotal = incoming
            ? appliedDocument
            : RoundDocument(appliedDocument + feeDocument);

        return new ForeignCurrencySettlementResult(
            appliedDocument,
            allocatedPayment,
            carryingApplied,
            settlementFunctional,
            bankFunctional,
            feeFunctional,
            writeOffFunctional,
            realized,
            0m,
            documentOutstanding,
            functionalOutstanding,
            journalDocumentTotal,
            isFinal);
    }

    private static decimal Round(decimal value, ForeignCurrencySettlementInput input) =>
        decimal.Round(value, input.FunctionalPrecision,
            input.RoundingMode == AccountingRoundingModeValues.AwayFromZero
                ? MidpointRounding.AwayFromZero
                : MidpointRounding.ToEven);

    private static decimal RoundDocument(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Positive(decimal value, string name) =>
        value > 0m ? value : throw new ArgumentOutOfRangeException(name, "The amount must be greater than zero.");

    private static decimal NonNegative(decimal value, string name) =>
        value >= 0m ? value : throw new ArgumentOutOfRangeException(name, "The amount cannot be negative.");
}
