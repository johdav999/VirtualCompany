using System.Security.Cryptography;
using System.Text;
using Cronos;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAutonomyTriggerService : IFinanceAutonomyTriggerService
{
    private const int MaximumTriggerAttempts = 3;
    private const int MaximumOperationalTake = 200;
    private static readonly TimeSpan TriggerLease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);
    private readonly VirtualCompanyDbContext _db;
    private readonly IFinanceAutonomyRunService _runs;
    private readonly IFinanceAgentCoverageCatalogue _coverage;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _clock;

    public FinanceAutonomyTriggerService(VirtualCompanyDbContext db, IFinanceAutonomyRunService runs,
        IFinanceAgentCoverageCatalogue coverage, ICompanyMembershipContextResolver memberships,
        IAuditEventWriter audit, TimeProvider clock)
    {
        _db = db;
        _runs = runs;
        _coverage = coverage;
        _memberships = memberships;
        _audit = audit;
        _clock = clock;
    }

    public async Task<FinanceAutonomyTriggerBatchResult> ProcessDueSchedulesAsync(DateTime utcNow,
        string workerId, int batchSize, CancellationToken cancellationToken)
    {
        var now = Utc(utcNow);
        var take = Math.Clamp(batchSize, 1, 100);
        var candidates = (await _db.FinanceAutonomyGrantVersions.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.Grant)
                .Where(x => x.Status == FinanceAutonomyGrantVersionStatus.Active &&
                            x.Grant.ActiveVersionId == x.Id && x.EffectiveFromUtc <= now &&
                            (!x.ExpiresUtc.HasValue || x.ExpiresUtc > now) &&
                            x.ScheduleExpression != null)
                .OrderBy(x => x.CompanyId).ThenBy(x => x.GrantId).Take(take * 4)
                .ToListAsync(cancellationToken))
            .Where(x => x.AllowedTriggers.Contains(FinanceAutonomyTriggers.Schedule, StringComparer.Ordinal) &&
                        !string.IsNullOrWhiteSpace(x.ScheduleExpression))
            .Take(take).ToArray();

        var started = 0;
        var coalesced = 0;
        var suppressed = 0;
        var failed = 0;
        var deadLettered = 0;
        foreach (var grant in candidates)
        {
            var result = await ProcessScheduleGrantAsync(grant, now, workerId, cancellationToken);
            if (result is null) continue;
            if (result.Accepted && result.Coalesced) coalesced++;
            else if (result.Accepted) started++;
            else if (result.ReasonCode == FinanceAutonomyTriggerReasonCodes.DeadLettered) deadLettered++;
            else if (result.ReasonCode == FinanceAutonomyTriggerReasonCodes.ProcessingFailed) failed++;
            else suppressed++;
        }

        return new(candidates.Length, started, coalesced, suppressed, failed, deadLettered);
    }

    public async Task<FinanceAutonomyTriggerProcessResult> ProcessEventAsync(
        FinanceAutonomyEventSignal signal, string workerId, CancellationToken cancellationToken)
    {
        ValidateSignal(signal);
        var now = UtcNow();
        var normalizedType = Normalize(signal.EventType);
        var grants = (await _db.FinanceAutonomyGrantVersions.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.Grant)
                .Where(x => x.CompanyId == signal.CompanyId &&
                            x.Status == FinanceAutonomyGrantVersionStatus.Active &&
                            x.Grant.ActiveVersionId == x.Id && x.EffectiveFromUtc <= now &&
                            (!x.ExpiresUtc.HasValue || x.ExpiresUtc > now))
                .OrderBy(x => x.GrantId).ToListAsync(cancellationToken))
            .Where(x => x.AllowedTriggers.Contains(FinanceAutonomyTriggers.BusinessEvent, StringComparer.Ordinal) &&
                        x.AllowedEventTypes.Contains(normalizedType, StringComparer.Ordinal) &&
                        (string.IsNullOrWhiteSpace(signal.CapabilityId) ||
                         string.Equals(x.Grant.CapabilityId, Normalize(signal.CapabilityId), StringComparison.Ordinal)))
            .ToArray();
        if (grants.Length == 0)
            return new(false, false, false, null, FinanceAutonomyTriggerReasonCodes.GrantUnavailable,
                "No active reviewed Finance autonomy grant accepts this event.");

        FinanceAutonomyTriggerProcessResult? first = null;
        foreach (var grant in grants)
        {
            var result = await ProcessEventGrantAsync(grant, signal with { EventType = normalizedType },
                workerId, now, cancellationToken);
            first ??= result;
        }
        return first!;
    }

    public async Task<FinanceAutonomyTriggerQueryResult> GetOperationalStateAsync(Guid companyId, int take,
        CancellationToken cancellationToken)
    {
        await RequireMemberAsync(companyId, manager: false, cancellationToken);
        var boundedTake = Math.Clamp(take, 1, MaximumOperationalTake);
        var cursors = await _db.FinanceAutonomyTriggerCursors.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.UpdatedUtc)
            .Take(boundedTake).ToListAsync(cancellationToken);
        var cursorIds = cursors.Select(x => x.Id).ToArray();
        var events = await _db.FinanceAutonomyTriggerEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && cursorIds.Contains(x.CursorId))
            .OrderByDescending(x => x.CreatedUtc).Take(boundedTake).ToListAsync(cancellationToken);
        return new(cursors.Select(Map).ToArray(), events.Select(Map).ToArray());
    }

    public async Task<FinanceAutonomyTriggerCursorDto> RetryDeadLetterAsync(Guid companyId, Guid cursorId,
        long expectedVersion, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(companyId, manager: true, cancellationToken);
        var cursor = await _db.FinanceAutonomyTriggerCursors.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == cursorId, cancellationToken)
            ?? throw new KeyNotFoundException("Finance autonomy trigger cursor was not found.");
        if (expectedVersion > 0 && cursor.Version != expectedVersion)
            throw new FinanceAutonomyRunConcurrencyException("The Finance autonomy trigger changed. Refresh and retry.");
        cursor.Reset(UtcNow());
        var receipt = await _db.FinanceAutonomyTriggerEvents.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.CursorId == cursorId &&
                        x.Status == FinanceAutonomyTriggerEventStatus.DeadLettered &&
                        x.FailureCode == FinanceAutonomyTriggerReasonCodes.DeadLettered)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        receipt?.ResetForRetry();
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
            AuditEventActions.FinanceAutonomyTriggerRetried, AuditTargetTypes.FinanceAutonomyTrigger,
            cursor.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "An operator released a dead-lettered Finance autonomy trigger and its latest failed receipt for a bounded retry.",
            CorrelationId: $"finance-trigger-retry:{cursor.Id:N}:{cursor.Version}"), cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(cursor);
    }

    private async Task<FinanceAutonomyTriggerProcessResult?> ProcessScheduleGrantAsync(
        FinanceAutonomyGrantVersion grant, DateTime now, string workerId, CancellationToken ct)
    {
        var cursor = await GetOrCreateCursorAsync(grant, "schedule", "schedule", now, ct);
        var occurrences = GetDueOccurrences(grant, cursor.CursorUtc, now);
        if (occurrences.Count == 0)
        {
            if (!cursor.CursorUtc.HasValue && grant.CatchUpBehavior == FinanceAutonomyCatchUpBehaviors.Skip)
            {
                var skipClaim = await TryClaimAsync(cursor, workerId, now, ct);
                if (skipClaim.Claimed)
                {
                    cursor.Suppress(skipClaim.Token!, now, FinanceAutonomyTriggerReasonCodes.GrantUnavailable,
                        "Earlier schedule windows were skipped by the reviewed catch-up policy.", now, now);
                    await SaveAsync(ct);
                }
            }
            return null;
        }

        var occurrence = occurrences[^1];
        if (cursor.NextEligibleUtc > now) return null;
        var claim = await TryClaimAsync(cursor, workerId, now, ct);
        if (!claim.Claimed)
            return new(false, false, false, null, FinanceAutonomyTriggerReasonCodes.LeaseUnavailable,
                "Another host owns the Finance trigger lease.");
        var leaseToken = claim.Token!;

        var quota = GetCompanyLocalWindow(grant, occurrence);
        if (cursor.QuotaWindowStartUtc == quota.StartUtc && cursor.QuotaWindowEndUtc == quota.EndUtc &&
            cursor.RunsInWindow >= grant.MaximumRunsPerWindow)
        {
            cursor.Suppress(leaseToken, occurrence, FinanceAutonomyTriggerReasonCodes.WindowLimit,
                "The reviewed maximum runs for this company-local window has been reached.", quota.EndUtc, now);
            await SaveAsync(ct);
            return new(false, false, false, cursor.LastRunId, FinanceAutonomyTriggerReasonCodes.WindowLimit,
                "The reviewed maximum runs for this company-local window has been reached.");
        }

        try
        {
            var pair = ResolvePlanPair(grant, FinanceAutonomyTriggers.Schedule, null);
            var occurrenceKey = occurrence.ToString("O");
            var sourceHash = Hash($"{grant.Id:N}|{grant.VersionNumber}|{occurrenceKey}|{grant.ScheduleExpression}");
            var run = await _runs.CreateOrCoalesceAsync(grant.CompanyId,
                BuildRunCommand(grant, FinanceAutonomyTriggers.Schedule,
                    $"schedule:{grant.GrantId:N}:{occurrenceKey}", quota.StartUtc, quota.EndUtc,
                    null, null, occurrence, $"finance-trigger:{grant.CompanyId:N}:{grant.Id:N}:{occurrenceKey}",
                    new Dictionary<string, string?>
                    {
                        ["triggerType"] = "schedule", ["scheduledOccurrenceUtc"] = occurrenceKey,
                        ["scheduleExpression"] = grant.ScheduleExpression,
                        ["grantVersionId"] = grant.Id.ToString("N")
                    }, pair,
                    [new("schedule", "finance_autonomy_grant_version", grant.Id.ToString("N"),
                        grant.VersionNumber.ToString(), sourceHash, "Reviewed Finance schedule")]), ct);
            var isCoalesced = cursor.LastRunId == run.Id;
            cursor.RecordRun(leaseToken, run.Id, occurrence, null, quota.StartUtc, quota.EndUtc,
                quota.StartUtc, quota.EndUtc, !isCoalesced, now.AddMinutes(grant.MinimumIntervalMinutes),
                isCoalesced, now);
            await WriteSystemAuditAsync(cursor, run.Id, isCoalesced, ct);
            await SaveAsync(ct);
            return new(true, false, isCoalesced, run.Id,
                isCoalesced ? FinanceAutonomyTriggerReasonCodes.Coalesced : FinanceAutonomyTriggerReasonCodes.Processed,
                isCoalesced ? "The scheduled signal coalesced into the existing durable run." : "The reviewed schedule started one durable Finance run.");
        }
        catch (FinanceAutonomyRunValidationException ex)
        {
            var summary = SafeValidationSummary(ex);
            cursor.Suppress(leaseToken, occurrence, FinanceAutonomyTriggerReasonCodes.GrantUnavailable,
                summary, occurrence.AddMinutes(grant.MinimumIntervalMinutes), now);
            await SaveAsync(ct);
            return new(false, false, false, null, FinanceAutonomyTriggerReasonCodes.GrantUnavailable, summary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cursor.Fail(leaseToken, FinanceAutonomyTriggerReasonCodes.ProcessingFailed,
                SafeFailure(ex), MaximumTriggerAttempts, now.Add(RetryDelay), now);
            await SaveAsync(ct);
            return new(false, false, false, null,
                cursor.Status == FinanceAutonomyTriggerCursorStatus.DeadLettered
                    ? FinanceAutonomyTriggerReasonCodes.DeadLettered
                    : FinanceAutonomyTriggerReasonCodes.ProcessingFailed,
                cursor.FailureSummary!);
        }
    }

    private async Task<FinanceAutonomyTriggerProcessResult> ProcessEventGrantAsync(
        FinanceAutonomyGrantVersion grant, FinanceAutonomyEventSignal signal, string workerId,
        DateTime now, CancellationToken ct)
    {
        var cursorKey = $"{signal.EventType}:{Normalize(signal.CoalescingKey)}";
        var cursor = await GetOrCreateCursorAsync(grant, "business_event", cursorKey, now, ct);
        var existing = await _db.FinanceAutonomyTriggerEvents.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == signal.CompanyId && x.CursorId == cursor.Id &&
                x.SourceEventId == signal.SourceEventId && x.SourceEventVersion == signal.SourceEventVersion, ct);
        if (existing is not null && existing.Status != FinanceAutonomyTriggerEventStatus.Received)
            return new(existing.RunId.HasValue, true, existing.Status == FinanceAutonomyTriggerEventStatus.Coalesced,
                existing.RunId, FinanceAutonomyTriggerReasonCodes.Duplicate,
                "This authoritative Finance event version was already received.");

        var receipt = existing ?? new FinanceAutonomyTriggerEvent(Guid.NewGuid(), signal.CompanyId, cursor.Id,
            signal.EventType, signal.SourceEventId, signal.SourceEventVersion, signal.SourceEntityType,
            signal.SourceEntityId, signal.OccurredUtc, signal.EvidenceObservedUtc, signal.CoalescingKey,
            signal.ContentHash, signal.SafeLabel, signal.CorrelationId, now);
        if (existing is null) _db.FinanceAutonomyTriggerEvents.Add(receipt);

        if (Utc(signal.OccurredUtc) > now.AddMinutes(1) ||
            now - Utc(signal.OccurredUtc) > TimeSpan.FromMinutes(grant.LateEventToleranceMinutes))
        {
            receipt.DeadLetter(FinanceAutonomyTriggerReasonCodes.LateEvent,
                "The event is outside the reviewed late-event tolerance and requires a new authoritative version.", now);
            await SaveAsync(ct);
            return new(false, false, false, null, FinanceAutonomyTriggerReasonCodes.LateEvent,
                "The event is outside the reviewed late-event tolerance.");
        }

        if (cursor.Status == FinanceAutonomyTriggerCursorStatus.DeadLettered)
        {
            receipt.DeadLetter(FinanceAutonomyTriggerReasonCodes.DeadLettered,
                "The Finance trigger is dead-lettered and requires an operator retry.", now);
            await SaveAsync(ct);
            return new(false, false, false, null, FinanceAutonomyTriggerReasonCodes.DeadLettered,
                "The Finance trigger is dead-lettered and requires an operator retry.");
        }
        if (cursor.Status == FinanceAutonomyTriggerCursorStatus.RetryScheduled && cursor.NextEligibleUtc > now)
        {
            await SaveAsync(ct);
            return new(false, false, false, null, FinanceAutonomyTriggerReasonCodes.ProcessingFailed,
                "The Finance trigger is waiting for its bounded retry time.");
        }

        var claim = await TryClaimAsync(cursor, workerId, now, ct);
        if (!claim.Claimed)
        {
            _db.Entry(receipt).State = EntityState.Detached;
            return new(false, false, false, null, FinanceAutonomyTriggerReasonCodes.LeaseUnavailable,
                "Another host owns the Finance trigger lease.");
        }
        var leaseToken = claim.Token!;

        var quota = GetCompanyLocalWindow(grant, signal.OccurredUtc);
        var coalesceWithLast = cursor.LastRunId.HasValue && cursor.NextEligibleUtc > now &&
                               cursor.CurrentWindowStartUtc.HasValue && cursor.CurrentWindowEndUtc.HasValue;
        var runWindow = coalesceWithLast
            ? (cursor.CurrentWindowStartUtc!.Value, cursor.CurrentWindowEndUtc!.Value)
            : GetDebounceWindow(signal.OccurredUtc, grant.DebounceMinutes);
        if (!coalesceWithLast && cursor.QuotaWindowStartUtc == quota.StartUtc &&
            cursor.QuotaWindowEndUtc == quota.EndUtc && cursor.RunsInWindow >= grant.MaximumRunsPerWindow)
        {
            receipt.Suppress(FinanceAutonomyTriggerReasonCodes.WindowLimit,
                "The reviewed maximum runs for this company-local window has been reached.", now);
            cursor.Suppress(leaseToken, signal.OccurredUtc, FinanceAutonomyTriggerReasonCodes.WindowLimit,
                "The reviewed maximum runs for this company-local window has been reached.", quota.EndUtc, now);
            await SaveAsync(ct);
            return new(false, false, false, cursor.LastRunId, FinanceAutonomyTriggerReasonCodes.WindowLimit,
                "The reviewed maximum runs for this company-local window has been reached.");
        }

        try
        {
            var pair = ResolvePlanPair(grant, FinanceAutonomyTriggers.BusinessEvent, signal.EventType);
            var aggregateId = Hash($"{grant.Id:N}|{signal.EventType}|{Normalize(signal.CoalescingKey)}|{runWindow.Item1:O}|{runWindow.Item2:O}");
            var run = await _runs.CreateOrCoalesceAsync(signal.CompanyId,
                BuildRunCommand(grant, FinanceAutonomyTriggers.BusinessEvent,
                    $"event:{signal.EventType}:{Normalize(signal.CoalescingKey)}:{runWindow.Item1:O}",
                    runWindow.Item1, runWindow.Item2, aggregateId, "aggregate-v1",
                    signal.EvidenceObservedUtc, signal.CorrelationId,
                    new Dictionary<string, string?>
                    {
                        ["triggerType"] = "business_event", ["eventType"] = signal.EventType,
                        ["coalescingKey"] = signal.CoalescingKey, ["aggregateEventId"] = aggregateId
                    }, pair,
                    [new("authoritative_event", signal.SourceEntityType, signal.SourceEntityId,
                        signal.SourceEventVersion, signal.ContentHash, signal.SafeLabel)]), ct);
            var isCoalesced = coalesceWithLast || cursor.LastRunId == run.Id;
            receipt.Complete(run.Id, isCoalesced, now);
            cursor.RecordRun(leaseToken, run.Id, signal.OccurredUtc, signal.SourceEventVersion,
                runWindow.Item1, runWindow.Item2, quota.StartUtc, quota.EndUtc, !isCoalesced,
                now.AddMinutes(grant.MinimumIntervalMinutes), isCoalesced, now);
            await WriteSystemAuditAsync(cursor, run.Id, isCoalesced, ct);
            await SaveAsync(ct);
            return new(true, false, isCoalesced, run.Id,
                isCoalesced ? FinanceAutonomyTriggerReasonCodes.Coalesced : FinanceAutonomyTriggerReasonCodes.Processed,
                isCoalesced ? "The event was retained on the existing bounded Finance run." : "The authoritative event started one durable Finance run.");
        }
        catch (FinanceAutonomyRunValidationException ex)
        {
            var summary = SafeValidationSummary(ex);
            receipt.Suppress(FinanceAutonomyTriggerReasonCodes.GrantUnavailable, summary, now);
            cursor.Suppress(leaseToken, signal.OccurredUtc, FinanceAutonomyTriggerReasonCodes.GrantUnavailable,
                summary, now.AddMinutes(grant.MinimumIntervalMinutes), now);
            await SaveAsync(ct);
            return new(false, false, false, null, FinanceAutonomyTriggerReasonCodes.GrantUnavailable, summary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cursor.Fail(leaseToken, FinanceAutonomyTriggerReasonCodes.ProcessingFailed,
                SafeFailure(ex), MaximumTriggerAttempts, now.Add(RetryDelay), now);
            if (cursor.Status == FinanceAutonomyTriggerCursorStatus.DeadLettered)
                receipt.DeadLetter(FinanceAutonomyTriggerReasonCodes.DeadLettered, cursor.FailureSummary!, now);
            await SaveAsync(ct);
            return new(false, false, false, null,
                cursor.Status == FinanceAutonomyTriggerCursorStatus.DeadLettered
                    ? FinanceAutonomyTriggerReasonCodes.DeadLettered
                    : FinanceAutonomyTriggerReasonCodes.ProcessingFailed,
                cursor.FailureSummary!);
        }
    }

    private CreateOrCoalesceFinanceAutonomyRunCommand BuildRunCommand(FinanceAutonomyGrantVersion grant,
        string trigger, string triggerKey, DateTime windowStartUtc, DateTime windowEndUtc,
        string? authoritativeEventId, string? authoritativeEventVersion, DateTime evidenceObservedUtc,
        string correlationId, IReadOnlyDictionary<string, string?> evidence, PlanPair pair,
        IReadOnlyList<FinanceAutonomyRunSourceDefinition> sources)
    {
        var requestedHash = Hash($"{grant.Id:N}|{triggerKey}|{pair.ActionClass}|{pair.ToolName}");
        var evidenceSnapshot = evidence.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (pair.Template is not null)
        {
            evidenceSnapshot["workflowTemplateCode"] = pair.Template.Code;
            evidenceSnapshot["workflowTemplateVersion"] = pair.Template.Version;
            evidenceSnapshot["workflowOwnerRole"] = pair.Template.OwnerRole;
            evidenceSnapshot["workflowNextHumanAction"] = pair.Template.NextHumanAction.En;
            evidenceSnapshot["workflowApprovalBehavior"] = pair.Template.ApprovalBehavior;
        }
        var stepKey = pair.Template is null ? "validate_and_prepare" : $"reviewed_template:{pair.Template.Code}";
        var payload = pair.Template?.RequestPayload.ToDictionary(
            x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
        if (pair.Template is not null && payload is not null)
            AddCurrentTemplateSelectors(pair.Template, payload, evidenceObservedUtc, windowStartUtc, windowEndUtc);
        return new(grant.Grant.AgentId, grant.Grant.CapabilityId, trigger, triggerKey,
            Utc(windowStartUtc), Utc(windowEndUtc), authoritativeEventId, authoritativeEventVersion,
            $"finance-trigger:{grant.CompanyId:N}:{grant.Id:N}:{Hash(triggerKey)[..24]}", correlationId,
            Utc(evidenceObservedUtc), evidenceSnapshot,
            pair.Template?.Version ?? "finance-autonomy-trigger-plan-v1",
            [new(stepKey, pair.ActionClass, pair.ToolName, [], requestedHash,
                pair.Template?.Description.En ?? "Validate current Finance evidence and prepare only the reviewed bounded work.",
                3, false, RequestPayload: payload,
                BusinessIdempotencyKey: $"finance-workflow:{grant.CompanyId:N}:{grant.GrantId:N}:{Hash(triggerKey)}")],
            new Dictionary<string, decimal>
            {
                ["maximumRecords"] = grant.MaximumRecordsPerRun,
                ["maximumActions"] = grant.MaximumActionsPerRun,
                ["maximumAmount"] = grant.MaximumAmountPerRun ?? 0m
            }, sources, 1);
    }

    private PlanPair ResolvePlanPair(FinanceAutonomyGrantVersion grant, string trigger, string? eventType)
    {
        var capability = _coverage.ListManifests().SingleOrDefault(x =>
            string.Equals(x.Id, grant.Grant.CapabilityId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The reviewed Finance capability is no longer available.");
        var template = FinanceAutonomyWorkflowTemplateCatalogue.Resolve(grant.Grant.CapabilityId, trigger, eventType);
        if (template is not null && grant.AllowedTools.Contains(template.ToolName, StringComparer.OrdinalIgnoreCase) &&
            grant.AllowedActionClasses.Contains(template.ActionClass, StringComparer.Ordinal))
            return new(template.ActionClass, template.ToolName, template);
        foreach (var tool in grant.AllowedTools.OrderBy(x => x, StringComparer.Ordinal))
        {
            var operation = capability.Operations.SingleOrDefault(x =>
                string.Equals(x.ToolName, tool, StringComparison.OrdinalIgnoreCase));
            if (operation is not null && grant.AllowedActionClasses.Contains(operation.ActionClass, StringComparer.Ordinal))
                return new(operation.ActionClass, operation.ToolName!, null);
        }
        throw new InvalidOperationException("The reviewed Finance grant no longer has a matching tool/action pair.");
    }

    private static void AddCurrentTemplateSelectors(FinanceAutonomyWorkflowTemplate template,
        IDictionary<string, System.Text.Json.Nodes.JsonNode?> payload, DateTime evidenceObservedUtc,
        DateTime windowStartUtc, DateTime windowEndUtc)
    {
        var observed = Utc(evidenceObservedUtc);
        switch (template.ToolName)
        {
            case "get_cash_balance":
            case "resolve_finance_agent_query":
            case FinanceAgentAnalysisToolIds.Analyze:
                payload["asOfUtc"] = System.Text.Json.Nodes.JsonValue.Create(observed.ToString("O"));
                break;
            case "list_uncategorized_transactions":
                payload["startUtc"] = System.Text.Json.Nodes.JsonValue.Create(Utc(windowStartUtc).ToString("O"));
                payload["endUtc"] = System.Text.Json.Nodes.JsonValue.Create(Utc(windowEndUtc).ToString("O"));
                break;
            case FinanceCloseComplianceAgentToolIds.ReadComplianceObligations:
                payload["from"] = System.Text.Json.Nodes.JsonValue.Create(DateOnly.FromDateTime(observed).ToString("yyyy-MM-dd"));
                payload["to"] = System.Text.Json.Nodes.JsonValue.Create(DateOnly.FromDateTime(observed.AddDays(30)).ToString("yyyy-MM-dd"));
                break;
        }
    }

    private async Task<FinanceAutonomyTriggerCursor> GetOrCreateCursorAsync(FinanceAutonomyGrantVersion grant,
        string triggerKind, string triggerKey, DateTime now, CancellationToken ct)
    {
        var existing = await _db.FinanceAutonomyTriggerCursors.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == grant.CompanyId && x.GrantVersionId == grant.Id &&
                x.TriggerKind == triggerKind && x.TriggerKey == triggerKey, ct);
        if (existing is not null) return existing;
        var cursor = new FinanceAutonomyTriggerCursor(Guid.NewGuid(), grant.CompanyId, grant.GrantId,
            grant.Id, grant.Grant.AgentId, grant.Grant.CapabilityId, triggerKind, triggerKey, now);
        _db.FinanceAutonomyTriggerCursors.Add(cursor);
        try { await _db.SaveChangesAsync(ct); return cursor; }
        catch (DbUpdateException)
        {
            _db.Entry(cursor).State = EntityState.Detached;
            return await _db.FinanceAutonomyTriggerCursors.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == grant.CompanyId && x.GrantVersionId == grant.Id &&
                x.TriggerKind == triggerKind && x.TriggerKey == triggerKey, ct);
        }
    }

    private async Task<(bool Claimed, string? Token)> TryClaimAsync(FinanceAutonomyTriggerCursor cursor,
        string workerId, DateTime now, CancellationToken ct)
    {
        var leaseToken = Guid.NewGuid().ToString("N");
        if (!cursor.TryClaim(workerId, leaseToken, now, TriggerLease)) return (false, null);
        try { await _db.SaveChangesAsync(ct); return (true, leaseToken); }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(cursor).State = EntityState.Detached;
            return (false, null);
        }
    }

    private static IReadOnlyList<DateTime> GetDueOccurrences(FinanceAutonomyGrantVersion grant,
        DateTime? cursorUtc, DateTime nowUtc)
    {
        var expression = CronExpression.Parse(grant.ScheduleExpression!, CronFormat.Standard);
        var zone = CronosScheduleExpressionValidator.ResolveTimeZone(grant.Timezone);
        var from = cursorUtc ?? (grant.CatchUpBehavior == FinanceAutonomyCatchUpBehaviors.Skip
            ? nowUtc
            : new[] { grant.EffectiveFromUtc, nowUtc.AddDays(-31) }.Max());
        if (from >= nowUtc) return [];
        var occurrences = expression.GetOccurrences(from, nowUtc, zone, fromInclusive: false, toInclusive: true)
            .Select(Utc).ToArray();
        if (occurrences.Length == 0) return [];
        return grant.CatchUpBehavior == FinanceAutonomyCatchUpBehaviors.Latest
            ? [occurrences[^1]]
            : occurrences.TakeLast(Math.Max(1, grant.MaximumCatchUpWindows)).ToArray();
    }

    private static (DateTime StartUtc, DateTime EndUtc) GetCompanyLocalWindow(
        FinanceAutonomyGrantVersion grant, DateTime referenceUtc)
    {
        var zone = CronosScheduleExpressionValidator.ResolveTimeZone(grant.Timezone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(Utc(referenceUtc), zone);
        var startTime = TimeOnly.ParseExact(grant.WindowStartLocal, "HH:mm");
        var endTime = TimeOnly.ParseExact(grant.WindowEndLocal, "HH:mm");
        var startLocal = local.Date.Add(startTime.ToTimeSpan());
        var endLocal = local.Date.Add(endTime.ToTimeSpan());
        if (endLocal <= startLocal) endLocal = endLocal.AddDays(1);
        return (LocalToUtc(startLocal, zone, chooseLaterOffset: false),
            LocalToUtc(endLocal, zone, chooseLaterOffset: true));
    }

    private static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone, bool chooseLaterOffset)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (!zone.IsAmbiguousTime(local)) return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        var offsets = zone.GetAmbiguousTimeOffsets(local);
        var utcValues = offsets.Select(offset => new DateTimeOffset(local, offset).UtcDateTime).OrderBy(x => x).ToArray();
        return chooseLaterOffset ? utcValues[^1] : utcValues[0];
    }

    private static (DateTime, DateTime) GetDebounceWindow(DateTime occurredUtc, int minutes)
    {
        var utc = Utc(occurredUtc);
        var ticks = TimeSpan.FromMinutes(Math.Max(1, minutes)).Ticks;
        var start = new DateTime(utc.Ticks - utc.Ticks % ticks, DateTimeKind.Utc);
        return (start, start.AddTicks(ticks));
    }

    private async Task WriteSystemAuditAsync(FinanceAutonomyTriggerCursor cursor, Guid runId,
        bool coalesced, CancellationToken ct) => await _audit.WriteAsync(new AuditEventWriteRequest(
            cursor.CompanyId, AuditActorTypes.System, null,
            coalesced ? AuditEventActions.FinanceAutonomyTriggerCoalesced : AuditEventActions.FinanceAutonomyTriggerProcessed,
            AuditTargetTypes.FinanceAutonomyTrigger, cursor.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            coalesced ? "An authoritative Finance signal was retained on an existing bounded run."
                : "An authoritative Finance trigger created one durable run.",
            Metadata: new Dictionary<string, string?>
            {
                ["grantId"] = cursor.GrantId.ToString("N"), ["grantVersionId"] = cursor.GrantVersionId.ToString("N"),
                ["runId"] = runId.ToString("N"), ["triggerKind"] = cursor.TriggerKind,
                ["triggerKey"] = cursor.TriggerKey
            }, CorrelationId: $"finance-trigger:{cursor.Id:N}:{cursor.Version}"), ct);

    private async Task<ResolvedCompanyMembershipContext> RequireMemberAsync(Guid companyId, bool manager,
        CancellationToken ct)
    {
        var member = await _memberships.ResolveAsync(companyId, ct)
            ?? throw new UnauthorizedAccessException("Active company membership is required.");
        if (manager && member.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))
            throw new UnauthorizedAccessException("Company manager access is required.");
        return member;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new FinanceAutonomyRunConcurrencyException($"The Finance autonomy trigger changed concurrently: {ex.Message}");
        }
    }

    private static void ValidateSignal(FinanceAutonomyEventSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(signal));
        if (!FinanceAutonomyEventTypes.All.Contains(Normalize(signal.EventType)))
            throw new FinanceAutonomyRunValidationException(new Dictionary<string, string[]>
                { [nameof(signal.EventType)] = ["The Finance event type is not in the authoritative allowlist."] });
        if (string.IsNullOrWhiteSpace(signal.SourceEventId) || string.IsNullOrWhiteSpace(signal.SourceEventVersion) ||
            string.IsNullOrWhiteSpace(signal.SourceEntityType) || string.IsNullOrWhiteSpace(signal.SourceEntityId) ||
            string.IsNullOrWhiteSpace(signal.CoalescingKey) || string.IsNullOrWhiteSpace(signal.CorrelationId))
            throw new FinanceAutonomyRunValidationException(new Dictionary<string, string[]>
                { [nameof(signal)] = ["Authoritative event identity, version, source, coalescing key, and correlation are required."] });
        if (signal.ContentHash.Length != 64 || !signal.ContentHash.All(Uri.IsHexDigit))
            throw new FinanceAutonomyRunValidationException(new Dictionary<string, string[]>
                { [nameof(signal.ContentHash)] = ["A SHA-256 content hash is required."] });
    }

    private static FinanceAutonomyTriggerCursorDto Map(FinanceAutonomyTriggerCursor x) => new(
        x.Id, x.CompanyId, x.GrantId, x.GrantVersionId, x.AgentId, x.CapabilityId, x.TriggerKind,
        x.TriggerKey, x.Status.ToStorageValue(), x.CursorUtc, x.LastEventVersion,
        x.CurrentWindowStartUtc, x.CurrentWindowEndUtc, x.RunsInWindow, x.LastRunId, x.LastRunUtc,
        x.NextEligibleUtc, x.AttemptCount, x.LeaseOwner, x.LeaseExpiresUtc, x.FailureCode,
        x.FailureSummary, x.CreatedUtc, x.UpdatedUtc, x.Version);

    private static FinanceAutonomyTriggerEventDto Map(FinanceAutonomyTriggerEvent x) => new(
        x.Id, x.CursorId, x.EventType, x.SourceEventId, x.SourceEventVersion, x.SourceEntityType,
        x.SourceEntityId, x.OccurredUtc, x.EvidenceObservedUtc, x.CoalescingKey, x.ContentHash,
        x.SafeLabel, x.CorrelationId, x.Status.ToStorageValue(), x.RunId, x.FailureCode,
        x.FailureSummary, x.CreatedUtc, x.ProcessedUtc);

    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string SafeFailure(Exception ex) => ex is InvalidOperationException or ArgumentException
        ? ex.Message[..Math.Min(ex.Message.Length, 1000)]
        : "Finance trigger processing failed safely. Review the operational record before retrying.";
    private static string SafeValidationSummary(FinanceAutonomyRunValidationException ex) =>
        string.Join(" ", ex.Errors.SelectMany(x => x.Value).Take(3))[..Math.Min(1000,
            string.Join(" ", ex.Errors.SelectMany(x => x.Value).Take(3)).Length)];
    private sealed record PlanPair(
        string ActionClass, string ToolName, FinanceAutonomyWorkflowTemplate? Template);
}
