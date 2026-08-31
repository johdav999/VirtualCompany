using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Domain.Finance;

public sealed record FixedAssetDepreciationCalculation(decimal Amount, decimal DepreciableBasis,
    decimal MonthlyAmount, decimal RemainingDepreciableAmount, int EligibleDays, int DaysInPeriod,
    string Method, string Explanation,
    IReadOnlyList<FixedAssetComponentDepreciationAllocation> ComponentAllocations);

public static class FixedAssetDepreciationCalculator
{
    public static FixedAssetDepreciationCalculation Calculate(FixedAssetRegisterItem asset,
        DateOnly periodStart, DateOnly periodEnd)
    {
        if (periodEnd < periodStart) throw new ArgumentOutOfRangeException(nameof(periodEnd));
        if (!asset.PlacedInServiceDate.HasValue || asset.Status == FixedAssetStatuses.Disposed)
            return Empty(asset, "The asset is not in service for depreciation.");
        if (!FixedAssetBookMethods.IsSupported(asset.BookMethod))
            throw new InvalidOperationException("The retained book depreciation method is unsupported.");

        var serviceStart = asset.PlacedInServiceDate.Value;
        var eligibilityStart = asset.LastDepreciationThrough?.AddDays(1) ?? serviceStart;
        eligibilityStart = eligibilityStart > periodStart ? eligibilityStart : periodStart;
        var eligibilityEnd = periodEnd;
        if (asset.DisposalDate is DateOnly disposal && disposal < eligibilityEnd) eligibilityEnd = disposal.AddDays(-1);
        if (eligibilityEnd < eligibilityStart) return Empty(asset, "No undepreciated service days fall in this period.");

        var basis = Money(asset.GrossBookValue - asset.ResidualValue);
        var remaining = Money(Math.Max(0m, basis - asset.AccumulatedDepreciation - asset.AccumulatedImpairment));
        if (remaining <= 0m) return Empty(asset, "The depreciable carrying amount is fully released.");
        if (asset.Components.Count > 0)
            return CalculateComponents(asset, periodStart, periodEnd, eligibilityStart, eligibilityEnd,
                basis, remaining);
        var monthly = Money(basis / asset.UsefulLifeMonths);
        var eligibleDays = eligibilityEnd.DayNumber - eligibilityStart.DayNumber + 1;
        var daysInPeriod = periodEnd.DayNumber - periodStart.DayNumber + 1;
        var amount = ProrateByCalendarMonth(monthly, eligibilityStart, eligibilityEnd);
        amount = Math.Min(amount, remaining);
        return new(amount, basis, monthly, remaining, eligibleDays, daysInPeriod,
            FixedAssetBookMethods.StraightLine,
            $"Straight-line depreciation for {eligibleDays} eligible days, prorated by each calendar month's actual days using the retained asset terms.",
            [new(null, amount, basis, remaining, eligibleDays, daysInPeriod, "Uncomponentized asset balance")]);
    }

    private static FixedAssetDepreciationCalculation CalculateComponents(FixedAssetRegisterItem asset,
        DateOnly periodStart, DateOnly periodEnd, DateOnly assetEligibilityStart, DateOnly assetEligibilityEnd,
        decimal totalBasis, decimal totalRemaining)
    {
        var daysInPeriod = periodEnd.DayNumber - periodStart.DayNumber + 1;
        var allocations = new List<FixedAssetComponentDepreciationAllocation>();
        foreach (var component in asset.Components.OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            var start = component.PlacedInServiceDate > assetEligibilityStart
                ? component.PlacedInServiceDate : assetEligibilityStart;
            var componentBasis = Money(component.Cost - component.ResidualValue);
            var componentRemaining = Money(Math.Max(0m,
                componentBasis - component.AccumulatedDepreciation));
            var eligibleDays = assetEligibilityEnd < start ? 0
                : assetEligibilityEnd.DayNumber - start.DayNumber + 1;
            var componentAmount = eligibleDays == 0 ? 0m : ProrateByCalendarMonth(
                componentBasis / component.UsefulLifeMonths, start, assetEligibilityEnd);
            componentAmount = Math.Min(componentAmount, componentRemaining);
            allocations.Add(new(component.Id, componentAmount, componentBasis, componentRemaining,
                eligibleDays, daysInPeriod, $"Component {component.Code} over {component.UsefulLifeMonths} months"));
        }

        var componentCost = Money(asset.Components.Sum(x => x.Cost));
        var componentResidual = Money(asset.Components.Sum(x => x.ResidualValue));
        var componentDepreciation = Money(asset.Components.Sum(x => x.AccumulatedDepreciation));
        var shellBasis = Money(Math.Max(0m, asset.GrossBookValue - componentCost -
            Math.Max(0m, asset.ResidualValue - componentResidual)));
        var shellRemaining = Money(Math.Max(0m, shellBasis -
            Math.Max(0m, asset.AccumulatedDepreciation - componentDepreciation)));
        var shellDays = assetEligibilityEnd.DayNumber - assetEligibilityStart.DayNumber + 1;
        if (shellBasis > 0m)
        {
            var shellAmount = Math.Min(ProrateByCalendarMonth(shellBasis / asset.UsefulLifeMonths,
                    assetEligibilityStart, assetEligibilityEnd),
                shellRemaining);
            allocations.Add(new(null, shellAmount, shellBasis, shellRemaining, shellDays,
                daysInPeriod, "Uncomponentized asset balance"));
        }

        var calculated = Money(allocations.Sum(x => x.Amount));
        var amount = Math.Min(calculated, totalRemaining);
        if (amount < calculated)
        {
            var excess = calculated - amount;
            for (var index = allocations.Count - 1; index >= 0 && excess > 0m; index--)
            {
                var reduction = Math.Min(excess, allocations[index].Amount);
                allocations[index] = allocations[index] with { Amount = allocations[index].Amount - reduction };
                excess = Money(excess - reduction);
            }
        }
        var eligible = allocations.Count == 0 ? 0 : allocations.Max(x => x.EligibleDays);
        var explanation = $"Component straight-line depreciation ({allocations.Count(x => x.ComponentId.HasValue)} retained components; allocations: {string.Join(", ", allocations.Where(x => x.Amount > 0m).Select(x => $"{x.Explanation}={x.Amount:0.00}"))}).";
        return new(amount, totalBasis, Money(allocations.Sum(x => x.DepreciableBasis /
            (x.ComponentId.HasValue ? asset.Components.Single(c => c.Id == x.ComponentId).UsefulLifeMonths : asset.UsefulLifeMonths))),
            totalRemaining, eligible, daysInPeriod, FixedAssetBookMethods.StraightLine,
            explanation, allocations);
    }

    private static FixedAssetDepreciationCalculation Empty(FixedAssetRegisterItem asset, string explanation)
    {
        var basis = Money(Math.Max(0m, asset.GrossBookValue - asset.ResidualValue));
        var remaining = Money(Math.Max(0m, basis - asset.AccumulatedDepreciation - asset.AccumulatedImpairment));
        return new(0m, basis, 0m, remaining, 0, 0, asset.BookMethod, explanation, []);
    }

    private static decimal ProrateByCalendarMonth(decimal monthlyAmount, DateOnly start, DateOnly end)
    {
        if (end < start) return 0m;

        var cursor = start;
        decimal total = 0m;
        while (cursor <= end)
        {
            var monthEnd = new DateOnly(cursor.Year, cursor.Month,
                DateTime.DaysInMonth(cursor.Year, cursor.Month));
            var segmentEnd = monthEnd < end ? monthEnd : end;
            var eligibleDays = segmentEnd.DayNumber - cursor.DayNumber + 1;
            total += monthlyAmount * eligibleDays / DateTime.DaysInMonth(cursor.Year, cursor.Month);
            cursor = segmentEnd.AddDays(1);
        }

        return Money(total);
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
