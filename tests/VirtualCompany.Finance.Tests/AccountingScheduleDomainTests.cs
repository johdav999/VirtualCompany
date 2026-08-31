using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Finance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingScheduleDomainTests
{
    [Fact]
    public void Total_schedule_allocation_uses_a_deterministic_final_residual()
    {
        var (schedule, version) = CreateSchedule(AccountingScheduleAmountBases.TotalSchedule,
            AccountingScheduleProrationRules.None, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 100m);

        var dates = AccountingScheduleCalculator.PlannedDates(schedule);
        var regular = AccountingScheduleCalculator.Calculate(schedule, version, dates[0]);
        var final = AccountingScheduleCalculator.Calculate(schedule, version, dates[^1]);

        Assert.Equal(12, dates.Count);
        Assert.Equal(8.33m, regular.DebitTotal);
        Assert.Equal(8.37m, final.DebitTotal);
        Assert.Equal(100m, dates.Sum(date => AccountingScheduleCalculator.Calculate(schedule, version, date).DebitTotal));
        Assert.Equal(dates.Select(date => AccountingScheduleCalculator.Calculate(schedule, version, date).DebitTotal),
            dates.Select(date => AccountingScheduleCalculator.Calculate(schedule, version, date).CreditTotal));
    }

    [Fact]
    public void Daily_proration_is_reproducible_and_preserves_dimensions()
    {
        var dimensionId = Guid.NewGuid();
        var (schedule, version) = CreateSchedule(AccountingScheduleAmountBases.PerOccurrence,
            AccountingScheduleProrationRules.Daily, new DateOnly(2026, 1, 16), new DateOnly(2026, 3, 31), 310m,
            dimensionId);

        var result = AccountingScheduleCalculator.Calculate(schedule, version, new DateOnly(2026, 1, 31));

        Assert.Equal(decimal.Round(15m / 31m, 6, MidpointRounding.AwayFromZero), result.ProrationFactor);
        Assert.Equal(150m, result.DebitTotal);
        Assert.Equal(result.DebitTotal, result.CreditTotal);
        Assert.Equal(dimensionId, Assert.Single(result.Lines[0].DimensionMemberIds));
    }

    [Fact]
    public void Prospective_version_invalidates_the_prior_approval_binding()
    {
        var (schedule, _) = CreateSchedule(AccountingScheduleAmountBases.PerOccurrence,
            AccountingScheduleProrationRules.None, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 100m);
        var actor = schedule.CreatedByUserId;
        schedule.Submit(Guid.NewGuid(), actor, Utc(1));

        schedule.ApplyProspectiveVersion("Updated", AccountingScheduleTypes.RecurringFixed,
            AccountingScheduleCadences.Monthly, AccountingScheduleAmountBases.PerOccurrence,
            AccountingScheduleProrationRules.None, schedule.StartDate, schedule.EndDate, 31, "Europe/Stockholm",
            "A", "SEK", AccountingScheduleReversalRules.None, Guid.NewGuid(), 2, Hash('b'), actor, Utc(2));

        Assert.Equal(AccountingScheduleStatuses.Draft, schedule.Status);
        Assert.Null(schedule.ApprovalRequestId);
        Assert.Equal(2, schedule.CurrentVersionNumber);
    }

    [Fact]
    public void Occurrence_lease_prevents_duplicate_posting_and_allows_expiry_recovery()
    {
        var now = Utc(0);
        var occurrence = new AccountingScheduleOccurrence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1, Hash('a'), new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 31), 100m, "SEK",
            AccountingScheduleReversalRules.None, null, now);

        Assert.True(occurrence.TryClaim("worker-one", now, TimeSpan.FromMinutes(5)));
        Assert.False(occurrence.TryClaim("worker-two", now.AddMinutes(1), TimeSpan.FromMinutes(5)));
        Assert.True(occurrence.TryClaim("worker-two", now.AddMinutes(6), TimeSpan.FromMinutes(5)));
        occurrence.MarkPosted("worker-two", Guid.NewGuid(), now.AddMinutes(7));

        Assert.Equal(AccountingScheduleOccurrenceStatuses.Posted, occurrence.Status);
        Assert.False(occurrence.TryClaim("worker-one", now.AddMinutes(8), TimeSpan.FromMinutes(5)));
        Assert.Equal(2, occurrence.AttemptCount);
    }

    [Fact]
    public async Task Schedule_persistence_is_tenant_filtered_and_optimistically_concurrent()
    {
        var firstCompany = Guid.NewGuid(); var secondCompany = Guid.NewGuid(); var actor = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using (var setup = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(firstCompany, actor)))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Companies.Add(new Company(firstCompany, "First"));
            setup.AccountingSchedules.Add(CreateDraft(firstCompany, actor, "FIRST"));
            await setup.SaveChangesAsync();
        }
        await using (var setup = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(secondCompany, actor)))
        {
            setup.Companies.Add(new Company(secondCompany, "Second"));
            setup.AccountingSchedules.Add(CreateDraft(secondCompany, actor, "SECOND"));
            await setup.SaveChangesAsync();
        }

        await using var first = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(firstCompany, actor));
        await using var second = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(firstCompany, actor));
        Assert.Equal("FIRST", (await first.AccountingSchedules.SingleAsync()).Code);
        var stale = await second.AccountingSchedules.SingleAsync();
        (await first.AccountingSchedules.SingleAsync()).End(actor, Utc(1));
        stale.End(actor, Utc(2));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static (AccountingSchedule Schedule, AccountingScheduleVersion Version) CreateSchedule(string amountBasis,
        string proration, DateOnly start, DateOnly end, decimal amount, Guid? dimensionId = null)
    {
        var company = Guid.NewGuid(); var actor = Guid.NewGuid(); var scheduleId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        var schedule = new AccountingSchedule(scheduleId, company, "sched-1", "Schedule", AccountingScheduleTypes.RecurringFixed,
            AccountingScheduleCadences.Monthly, amountBasis, proration, start, end, 31, "Europe/Stockholm", "A", "SEK",
            AccountingScheduleReversalRules.None, actor, Utc(0));
        var version = new AccountingScheduleVersion(versionId, company, scheduleId, 1, Hash('a'), "Evidence-bound version", start, actor, Utc(0));
        var debit = new AccountingScheduleLine(Guid.NewGuid(), company, versionId, 1, Guid.NewGuid(), amount, 0m, "Debit");
        var credit = new AccountingScheduleLine(Guid.NewGuid(), company, versionId, 2, Guid.NewGuid(), 0m, amount, "Credit");
        if (dimensionId.HasValue) debit.DimensionAssignments.Add(new(Guid.NewGuid(), company, debit.Id, dimensionId.Value));
        version.Lines.Add(debit); version.Lines.Add(credit);
        schedule.ApplyProspectiveVersion(schedule.Name, schedule.ScheduleType, schedule.Cadence, schedule.AmountBasis,
            schedule.ProrationRule, schedule.StartDate, schedule.EndDate, schedule.OccurrenceDay, schedule.TimeZoneId,
            schedule.VoucherSeriesCode, schedule.Currency, schedule.ReversalRule, versionId, 1, Hash('a'), actor, Utc(0));
        return (schedule, version);
    }

    private static string Hash(char value) => new(value, 64);
    private static DateTime Utc(int minute) => new(2026, 1, 1, 10, minute, 0, DateTimeKind.Utc);
    private static AccountingSchedule CreateDraft(Guid companyId, Guid actorId, string code) =>
        new(Guid.NewGuid(), companyId, code, code, AccountingScheduleTypes.RecurringFixed,
            AccountingScheduleCadences.Monthly, AccountingScheduleAmountBases.PerOccurrence,
            AccountingScheduleProrationRules.None, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31),
            31, "Europe/Stockholm", "A", "SEK", AccountingScheduleReversalRules.None, actorId, Utc(0));

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) => CompanyId = resolvedCompanyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
}
