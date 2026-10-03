using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesOperationalReportService(VirtualCompanyDbContext db, ITodayWorkspaceLensResolver access, TimeProvider clock)
    : ISalesOperationalReportService
{
    private async Task AuthorizeAsync(Guid companyId, CancellationToken ct)
    {
        var scope = await access.ResolveAsync(companyId, TodayWorkspaceLenses.Sales, ct);
        if (!scope.AvailableLenses.Any(x => x.Lens == TodayWorkspaceLenses.Sales)) throw new UnauthorizedAccessException();
    }

    public async Task<SalesOpportunityReport> GetOpportunitiesAsync(Guid companyId, bool forecast, int days, string? currency, Guid? stageId, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(companyId, cancellationToken);
        if (forecast && !RevenueForecastWindows.SupportedDays.Contains(days)) throw new ArgumentException("Choose a 30, 60 or 90 day forecast.");
        currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
        if (currency is not null && (currency.Length != 3 || !currency.All(char.IsAsciiLetter))) throw new ArgumentException("Choose a three-letter currency.");
        var now = clock.GetUtcNow().UtcDateTime;
        var deals = await db.Deals.IgnoreQueryFilters().AsNoTracking().Include(x => x.PipelineStage)
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Status == SalesStatuses.Open).ToListAsync(cancellationToken);
        var currencies = deals.Select(x => x.Currency).Distinct().Order().ToArray();
        var included = deals.Where(x => (currency is null || x.Currency == currency) && (!stageId.HasValue || x.PipelineStageId == stageId) &&
            (!forecast || x.Amount > 0 && x.ExpectedCloseUtc.HasValue && x.ExpectedCloseUtc >= now && x.ExpectedCloseUtc <= now.AddDays(days)))
            .OrderBy(x => x.ExpectedCloseUtc).ThenBy(x => x.Id).ToArray();
        var ids = included.Select(x => x.Id).ToArray();
        var risks = (await db.DealRiskScoreSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.DealId) && x.CalculatedUtc <= now)
            .OrderByDescending(x => x.ScoreDateUtc).ThenByDescending(x => x.CalculatedUtc).ToListAsync(cancellationToken))
            .GroupBy(x => x.DealId).ToDictionary(x => x.Key, x => x.First());
        var rows = included.Select(x =>
        {
            risks.TryGetValue(x.Id, out var risk);
            var score = risk?.Score ?? 0.50m;
            return new SalesOpportunityEvidence(x.Id, x.Title, x.PipelineStageId, x.PipelineStage?.Name ?? "Pipeline", x.Currency,
                x.Amount, x.ExpectedCloseUtc, x.UpdatedUtc, RevenueForecastCalculation.StageProbability(x.PipelineStageId), score,
                risk?.CalculatedUtc, forecast ? RevenueForecastCalculation.ExpectedAmount(x.Amount, x.PipelineStageId, score) :
                    Math.Round(x.Amount * RevenueForecastCalculation.StageProbability(x.PipelineStageId), 2));
        }).ToArray();
        var totals = rows.GroupBy(x => x.Currency).OrderBy(x => x.Key)
            .Select(x => new SalesCurrencyTotal(x.Key, x.Count(), x.Sum(y => y.Amount), x.Sum(y => y.ExpectedAmount))).ToArray();
        var windows = RevenueForecastWindows.SupportedDays.Where(x => x <= days).SelectMany(day => rows
            .Where(x => x.ExpectedCloseUtc <= now.AddDays(day)).GroupBy(x => x.Currency)
            .Select(group => new SalesForecastCurrencyWindow(day, group.Key, group.Count(), group.Sum(x => x.Amount), group.Sum(x => x.ExpectedAmount)))).ToArray();
        return new(companyId, now, forecast, forecast ? days : 0, currency, stageId,
            forecast ? RevenueForecastCalculation.Version : "stage-weight-v1", rows, totals, currencies, forecast ? windows : []);
    }

    public async Task<SalesActivityReport> GetActivitiesAsync(Guid companyId, string status, Guid? dealId, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(companyId, cancellationToken);
        if (status is not ("all" or "pending" or "overdue" or "completed")) throw new ArgumentException("Choose all, pending, overdue or completed activities.");
        var now = clock.GetUtcNow().UtcDateTime;
        var activities = await db.SalesActivities.IgnoreQueryFilters().AsNoTracking().Include(x => x.Deal)
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.ActivityType == "internal_follow_up" &&
                x.Deal != null && x.Deal.CompanyId == companyId && !x.Deal.IsDeleted && (!dealId.HasValue || x.DealId == dealId))
            .OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var commitments = activities.Where(x => status == "all" || status == "overdue" && x.Status == SalesStatuses.Pending && x.OccurredUtc < now ||
            status == "pending" && x.Status == SalesStatuses.Pending || status == "completed" && x.Status == SalesStatuses.Completed)
            .Select(x => Map(x, x.Deal!.Title)).ToArray();
        var meetings = await db.SalesMeetingInvitations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && (!dealId.HasValue || x.DealId == dealId)).OrderBy(x => x.StartsUtc).ToListAsync(cancellationToken);
        // Meetings retain their own delivery lifecycle. An ended meeting is not proof of attendance or completion.
        var meetingRows = meetings.Where(x => status == "all" ||
            x.Status != SalesMeetingInvitationStatus.Cancelled && x.Status != SalesMeetingInvitationStatus.Rejected &&
            (status == "pending" && x.EndsUtc >= now || status == "overdue" && x.EndsUtc < now))
            .Select(x => new SalesMeetingCommitmentDto(x.Id, x.LeadId, x.DealId, x.Title, x.StartsUtc, x.EndsUtc,
                x.Status.ToStorageValue(), x.UpdatedUtc, x.LastErrorCode)).ToArray();
        return new(companyId, now, status, dealId, commitments, meetingRows);
    }

    public async Task<SalesCommitmentDto?> RecordCommitmentAsync(Guid companyId, Guid userId, Guid dealId, RecordSalesCommitment request, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(companyId, cancellationToken);
        if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.Summary) || request.Summary.Trim().Length > 500 || request.DueUtc == default)
            throw new ArgumentException("A command, note up to 500 characters and due time are required.");
        var deal = await db.Deals.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == dealId && !x.IsDeleted, cancellationToken);
        if (deal is null) return null;
        var due = request.DueUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(request.DueUtc, DateTimeKind.Utc) : request.DueUtc.ToUniversalTime();
        var existing = await db.SalesActivities.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CommandId, cancellationToken);
        if (existing is not null)
        {
            if (existing.CompanyId != companyId || existing.DealId != dealId || existing.ActivityType != "internal_follow_up" ||
                existing.Summary != request.Summary.Trim() || existing.OccurredUtc != due) throw new InvalidOperationException("Reload activity history; this request no longer matches its recorded commitment.");
            return Map(existing, deal.Title);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var activity = new SalesActivity(request.CommandId, companyId, "internal_follow_up", request.Summary.Trim(), due,
            dealId: dealId, contactId: deal.PrimaryContactId, customerCompanyId: deal.CustomerCompanyId, status: SalesStatuses.Pending, createdUtc: now, updatedUtc: now);
        db.SalesActivities.Add(activity);
        Audit(companyId, userId, "sales.commitment.recorded", activity.Id, "Recorded an internal Sales commitment; no customer delivery.");
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw new InvalidOperationException("Reload activity history before retrying; another request may have recorded this commitment."); }
        return Map(activity, deal.Title);
    }

    public async Task<SalesCommitmentDto?> ReviewCommitmentAsync(Guid companyId, Guid userId, Guid activityId, ReviewSalesCommitment request, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(companyId, cancellationToken);
        var activity = await db.SalesActivities.IgnoreQueryFilters().AsNoTracking().Include(x => x.Deal)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == activityId && !x.IsDeleted && x.ActivityType == "internal_follow_up" &&
                x.Deal != null && x.Deal.CompanyId == companyId && !x.Deal.IsDeleted, cancellationToken);
        if (activity is null) return null;
        if (activity.Status == SalesStatuses.Completed) return Map(activity, activity.Deal!.Title);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.SalesActivities.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Id == activityId &&
            !x.IsDeleted && x.Status == SalesStatuses.Pending && x.UpdatedUtc == request.ExpectedUpdatedUtc)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.Status, SalesStatuses.Completed).SetProperty(y => y.UpdatedUtc, now), cancellationToken);
        if (changed != 1) throw new InvalidOperationException("This commitment changed. Reload activity history before reviewing it.");
        Audit(companyId, userId, "sales.commitment.reviewed", activityId, "Reviewed an internal Sales commitment; no delivery or deal outcome is inferred.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(activity, activity.Deal!.Title) with { Status = SalesStatuses.Completed, UpdatedUtc = now };
    }

    private static SalesCommitmentDto Map(SalesActivity activity, string title) =>
        new(activity.Id, activity.DealId!.Value, title, activity.Summary, activity.Status, activity.OccurredUtc, activity.UpdatedUtc);
    private void Audit(Guid company, Guid user, string action, Guid target, string summary) =>
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), company, "user", user, action, "sales_activity", target.ToString("D"), AuditEventOutcomes.Succeeded, summary));
}
