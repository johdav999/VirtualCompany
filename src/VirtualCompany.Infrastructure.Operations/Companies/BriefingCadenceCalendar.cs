using VirtualCompany.Application.Briefings;
namespace VirtualCompany.Infrastructure.Companies;

public sealed record BriefingCadenceSlot(string Key, DateTime Utc, string[] Kinds);
public static class BriefingCadenceCalendar
{
    public static readonly string[] Kinds = ["morning", "end_of_day", "shift_handover", "weekly", "monthly", "quarterly", "annual"];
    public static TimeZoneInfo Zone(string name) => TimeZoneInfo.FindSystemTimeZoneById(name);
    // Spring gap advances to the first valid minute. Autumn fold chooses the earlier UTC occurrence.
    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local)) return DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
    public static bool Allowed(DateTime local, BriefingCadenceSettings s)
    {
        var t = TimeOnly.FromDateTime(local);
        var working = s.WorkStart < s.WorkEnd ? t >= s.WorkStart && t <= s.WorkEnd : t >= s.WorkStart || t <= s.WorkEnd;
        var quiet = s.QuietStart == s.QuietEnd ? false : s.QuietStart < s.QuietEnd ? t >= s.QuietStart && t < s.QuietEnd : t >= s.QuietStart || t < s.QuietEnd;
        return s.Workdays.Contains((int)local.DayOfWeek) && working && !quiet;
    }
    public static IEnumerable<BriefingCadenceSlot> Slots(BriefingCadenceSettings s, DateTime fromUtc, DateTime untilUtc)
    {
        var zone = Zone(s.Timezone); var from = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, zone).Date.AddDays(-8);
        var end = TimeZoneInfo.ConvertTimeFromUtc(untilUtc, zone).Date;
        var slots = new List<(DateTime Local, DateTime Utc, string Kind)>();
        for (var date = from; date <= end; date = date.AddDays(1)) foreach (var row in s.Schedules.Where(x => x.Enabled))
        {
            var day = Math.Min(row.Day, DateTime.DaysInMonth(date.Year, date.Month));
            var matches = row.Kind switch { "weekly" => (int)date.DayOfWeek == row.Weekday,
                "monthly" => date.Day == day, "quarterly" => date.Day == day && (date.Month - row.Month + 12) % 3 == 0,
                "annual" => date.Day == day && date.Month == row.Month, _ => s.Workdays.Contains((int)date.DayOfWeek) };
            if (!matches) continue;
            var local = date + row.LocalTime.ToTimeSpan(); var limit = local.AddDays(8);
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            // Quiet / non-working slots defer to the first permitted minute, including overnight shifts.
            while ((!Allowed(local, s) || zone.IsInvalidTime(local)) && local < limit) local = local.AddMinutes(1);
            if (local >= limit) continue;
            var utc = ToUtc(local, zone);
            if (utc >= fromUtc && utc <= untilUtc) slots.Add((local, utc, row.Kind));
        }
        foreach (var group in slots.GroupBy(x => s.GroupUpdates ? x.Local.ToString("yyyyMMddHHmm") : x.Local.ToString("yyyyMMddHHmm") + ":" + x.Kind))
            yield return new(group.Key, group.First().Utc, group.Select(x => x.Kind).Distinct().Order().ToArray());
    }
    public static BriefingCadenceSlot? Next(BriefingCadenceSettings s, DateTime nowUtc) => Slots(s, nowUtc.AddTicks(1), nowUtc.AddDays(400)).OrderBy(x => x.Utc).FirstOrDefault();
}
