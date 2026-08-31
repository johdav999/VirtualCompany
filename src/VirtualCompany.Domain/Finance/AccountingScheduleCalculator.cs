using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Domain.Finance;

public sealed record AccountingScheduleCalculatedLine(Guid FinanceAccountId, decimal DebitAmount,
    decimal CreditAmount, string Description, IReadOnlyList<Guid> DimensionMemberIds);

public sealed record AccountingScheduleCalculation(DateOnly OccurrenceDate, int OccurrenceIndex,
    int PlannedOccurrences, decimal ProrationFactor, decimal DebitTotal, decimal CreditTotal,
    IReadOnlyList<AccountingScheduleCalculatedLine> Lines);

public static class AccountingScheduleCalculator
{
    public static AccountingScheduleCalculation Calculate(AccountingSchedule schedule,
        AccountingScheduleVersion version, DateOnly occurrenceDate, int precision = 2)
    {
        ArgumentNullException.ThrowIfNull(schedule); ArgumentNullException.ThrowIfNull(version);
        if (precision is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(precision));
        var dates = PlannedDates(schedule);
        var index = dates.Count == 0 ? 0 : dates.ToList().IndexOf(occurrenceDate);
        if (dates.Count > 0 && index < 0) throw new InvalidOperationException("The date is not part of the retained schedule cadence.");
        var divisor = schedule.AmountBasis == AccountingScheduleAmountBases.TotalSchedule
            ? dates.Count : 1;
        if (divisor == 0) throw new InvalidOperationException("A total-schedule allocation has no planned occurrences.");
        var factor = ProrationFactor(schedule, occurrenceDate);
        var lines = version.Lines.OrderBy(x => x.Sequence).Select(line =>
        {
            var debit = Allocate(line.DebitAmount, divisor, index, precision);
            var credit = Allocate(line.CreditAmount, divisor, index, precision);
            if (schedule.AmountBasis == AccountingScheduleAmountBases.PerOccurrence && factor != 1m)
            {
                debit = decimal.Round(debit * factor, precision, MidpointRounding.AwayFromZero);
                credit = decimal.Round(credit * factor, precision, MidpointRounding.AwayFromZero);
            }
            return new AccountingScheduleCalculatedLine(line.FinanceAccountId, debit, credit,
                line.Description, line.DimensionAssignments.Select(x => x.DimensionMemberId).OrderBy(x => x).ToArray());
        }).ToArray();
        return new(occurrenceDate, Math.Max(0, index), dates.Count, factor,
            lines.Sum(x => x.DebitAmount), lines.Sum(x => x.CreditAmount), lines);
    }

    public static IReadOnlyList<DateOnly> PlannedDates(AccountingSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Cadence == AccountingScheduleCadences.Once) return [schedule.StartDate];
        if (!schedule.EndDate.HasValue) return [];
        var dates = new List<DateOnly>();
        var current = schedule.ResolveOccurrence(schedule.StartDate);
        for (var i = 0; i < 600 && current <= schedule.EndDate.Value; i++)
        {
            dates.Add(current);
            current = schedule.NextAfter(current);
        }
        if (dates.Count == 600 && current <= schedule.EndDate.Value)
            throw new InvalidOperationException("The schedule exceeds the supported 600-occurrence planning bound.");
        return dates;
    }

    private static decimal Allocate(decimal total, int divisor, int index, int precision)
    {
        if (total == 0m) return 0m;
        if (divisor <= 1) return decimal.Round(total, precision, MidpointRounding.AwayFromZero);
        var regular = decimal.Round(total / divisor, precision, MidpointRounding.AwayFromZero);
        return index == divisor - 1 ? total - regular * (divisor - 1) : regular;
    }

    private static decimal ProrationFactor(AccountingSchedule schedule, DateOnly occurrenceDate)
    {
        if (schedule.ProrationRule != AccountingScheduleProrationRules.Daily || occurrenceDate != schedule.ResolveOccurrence(schedule.StartDate) ||
            schedule.Cadence == AccountingScheduleCadences.Once) return 1m;
        var months = AccountingScheduleCadences.Months(schedule.Cadence);
        var previous = occurrenceDate.AddMonths(-months);
        var periodDays = occurrenceDate.DayNumber - previous.DayNumber;
        var activeDays = occurrenceDate.DayNumber - Math.Max(previous.DayNumber, schedule.StartDate.DayNumber);
        if (periodDays <= 0 || activeDays <= 0) return 1m;
        return decimal.Round(Math.Clamp((decimal)activeDays / periodDays, 0m, 1m), 6, MidpointRounding.AwayFromZero);
    }
}
