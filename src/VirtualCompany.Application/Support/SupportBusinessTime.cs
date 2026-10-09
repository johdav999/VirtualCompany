namespace VirtualCompany.Application.Support;

public static class SupportBusinessTime
{
    // Replay the selected native calendar in local wall-clock minutes, matching native SLA duration semantics.
    public static decimal Minutes(DateTime startUtc, DateTime endUtc, SupportBusinessCalendarDto calendar)
    {
        if(endUtc<startUtc) throw new ArgumentException("A response cannot precede creation.");
        if(endUtc-startUtc>TimeSpan.FromDays(3660)) throw new ArgumentException("Business-time replay is bounded to ten years.");
        if(calendar.WorkdayEnd<=calendar.WorkdayStart || calendar.WorkingDays.Count==0) throw new ArgumentException("Support business hours are invalid.");
        var zone=TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZoneId);
        var start=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc,DateTimeKind.Utc),zone);
        var end=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(endUtc,DateTimeKind.Utc),zone);
        decimal total=0;
        for(var day=DateOnly.FromDateTime(start);day<=DateOnly.FromDateTime(end);day=day.AddDays(1))
        {
            if(!calendar.WorkingDays.Contains(day.DayOfWeek) || calendar.Holidays.Contains(day)) continue;
            var from=day.ToDateTime(calendar.WorkdayStart); var to=day.ToDateTime(calendar.WorkdayEnd);
            if(start>from) from=start; if(end<to) to=end;
            if(to>from) total+=(decimal)(to-from).TotalMinutes;
        }
        return decimal.Round(total,2,MidpointRounding.AwayFromZero);
    }
}
