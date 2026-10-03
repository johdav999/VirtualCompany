using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Support;

// Reads recorded targets; waiting and owner availability never suspend the SLA clock.
internal sealed record SupportSlaState(bool AtRisk, bool Breached, DateTime? NextDeadlineUtc, bool MissingTarget)
{
    public static SupportSlaState Evaluate(SupportCase c, DateTime nowUtc, int riskMinutes)
    {
        if (c.Status is SupportCaseStatuses.Resolved or SupportCaseStatuses.Closed)
            return new(false, false, null, false);
        var targets = new List<DateTime>();
        if (c.FirstResponseSentUtc is null && c.FirstResponseDueUtc is DateTime first) targets.Add(first);
        if (c.ResolutionDueUtc is DateTime resolution) targets.Add(resolution);
        var due = targets.Count == 0 ? (DateTime?)null : targets.Min();
        var breached = due < nowUtc;
        return new(!breached && due <= nowUtc.AddMinutes(riskMinutes), breached, due,
            c.ResolutionDueUtc is null || (c.FirstResponseSentUtc is null && c.FirstResponseDueUtc is null));
    }
}
