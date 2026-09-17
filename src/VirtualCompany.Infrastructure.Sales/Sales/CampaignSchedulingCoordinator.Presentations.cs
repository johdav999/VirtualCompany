using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class CampaignSchedulingCoordinator
{
    private async Task ExecutePresentationActivityAsync(SalesCampaignActivity activity, DateTime utcNow, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var configuration = await _db.SalesCampaignPresentationActivities.IgnoreQueryFilters()
            .Include(x => x.PresetVersion).ThenInclude(x => x.Preset)
            .Include(x => x.PresetVersion).ThenInclude(x => x.Asset).ThenInclude(x => x!.Slides)
            .SingleOrDefaultAsync(x => x.CompanyId == activity.CompanyId && x.SalesCampaignActivityId == activity.Id, cancellationToken)
            ?? throw new InvalidOperationException("Configure the presentation activity before it becomes due.");
        var version = configuration.PresetVersion;
        if (version.Lifecycle != SalesPresentationPresetVersionLifecycle.Published || version.Preset.Lifecycle == SalesPresentationPresetLifecycle.Archived ||
            !version.AllowCampaignActivity || version.Asset?.Status != SalesPresentationPresetAssetStatus.Processed || version.Asset.Slides.Count == 0)
            throw new InvalidOperationException("The pinned presentation preset version is no longer available or ready.");

        var presenterId = configuration.PresenterStrategy switch
        {
            SalesCampaignPresentationPresenterStrategy.Explicit => configuration.ExplicitPresenterAgentId,
            SalesCampaignPresentationPresenterStrategy.ActivityOwner => activity.OwnerAgentId,
            SalesCampaignPresentationPresenterStrategy.CampaignOwner => activity.SalesCampaign.OwnerAgentId,
            _ => null
        };
        if (!presenterId.HasValue || !await _db.Agents.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == activity.CompanyId && x.Id == presenterId && x.CanReceiveAssignments && x.Department == "Sales", cancellationToken))
            throw new InvalidOperationException("The presentation activity cannot resolve an active Sales presenter.");
        var actorUserId = activity.OwnerUserId ?? activity.SalesCampaign.OwnerUserId
            ?? throw new InvalidOperationException("Assign a company user to own presentation preparation.");

        var subjects = await ResolveSubjectsAsync(activity, configuration, cancellationToken);
        CampaignPresentationTelemetry.SubjectsResolved.Add(subjects.Count);
        if (subjects.Count == 0) throw new InvalidOperationException("The presentation activity has no eligible execution subjects.");
        if (subjects.Count > 500) throw new InvalidOperationException("The presentation activity exceeds the safe limit of 500 projected runs.");
        activity.TryClaim($"campaign-presentation:{activity.Id:N}", utcNow);
        await _db.SaveChangesAsync(cancellationToken);

        var created = 0; var duplicates = 0; var failures = 0;
        foreach (var subject in subjects)
        {
            var key = StableKey(activity.CompanyId, activity.SalesCampaignId, activity.Id, version.Id, configuration.ExecutionScope.ToValue(), subject.Id);
            var existing = await _db.SalesCampaignPresentationRuns.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == activity.CompanyId && x.IdempotencyKey == key, cancellationToken);
            if (existing is not null && existing.Status != "retrying")
            {
                duplicates++;
                continue;
            }
            SalesCampaignPresentationRun? link = existing;
            try
            {
                SalesPresentationRun run;
                if (existing is null)
                {
                    run = new SalesPresentationRun(Guid.NewGuid(), activity.CompanyId, version.Id,
                        $"campaign:{activity.SalesCampaignId:N}:{activity.Id:N}:{subject.Type}:{subject.Id:N}",
                        subject.MeetingSessionId, presenterId.Value, version.Goal, subject.Audience, version.DurationMinutes,
                        version.DemoScenario, version.Language, version.ControlMode, actorUserId, utcNow);
                    run.BeginPreparation(actorUserId, utcNow);
                    link = new SalesCampaignPresentationRun(Guid.NewGuid(), activity.CompanyId, configuration.Id, run.Id, subject.Type, subject.Id, key, utcNow);
                    _db.SalesPresentationRuns.Add(run); _db.SalesCampaignPresentationRuns.Add(link);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    run = await _db.SalesPresentationRuns.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == activity.CompanyId && x.Id == existing.PresentationRunId, cancellationToken);
                }

                if (configuration.WorkStrategy == SalesCampaignPresentationWorkStrategy.PreparationTask)
                {
                    await _tasks.CreateTaskAsync(activity.CompanyId,
                        new CreateTaskCommand("campaign_presentation", $"Prepare {version.Preset.Name} for {subject.Label}",
                            $"Review the pinned preset version and prepare the governed presentation run for {subject.Audience}.",
                            "normal", activity.DueUtc, presenterId,
                            new Dictionary<string, JsonNode?> { ["campaignId"] = activity.SalesCampaignId.ToString("D"), ["campaignActivityId"] = activity.Id.ToString("D"), ["presentationRunId"] = run.Id.ToString("D"), ["subjectType"] = subject.Type, ["subjectId"] = subject.Id.ToString("D") },
                            CorrelationId: key), cancellationToken);
                    CampaignPresentationTelemetry.TasksCreated.Add(1);
                }
                else if (configuration.WorkStrategy == SalesCampaignPresentationWorkStrategy.Handoff)
                {
                    var sender = activity.SalesCampaign.OwnerAgentId ?? presenterId.Value;
                    await _handoffs.CreateAsync(activity.CompanyId, sender,
                        new CreateAgentHandoffCommand("campaign_presentation", presenterId.Value, $"Prepare {version.Preset.Name} for {subject.Label}",
                            "Review the pinned presentation version and complete preparation; do not start a live presentation.", activity.DueUtc,
                            [$"presentation-run:{run.Id:D}", $"campaign-activity:{activity.Id:D}"]), cancellationToken);
                    CampaignPresentationTelemetry.HandoffsCreated.Add(1);
                }
                link!.MarkPrepared(DateTime.UtcNow);
                _db.AuditEvents.Add(new(Guid.NewGuid(), activity.CompanyId, "system", "campaign-scheduler",
                    "sales.campaign.presentation_run_created", "sales_presentation_run", run.Id.ToString("D"), "succeeded",
                    $"Created a governed {subject.Type} presentation run and {configuration.WorkStrategy.ToValue()} work without starting a live presentation.", DateTime.UtcNow));
                await _db.SaveChangesAsync(cancellationToken); created++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                if (link is not null) { link.Fail("campaign.presentation.subject_failed", "Preparation work could not be created for this subject.", DateTime.UtcNow); await _db.SaveChangesAsync(cancellationToken); }
                _logger.LogWarning(ex, "Campaign presentation subject failed. CompanyId={CompanyId} ActivityId={ActivityId} SubjectType={SubjectType} SubjectId={SubjectId}", activity.CompanyId, activity.Id, subject.Type, subject.Id);
            }
        }
        CampaignPresentationTelemetry.RunsCreated.Add(created);
        CampaignPresentationTelemetry.DuplicateCreationsPrevented.Add(duplicates);
        CampaignPresentationTelemetry.Failures.Add(failures);
        CampaignPresentationTelemetry.SchedulingLatency.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        if (failures > 0) activity.Fail($"Created {created} presentation runs; {failures} subjects need attention; {duplicates} existing runs were preserved.", retryable: true);
        else activity.Complete($"Created {created} presentation runs; {duplicates} duplicate creations were prevented. No live presentation or outbound message was started.");
        _db.AuditEvents.Add(new(Guid.NewGuid(), activity.CompanyId, "system", "campaign-scheduler",
            failures > 0 ? "sales.campaign.presentation_partial_failure" : "sales.campaign.presentation_completed",
            "sales_campaign_activity", activity.Id.ToString("D"), failures > 0 ? "failed" : "succeeded",
            $"Subjects={subjects.Count}; RunsCreated={created}; DuplicatesPrevented={duplicates}; Failures={failures}.", DateTime.UtcNow));
        _logger.LogInformation(
            "Campaign presentation scheduling completed. CompanyId={CompanyId} CampaignId={CampaignId} ActivityId={ActivityId} SubjectsResolved={SubjectsResolved} RunsCreated={RunsCreated} DuplicatesPrevented={DuplicatesPrevented} Failures={Failures} LatencyMs={LatencyMs}",
            activity.CompanyId, activity.SalesCampaignId, activity.Id, subjects.Count, created, duplicates, failures,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private async Task<List<PresentationSubject>> ResolveSubjectsAsync(SalesCampaignActivity activity,
        SalesCampaignPresentationActivity configuration, CancellationToken cancellationToken)
    {
        if (configuration.ExecutionScope == SalesCampaignPresentationExecutionScope.CampaignEvent)
        {
            var sessionId = configuration.EventSessionId ?? throw new InvalidOperationException("Campaign-event scope requires a session.");
            var exists = await _db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == activity.CompanyId && x.Id == sessionId, cancellationToken);
            return exists ? [new("campaign_event", sessionId, "campaign event", "campaign event attendees", sessionId)] : [];
        }
        var contacts = await _db.SalesCampaignContacts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == activity.CompanyId && x.SalesCampaignId == activity.SalesCampaignId)
            .Join(_db.Contacts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == activity.CompanyId && !x.IsDeleted),
                link => link.ContactId, contact => contact.Id,
                (_, contact) => new { contact.Id, contact.FullName, contact.CustomerCompanyId })
            .ToListAsync(cancellationToken);
        if (configuration.ExecutionScope == SalesCampaignPresentationExecutionScope.PerContact)
            return contacts.Select(x => new PresentationSubject("contact", x.Id, x.FullName, $"contact {x.FullName}", null)).ToList();
        if (contacts.Any(x => !x.CustomerCompanyId.HasValue)) throw new InvalidOperationException("Every eligible contact must have an account for per-account execution.");
        return contacts.GroupBy(x => x.CustomerCompanyId!.Value)
            .Select(x => new PresentationSubject("account", x.Key, $"account {x.Key:D}", $"{x.Count()} campaign contacts at the account", null)).ToList();
    }

    private static string StableKey(Guid companyId, Guid campaignId, Guid activityId, Guid versionId, string scope, Guid subjectId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{companyId:N}|{campaignId:N}|{activityId:N}|{versionId:N}|{scope}|{subjectId:N}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
    private sealed record PresentationSubject(string Type, Guid Id, string Label, string Audience, Guid? MeetingSessionId);
}
