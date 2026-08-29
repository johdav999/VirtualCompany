using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class PaymentBatchEligibilityPolicyTests
{
    private static readonly DateTime FridayBeforeCutoff = new(2026, 8, 28, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_optimizes_weekend_due_date_to_previous_business_day()
    {
        var result = Policy().Evaluate(Input(dueDate: new DateOnly(2026, 9, 6), requestedDate: new DateOnly(2026, 9, 4)));

        Assert.True(result.IsEligible);
        Assert.Equal(PaymentBatchReasonCodes.Ready, result.ReasonCode);
        Assert.Equal(new DateOnly(2026, 9, 4), result.RecommendedExecutionDate);
    }

    [Fact]
    public void Evaluate_moves_earliest_execution_to_next_business_day_after_stockholm_cutoff()
    {
        var afterCutoff = new DateTime(2026, 8, 28, 14, 30, 0, DateTimeKind.Utc);

        var result = Policy().Evaluate(Input(
            dueDate: new DateOnly(2026, 9, 2),
            requestedDate: new DateOnly(2026, 8, 28),
            now: afterCutoff));

        Assert.False(result.IsEligible);
        Assert.Equal(PaymentBatchReasonCodes.InvalidExecutionDate, result.ReasonCode);
        Assert.Equal(new DateOnly(2026, 9, 2), result.RecommendedExecutionDate);
    }

    [Theory]
    [InlineData(true, false, false, false, true, true, 5000, PaymentBatchReasonCodes.ObligationHeld)]
    [InlineData(false, true, false, false, true, true, 5000, PaymentBatchReasonCodes.ObligationDisputed)]
    [InlineData(false, false, true, false, true, true, 5000, PaymentBatchReasonCodes.ObligationSettled)]
    [InlineData(false, false, false, true, true, true, 5000, PaymentBatchReasonCodes.ObligationDuplicate)]
    [InlineData(false, false, false, false, false, true, 5000, PaymentBatchReasonCodes.BeneficiaryUnverified)]
    [InlineData(false, false, false, false, true, false, 5000, PaymentBatchReasonCodes.SourceChanged)]
    [InlineData(false, false, false, false, true, true, 100, PaymentBatchReasonCodes.InsufficientCash)]
    public void Evaluate_blocks_control_failures_with_stable_reason_codes(bool held, bool disputed,
        bool settled, bool duplicate, bool beneficiaryVerified, bool sourceCurrent, decimal cash,
        string expectedReasonCode)
    {
        var input = Input(new DateOnly(2026, 9, 4), new DateOnly(2026, 8, 31)) with
        {
            IsHeld = held,
            IsDisputed = disputed,
            IsSettled = settled,
            IsDuplicate = duplicate,
            IsBeneficiaryVerified = beneficiaryVerified,
            IsSourceCurrent = sourceCurrent,
            AvailableCash = cash
        };

        var result = Policy().Evaluate(input);

        Assert.False(result.IsEligible);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
    }

    [Fact]
    public void Evaluate_prefers_a_valid_early_payment_discount_date()
    {
        var result = Policy().Evaluate(Input(
            dueDate: new DateOnly(2026, 9, 18),
            requestedDate: new DateOnly(2026, 9, 4)) with
        {
            DiscountDate = new DateOnly(2026, 9, 5)
        });

        Assert.True(result.IsEligible);
        Assert.True(result.UsesEarlyPaymentDiscount);
        Assert.Equal(new DateOnly(2026, 9, 4), result.RecommendedExecutionDate);
    }

    [Fact]
    public void Evaluate_honors_configured_bank_holidays()
    {
        var policy = new PaymentBatchEligibilityPolicy(Options.Create(new PaymentBatchPolicyOptions
        {
            HolidayDates = ["2026-09-04"]
        }));

        var result = policy.Evaluate(Input(
            dueDate: new DateOnly(2026, 9, 6),
            requestedDate: new DateOnly(2026, 9, 3)));

        Assert.True(result.IsEligible);
        Assert.Equal(new DateOnly(2026, 9, 3), result.RecommendedExecutionDate);
    }

    private static PaymentBatchEligibilityPolicy Policy() => new(Options.Create(new PaymentBatchPolicyOptions()));

    private static PaymentBatchEligibilityInput Input(DateOnly dueDate, DateOnly requestedDate,
        DateTime? now = null) => new(
        PaymentBatchObligationTypes.SupplierPaymentProposal,
        1250m,
        "SEK",
        dueDate,
        null,
        false,
        false,
        false,
        false,
        true,
        true,
        PaymentRails.Bankgiro,
        5000m,
        requestedDate,
        now ?? FridayBeforeCutoff);
}
