using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class CampaignPlanningService
{
    private const int MaximumProjectedPresentationRuns = 500;

    public async Task<CampaignPresentationActivityResponse?> GetPresentationActivityAsync(
        Guid companyId, Guid campaignId, Guid activityId, CancellationToken cancellationToken)
    {
        var configuration = await PresentationQuery(companyId, campaignId, activityId, tracking: false)
            .SingleOrDefaultAsync(cancellationToken);
        return configuration is null ? null : await MapPresentationAsync(configuration, cancellationToken);
    }

    public async Task<CampaignPresentationActivityResponse?> SavePresentationActivityAsync(
        Guid companyId, Guid userId, Guid campaignId, Guid activityId, SaveCampaignPresentationActivityRequest request,
        CancellationToken cancellationToken)
    {
        if (!await IsCompanyMember(companyId, userId, cancellationToken)) throw new UnauthorizedAccessException("An active company membership is required.");
        var activity = await _db.SalesCampaignActivities.IgnoreQueryFilters()
            .Include(x => x.SalesCampaign)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId && x.Id == activityId, cancellationToken);
        if (activity is null) return null;
        if (activity.ActivityType != SalesCampaignPresentationValues.ActivityType || activity.Channel != SalesCampaignPresentationValues.Channel)
            throw new InvalidOperationException("Only a presentation campaign activity can receive presentation configuration.");

        var scope = SalesCampaignPresentationValues.ParseScope(request.ExecutionScope);
        var presenterStrategy = SalesCampaignPresentationValues.ParsePresenterStrategy(request.PresenterStrategy);
        var workStrategy = SalesCampaignPresentationValues.ParseWorkStrategy(request.WorkStrategy);
        var version = await _db.SalesPresentationPresetVersions.IgnoreQueryFilters()
            .Include(x => x.Preset).Include(x => x.Asset).ThenInclude(x => x!.Slides)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.PresetVersionId, cancellationToken);
        if (version is null || version.Lifecycle != SalesPresentationPresetVersionLifecycle.Published ||
            version.Preset.Lifecycle == SalesPresentationPresetLifecycle.Archived || !version.AllowCampaignActivity)
            throw new InvalidOperationException("Choose a published, available preset version that supports campaign activities.");

        if (request.ExplicitPresenterAgentId.HasValue && !await IsValidPresenter(companyId, request.ExplicitPresenterAgentId.Value, cancellationToken))
            throw new InvalidOperationException("Choose an active, company-owned Sales presenter.");
        if (request.EventSessionId.HasValue && !await _db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == request.EventSessionId, cancellationToken))
            throw new InvalidOperationException("Choose a meeting session owned by this company.");

        var configuration = await _db.SalesCampaignPresentationActivities.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SalesCampaignActivityId == activityId, cancellationToken);
        if (configuration is null)
        {
            if (request.ExpectedVersion != 0) throw new DbUpdateConcurrencyException("The presentation activity changed. Refresh before saving.");
            configuration = new SalesCampaignPresentationActivity(Guid.NewGuid(), companyId, activityId, request.PresetVersionId,
                scope, presenterStrategy, request.ExplicitPresenterAgentId, workStrategy, request.AllowOverrides,
                request.EventSessionId, request.PreparationLeadTimeHours, userId, DateTime.UtcNow);
            _db.SalesCampaignPresentationActivities.Add(configuration);
            Audit(companyId, userId, "sales.campaign.presentation_configured", activityId,
                $"Presentation activity pinned preset version {request.PresetVersionId:D} with {request.ExecutionScope} scope.");
        }
        else
        {
            configuration.Configure(request.PresetVersionId, scope, presenterStrategy, request.ExplicitPresenterAgentId,
                workStrategy, request.AllowOverrides, request.EventSessionId, request.PreparationLeadTimeHours,
                userId, DateTime.UtcNow, request.ExpectedVersion);
            Audit(companyId, userId, "sales.campaign.presentation_updated", activityId,
                $"Presentation activity configuration version {configuration.Version} was saved.");
        }
        await _db.SaveChangesAsync(cancellationToken);
        return await GetPresentationActivityAsync(companyId, campaignId, activityId, cancellationToken);
    }

    public async Task<bool> RemovePresentationActivityAsync(Guid companyId, Guid userId, Guid campaignId,
        Guid activityId, int expectedVersion, CancellationToken cancellationToken)
    {
        if (!await IsCompanyMember(companyId, userId, cancellationToken)) throw new UnauthorizedAccessException("An active company membership is required.");
        var configuration = await PresentationQuery(companyId, campaignId, activityId, tracking: true)
            .Include(x => x.Runs).SingleOrDefaultAsync(cancellationToken);
        if (configuration is null) return false;
        if (configuration.Version != expectedVersion) throw new DbUpdateConcurrencyException("The presentation activity changed. Refresh before removing it.");
        if (configuration.Runs.Count != 0) throw new InvalidOperationException("A presentation activity with historical runs cannot be removed.");
        _db.SalesCampaignPresentationActivities.Remove(configuration);
        Audit(companyId, userId, "sales.campaign.presentation_removed", activityId, "Presentation activity configuration was removed before execution.");
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CampaignPresentationActivityResponse?> RetryPresentationActivityAsync(Guid companyId, Guid userId,
        Guid campaignId, Guid activityId, CancellationToken cancellationToken)
    {
        if (!await IsCompanyMember(companyId, userId, cancellationToken)) throw new UnauthorizedAccessException("An active company membership is required.");
        var configuration = await PresentationQuery(companyId, campaignId, activityId, tracking: true)
            .Include(x => x.Runs).SingleOrDefaultAsync(cancellationToken);
        if (configuration is null) return null;
        var failed = configuration.Runs.Where(x => x.Status == "failed").ToList();
        foreach (var run in failed) run.QueueRetry(DateTime.UtcNow);
        CampaignPresentationTelemetry.RetriesQueued.Add(failed.Count);
        var activity = await _db.SalesCampaignActivities.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId && x.Id == activityId, cancellationToken);
        activity.Fail("Failed presentation subjects were queued for a bounded retry.", retryable: true);
        Audit(companyId, userId, "sales.campaign.presentation_retry", activityId, $"Queued {failed.Count} failed presentation subjects for retry without duplicating successful runs.");
        await _db.SaveChangesAsync(cancellationToken);
        return await GetPresentationActivityAsync(companyId, campaignId, activityId, cancellationToken);
    }

    private IQueryable<SalesCampaignPresentationActivity> PresentationQuery(Guid companyId, Guid campaignId, Guid activityId, bool tracking)
    {
        var query = _db.SalesCampaignPresentationActivities.IgnoreQueryFilters()
            .Include(x => x.Activity).ThenInclude(x => x.SalesCampaign)
            .Include(x => x.PresetVersion).ThenInclude(x => x.Preset)
            .Include(x => x.PresetVersion).ThenInclude(x => x.Asset).ThenInclude(x => x!.Slides)
            .Where(x => x.CompanyId == companyId && x.SalesCampaignActivityId == activityId && x.Activity.SalesCampaignId == campaignId);
        return tracking ? query : query.AsNoTracking();
    }

    private async Task<CampaignPresentationActivityResponse> MapPresentationAsync(SalesCampaignPresentationActivity configuration, CancellationToken cancellationToken)
    {
        var contacts = await _db.SalesCampaignContacts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == configuration.CompanyId && x.SalesCampaignId == configuration.Activity.SalesCampaignId)
            .Join(_db.Contacts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == configuration.CompanyId && !x.IsDeleted),
                link => link.ContactId, contact => contact.Id, (_, contact) => new { contact.Id, contact.CustomerCompanyId })
            .ToListAsync(cancellationToken);
        var accounts = contacts.Where(x => x.CustomerCompanyId.HasValue).Select(x => x.CustomerCompanyId!.Value).Distinct().Count();
        var projected = configuration.ExecutionScope switch
        {
            SalesCampaignPresentationExecutionScope.PerContact => contacts.Count,
            SalesCampaignPresentationExecutionScope.PerAccount => accounts,
            SalesCampaignPresentationExecutionScope.CampaignEvent => configuration.EventSessionId.HasValue ? 1 : 0,
            _ => 0
        };
        var blockers = new List<CampaignPresentationReadinessBlocker>();
        var preset = configuration.PresetVersion;
        if (preset.Lifecycle != SalesPresentationPresetVersionLifecycle.Published || preset.Preset.Lifecycle == SalesPresentationPresetLifecycle.Archived || !preset.AllowCampaignActivity)
            Add(CampaignPresentationBlockerCodes.PresetUnavailable, "The pinned preset version is unavailable for campaign use.", "The exact version is unpublished, archived, or does not allow campaign activities.", "Choose an available published version.");
        if (preset.Asset?.Status != SalesPresentationPresetAssetStatus.Processed || preset.Asset.Slides.Count == 0)
            Add(CampaignPresentationBlockerCodes.AssetUnavailable, "The preset slides are not ready.", "Reusable processing has not produced any available slides.", "Process or replace the preset asset before launch.");
        if (contacts.Count == 0 && configuration.ExecutionScope != SalesCampaignPresentationExecutionScope.CampaignEvent)
            Add(CampaignPresentationBlockerCodes.AudienceUnavailable, "No eligible campaign audience is available.", "The campaign has no current company-scoped contacts.", "Capture or add an eligible campaign audience.");
        if (configuration.ExecutionScope == SalesCampaignPresentationExecutionScope.PerAccount && contacts.Any(x => !x.CustomerCompanyId.HasValue))
            Add(CampaignPresentationBlockerCodes.AccountMissing, "Some contacts are missing an account.", $"{contacts.Count(x => !x.CustomerCompanyId.HasValue)} contacts cannot be grouped safely.", "Link every eligible contact to a company account or use per-contact scope.");
        var presenterId = ResolvePresenter(configuration);
        if (!presenterId.HasValue || !await IsValidPresenter(configuration.CompanyId, presenterId.Value, cancellationToken))
            Add(CampaignPresentationBlockerCodes.PresenterUnavailable, "A valid Sales presenter cannot be resolved.", "The selected strategy did not resolve an active company-owned Sales agent.", "Choose an explicit presenter or assign the appropriate campaign/activity owner.");
        if (configuration.ExecutionScope == SalesCampaignPresentationExecutionScope.CampaignEvent && !configuration.EventSessionId.HasValue)
            Add(CampaignPresentationBlockerCodes.EventMissing, "Campaign-event scope needs a meeting session.", "No company-owned event or session is bound.", "Choose the event session before launch.");
        if (projected > MaximumProjectedPresentationRuns)
            Add(CampaignPresentationBlockerCodes.CardinalityExceeded, "The projected run count exceeds the safe batch limit.", $"{projected} runs are projected; the limit is {MaximumProjectedPresentationRuns}.", "Narrow the audience or split this activity.");

        var runs = await _db.SalesCampaignPresentationRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == configuration.CompanyId && x.ConfigurationId == configuration.Id)
            .Join(_db.SalesPresentationRuns.IgnoreQueryFilters().AsNoTracking(), x => new { x.CompanyId, Id = x.PresentationRunId }, x => new { x.CompanyId, x.Id },
                (link, run) => new CampaignPresentationRunResponse(link.Id, run.Id, link.SubjectType, link.SubjectId, link.Status,
                    link.FailureCode, link.FailureSummary, run.PreparationStatus.ToStorageValue(), run.PresenterAgentId))
            .OrderBy(x => x.SubjectType).ThenBy(x => x.SubjectId).ToListAsync(cancellationToken);
        var projection = new CampaignPresentationRunProjection(contacts.Count, accounts, projected,
            configuration.ExecutionScope == SalesCampaignPresentationExecutionScope.PerContact ? "contact" : configuration.ExecutionScope == SalesCampaignPresentationExecutionScope.PerAccount ? "account" : "campaign_event",
            projected <= MaximumProjectedPresentationRuns);
        return new CampaignPresentationActivityResponse(configuration.Id, configuration.Activity.SalesCampaignId, configuration.SalesCampaignActivityId,
            preset.PresetId, preset.Preset.Name, preset.Id, preset.VersionNumber, configuration.ExecutionScope.ToValue(), configuration.PresenterStrategy.ToValue(),
            configuration.ExplicitPresenterAgentId, configuration.WorkStrategy.ToValue(), configuration.AllowOverrides, configuration.EventSessionId,
            configuration.PreparationLeadTimeHours, configuration.Version, blockers.Count == 0, projection, blockers, runs);
        void Add(string code, string explanation, string evidence, string correction) => blockers.Add(new(code, explanation, evidence, correction, true, true));
    }

    private static Guid? ResolvePresenter(SalesCampaignPresentationActivity configuration) => configuration.PresenterStrategy switch
    {
        SalesCampaignPresentationPresenterStrategy.Explicit => configuration.ExplicitPresenterAgentId,
        SalesCampaignPresentationPresenterStrategy.ActivityOwner => configuration.Activity.OwnerAgentId,
        SalesCampaignPresentationPresenterStrategy.CampaignOwner => configuration.Activity.SalesCampaign.OwnerAgentId,
        _ => null
    };
    private Task<bool> IsValidPresenter(Guid companyId, Guid agentId, CancellationToken cancellationToken) =>
        _db.Agents.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == agentId && x.CanReceiveAssignments && x.Department == "Sales", cancellationToken);
}
