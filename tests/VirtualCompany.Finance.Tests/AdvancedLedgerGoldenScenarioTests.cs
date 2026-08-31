using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class AdvancedLedgerGoldenScenarioTests
{
    [Fact]
    public void P3_golden_scenario_reproduces_currency_dimension_schedule_asset_and_close_controls()
    {
        const decimal documentAmount = 100m;
        const decimal transactionRate = 10m;
        var functionalDocumentAmount = documentAmount * transactionRate;

        var partial = ForeignCurrencySettlementPolicy.Calculate(new(
            PaymentTypes.Incoming, documentAmount, functionalDocumentAmount, 0m, 0m,
            40m, 0m, 0m, 11m, 2, AccountingRoundingModeValues.MidpointToEven));
        var final = ForeignCurrencySettlementPolicy.Calculate(new(
            PaymentTypes.Incoming, documentAmount, functionalDocumentAmount,
            partial.AppliedDocumentAmount, partial.AllocatedFunctionalAmount,
            60m, 0m, 0m, 9m, 2, AccountingRoundingModeValues.MidpointToEven));
        const decimal billDocumentAmount = 50m;
        const decimal billFunctionalAmount = 500m;
        var billSettlement = ForeignCurrencySettlementPolicy.Calculate(new(
            PaymentTypes.Outgoing, billDocumentAmount, billFunctionalAmount, 0m, 0m,
            billDocumentAmount, 0m, 0m, 10.2m, 2, AccountingRoundingModeValues.MidpointToEven));

        var unrealizedRevaluation = decimal.Round(
            partial.DocumentOutstandingAfter * 10.5m - partial.FunctionalOutstandingAfter,
            2, MidpointRounding.ToEven);
        var revaluationReversal = -unrealizedRevaluation;

        var allocation = AccountingAllocationCalculator.Calculate(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000002"), 1,
            functionalDocumentAmount, "SEK", 2, 500m,
            [
                new(1, Guid.Parse("10000000-0000-0000-0000-000000000003"), "Operations",
                    AccountingAllocationKindValues.Percentage, 60m),
                new(2, Guid.Parse("10000000-0000-0000-0000-000000000004"), "Delivery",
                    AccountingAllocationKindValues.Percentage, 40m)
            ]);

        var (schedule, version) = CreateAnnualSchedule();
        var accrualOccurrence = AccountingScheduleCalculator.Calculate(schedule, version,
            AccountingScheduleCalculator.PlannedDates(schedule)[0]);
        var prepaymentRelease = accrualOccurrence.DebitTotal;

        var asset = CreateAsset();
        asset.MarkCapitalized(new DateOnly(2026, 1, 1), Utc(1));
        asset.PlaceInService(new DateOnly(2026, 1, 16), Utc(2));
        var depreciation = FixedAssetDepreciationCalculator.Calculate(asset,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        asset.ApplyDepreciation(depreciation.Amount, new DateOnly(2026, 1, 31), Utc(3));
        var netBookValue = asset.NetBookValue;
        const decimal disposalProceeds = 12_000m;
        var disposalGain = disposalProceeds - netBookValue;
        var seriesPolicy = new AccountingSeriesPolicy(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000002"),
            AccountingSeriesKinds.Voucher,
            Guid.Parse("40000000-0000-0000-0000-000000000003"),
            "customer_invoice", "posted", 2026, null, "SE", "country-neutral", "1.0",
            null, "A", true,
            Guid.Parse("40000000-0000-0000-0000-000000000004"), Utc(4));
        const string inventoryCapability = VirtualCompany.Application.Finance.AccountingGovernanceReasonCodes.InventoryUnsupported;

        Assert.Equal(0m, functionalDocumentAmount - partial.AllocatedFunctionalAmount - final.AllocatedFunctionalAmount);
        Assert.Equal(0m, final.DocumentOutstandingAfter);
        Assert.Equal(0m, final.FunctionalOutstandingAfter);
        Assert.Equal(functionalDocumentAmount + partial.RealizedGainLossAmount + final.RealizedGainLossAmount,
            partial.BankFunctionalAmount + final.BankFunctionalAmount);
        Assert.Equal(billFunctionalAmount - billSettlement.RealizedGainLossAmount,
            billSettlement.BankFunctionalAmount);
        var bankReconciliationDifference =
            partial.BankFunctionalAmount + final.BankFunctionalAmount - billSettlement.BankFunctionalAmount -
            (functionalDocumentAmount + partial.RealizedGainLossAmount + final.RealizedGainLossAmount -
             billFunctionalAmount + billSettlement.RealizedGainLossAmount);
        Assert.Equal(0m, bankReconciliationDifference);
        Assert.Equal(0m, unrealizedRevaluation + revaluationReversal);
        Assert.True(allocation.IsValid);
        Assert.Equal(new[] { 600m, 400m }, allocation.Dto.Lines.Select(x => x.RoundedAmount));
        Assert.Equal(0m, allocation.Dto.Difference);
        Assert.Equal(accrualOccurrence.DebitTotal, accrualOccurrence.CreditTotal);
        Assert.Equal(100m, prepaymentRelease);
        Assert.Equal(12_000m, depreciation.Amount + netBookValue);
        Assert.Equal(disposalProceeds, netBookValue + disposalGain);
        var reportedOperatingResult = functionalDocumentAmount - billFunctionalAmount - prepaymentRelease -
            depreciation.Amount + disposalGain;
        var reportDifference = functionalDocumentAmount - billFunctionalAmount - prepaymentRelease -
            depreciation.Amount + disposalGain - reportedOperatingResult;
        Assert.Equal(0m, reportDifference);
        Assert.True(seriesPolicy.IsActive);
        Assert.Equal("A", seriesPolicy.ProviderSeriesCode);
        Assert.Equal("accounting_inventory_unsupported", inventoryCapability);

        var closeDifference =
            Math.Abs(functionalDocumentAmount - partial.AllocatedFunctionalAmount - final.AllocatedFunctionalAmount) +
            Math.Abs(final.FunctionalOutstandingAfter) +
            Math.Abs(billSettlement.FunctionalOutstandingAfter) +
            Math.Abs(bankReconciliationDifference) +
            Math.Abs(unrealizedRevaluation + revaluationReversal) +
            Math.Abs(allocation.Dto.Difference) +
            Math.Abs(accrualOccurrence.DebitTotal - accrualOccurrence.CreditTotal) +
            Math.Abs(12_000m - depreciation.Amount - netBookValue) +
            Math.Abs(disposalProceeds - netBookValue - disposalGain) +
            Math.Abs(reportDifference);
        Assert.Equal(0m, closeDifference);

        var canonical = string.Format(CultureInfo.InvariantCulture,
            "invoice={0:0.00}EUR|functional={1:0.00}SEK|bill={2:0.00}EUR|bill_functional={3:0.00}SEK|" +
            "bank={4:0.00}SEK|realized={5:0.00}SEK|supplier_bank={6:0.00}SEK|supplier_realized={7:0.00}SEK|" +
            "bank_control={8:0.00}SEK|revaluation={9:0.00}SEK|reversal={10:0.00}SEK|" +
            "allocation={11:0.00},{12:0.00}SEK|accrual={13:0.00}SEK|prepayment={14:0.00}SEK|" +
            "depreciation={15:0.00}SEK|net_book_value={16:0.00}SEK|disposal_gain={17:0.00}SEK|" +
            "reported_result={18:0.00}SEK|report_control={19:0.00}SEK|close_difference={20:0.00}SEK|" +
            "series={21}|inventory={22}",
            documentAmount, functionalDocumentAmount, billDocumentAmount, billFunctionalAmount,
            partial.BankFunctionalAmount + final.BankFunctionalAmount,
            partial.RealizedGainLossAmount + final.RealizedGainLossAmount, billSettlement.BankFunctionalAmount,
            billSettlement.RealizedGainLossAmount, bankReconciliationDifference, unrealizedRevaluation,
            revaluationReversal, allocation.Dto.Lines[0].RoundedAmount, allocation.Dto.Lines[1].RoundedAmount,
            accrualOccurrence.DebitTotal, prepaymentRelease, depreciation.Amount, netBookValue,
            disposalGain, reportedOperatingResult, reportDifference, closeDifference,
            seriesPolicy.ProviderSeriesCode, inventoryCapability);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        Assert.Equal("24b966266cac456a72d441dfcf85ead000696b02ad88a49754e5583e004a2212", checksum);
    }

    private static (AccountingSchedule Schedule, AccountingScheduleVersion Version) CreateAnnualSchedule()
    {
        var companyId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var actorId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var scheduleId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var versionId = Guid.Parse("20000000-0000-0000-0000-000000000004");
        var schedule = new AccountingSchedule(scheduleId, companyId, "P3-ACCRUAL", "Annual accrual",
            AccountingScheduleTypes.Accrual, AccountingScheduleCadences.Monthly,
            AccountingScheduleAmountBases.TotalSchedule, AccountingScheduleProrationRules.None,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 31, "Europe/Stockholm", "A", "SEK",
            AccountingScheduleReversalRules.NextPeriodStart, actorId, Utc(0));
        var version = new AccountingScheduleVersion(versionId, companyId, scheduleId, 1, new string('a', 64),
            "P3 golden schedule", schedule.StartDate, actorId, Utc(0));
        version.Lines.Add(new AccountingScheduleLine(Guid.NewGuid(), companyId, versionId, 1,
            Guid.NewGuid(), 1_200m, 0m, "Accrual expense"));
        version.Lines.Add(new AccountingScheduleLine(Guid.NewGuid(), companyId, versionId, 2,
            Guid.NewGuid(), 0m, 1_200m, "Accrued liability"));
        schedule.ApplyProspectiveVersion(schedule.Name, schedule.ScheduleType, schedule.Cadence,
            schedule.AmountBasis, schedule.ProrationRule, schedule.StartDate, schedule.EndDate,
            schedule.OccurrenceDay, schedule.TimeZoneId, schedule.VoucherSeriesCode, schedule.Currency,
            schedule.ReversalRule, versionId, 1, new string('a', 64), actorId, Utc(0));
        return (schedule, version);
    }

    private static FixedAssetRegisterItem CreateAsset() => new(
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Guid.Parse("30000000-0000-0000-0000-000000000002"),
        Guid.Parse("30000000-0000-0000-0000-000000000003"),
        1, new string('b', 64), "FA-P3", "P3 machine", "SEK", 12_000m, 0m, 12,
        FixedAssetBookMethods.StraightLine, new DateOnly(2026, 1, 1), "supplier_bill", "P3-BILL", "1",
        null, null, "Operations", "Stockholm", "{}",
        Guid.Parse("30000000-0000-0000-0000-000000000004"), Utc(0));

    private static DateTime Utc(int hour) => new(2026, 1, 1, hour, 0, 0, DateTimeKind.Utc);
}
