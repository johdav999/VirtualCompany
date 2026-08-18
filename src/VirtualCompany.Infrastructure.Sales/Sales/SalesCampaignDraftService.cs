using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesCampaignDraftService(VirtualCompanyDbContext db) : ISalesCampaignDraftService
{
    public async Task<SalesCampaignDraftResult> CreateDraftAsync(CreateSalesCampaignDraftCommand c, CancellationToken ct)
    {
        if (c.CompanyId == Guid.Empty || c.OwnerUserId == Guid.Empty) throw new ArgumentException("Company and owner are required.");
        if (string.IsNullOrWhiteSpace(c.IdempotencyKey)) throw new ArgumentException("Idempotency key is required.");

        var existingId = await db.MarketingPlanCampaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == c.CompanyId && x.IdempotencyKey == c.IdempotencyKey)
            .Select(x => (Guid?)x.SalesCampaignId).SingleOrDefaultAsync(ct);
        if (existingId.HasValue)
        {
            return await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.Id == existingId)
                .Select(x => new SalesCampaignDraftResult(x.Id, x.SalesSequenceId, x.Status, x.LifecycleStatus)).SingleAsync(ct);
        }

        var sequence = new SalesSequence(Guid.NewGuid(), c.CompanyId, $"{c.Name} draft sequence", description: "Draft sequence. Add and review steps before launch.");
        var campaign = new SalesCampaign(Guid.NewGuid(), c.CompanyId, sequence.Id, c.Name, c.AudienceType, communicationLanguage: c.CommunicationLanguage);
        campaign.ConfigureInitiative(c.CampaignType, c.Purpose, c.OwnerUserId, c.OwnerAgentId, c.ObjectiveType,
            c.ObjectiveTarget, c.ObjectiveUnit, c.ObjectiveTargetUtc, c.PlanningStartsUtc, c.ScheduledLaunchUtc,
            c.EndsUtc, c.TimeZoneId, c.PlannedBudget, c.BudgetCurrency, c.ReviewDueUtc);
        campaign.Objectives.Add(new SalesCampaignObjective(Guid.NewGuid(), c.CompanyId, campaign.Id, c.ObjectiveType,
            c.ObjectiveTarget, c.ObjectiveUnit, c.ObjectiveTargetUtc, true));
        campaign.Offers.Add(new SalesCampaignOffer(Guid.NewGuid(), c.CompanyId, campaign.Id, c.OfferName,
            c.OfferSourceType, c.OfferSourceReference, c.OfferKnowledgeDocumentId, c.NoOfferRequired));
        db.SalesSequences.Add(sequence); db.SalesCampaigns.Add(campaign);
        foreach (var activity in c.Activities ?? [])
            db.SalesCampaignActivities.Add(new SalesCampaignActivity(Guid.NewGuid(), c.CompanyId, campaign.Id,
                activity.Name, activity.ActivityType, activity.Channel, "manual", activity.PlannedStartUtc,
                activity.DueUtc, activity.TimeZoneId, c.OwnerUserId, c.OwnerAgentId));
        await db.SaveChangesAsync(ct);
        return new SalesCampaignDraftResult(campaign.Id, sequence.Id, campaign.Status, campaign.LifecycleStatus);
    }

    public async Task<SalesCampaignDraftResult> PopulateDraftAsync(PopulateSalesCampaignDraftCommand c, CancellationToken ct)
    {
        if (c.CompanyId == Guid.Empty || c.CampaignId == Guid.Empty || c.OwnerUserId == Guid.Empty)
            throw new ArgumentException("Company, campaign, and owner are required.");
        if (c.Steps.Count == 0 || c.Steps.Select(x => x.StepOrder).Distinct().Count() != c.Steps.Count)
            throw new ArgumentException("Provide uniquely ordered draft sequence steps.");
        var campaign = await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == c.CompanyId && x.Id == c.CampaignId, ct) ?? throw new KeyNotFoundException("Campaign draft not found.");
        if (campaign.LifecycleStatus is not (CampaignLifecycleStatuses.Draft or CampaignLifecycleStatuses.Planning))
            throw new InvalidOperationException("Only an incomplete campaign draft can be populated.");
        var sequence = await db.SalesSequences.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == c.CompanyId && x.Id == campaign.SalesSequenceId, ct);
        var hasSteps = await db.SalesSequenceSteps.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == c.CompanyId && x.SalesSequenceId == sequence.Id, ct);
        if (!hasSteps)
        {
            db.SalesSequenceSteps.AddRange(c.Steps.OrderBy(x => x.StepOrder).Select(step =>
                new SalesSequenceStep(Guid.NewGuid(), c.CompanyId, sequence.Id, step.StepOrder,
                    step.DelayDays, step.Body, templateSubject: step.Subject, aiPersonalizationEnabled: false)));
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), c.CompanyId,
                c.OwnerAgentId == c.OwnerUserId ? AuditActorTypes.Agent : AuditActorTypes.Human, c.OwnerUserId,
                "sales.campaign.draft_populated", "sales_campaign", campaign.Id.ToString("D"), AuditEventOutcomes.Succeeded,
                "Internal sequence drafts were added without activation, enrollment, scheduling, or delivery.",
                metadata: new Dictionary<string, string?> { ["idempotencyKey"] = c.IdempotencyKey, ["stepCount"] = c.Steps.Count.ToString() }));
            await db.SaveChangesAsync(ct);
        }
        return new SalesCampaignDraftResult(campaign.Id, sequence.Id, campaign.Status, campaign.LifecycleStatus);
    }
}
