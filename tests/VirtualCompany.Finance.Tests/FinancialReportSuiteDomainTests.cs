using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class FinancialReportSuiteDomainTests
{
    [Fact]
    public void Golden_report_calculation_is_order_independent_and_checksum_stable()
    {
        FinancialReportAmountSeed[] seeds =
        [
            new("cash:bank", "operating", "Bank", "SEK", 100m, 80m, 900m, 2),
            new("cash:bank", "operating", "Bank", "SEK", 25.125m, 20m, 50m, 1),
            new("cash:asset", "investing", "Asset purchase", "SEK", -40m, -10m, -40m, 1)
        ];

        var first = FinancialReportSuiteCalculator.Calculate(seeds);
        var second = FinancialReportSuiteCalculator.Calculate(seeds.Reverse());

        Assert.Equal(first, second);
        Assert.Equal("financial-report-suite/1.0", FinancialReportSuiteCalculator.CalculationVersion);
        Assert.Equal(FinancialReportSuiteCalculator.Checksum(first), FinancialReportSuiteCalculator.Checksum(second));
        Assert.Equal(125.12m, first.Single(x => x.LineKey == "cash:bank").Amount);
    }

    [Theory]
    [InlineData("2026-08-31", "2026-08-31", "current", 0)]
    [InlineData("2026-08-01", "2026-08-31", "past_due_1_30", 30)]
    [InlineData("2026-06-01", "2026-08-31", "past_due_over_90", 91)]
    public void Aging_buckets_are_deterministic(string due, string asOf, string bucket, int days)
    {
        var result = FinancialReportSuiteCalculator.AgingBucket(DateOnly.Parse(due), DateOnly.Parse(asOf));
        Assert.Equal(bucket, result.Bucket);
        Assert.Equal(days, result.DaysPastDue);
    }

    [Fact]
    public void Mapping_retirement_preserves_the_original_classification()
    {
        var mapping = new FinancialStatementMapping(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            FinancialStatementType.CashFlow, FinancialStatementReportSection.CashFlowOperatingActivities,
            FinancialStatementLineClassification.WorkingCapital, true,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 3, new DateOnly(2026, 1, 1));

        mapping.Retire(new DateOnly(2026, 9, 1), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.False(mapping.IsActive);
        Assert.True(mapping.IsEffectiveOn(new DateOnly(2026, 8, 31)));
        Assert.False(mapping.IsEffectiveOn(new DateOnly(2026, 9, 1)));
        Assert.Equal(FinancialStatementLineClassification.WorkingCapital, mapping.LineClassification);
        Assert.Equal(3, mapping.VersionNumber);
    }

    [Fact]
    public void Snapshot_rejects_non_sha256_provenance()
    {
        Assert.Throws<ArgumentException>(() => new FinancialReportSuiteSnapshot(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), FinancialReportKinds.CashFlow, FinancialReportSuiteCalculator.CalculationVersion,
            "mapping", "not-a-hash", new string('a', 64), "{}", Guid.NewGuid(), "capture-1", DateTime.UtcNow));
    }
}
