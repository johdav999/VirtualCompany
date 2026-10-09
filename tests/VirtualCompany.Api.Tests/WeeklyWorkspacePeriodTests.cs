using VirtualCompany.Application.Cockpit;

namespace VirtualCompany.Api.Tests;

public sealed class WeeklyWorkspacePeriodTests
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
    [Theory]
    [InlineData(2026, 3, 23, 167)]
    [InlineData(2026, 10, 19, 169)]
    [InlineData(2026, 9, 28, 168)]
    public void Completed_calendar_week_tracks_actual_DST_duration(int year, int month, int day, int hours)
    {
        var selected = new DateOnly(year, month, day);
        var p = WeeklyWorkspacePeriod.Resolve(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), Stockholm, selected);
        Assert.Equal(hours, (p.EndUtc - p.StartUtc).TotalHours); Assert.Equal(p.EndUtc, p.ActivityEndUtc);
        Assert.Equal(p.StartUtc, p.ComparisonEndUtc); Assert.False(p.IsWeekToDate); Assert.Equal(selected, p.WeekStart);
    }
    [Fact]
    public void Week_to_date_compares_same_elapsed_local_time_across_DST()
    {
        var now = new DateTime(2026, 3, 29, 12, 0, 0, DateTimeKind.Utc);
        var p = WeeklyWorkspacePeriod.Resolve(now, Stockholm);
        Assert.Equal(now, p.ActivityEndUtc); Assert.True(p.IsWeekToDate);
        Assert.Equal(new DateTime(2026, 3, 22, 13, 0, 0, DateTimeKind.Utc), p.ComparisonEndUtc);
        Assert.Contains("same elapsed local interval", p.ComparisonLabel);
    }
    [Theory]
    [InlineData(DayOfWeek.Monday, 2025, 12, 29)]
    [InlineData(DayOfWeek.Sunday, 2025, 12, 28)]
    [InlineData(DayOfWeek.Thursday, 2026, 1, 1)]
    public void Week_start_configuration_crosses_year_and_does_not_follow_fiscal_month(DayOfWeek start, int year, int month, int day)
    {
        var p = WeeklyWorkspacePeriod.Resolve(new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc, weekStartsOn: start);
        Assert.Equal(new DateOnly(year, month, day), p.WeekStart); Assert.Equal(start, p.WeekStartsOn);
    }
    [Fact]
    public void Company_local_date_selects_next_week_before_UTC_midnight()
    {
        var p = WeeklyWorkspacePeriod.Resolve(new DateTime(2026, 9, 27, 22, 30, 0, DateTimeKind.Utc), Stockholm);
        Assert.Equal(new DateOnly(2026, 9, 28), p.WeekStart); Assert.Equal(WeeklyWorkspaceFixture.Start, p.StartUtc);
    }
    [Fact]
    public void Future_or_invalid_calendar_requests_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => WeeklyWorkspacePeriod.Resolve(WeeklyWorkspaceFixture.Now, Stockholm, new DateOnly(2026, 10, 12)));
        Assert.Throws<ArgumentOutOfRangeException>(() => WeeklyWorkspacePeriod.Resolve(WeeklyWorkspaceFixture.Now, Stockholm, new DateOnly(1999, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => WeeklyWorkspacePeriod.Resolve(WeeklyWorkspaceFixture.Now, Stockholm, weekStartsOn: (DayOfWeek)99));
    }
}
