using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingReportingDomainTests
{
    [Fact]
    public void Reopening_preserves_reporting_history_fields_without_touching_vouchers()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var period = new FiscalPeriod(Guid.NewGuid(), companyId, "August 2026",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), isClosed: true,
            closedUtc: new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc));
        period.LockReporting(actorId, new DateTime(2026, 9, 2, 8, 5, 0, DateTimeKind.Utc));

        period.Reopen(actorId, new DateTime(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc));

        Assert.False(period.IsClosed);
        Assert.False(period.IsReportingLocked);
        Assert.Null(period.ClosedUtc);
        Assert.Equal(actorId, period.ReportingUnlockedByUserId);
        Assert.NotNull(period.ReportingLockedUtc);
    }

    [Fact]
    public void Export_job_has_recoverable_bounded_state_transitions_and_download_metadata()
    {
        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var job = new AccountingExportJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "export:period:1", now, now.AddDays(30));

        job.Start(now.AddSeconds(1));
        job.Retry("temporary_failure", "The export will be retried.", now.AddMinutes(1), now.AddSeconds(2));
        job.Start(now.AddMinutes(1));
        job.Complete([1, 2, 3], new string('a', 64), "accounting-export.json", now.AddMinutes(1).AddSeconds(1));

        Assert.Equal(AccountingExportStatuses.Completed, job.Status);
        Assert.Equal(2, job.AttemptCount);
        Assert.Equal(3, job.ContentLength);
        Assert.Equal("application/json", job.MediaType);
        Assert.Equal(new string('a', 64), job.Checksum);
        Assert.Null(job.FailureSummary);
    }

    [Fact]
    public void Tax_review_replacement_binds_review_to_the_current_summary_checksum()
    {
        var actor = Guid.NewGuid();
        var review = new AccountingTaxReview(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "{}",
            new string('a', 64), actor, DateTime.UtcNow);

        review.Replace("{\"lines\":[]}", new string('b', 64), actor, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(new string('b', 64), review.Checksum);
        Assert.Contains("lines", review.SummaryJson, StringComparison.Ordinal);
    }
}
