using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class FixedAssetDomainTests
{
    [Fact]
    public void Straight_line_depreciation_prorates_first_period_and_retains_rounding()
    {
        var asset = CreateAsset(12_000m, 0m, 12);
        asset.MarkCapitalized(new DateOnly(2026, 1, 1), Utc(1));
        asset.PlaceInService(new DateOnly(2026, 1, 16), Utc(2));

        var result = FixedAssetDepreciationCalculator.Calculate(asset,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Equal(516.13m, result.Amount);
        Assert.Equal(1_000m, result.MonthlyAmount);
        Assert.Equal(16, result.EligibleDays);
        Assert.Equal(31, result.DaysInPeriod);
        Assert.Equal(FixedAssetBookMethods.StraightLine, result.Method);
    }

    [Fact]
    public void Straight_line_depreciation_sums_each_calendar_month_in_a_multi_month_period()
    {
        var asset = CreateAsset(12_000m, 0m, 12);
        asset.MarkCapitalized(new DateOnly(2026, 1, 1), Utc(1));
        asset.PlaceInService(new DateOnly(2026, 1, 16), Utc(2));

        var result = FixedAssetDepreciationCalculator.Calculate(asset,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        Assert.Equal(2_516.13m, result.Amount);
        Assert.Equal(75, result.EligibleDays);
        Assert.Equal(90, result.DaysInPeriod);
    }

    [Fact]
    public void Depreciation_and_impairment_never_cross_retained_residual_value()
    {
        var asset = CreateAsset(10_000m, 1_000m, 60);
        asset.MarkCapitalized(new DateOnly(2026, 1, 1), Utc(1));
        asset.PlaceInService(new DateOnly(2026, 1, 1), Utc(2));
        asset.ApplyDepreciation(2_000m, new DateOnly(2026, 12, 31), Utc(3));
        asset.ApplyImpairment(3_000m, Utc(4));

        Assert.Equal(5_000m, asset.NetBookValue);
        Assert.Throws<InvalidOperationException>(() => asset.ApplyImpairment(4_001m, Utc(5)));
        Assert.Throws<InvalidOperationException>(() => asset.ApplyDepreciation(4_001m,
            new DateOnly(2027, 12, 31), Utc(5)));
        Assert.Throws<InvalidOperationException>(() => asset.ApplyDepreciation(100m,
            new DateOnly(2026, 12, 31), Utc(5)));
    }

    [Fact]
    public void Disposal_and_reversal_restore_the_pre_disposal_book_state()
    {
        var asset = CreateAsset(20_000m, 2_000m, 60);
        asset.MarkCapitalized(new DateOnly(2026, 1, 1), Utc(1));
        asset.PlaceInService(new DateOnly(2026, 1, 1), Utc(2));
        asset.ApplyDepreciation(6_000m, new DateOnly(2027, 12, 31), Utc(3));

        asset.Dispose(new DateOnly(2028, 1, 31), 15_000m, 1_000m, Utc(4));
        Assert.Equal(FixedAssetStatuses.Disposed, asset.Status);
        Assert.Equal(14_000m, asset.NetBookValue);

        asset.ReverseDisposal(Utc(5));
        Assert.Equal(FixedAssetStatuses.InService, asset.Status);
        Assert.Null(asset.DisposalDate);
        Assert.Equal(14_000m, asset.NetBookValue);
    }

    [Fact]
    public void Unsupported_book_method_is_blocked_explicitly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedAssetClass(Guid.NewGuid(), Guid.NewGuid(),
            "MACH", "Machinery", "declining_balance", 60, 0m, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "A", true, Guid.NewGuid(), Utc(0)));
    }

    [Fact]
    public void Lifecycle_rejects_posting_states_and_dates_that_cannot_reconcile()
    {
        var asset = CreateAsset(10_000m, 0m, 60);

        Assert.Throws<InvalidOperationException>(() => asset.EnsureCanImprove(500m));
        Assert.Throws<InvalidOperationException>(() => asset.EnsureCanImpair(500m));
        Assert.Throws<InvalidOperationException>(() => asset.EnsureCanDispose(new DateOnly(2026, 1, 31), 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => asset.MarkCapitalized(
            new DateOnly(2025, 12, 31), Utc(1)));

        asset.MarkCapitalized(new DateOnly(2026, 1, 10), Utc(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => asset.PlaceInService(
            new DateOnly(2026, 1, 9), Utc(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => asset.EnsureCanDispose(
            new DateOnly(2026, 1, 31), -1m));
    }

    [Fact]
    public void Component_depreciation_uses_each_retained_life_and_reconciles_to_asset_total()
    {
        var asset = CreateAsset(18_000m, 0m, 60);
        asset.Components.Add(new(Guid.NewGuid(), asset.CompanyId, asset.Id, "ENGINE", "Engine",
            12_000m, 0m, 24, new DateOnly(2026, 1, 1)));
        asset.MarkCapitalized(new DateOnly(2026, 1, 1), Utc(1));
        asset.PlaceInService(new DateOnly(2026, 1, 1), Utc(2));

        var result = FixedAssetDepreciationCalculator.Calculate(asset,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        asset.ApplyDepreciation(result.Amount, new DateOnly(2026, 1, 31),
            result.ComponentAllocations, Utc(3));

        Assert.Equal(600m, result.Amount); // 500 component + 100 uncomponentized shell.
        Assert.Equal(500m, asset.Components.Single().AccumulatedDepreciation);
        Assert.Equal(result.Amount, asset.AccumulatedDepreciation);
        Assert.Contains("ENGINE", result.Explanation);
    }

    [Fact]
    public void Component_depreciation_reversal_restores_exact_retained_allocations()
    {
        var asset = CreateAsset(18_000m, 0m, 60);
        asset.Components.Add(new(Guid.NewGuid(), asset.CompanyId, asset.Id, "ENGINE", "Engine",
            12_000m, 0m, 24, new DateOnly(2026, 1, 1)));
        asset.MarkCapitalized(new DateOnly(2026, 1, 1), Utc(1));
        asset.PlaceInService(new DateOnly(2026, 1, 1), Utc(2));
        var result = FixedAssetDepreciationCalculator.Calculate(asset,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        asset.ApplyDepreciation(result.Amount, new DateOnly(2026, 1, 31),
            result.ComponentAllocations, Utc(3));

        asset.ReverseDepreciation(result.Amount, result.ComponentAllocations, null, Utc(4));

        Assert.Equal(0m, asset.AccumulatedDepreciation);
        Assert.Equal(0m, asset.Components.Single().AccumulatedDepreciation);
        Assert.Null(asset.LastDepreciationThrough);
    }

    [Fact]
    public void Rejected_component_depreciation_does_not_partially_mutate_component_balances()
    {
        var asset = CreateAsset(18_000m, 0m, 60);
        var component = new FixedAssetComponent(Guid.NewGuid(), asset.CompanyId, asset.Id,
            "ENGINE", "Engine", 12_000m, 0m, 24, new DateOnly(2026, 1, 1));
        asset.Components.Add(component);
        asset.MarkCapitalized(new DateOnly(2026, 1, 1), Utc(1));
        asset.PlaceInService(new DateOnly(2026, 1, 1), Utc(2));
        var result = FixedAssetDepreciationCalculator.Calculate(asset,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        asset.ApplyDepreciation(result.Amount, new DateOnly(2026, 1, 31),
            result.ComponentAllocations, Utc(3));
        var retainedComponentBalance = component.AccumulatedDepreciation;

        Assert.Throws<InvalidOperationException>(() => asset.ApplyDepreciation(100m,
            new DateOnly(2026, 1, 31),
            [new(component.Id, 100m, 12_000m, 11_500m, 31, 31, "retry")], Utc(4)));

        Assert.Equal(retainedComponentBalance, component.AccumulatedDepreciation);
    }

    private static FixedAssetRegisterItem CreateAsset(decimal cost, decimal residual, int life) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), "FA-100", "Machine", "SEK",
        cost, residual, life, FixedAssetBookMethods.StraightLine, new DateOnly(2026, 1, 1), "supplier_bill",
        "bill-1", "1", null, null, "Operations", "Stockholm", "{}", Guid.NewGuid(), Utc(0));
    private static DateTime Utc(int hour) => new(2026, 1, 1, hour, 0, 0, DateTimeKind.Utc);
}
