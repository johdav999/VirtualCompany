namespace VirtualCompany.Application.Support;

public static class SupportCapacityCalculation
{
    public const string Version = "support-quality.v1";
    public static SupportCapacityResult Calculate(SupportQualityReport report, SupportCapacityAssumptions a, DateTime now)
    {
        if (!report.CompleteSourceCoverage) throw new ArgumentException("Capacity needs a complete bounded source report. Narrow the company source coverage before planning.");
        if (a.TargetYear is <2000 or >2100 || a.TargetMonth is <1 or >12) throw new ArgumentException("Choose a valid target month.");
        var calendar=report.Calendar; var zone=TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZoneId);
        var localNow=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now,DateTimeKind.Utc),zone);
        var target=new DateTime(a.TargetYear,a.TargetMonth,1); var current=new DateTime(localNow.Year,localNow.Month,1);
        if (target<current || target>current.AddMonths(12)) throw new ArgumentException("Target month must be current or within the next twelve months.");
        if (a.ExpectedArrivals is <0 or >100000 || a.BacklogToClear is <0 or >100000 ||
            a.HandlingMinutes is <=0 or >480 || a.AvailablePeople is <0 or >1000 ||
            a.HoursPerBusinessDay is <=0 or >24 || a.UtilizationPercent is <=0 or >100 ||
            a.ResponseTargetMinutes is <=0 or >10080) throw new ArgumentException("Check bounded arrival, handling, capacity and service target assumptions.");
        foreach(var n in new[]{a.HandlingMinutes,a.AvailablePeople,a.HoursPerBusinessDay,a.UtilizationPercent})
            if(decimal.Round(n,2)!=n) throw new ArgumentException("Capacity decimal assumptions support at most two decimal places.");
        if(string.IsNullOrWhiteSpace(a.Explanation) || a.Explanation.Length>2000) throw new ArgumentException("Explain the capacity assumptions in at most 2,000 characters.");
        if(calendar.WorkdayEnd<=calendar.WorkdayStart || calendar.WorkingDays.Count==0) throw new ArgumentException("Configure valid Support business hours before planning.");
        var hours=(decimal)(calendar.WorkdayEnd-calendar.WorkdayStart).TotalHours;
        if(a.HoursPerBusinessDay>hours) throw new ArgumentException("Assumed hours cannot exceed the configured Support workday.");
        var days=Enumerable.Range(0,DateTime.DaysInMonth(a.TargetYear,a.TargetMonth)).Select(d=>DateOnly.FromDateTime(target.AddDays(d)))
            .Count(d=>calendar.WorkingDays.Contains(d.DayOfWeek) && !calendar.Holidays.Contains(d));
        var required=(a.ExpectedArrivals+a.BacklogToClear)*a.HandlingMinutes/60m;
        var perPerson=days*a.HoursPerBusinessDay*a.UtilizationPercent/100m;
        var available=perPerson*a.AvailablePeople;
        var capacity=decimal.Floor(available*60/a.HandlingMinutes);
        if(capacity>int.MaxValue)throw new ArgumentException("These assumptions exceed the bounded case-capacity calculation.");
        return new(report.Metrics.Arrivals,a.ExpectedArrivals,a.BacklogToClear,days,decimal.Round(hours,2),
            Round(required),Round(available),(int)capacity,Round(Math.Max(0,required-available)),
            perPerson==0 ? null : decimal.Ceiling(required/perPerson*100)/100,
            $"Aggregate handling capacity only; no queue distribution or response-time guarantee. {a.ResponseTargetMinutes} minute response target is an explicit planning assumption, not a customer promise. Waiting and paused owners do not pause native SLA targets. Handling time is assumed, not inferred employee performance. Calendar is retained from this report; historical calendar changes are not reconstructed.");
    }
    private static decimal Round(decimal n)=>decimal.Round(n,2,MidpointRounding.AwayFromZero);
}
