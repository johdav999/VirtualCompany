using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Finance;
using Xunit;

namespace VirtualCompany.Finance.Tests;

public sealed class ForeignCurrencySettlementPolicyTests
{
    [Fact]
    public void Partial_incoming_settlement_recognizes_gain_and_retains_historical_carrying_amount()
    {
        var result = ForeignCurrencySettlementPolicy.Calculate(new ForeignCurrencySettlementInput(
            PaymentTypes.Incoming,
            DocumentTotalAmount: 100m,
            FunctionalDocumentTotalAmount: 1_000m,
            PreviouslyAppliedDocumentAmount: 0m,
            PreviouslyAppliedFunctionalAmount: 0m,
            AllocatedDocumentAmount: 40m,
            FeeDocumentAmount: 0m,
            WriteOffDocumentAmount: 0m,
            SettlementRate: 11m,
            FunctionalPrecision: 2,
            RoundingMode: AccountingRoundingModeValues.MidpointToEven));

        Assert.Equal(40m, result.AppliedDocumentAmount);
        Assert.Equal(40m, result.AllocatedPaymentAmount);
        Assert.Equal(400m, result.AllocatedFunctionalAmount);
        Assert.Equal(440m, result.SettlementFunctionalAmount);
        Assert.Equal(40m, result.RealizedGainLossAmount);
        Assert.Equal(60m, result.DocumentOutstandingAfter);
        Assert.Equal(600m, result.FunctionalOutstandingAfter);
        Assert.False(result.IsFinalSettlement);
    }

    [Fact]
    public void Final_incoming_settlement_at_second_rate_absorbs_exact_functional_residual_and_recognizes_loss()
    {
        var result = ForeignCurrencySettlementPolicy.Calculate(new ForeignCurrencySettlementInput(
            PaymentTypes.Incoming,
            DocumentTotalAmount: 100m,
            FunctionalDocumentTotalAmount: 1_000m,
            PreviouslyAppliedDocumentAmount: 40m,
            PreviouslyAppliedFunctionalAmount: 400m,
            AllocatedDocumentAmount: 60m,
            FeeDocumentAmount: 0m,
            WriteOffDocumentAmount: 0m,
            SettlementRate: 9m,
            FunctionalPrecision: 2,
            RoundingMode: AccountingRoundingModeValues.MidpointToEven));

        Assert.Equal(600m, result.AllocatedFunctionalAmount);
        Assert.Equal(540m, result.SettlementFunctionalAmount);
        Assert.Equal(-60m, result.RealizedGainLossAmount);
        Assert.Equal(0m, result.DocumentOutstandingAfter);
        Assert.Equal(0m, result.FunctionalOutstandingAfter);
        Assert.True(result.IsFinalSettlement);
    }

    [Fact]
    public void Final_settlement_absorbs_fractional_carrying_residual_after_rounded_partial()
    {
        var partial = ForeignCurrencySettlementPolicy.Calculate(new ForeignCurrencySettlementInput(
            PaymentTypes.Incoming, 3m, 100m, 0m, 0m, 1m, 0m, 0m, 34m, 2,
            AccountingRoundingModeValues.MidpointToEven));
        var final = ForeignCurrencySettlementPolicy.Calculate(new ForeignCurrencySettlementInput(
            PaymentTypes.Incoming, 3m, 100m, partial.AppliedDocumentAmount,
            partial.AllocatedFunctionalAmount, 2m, 0m, 0m, 32m, 2,
            AccountingRoundingModeValues.MidpointToEven));

        Assert.Equal(33.33m, partial.AllocatedFunctionalAmount);
        Assert.Equal(66.67m, final.AllocatedFunctionalAmount);
        Assert.Equal(100m, partial.AllocatedFunctionalAmount + final.AllocatedFunctionalAmount);
        Assert.Equal(0m, final.FunctionalOutstandingAfter);
        Assert.True(final.IsFinalSettlement);
    }

    [Fact]
    public void Incoming_fee_reduces_bank_receipt_without_changing_settled_document_amount()
    {
        var result = ForeignCurrencySettlementPolicy.Calculate(new ForeignCurrencySettlementInput(
            PaymentTypes.Incoming,
            DocumentTotalAmount: 100m,
            FunctionalDocumentTotalAmount: 1_000m,
            PreviouslyAppliedDocumentAmount: 0m,
            PreviouslyAppliedFunctionalAmount: 0m,
            AllocatedDocumentAmount: 100m,
            FeeDocumentAmount: 2m,
            WriteOffDocumentAmount: 0m,
            SettlementRate: 10m,
            FunctionalPrecision: 2,
            RoundingMode: AccountingRoundingModeValues.MidpointToEven));

        Assert.Equal(98m, result.AllocatedPaymentAmount);
        Assert.Equal(1_000m, result.SettlementFunctionalAmount);
        Assert.Equal(980m, result.BankFunctionalAmount);
        Assert.Equal(20m, result.FeeFunctionalAmount);
        Assert.Equal(0m, result.RealizedGainLossAmount);
        Assert.True(result.IsFinalSettlement);
    }

    [Fact]
    public void Outgoing_credit_refund_uses_payable_direction_for_realized_gain()
    {
        var result = ForeignCurrencySettlementPolicy.Calculate(new ForeignCurrencySettlementInput(
            PaymentTypes.Outgoing,
            DocumentTotalAmount: 100m,
            FunctionalDocumentTotalAmount: 1_000m,
            PreviouslyAppliedDocumentAmount: 0m,
            PreviouslyAppliedFunctionalAmount: 0m,
            AllocatedDocumentAmount: 40m,
            FeeDocumentAmount: 0m,
            WriteOffDocumentAmount: 0m,
            SettlementRate: 9m,
            FunctionalPrecision: 2,
            RoundingMode: AccountingRoundingModeValues.MidpointToEven));

        Assert.Equal(400m, result.AllocatedFunctionalAmount);
        Assert.Equal(360m, result.BankFunctionalAmount);
        Assert.Equal(40m, result.RealizedGainLossAmount);
        Assert.Equal(60m, result.DocumentOutstandingAfter);
    }

    [Fact]
    public void Final_writeoff_closes_document_and_retains_separate_functional_evidence()
    {
        var result = ForeignCurrencySettlementPolicy.Calculate(new ForeignCurrencySettlementInput(
            PaymentTypes.Incoming,
            DocumentTotalAmount: 100m,
            FunctionalDocumentTotalAmount: 1_000m,
            PreviouslyAppliedDocumentAmount: 0m,
            PreviouslyAppliedFunctionalAmount: 0m,
            AllocatedDocumentAmount: 98m,
            FeeDocumentAmount: 0m,
            WriteOffDocumentAmount: 2m,
            SettlementRate: 10m,
            FunctionalPrecision: 2,
            RoundingMode: AccountingRoundingModeValues.MidpointToEven));

        Assert.Equal(100m, result.AppliedDocumentAmount);
        Assert.Equal(980m, result.SettlementFunctionalAmount);
        Assert.Equal(20m, result.WriteOffFunctionalAmount);
        Assert.Equal(0m, result.RealizedGainLossAmount);
        Assert.Equal(0m, result.DocumentOutstandingAfter);
        Assert.Equal(0m, result.FunctionalOutstandingAfter);
    }

    [Fact]
    public void Partial_writeoff_and_over_settlement_are_rejected()
    {
        var partialWriteOff = CreateInput(allocatedAmount: 90m, writeOffAmount: 2m);
        var overSettlement = CreateInput(allocatedAmount: 99m, writeOffAmount: 2m);

        Assert.Throws<InvalidOperationException>(() => ForeignCurrencySettlementPolicy.Calculate(partialWriteOff));
        Assert.Throws<InvalidOperationException>(() => ForeignCurrencySettlementPolicy.Calculate(overSettlement));
    }

    private static ForeignCurrencySettlementInput CreateInput(decimal allocatedAmount, decimal writeOffAmount) =>
        new(
            PaymentTypes.Incoming,
            DocumentTotalAmount: 100m,
            FunctionalDocumentTotalAmount: 1_000m,
            PreviouslyAppliedDocumentAmount: 0m,
            PreviouslyAppliedFunctionalAmount: 0m,
            AllocatedDocumentAmount: allocatedAmount,
            FeeDocumentAmount: 0m,
            WriteOffDocumentAmount: writeOffAmount,
            SettlementRate: 10m,
            FunctionalPrecision: 2,
            RoundingMode: AccountingRoundingModeValues.MidpointToEven);
}
