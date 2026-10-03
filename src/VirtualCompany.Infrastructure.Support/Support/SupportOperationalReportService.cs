using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportOperationalReportService(VirtualCompanyDbContext db, ISupportSlaPolicyService sla,
    TimeProvider? clock = null) : ISupportOperationalReportService
{
    public async Task<SupportOperationalReport> GetAsync(Guid companyId, SupportOperationalReportQuery query, CancellationToken cancellationToken)
    {
        SupportValidationException.ThrowIfEmpty(companyId, nameof(companyId));
        if (query.View is not ("all" or "sla" or "backlog" or "unresolved" or "aging"))
            throw new SupportValidationException(new Dictionary<string, string[]> { ["view"] = ["Choose SLA risk, backlog, unresolved or aging."] });
        if (query.AgeBucket is not (null or "" or "under-1d" or "1-7d" or "7-30d" or "30d-plus"))
            throw new SupportValidationException(new Dictionary<string, string[]> { ["ageBucket"] = ["Choose a supported age range."] });
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var calendar = await sla.GetCalendarAsync(companyId, cancellationToken);
        var policies = await sla.ListAsync(companyId, cancellationToken);
        var source = db.SupportCases.AsNoTracking().Where(x => x.CompanyId == companyId && x.CreatedUtc <= now);
        if (query.View != "all") source = source.Where(x => x.Status != SupportCaseStatuses.Resolved && x.Status != SupportCaseStatuses.Closed);
        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(x => x.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Priority)) source = source.Where(x => x.Priority == query.Priority);
        if (!string.IsNullOrWhiteSpace(query.Category)) source = source.Where(x => x.Category == query.Category);
        if (!string.IsNullOrWhiteSpace(query.Search)) source = source.Where(x => x.Subject.Contains(query.Search) || x.CaseNumber.Contains(query.Search));
        if (query.AssignedUserId is Guid user) source = source.Where(x => x.AssignedUserId == user);
        if (query.AssignedAgentId is Guid agent) source = source.Where(x => x.AssignedAgentId == agent);
        if (query.Unassigned) source = source.Where(x => x.AssignedAgentId == null && x.AssignedUserId == null);
        if (query.ContactId is Guid filterContact) source = source.Where(x => x.ContactId == filterContact);
        if (query.CustomerCompanyId is Guid filterCustomer) source = source.Where(x => x.CustomerCompanyId == filterCustomer);
        if (query.FailedReply) source = source.Where(x => db.SupportReplyDrafts.Any(d => d.CompanyId == companyId && d.SupportCaseId == x.Id &&
            (d.DeliveryStatus == SupportReplyDeliveryStatuses.Failed || d.DeliveryStatus == SupportReplyDeliveryStatuses.ReconciliationRequired)));
        var cases = await source.ToListAsync(cancellationToken);
        var contacts = await db.Contacts.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, cancellationToken);
        var customers = await db.CustomerCompanies.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, cancellationToken);
        var agents = await db.Agents.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, cancellationToken);
        var people = await db.CompanyMemberships.AsNoTracking().Where(x => x.CompanyId == companyId && x.UserId != null)
            .Include(x => x.User).ToListAsync(cancellationToken);
        var rows = new List<SupportOperationalCase>();
        foreach (var c in cases)
        {
            var policy = policies.FirstOrDefault(x => x.IsActive && x.Category == c.Category && x.Priority == c.Priority && x.CustomerTier == null);
            var defaultResolution = SupportSlaPolicyService.DefaultDurations(c.Priority).Resolution;
            var state = SupportSlaState.Evaluate(c, now, policy?.RiskThresholdMinutes ?? Math.Min(240, Math.Max(15, defaultResolution / 4)));
            var age = (decimal)(now - c.CreatedUtc).TotalHours;
            var bucket = age < 24 ? "under-1d" : age < 168 ? "1-7d" : age < 720 ? "7-30d" : "30d-plus";
            if (query.View == "sla" && !state.AtRisk && !state.Breached) continue;
            if (!string.IsNullOrWhiteSpace(query.AgeBucket) && query.AgeBucket != bucket) continue;
            var contact = c.ContactId is Guid contactId ? contacts.GetValueOrDefault(contactId) : null;
            var customer = c.CustomerCompanyId is Guid customerId ? customers.GetValueOrDefault(customerId) : null;
            var item = SupportCaseService.MapListItem(c, contact, customer) with { IsSlaRisk = state.AtRisk, IsSlaBreached = state.Breached };
            var owner = c.AssignedAgentId is Guid agentId ? agents.GetValueOrDefault(agentId)?.DisplayName ?? "Unavailable owner"
                : c.AssignedUserId is Guid userId ? people.FirstOrDefault(x => x.UserId == userId)?.User?.DisplayName ?? "Unavailable owner" : "Unassigned";
            rows.Add(new(item, owner, decimal.Round(age, 2), bucket, state.NextDeadlineUtc,
                c.Status is SupportCaseStatuses.WaitingForCustomer or SupportCaseStatuses.WaitingInternal, state.MissingTarget));
        }
        rows = rows.OrderByDescending(x => x.Case.IsSlaBreached).ThenByDescending(x => x.Case.IsSlaRisk)
            .ThenBy(x => x.NextDeadlineUtc ?? DateTime.MaxValue).ThenBy(x => x.Case.Id).ToList();
        var definition = query.View switch {
            "sla" => "Unresolved cases whose earliest outstanding recorded response or resolution target is breached or within the current policy risk threshold.",
            "all" => "All current cases matching these filters, including resolved and closed history.",
            "aging" => "Unresolved cases grouped by elapsed time since original creation: under 1 day, 1–7 days, 7–30 days and 30 days or more.",
            _ => "All unresolved cases, including waiting, escalated and reopened cases; resolved and closed cases are excluded. Backlog and unresolved use the same denominator." };
        return new(companyId, query.View, now, calendar, definition,
            "Recorded business-hour deadlines include the configured working days, hours, holidays and timezone. Waiting for customer, waiting internally and paused owners do not pause targets. Reopened cases retain original targets and elapsed age. Historical state and past calendar changes are not reconstructed. A draft or delivery request does not resolve a case.",
            rows, rows.Count(x => x.Case.IsSlaRisk), rows.Count(x => x.Case.IsSlaBreached), rows.Count(x => x.Waiting),
            rows.Count(x => x.Case.Status == SupportCaseStatuses.Escalated), rows.Count(x => x.Case.Status == SupportCaseStatuses.AwaitingApproval),
            rows.Count(x => x.MissingTarget), new[] { "under-1d", "1-7d", "7-30d", "30d-plus" }.Select(x => new SupportMetricBucket(x,
                x switch { "under-1d" => "Under 1 day", "1-7d" => "1–7 days", "7-30d" => "7–30 days", _ => "30 days or more" }, rows.Count(r => r.AgeBucket == x))).ToList());
    }
}
