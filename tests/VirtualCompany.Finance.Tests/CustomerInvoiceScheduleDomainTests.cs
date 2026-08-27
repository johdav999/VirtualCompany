using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class CustomerInvoiceScheduleDomainTests
{
    [Fact]
    public void Month_end_and_leap_day_occurrences_are_deterministic()
    {
        var schedule = Create(new DateOnly(2026, 1, 31), "monthly", 31);

        Assert.Equal(new DateOnly(2026, 2, 28), schedule.ResolveOccurrence(new DateOnly(2026, 2, 1)));
        schedule.AdvanceAfterGeneration(new DateOnly(2026, 1, 31), Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(new DateOnly(2026, 2, 28), schedule.NextOccurrenceDate);
        Assert.Equal(new DateOnly(2026, 3, 31), schedule.ResolveOccurrence(schedule.NextOccurrenceDate.AddMonths(1)));
    }

    [Fact]
    public void Weekend_convention_and_occurrence_lease_prevent_duplicate_claims()
    {
        var schedule = Create(new DateOnly(2026, 8, 1), "monthly", 1, "following");
        Assert.Equal(new DateOnly(2026, 8, 3), schedule.NextOccurrenceDate);
        var occurrence = new CustomerInvoiceScheduleOccurrence(Guid.NewGuid(), schedule.CompanyId, schedule.Id,
            schedule.NextOccurrenceDate, schedule.NextOccurrenceDate, schedule.DueDateFor(schedule.NextOccurrenceDate),
            schedule.Version, schedule.TemplateVersion, schedule.TemplateHash, DateTime.UtcNow);
        Assert.True(occurrence.TryClaim("worker-a", DateTime.UtcNow, TimeSpan.FromMinutes(1)));
        Assert.False(occurrence.TryClaim("worker-b", DateTime.UtcNow, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Preceding_business_day_advances_without_repeating_the_same_occurrence()
    {
        var schedule = Create(new DateOnly(2026, 7, 1), "monthly", 1, "preceding");

        Assert.Equal(new DateOnly(2026, 7, 1), schedule.NextOccurrenceDate);
        schedule.AdvanceAfterGeneration(schedule.NextOccurrenceDate, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(new DateOnly(2026, 7, 31), schedule.NextOccurrenceDate);
        schedule.AdvanceAfterGeneration(schedule.NextOccurrenceDate, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(new DateOnly(2026, 9, 1), schedule.NextOccurrenceDate);
    }

    [Fact]
    public void Resume_skips_past_occurrences_unless_backdated_generation_is_explicitly_allowed()
    {
        var schedule = Create(new DateOnly(2026, 1, 1), "monthly", 1);
        schedule.Activate(Guid.NewGuid(), DateTime.UtcNow, new DateOnly(2026, 1, 1));
        schedule.Pause(Guid.NewGuid(), DateTime.UtcNow);

        schedule.Resume(Guid.NewGuid(), DateTime.UtcNow, new DateOnly(2026, 3, 15), false);

        Assert.Equal(new DateOnly(2026, 4, 1), schedule.NextOccurrenceDate);
    }

    [Fact]
    public void Daily_proration_is_applied_only_to_the_first_partial_billing_period()
    {
        var schedule = new CustomerInvoiceSchedule(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Monthly services",
            new DateOnly(2026, 1, 15), null, "monthly", 1, "Europe/Stockholm", "calendar", "daily", 30,
            CustomerInvoiceDraftDocumentTypes.Invoice, "SEK", "net", 30, null, null, null, "email", false,
            new string('a', 64), Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(new DateOnly(2026, 2, 1), schedule.NextOccurrenceDate);
        Assert.Equal(17m / 31m, schedule.ProrationFactorFor(schedule.NextOccurrenceDate), 6);
        Assert.Equal(1m, schedule.ProrationFactorFor(schedule.NextOccurrenceAfter(schedule.NextOccurrenceDate)));
    }

    [Fact]
    public void Template_change_invalidates_bound_approval_and_increments_template_version()
    {
        var schedule = Create(new DateOnly(2026, 1, 1), "monthly", 1);
        var approvalId = Guid.NewGuid();
        schedule.BindApproval(approvalId, Guid.NewGuid(), DateTime.UtcNow);

        schedule.Update(schedule.CustomerId, "Updated services", schedule.StartDate, schedule.EndDate,
            schedule.Cadence, schedule.BillingDay, schedule.TimeZoneId, schedule.BusinessDayConvention,
            schedule.ProrationRule, schedule.DueDateOffsetDays, schedule.DocumentType, schedule.Currency,
            schedule.PaymentTermKind, schedule.PaymentTermDays, null, null, null, schedule.DeliveryIntent,
            false, new string('b', 64), Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(2, schedule.TemplateVersion);
        Assert.Equal(new string('b', 64), schedule.TemplateHash);
        Assert.Null(schedule.ApprovalRequestId);
        Assert.Null(schedule.ApprovalTemplateVersion);
        Assert.Equal(CustomerInvoiceScheduleStatuses.Draft, schedule.Status);
    }

    [Fact]
    public void Transient_occurrence_failure_obeys_durable_retry_time_and_stale_owner_cannot_complete()
    {
        var schedule = Create(new DateOnly(2026, 1, 1), "monthly", 1);
        var now = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var occurrence = new CustomerInvoiceScheduleOccurrence(Guid.NewGuid(), schedule.CompanyId, schedule.Id,
            schedule.NextOccurrenceDate, schedule.NextOccurrenceDate, schedule.DueDateFor(schedule.NextOccurrenceDate),
            schedule.Version, schedule.TemplateVersion, schedule.TemplateHash, now);

        Assert.True(occurrence.TryClaim("worker-a", now, TimeSpan.FromMinutes(1)));
        Assert.True(occurrence.TryReleaseRetry("worker-a", "temporary", "Try later.", now,
            TimeSpan.FromMinutes(5)));
        Assert.False(occurrence.TryClaim("worker-b", now.AddMinutes(4), TimeSpan.FromMinutes(1)));
        Assert.True(occurrence.TryClaim("worker-b", now.AddMinutes(5), TimeSpan.FromMinutes(1)));
        Assert.False(occurrence.TryMarkGenerated("worker-a", Guid.NewGuid(), now.AddMinutes(5)));
        Assert.True(occurrence.TryMarkGenerated("worker-b", Guid.NewGuid(), now.AddMinutes(5)));
    }

    private static CustomerInvoiceSchedule Create(DateOnly start, string cadence, int day, string convention = "calendar") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Monthly services", start, null, cadence, day,
            "Europe/Stockholm", convention, "none", 30, CustomerInvoiceDraftDocumentTypes.Invoice, "SEK", "net", 30,
            null, null, null, "email", false, new string('a', 64), Guid.NewGuid(), DateTime.UtcNow);
}
