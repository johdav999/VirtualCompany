using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingReportingDomainTests
{
    [Fact]
    public void Fiscal_period_maps_a_concurrency_token_for_close_workflows()
    {
        using var dbContext = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options);

        var rowVersion = dbContext.Model.FindEntityType(typeof(FiscalPeriod))!
            .FindProperty(nameof(FiscalPeriod.RowVersion));

        Assert.NotNull(rowVersion);
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, rowVersion.ValueGenerated);
    }

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
    public void Statutory_export_lease_can_only_be_reclaimed_after_expiry_and_persists_object_metadata()
    {
        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var job = new AccountingExportJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "statutory:2026", now, now.AddDays(30), AccountingExportTypeValues.SwedishStatutoryArchive, "corr-1");

        job.Start("worker-1", now.AddMinutes(5), now);
        Assert.Throws<InvalidOperationException>(() => job.Start("worker-2", now.AddMinutes(6), now.AddMinutes(1)));
        job.Start("worker-2", now.AddMinutes(11), now.AddMinutes(6));
        job.Complete(null, new string('a', 64), "archive.zip", "application/zip", "companies/c/archive.zip",
            "Virtual Company Swedish statutory archive 1.0", new string('b', 64), "zip", 4, 2, 6,
            1250m, 1250m, "{}", now.AddMinutes(7));
        job.SetStoredContentLength(4096);

        Assert.Equal(2, job.AttemptCount);
        Assert.Equal(AccountingExportTypeValues.SwedishStatutoryArchive, job.ExportType);
        Assert.Equal("corr-1", job.CorrelationId);
        Assert.Null(job.Content);
        Assert.Equal("companies/c/archive.zip", job.StorageKey);
        Assert.Equal(4096, job.ContentLength);
        Assert.Null(job.LeaseOwner);
        Assert.Null(job.LeaseExpiresUtc);
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
