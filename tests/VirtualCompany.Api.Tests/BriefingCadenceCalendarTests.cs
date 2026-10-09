using VirtualCompany.Application.Briefings;
using VirtualCompany.Infrastructure.Companies;
namespace VirtualCompany.Api.Tests;
public sealed class BriefingCadenceCalendarTests
{
    [Fact] public void Morning_on_working_days_does_not_accumulate_weekend_digests_on_monday()
    {
        var start = new DateTime(2026,10,5,0,0,0,DateTimeKind.Utc); var slot = Assert.Single(BriefingCadenceCalendar.Slots(BriefingCadenceFixture.Settings, start, start.AddHours(23)));
        Assert.Equal(start.AddHours(6).AddMinutes(30), slot.Utc); Assert.Equal(["morning"], slot.Kinds);
    }
    [Fact] public void Spring_gap_deferral_also_honors_the_actual_working_window()
    {
        var s = BriefingCadenceFixture.Settings with { WorkStart = new(0,0), WorkEnd = new(2,45), Workdays = [0,1,2,3,4,5,6], QuietStart = new(0,0), QuietEnd = new(0,0), Schedules = BriefingCadenceCalendar.Kinds.Select(k => new BriefingCadenceRow(k, k == "morning", new(2,30))).ToArray() };
        var next = BriefingCadenceCalendar.Next(s, new(2026,3,29,0,0,0,DateTimeKind.Utc))!;
        Assert.True(BriefingCadenceCalendar.Allowed(TimeZoneInfo.ConvertTimeFromUtc(next.Utc, BriefingCadenceCalendar.Zone(s.Timezone)), s));
        Assert.Equal(new DateTime(2026,3,29,22,0,0,DateTimeKind.Utc), next.Utc);
    }
    [Theory][InlineData(3,29,1,0)][InlineData(10,25,0,30)]
    public void Gap_advances_and_fold_has_one_earlier_utc_occurrence(int month, int day, int utcHour, int utcMinute)
    {
        var s = BriefingCadenceFixture.Settings with { WorkStart = new(0, 0), WorkEnd = new(23, 59), Workdays = [0,1,2,3,4,5,6], QuietStart = new(0,0), QuietEnd = new(0,0), Schedules = BriefingCadenceCalendar.Kinds.Select(k => new BriefingCadenceRow(k, k == "morning", new(2,30))).ToArray() };
        var start = new DateTime(2026, month, day, 0, 0, 0, DateTimeKind.Utc); var slots = BriefingCadenceCalendar.Slots(s, start, start.AddHours(23)).ToArray();
        var slot = Assert.Single(slots); Assert.Equal(new DateTime(2026, month, day, utcHour, utcMinute, 0, DateTimeKind.Utc), slot.Utc);
    }
    [Fact] public void Quiet_hours_weekend_and_overnight_shift_defer_without_duplicate_groups()
    {
        var s = BriefingCadenceFixture.Settings with { QuietStart = new(8, 0), QuietEnd = new(10, 0), Schedules = BriefingCadenceCalendar.Kinds.Select(k => new BriefingCadenceRow(k, k is "morning" or "weekly", new(8,30))).ToArray() };
        var monday = new DateTime(2026,10,5,0,0,0,DateTimeKind.Utc); var slot = Assert.Single(BriefingCadenceCalendar.Slots(s, monday, monday.AddHours(23)));
        Assert.Equal(monday.AddHours(8), slot.Utc); Assert.Equal(2, slot.Kinds.Length);
        var overnight = s with { WorkStart = new(22,0), WorkEnd = new(6,0), QuietStart = new(12,0), QuietEnd = new(13,0) };
        Assert.True(BriefingCadenceCalendar.Allowed(new(2026,10,5,23,0,0), overnight)); Assert.False(BriefingCadenceCalendar.Allowed(new(2026,10,5,15,0,0), overnight));
    }
    [Theory][InlineData("monthly",2,28)][InlineData("quarterly",4,1)][InlineData("annual",1,1)]
    public void Calendar_checkpoints_use_configured_day_and_month(string kind,int month,int day)
    {
        var s=BriefingCadenceFixture.Settings with { Workdays=[0,1,2,3,4,5,6],Schedules=BriefingCadenceCalendar.Kinds.Select(k=>new BriefingCadenceRow(k,k==kind,new(9,0),Day:kind=="monthly"?31:1)).ToArray() };
        var start=new DateTime(2026,month,day,0,0,0,DateTimeKind.Utc); Assert.Single(BriefingCadenceCalendar.Slots(s,start,start.AddHours(23)));
    }
}
