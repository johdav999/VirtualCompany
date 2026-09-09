using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingChangeProposalService(VirtualCompanyDbContext db, ISalesMeetingChangePolicy policy,
    IApprovalRequestService approvals, ISalesMeetingCanonicalChangeCommandHandler canonical,
    ICompanyOutboxEnqueuer outbox, TimeProvider time, ISalesBrowserMeetingScheduling? browserMeetings=null) : ISalesMeetingChangeProposalService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SalesMeetingChangeProposalDto>> ListAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        var role = await RequireAccessAsync(companyId, userId, sessionId, ct);
        var items = await db.SalesMeetingChangeProposals.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId).OrderBy(x => x.CreatedUtc).ToListAsync(ct);
        var result = new List<SalesMeetingChangeProposalDto>(items.Count); foreach (var x in items) result.Add(await ToDtoAsync(x, role, ct)); return result;
    }
    public async Task<SalesMeetingChangeProposalDto?> GetAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, CancellationToken ct)
    { var role = await RequireAccessAsync(companyId, userId, sessionId, ct); var x = await FindAsync(companyId, sessionId, proposalId, false, ct); return x is null ? null : await ToDtoAsync(x, role, ct); }

    public async Task<IReadOnlyList<SalesMeetingChangeProposalDto>> GenerateAsync(Guid companyId, Guid userId, Guid sessionId, GenerateSalesMeetingChangeProposalsRequest request, string? correlationId, CancellationToken ct)
    {
        var role = await RequireAccessAsync(companyId, userId, sessionId, ct); if (request.Proposals is null || request.Proposals.Count is < 1 or > 50) throw Validation("proposals", "Provide between one and 50 typed proposals.");
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct);
        var created = new List<SalesMeetingChangeProposal>();
        foreach (var item in request.Proposals)
        {
            var target = Parse(() => SalesMeetingChangeProposalEnumValues.ParseTarget(item.TargetType), nameof(item.TargetType));
            var action = Parse(() => SalesMeetingChangeProposalEnumValues.ParseAction(item.Action), nameof(item.Action));
            var field = Parse(() => SalesMeetingChangeProposalEnumValues.ParseField(item.Field), nameof(item.Field));
            var value = ValidateValue(item.ProposedValue); var decision = policy.Evaluate(target, action, field, value.Kind, role);
            if (!decision.IsAllowed) throw Validation("proposals", decision.Explanation);
            await ValidateRelationshipAndEvidenceAsync(companyId, session, target, item.TargetId, item.EvidenceArtifactId, item.SourceIds, ct);
            var before = await CaptureBeforeAsync(companyId, sessionId, target, item.TargetId, field, ct);
            await ValidateBusinessValueAsync(field, value.Value, companyId, ct);
            var sources = NormalizeSources(item.SourceIds); var evidenceHash = Hash($"{session.CaptureVersion}|{item.EvidenceArtifactId:N}|{string.Join('|', sources)}");
            var proposal = new SalesMeetingChangeProposal(Guid.NewGuid(), companyId, sessionId, item.EvidenceArtifactId, target, item.TargetId, action, field, value.Kind,
                JsonSerializer.Serialize(value.Value, Json), before.Json, before.Version, item.Confidence, item.Rationale, JsonSerializer.Serialize(sources, Json), evidenceHash,
                decision.RiskClass, decision.RequiresApproval, decision.PolicyVersion, userId, Now());
            var existing = await db.SalesMeetingChangeProposals.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == proposal.IdempotencyKey, ct);
            if (existing is not null) { created.Add(existing); continue; }
            db.SalesMeetingChangeProposals.Add(proposal); created.Add(proposal);
            AddAudit(companyId, userId, "sales.meeting_change.proposed", proposal.Id, AuditEventOutcomes.Requested, "A sourced sales change was proposed without modifying its target.", correlationId, proposal);
        }
        await SaveAsync(ct); var result = new List<SalesMeetingChangeProposalDto>(); foreach (var x in created) result.Add(await ToDtoAsync(x, role, ct)); return result;
    }

    public async Task<SalesMeetingChangeProposalDto?> EditAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, EditSalesMeetingChangeProposalRequest request, string? correlationId, CancellationToken ct)
    {
        var role = await RequireAccessAsync(companyId, userId, sessionId, ct); var x = await FindAsync(companyId, sessionId, proposalId, true, ct); if (x is null) return null;
        var value = ValidateValue(request.ProposedValue); var decision = policy.Evaluate(x.TargetType, x.Action, x.Field, value.Kind, role); if (!decision.IsAllowed) throw Validation("proposedValue", decision.Explanation);
        await ValidateSourceIdsAsync(companyId, sessionId, request.SourceIds, ct); await ValidateBusinessValueAsync(x.Field, value.Value, companyId, ct);
        var sources = NormalizeSources(request.SourceIds); var session = await db.SalesMeetingSessions.AsNoTracking().SingleAsync(y => y.CompanyId == companyId && y.Id == sessionId, ct);
        x.Edit(request.ExpectedVersion, value.Kind, JsonSerializer.Serialize(value.Value, Json), request.Confidence, request.Rationale,
            JsonSerializer.Serialize(sources, Json), Hash($"{session.CaptureVersion}|{x.EvidenceArtifactId:N}|{string.Join('|', sources)}"), decision.RiskClass, decision.RequiresApproval, decision.PolicyVersion, userId, Now());
        AddAudit(companyId, userId, "sales.meeting_change.edited", x.Id, AuditEventOutcomes.Succeeded, "The proposal was edited and its prior approval binding was invalidated.", correlationId, x); await SaveAsync(ct); return await ToDtoAsync(x, role, ct);
    }

    public async Task<SalesMeetingChangeProposalDto?> ApproveAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, ReviewSalesMeetingChangeProposalRequest request, string? correlationId, CancellationToken ct)
    {
        var role = await RequireAccessAsync(companyId, userId, sessionId, ct); var x = await FindAsync(companyId, sessionId, proposalId, true, ct); if (x is null) return null; EnsureExpected(x, request.ExpectedVersion);
        var decision = policy.Evaluate(x.TargetType, x.Action, x.Field, x.ValueKind, role); if (!decision.IsAllowed) throw Conflict(SalesMeetingChangeProposalProblemCodes.Conflict, decision.Explanation);
        var binding = Binding(x, decision.PolicyVersion);
        if (!decision.RequiresApproval) x.Approve(request.ExpectedVersion, binding, userId, Now());
        else if (x.ApprovalRequestId is null)
        {
            var approval = await approvals.CreateAsync(companyId, new CreateApprovalRequestCommand(ApprovalTargetEntityType.SalesMeetingChangeProposal.ToStorageValue(), x.Id, "user", userId, decision.ApprovalType,
                new Dictionary<string, JsonNode?> { ["bindingHash"] = binding, ["targetType"] = x.TargetType.ToStorageValue(), ["targetId"] = x.TargetId, ["field"] = x.Field.ToStorageValue(), ["proposedValue"] = x.ProposedValueJson, ["evidenceVersionHash"] = x.EvidenceVersionHash, ["targetVersion"] = x.TargetVersion, ["policyVersion"] = decision.PolicyVersion }, RequiredRole: "owner"), ct);
            x.RequestApproval(request.ExpectedVersion, approval.Id, binding, userId, Now());
        }
        else
        {
            if (!string.Equals(x.ApprovalBindingHash, binding, StringComparison.Ordinal)) throw Conflict(SalesMeetingChangeProposalProblemCodes.ApprovalBindingChanged, "The exact proposal binding changed. Request a fresh approval.");
            var approval = await db.ApprovalRequests.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.Id == x.ApprovalRequestId, ct);
            if (approval.Status == ApprovalRequestStatus.Approved) x.Approve(request.ExpectedVersion, binding, userId, Now());
            else if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled) x.Reject(request.ExpectedVersion, userId, "The linked approval is no longer valid.", Now());
        }
        AddAudit(companyId, userId, "sales.meeting_change.reviewed", x.Id, AuditEventOutcomes.Succeeded, decision.RequiresApproval ? "The proposal was bound to the approval workflow." : "The authorized reviewer explicitly confirmed the proposal.", correlationId, x); await SaveAsync(ct); return await ToDtoAsync(x, role, ct);
    }

    public async Task<SalesMeetingChangeProposalDto?> RejectAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, ReviewSalesMeetingChangeProposalRequest request, string? correlationId, CancellationToken ct)
    { var role = await RequireAccessAsync(companyId, userId, sessionId, ct); var x = await FindAsync(companyId, sessionId, proposalId, true, ct); if (x is null) return null; x.Reject(request.ExpectedVersion, userId, request.Reason, Now()); AddAudit(companyId, userId, "sales.meeting_change.rejected", x.Id, AuditEventOutcomes.Succeeded, request.Reason ?? "The proposal was rejected.", correlationId, x); await SaveAsync(ct); return await ToDtoAsync(x, role, ct); }

    public async Task<BulkApproveSalesMeetingChangeProposalsResult> BulkApproveSafeAsync(Guid companyId, Guid userId, Guid sessionId, BulkApproveSalesMeetingChangeProposalsRequest request, string? correlationId, CancellationToken ct)
    {
        var role = await RequireAccessAsync(companyId, userId, sessionId, ct); var ids = request.ProposalIds?.Where(x => x != Guid.Empty).Distinct().Take(100).ToArray() ?? [];
        var items = await db.SalesMeetingChangeProposals.Where(x => x.CompanyId == companyId && x.SessionId == sessionId && ids.Contains(x.Id)).ToListAsync(ct); var approved = new List<SalesMeetingChangeProposalDto>(); var skipped = new List<Guid>();
        foreach (var id in ids)
        {
            var x = items.SingleOrDefault(y => y.Id == id); if (x is null || x.Status != SalesMeetingChangeProposalStatus.Draft) { skipped.Add(id); continue; }
            var d = policy.Evaluate(x.TargetType, x.Action, x.Field, x.ValueKind, role); if (!d.IsAllowed || d.RequiresApproval || !d.IsSafeForBulkApproval) { skipped.Add(id); continue; }
            x.Approve(x.ConcurrencyVersion, Binding(x, d.PolicyVersion), userId, Now()); AddAudit(companyId, userId, "sales.meeting_change.bulk_confirmed", x.Id, AuditEventOutcomes.Succeeded, "Server policy independently classified this proposal as safe for bulk confirmation.", correlationId, x);
        }
        await SaveAsync(ct); foreach (var x in items.Where(x => x.Status == SalesMeetingChangeProposalStatus.Approved)) approved.Add(await ToDtoAsync(x, role, ct)); return new(approved, skipped);
    }

    public async Task<SalesMeetingChangeProposalDto?> ExecuteAsync(Guid companyId, Guid userId, Guid sessionId, Guid proposalId, ExecuteSalesMeetingChangeProposalRequest request, string? correlationId, CancellationToken ct)
    {
        var role = await RequireAccessAsync(companyId, userId, sessionId, ct); var x = await FindAsync(companyId, sessionId, proposalId, true, ct); if (x is null) return null;
        if (x.Status is SalesMeetingChangeProposalStatus.Executed or SalesMeetingChangeProposalStatus.Queued or SalesMeetingChangeProposalStatus.ReconciliationRequired) return await ToDtoAsync(x, role, ct); EnsureExpected(x, request.ExpectedVersion);
        var d = policy.Evaluate(x.TargetType, x.Action, x.Field, x.ValueKind, role); if (!d.IsAllowed) throw Conflict(SalesMeetingChangeProposalProblemCodes.Conflict, d.Explanation);
        var binding = Binding(x, d.PolicyVersion); if (x.Status != SalesMeetingChangeProposalStatus.Approved || !string.Equals(x.ApprovalBindingHash, binding, StringComparison.Ordinal)) throw Conflict(SalesMeetingChangeProposalProblemCodes.ApprovalBindingChanged, "Execution requires the exact currently approved proposal binding.");
        if (d.RequiresApproval)
        {
            if (x.ApprovalRequestId is null) throw Conflict(SalesMeetingChangeProposalProblemCodes.ApprovalRequired, "This proposal needs an owner approval.");
            var approval = await db.ApprovalRequests.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.Id == x.ApprovalRequestId, ct); if (approval.Status != ApprovalRequestStatus.Approved) throw Conflict(SalesMeetingChangeProposalProblemCodes.ApprovalRequired, "The linked approval is not approved.");
        }
        var current = await CaptureBeforeAsync(companyId, sessionId, x.TargetType, x.TargetId, x.Field, ct); if (!string.Equals(current.Version, x.TargetVersion, StringComparison.Ordinal)) { x.MarkConflict(SalesMeetingChangeProposalProblemCodes.TargetChanged, "The target changed after capture. Review the new value before executing."); await SaveAsync(ct); return await ToDtoAsync(x, role, ct); }
        x.BeginExecution();
        if (x.Action == SalesMeetingChangeAction.UpdateField)
        {
            var result = await canonical.ApplyAsync(new(companyId, x.TargetType.ToStorageValue(), x.TargetId, x.Field.ToStorageValue(), DeserializeValue(x), x.TargetVersion, userId), ct);
            x.MarkExecuted(result.BeforeValueJson, result.AfterValueJson, null, Now());
        }
        else if (x.Action == SalesMeetingChangeAction.SendCustomerMinutes)
        {
            x.MarkQueued(current.Json, JsonSerializer.Serialize("delivery_queued", Json), Now()); outbox.Enqueue(companyId, CompanyOutboxTopics.SalesMeetingCustomerMinutesDeliveryRequested,
                new SalesMeetingCustomerMinutesDeliveryRequestedMessage(companyId, x.Id, x.IdempotencyKey, correlationId), correlationId, idempotencyKey: x.IdempotencyKey, causationId: x.ApprovalRequestId?.ToString("N"));
        }
        else if (x.Action == SalesMeetingChangeAction.ScheduleNextMeeting) await QueueNextMeetingAsync(x, userId, correlationId, ct);
        AddAudit(companyId, userId, "sales.meeting_change.execution_requested", x.Id, AuditEventOutcomes.Succeeded, x.Status == SalesMeetingChangeProposalStatus.Executed ? "The typed Sales command executed once." : "The approved external action was queued durably.", correlationId, x); await SaveAsync(ct); return await ToDtoAsync(x, role, ct);
    }

    private async Task QueueNextMeetingAsync(SalesMeetingChangeProposal x, Guid userId, string? correlationId, CancellationToken ct)
    {
        var value = DeserializeValue(x).NextMeetingValue ?? throw Validation("proposedValue", "A complete next-meeting value is required.");
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleAsync(s => s.CompanyId == x.CompanyId && s.Id == x.SessionId, ct);
        var connection = await db.CalendarConnections.AsNoTracking().SingleOrDefaultAsync(c => c.CompanyId == x.CompanyId && c.Id == value.CalendarConnectionId && c.Status == ExternalConnectionStatus.Active && c.Capabilities.HasFlag(CalendarCapability.CreateEvents), ct) ?? throw Conflict(SalesMeetingChangeProposalProblemCodes.Conflict, "The approved calendar connection is no longer active or allowed to create events.");
        var contact = session.ContactId.HasValue ? await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.CompanyId == x.CompanyId && c.Id == session.ContactId, ct) : null;
        if (contact is null) throw Conflict(SalesMeetingChangeProposalProblemCodes.Conflict, "A meeting contact with an email address is required.");
        var deliveryKey = $"sales-next-meeting:{x.CompanyId:N}:{x.SessionId:N}:{contact.Email.ToLowerInvariant()}:{x.ConcurrencyVersion}";
        var conferencing=SalesMeetingConferencing.Resolve(value.Conferencing,value.CreateOnlineMeeting,connection.Provider);
        if(conferencing==SalesMeetingConferencing.Browser)
        {
            (browserMeetings??throw new InvalidOperationException("Browser scheduling is not configured.")).ValidateWindow(value.StartsUtc,value.EndsUtc);
            value=value with {Description=$"{value.Description}\n\n{SalesMeetingSchedulingService.AiMeetingDisclosure}"};
        }
        var invitation = new SalesMeetingInvitation(Guid.NewGuid(), x.CompanyId, session.LeadId, session.DealId, session.ContactId, connection.Id, connection.Provider, connection.AccountEmail, contact.Email, contact.FullName, value.Title, value.Description, value.StartsUtc, value.EndsUtc, value.TimeZoneId, value.Location, value.CreateOnlineMeeting, userId, Now(), deliveryKey);
        invitation.SelectConferencing(conferencing);invitation.UseCalendar(connection.CalendarId);
        invitation.SubmitForApproval(x.ApprovalRequestId!.Value); invitation.MarkApproved(userId, Now()); db.SalesMeetingInvitations.Add(invitation);
        x.MarkQueued(JsonSerializer.Serialize("not_scheduled", Json), JsonSerializer.Serialize(new { invitationId = invitation.Id, status = "queued" }, Json), Now());
        x.BindProviderReference(invitation.Id.ToString("D"), Now());
        outbox.Enqueue(x.CompanyId, CompanyOutboxTopics.SalesMeetingInvitationDeliveryRequested, new SalesMeetingInvitationDeliveryRequestedMessage(x.CompanyId, invitation.Id, invitation.IdempotencyKey, correlationId), correlationId, idempotencyKey: deliveryKey, causationId: x.ApprovalRequestId?.ToString("N"));
    }

    private async Task<CompanyMembershipRole> RequireAccessAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        if (companyId == Guid.Empty || userId == Guid.Empty || sessionId == Guid.Empty) throw new ArgumentException("Company, user, and session are required.");
        var membership = await db.CompanyMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct) ?? throw new UnauthorizedAccessException("An active company membership is required.");
        if (membership.Role == CompanyMembershipRole.Accountant) throw new UnauthorizedAccessException("This membership cannot access internal sales proposals.");
        if (!await db.SalesMeetingSessions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct)) throw new KeyNotFoundException("Meeting session not found."); return membership.Role;
    }

    private async Task ValidateRelationshipAndEvidenceAsync(Guid companyId, SalesMeetingSession session, SalesMeetingChangeTargetType target, Guid targetId, Guid artifactId, IReadOnlyList<string> sources, CancellationToken ct)
    {
        if (targetId == Guid.Empty || artifactId == Guid.Empty) throw Validation("targetId", "Target and evidence artifact are required.");
        var related = target switch { SalesMeetingChangeTargetType.Deal => session.DealId == targetId, SalesMeetingChangeTargetType.Lead => session.LeadId == targetId, SalesMeetingChangeTargetType.Contact => session.ContactId == targetId, SalesMeetingChangeTargetType.CustomerMinutes => await db.SalesMeetingMinutes.AnyAsync(x => x.CompanyId == companyId && x.SessionId == session.Id && x.Id == targetId, ct), SalesMeetingChangeTargetType.MeetingSession => session.Id == targetId, _ => false };
        if (!related) throw Validation("targetId", "The target does not belong to this meeting context.");
        if (!await db.SalesMeetingArtifacts.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == session.Id && x.Id == artifactId, ct)) throw Validation("evidenceArtifactId", "The evidence artifact is not available in this meeting.");
        await ValidateSourceIdsAsync(companyId, session.Id, sources, ct);
    }

    private async Task ValidateSourceIdsAsync(Guid companyId, Guid sessionId, IReadOnlyList<string> sourceIds, CancellationToken ct)
    {
        var sources = NormalizeSources(sourceIds); if (sources.Count == 0) throw Validation("sourceIds", "At least one meeting evidence source is required.");
        foreach (var source in sources)
        {
            var parts = source.Split(':', 2); if (parts.Length != 2 || !Guid.TryParse(parts[1], out var id)) throw Validation("sourceIds", $"Evidence source '{source}' is not a typed meeting source.");
            var exists = parts[0] switch { "artifact" => await db.SalesMeetingArtifacts.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == id, ct), "observation" => await db.SalesMeetingObservations.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == id, ct), "action-item" => await db.SalesMeetingActionItems.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == id, ct), "question" => await db.SalesMeetingQuestions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == id, ct), "minutes" => await db.SalesMeetingMinutes.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == id, ct), "internal" => await db.SalesMeetingInternalIntelligence.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == id, ct), _ => false };
            if (!exists) throw Validation("sourceIds", $"Evidence source '{source}' is not available in this meeting.");
        }
    }

    private async Task<Captured> CaptureBeforeAsync(Guid companyId, Guid sessionId, SalesMeetingChangeTargetType target, Guid targetId, SalesMeetingChangeField field, CancellationToken ct)
    {
        if (target == SalesMeetingChangeTargetType.Deal)
        { var x = await db.Deals.AsNoTracking().Include(y => y.PipelineStage).SingleOrDefaultAsync(y => y.CompanyId == companyId && y.Id == targetId, ct) ?? throw new KeyNotFoundException("Deal not found."); object? v = field switch { SalesMeetingChangeField.DealStage => x.PipelineStageId, SalesMeetingChangeField.DealProbability => x.Probability, SalesMeetingChangeField.DealValue => x.Amount, SalesMeetingChangeField.DealNextStep => x.NextStep, _ => null }; return new(JsonSerializer.Serialize(v, Json), SalesMeetingCanonicalChangeCommandHandler.Version(x.UpdatedUtc), x.Title); }
        if (target == SalesMeetingChangeTargetType.Lead)
        { var x = await db.Leads.AsNoTracking().SingleOrDefaultAsync(y => y.CompanyId == companyId && y.Id == targetId, ct) ?? throw new KeyNotFoundException("Lead not found."); object? v = field == SalesMeetingChangeField.LeadEstimatedValue ? x.EstimatedValue : x.SuggestedNextAction; return new(JsonSerializer.Serialize(v, Json), SalesMeetingCanonicalChangeCommandHandler.Version(x.UpdatedUtc), x.Title); }
        if (target == SalesMeetingChangeTargetType.Contact)
        { var x = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(y => y.CompanyId == companyId && y.Id == targetId, ct) ?? throw new KeyNotFoundException("Contact not found."); object? v = field switch { SalesMeetingChangeField.ContactFullName => x.FullName, SalesMeetingChangeField.ContactEmail => x.Email, SalesMeetingChangeField.ContactTitle => x.Title, _ => x.Phone }; return new(JsonSerializer.Serialize(v, Json), SalesMeetingCanonicalChangeCommandHandler.Version(x.UpdatedUtc), x.FullName); }
        if (target == SalesMeetingChangeTargetType.CustomerMinutes)
        { var x = await db.SalesMeetingMinutes.AsNoTracking().SingleOrDefaultAsync(y => y.CompanyId == companyId && y.SessionId == sessionId && y.Id == targetId, ct) ?? throw new KeyNotFoundException("Customer minutes not found."); return new(JsonSerializer.Serialize("not_sent", Json), x.ConcurrencyVersion.ToString(CultureInfo.InvariantCulture), $"Customer minutes v{x.ArtifactVersion}"); }
        var s = await db.SalesMeetingSessions.AsNoTracking().SingleAsync(y => y.CompanyId == companyId && y.Id == targetId, ct); return new(JsonSerializer.Serialize("not_scheduled", Json), s.ConcurrencyVersion.ToString(CultureInfo.InvariantCulture), "Next meeting");
    }

    private static ValidatedValue ValidateValue(SalesMeetingProposedValue value)
    {
        if (value is null) throw Validation("proposedValue", "A typed proposed value is required."); var kind = Parse(() => SalesMeetingChangeProposalEnumValues.ParseValueKind(value.Kind), "kind");
        object typed = kind switch { SalesMeetingChangeValueKind.String => value.StringValue ?? throw Validation("proposedValue", "A string value is required."), SalesMeetingChangeValueKind.Email => NormalizeEmail(value.StringValue), SalesMeetingChangeValueKind.Decimal => value.DecimalValue ?? throw Validation("proposedValue", "A decimal value is required."), SalesMeetingChangeValueKind.Guid => value.GuidValue is { } id && id != Guid.Empty ? id : throw Validation("proposedValue", "A non-empty identifier is required."), SalesMeetingChangeValueKind.DateTime => value.DateTimeValue?.ToUniversalTime() ?? throw Validation("proposedValue", "A date-time value is required."), SalesMeetingChangeValueKind.NextMeeting => ValidateNextMeeting(value.NextMeetingValue), _ => throw Validation("proposedValue", "The value kind is not supported.") };
        return new(kind, typed);
    }
    private static SalesMeetingNextMeetingValue ValidateNextMeeting(SalesMeetingNextMeetingValue? x) { if (x is null || x.CalendarConnectionId == Guid.Empty || x.StartsUtc <= DateTime.UtcNow.AddMinutes(5) || x.EndsUtc <= x.StartsUtc || x.EndsUtc - x.StartsUtc > TimeSpan.FromHours(8) || string.IsNullOrWhiteSpace(x.TimeZoneId) || string.IsNullOrWhiteSpace(x.Title) || string.IsNullOrWhiteSpace(x.Description)) throw Validation("proposedValue", "Next meeting requires an active calendar, valid future times, time zone, title, and description."); return x with { StartsUtc = x.StartsUtc.ToUniversalTime(), EndsUtc = x.EndsUtc.ToUniversalTime(), Title = x.Title.Trim(), Description = x.Description.Trim(), TimeZoneId = x.TimeZoneId.Trim(), Location = string.IsNullOrWhiteSpace(x.Location) ? null : x.Location.Trim() }; }
    private async Task ValidateBusinessValueAsync(SalesMeetingChangeField field, object value, Guid companyId, CancellationToken ct)
    {
        if (field == SalesMeetingChangeField.DealProbability && value is decimal p && p is < 0m or > 1m) throw Validation("proposedValue", "Deal probability must be between 0 and 1.");
        if ((field is SalesMeetingChangeField.DealValue or SalesMeetingChangeField.LeadEstimatedValue) && value is decimal m && m < 0m) throw Validation("proposedValue", "Sales values cannot be negative.");
        if (value is string s && s.Length > 500) throw Validation("proposedValue", "The proposed text cannot exceed 500 characters.");
        if (field == SalesMeetingChangeField.DealStage && value is Guid stageId && !await db.SalesPipelineStages.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == stageId && x.IsActive, ct)) throw Validation("proposedValue", "The proposed pipeline stage is not active in this company.");
    }

    private async Task<SalesMeetingChangeProposalDto> ToDtoAsync(SalesMeetingChangeProposal x, CompanyMembershipRole role, CancellationToken ct)
    {
        var d = policy.Evaluate(x.TargetType, x.Action, x.Field, x.ValueKind, role);
        var proposed = DeserializeValue(x);
        var before = await DisplayValueAsync(x.CompanyId, x.Field, x.BeforeValueJson, ct);
        var after = await DisplayValueAsync(x.CompanyId, x.Field, x.ProposedValueJson, ct);
        var executedBefore = x.ExecutedBeforeValueJson is null ? null : await DisplayValueAsync(x.CompanyId, x.Field, x.ExecutedBeforeValueJson, ct);
        var executedAfter = x.ExecutedAfterValueJson is null ? null : await DisplayValueAsync(x.CompanyId, x.Field, x.ExecutedAfterValueJson, ct);
        var label = await TargetLabelAsync(x, ct);
        return new(x.Id, x.SessionId, x.EvidenceArtifactId, x.TargetType.ToStorageValue(), x.TargetId, label, x.Action.ToStorageValue(), x.Field.ToStorageValue(), FieldLabel(x.Field), proposed, before, after, x.TargetVersion, x.Confidence, x.Rationale, JsonSerializer.Deserialize<string[]>(x.SourceIdsJson, Json) ?? [], x.EvidenceVersionHash, new(d.IsAllowed, d.ReasonCode, d.Explanation, d.RequiresApproval, d.IsSafeForBulkApproval, d.RiskClass.ToStorageValue(), d.PolicyVersion), x.Status.ToStorageValue(), x.ApprovalRequestId, x.ApprovalBindingHash, x.ReviewedByUserId, x.ReviewedUtc, x.ApprovedUtc, x.RejectedUtc, x.ExecutionAttemptCount, x.IdempotencyKey, executedBefore, executedAfter, x.ProviderReference, x.LastErrorCode, x.LastErrorSummary, x.ExecutedUtc, x.CreatedUtc, x.UpdatedUtc, x.ConcurrencyVersion);
    }
    private static SalesMeetingProposedValue DeserializeValue(SalesMeetingChangeProposal x)
    {
        return x.ValueKind switch { SalesMeetingChangeValueKind.String => new(x.ValueKind.ToStorageValue(), JsonSerializer.Deserialize<string>(x.ProposedValueJson, Json)), SalesMeetingChangeValueKind.Email => new(x.ValueKind.ToStorageValue(), JsonSerializer.Deserialize<string>(x.ProposedValueJson, Json)), SalesMeetingChangeValueKind.Decimal => new(x.ValueKind.ToStorageValue(), DecimalValue: JsonSerializer.Deserialize<decimal>(x.ProposedValueJson, Json)), SalesMeetingChangeValueKind.Guid => new(x.ValueKind.ToStorageValue(), GuidValue: JsonSerializer.Deserialize<Guid>(x.ProposedValueJson, Json)), SalesMeetingChangeValueKind.DateTime => new(x.ValueKind.ToStorageValue(), DateTimeValue: JsonSerializer.Deserialize<DateTime>(x.ProposedValueJson, Json)), SalesMeetingChangeValueKind.NextMeeting => new(x.ValueKind.ToStorageValue(), NextMeetingValue: JsonSerializer.Deserialize<SalesMeetingNextMeetingValue>(x.ProposedValueJson, Json)), _ => throw new InvalidOperationException("Unsupported stored proposal value.") };
    }
    private static string Binding(SalesMeetingChangeProposal x, string policyVersion) => Hash($"{x.CompanyId:N}|{x.Id:N}|{x.TargetType.ToStorageValue()}|{x.TargetId:N}|{x.Action.ToStorageValue()}|{x.Field.ToStorageValue()}|{x.ValueKind.ToStorageValue()}|{x.ProposedValueJson}|{x.TargetVersion}|{x.EvidenceVersionHash}|{policyVersion}");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private async Task<string> DisplayValueAsync(Guid companyId, SalesMeetingChangeField field, string json, CancellationToken ct)
    {
        if (field == SalesMeetingChangeField.DealStage && Guid.TryParse(Display(json), out var stageId))
            return await db.SalesPipelineStages.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == stageId).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? $"Deleted stage ({stageId:D})";
        if (field == SalesMeetingChangeField.DealProbability && decimal.TryParse(Display(json), NumberStyles.Number, CultureInfo.InvariantCulture, out var probability))
            return probability.ToString("P0", CultureInfo.CurrentCulture);
        if (field is SalesMeetingChangeField.DealValue or SalesMeetingChangeField.LeadEstimatedValue && decimal.TryParse(Display(json), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return amount.ToString("N2", CultureInfo.CurrentCulture);
        if (field == SalesMeetingChangeField.NextMeeting)
        {
            try
            {
                var meeting = JsonSerializer.Deserialize<SalesMeetingNextMeetingValue>(json, Json);
                if (meeting is not null) return $"{meeting.Title} — {meeting.StartsUtc.ToLocalTime():g} to {meeting.EndsUtc.ToLocalTime():g} ({meeting.TimeZoneId})";
            }
            catch (JsonException) { }
        }
        return Display(json);
    }

    private async Task<string> TargetLabelAsync(SalesMeetingChangeProposal x, CancellationToken ct)
    {
        string? label = x.TargetType switch
        {
            SalesMeetingChangeTargetType.Deal => await db.Deals.AsNoTracking().Where(y => y.CompanyId == x.CompanyId && y.Id == x.TargetId).Select(y => y.Title).SingleOrDefaultAsync(ct),
            SalesMeetingChangeTargetType.Lead => await db.Leads.AsNoTracking().Where(y => y.CompanyId == x.CompanyId && y.Id == x.TargetId).Select(y => y.Title).SingleOrDefaultAsync(ct),
            SalesMeetingChangeTargetType.Contact => await db.Contacts.AsNoTracking().Where(y => y.CompanyId == x.CompanyId && y.Id == x.TargetId).Select(y => y.FullName).SingleOrDefaultAsync(ct),
            SalesMeetingChangeTargetType.CustomerMinutes => await db.SalesMeetingMinutes.AsNoTracking().Where(y => y.CompanyId == x.CompanyId && y.Id == x.TargetId).Select(y => "Customer minutes v" + y.ArtifactVersion).SingleOrDefaultAsync(ct),
            SalesMeetingChangeTargetType.MeetingSession => "Next meeting",
            _ => null
        };
        return label ?? $"Unavailable {x.TargetType.ToStorageValue()} ({x.TargetId:D})";
    }

    private static string Display(string json) { try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.String ? d.RootElement.GetString() ?? "—" : d.RootElement.ToString(); } catch { return "—"; } }
    private static string FieldLabel(SalesMeetingChangeField f) => f switch { SalesMeetingChangeField.DealStage => "Deal stage", SalesMeetingChangeField.DealProbability => "Deal probability", SalesMeetingChangeField.DealValue => "Deal value", SalesMeetingChangeField.DealNextStep => "Deal next step", SalesMeetingChangeField.LeadEstimatedValue => "Lead estimated value", SalesMeetingChangeField.LeadNextAction => "Lead next action", SalesMeetingChangeField.ContactFullName => "Contact name", SalesMeetingChangeField.ContactEmail => "Contact email", SalesMeetingChangeField.ContactTitle => "Contact title", SalesMeetingChangeField.ContactPhone => "Contact phone", SalesMeetingChangeField.CustomerMinutesRecipient => "Customer minutes recipient", SalesMeetingChangeField.NextMeeting => "Next meeting", SalesMeetingChangeField.Discount => "Discount", SalesMeetingChangeField.PricePromise => "Price or promise", _ => "Contract term" };
    private async Task<SalesMeetingChangeProposal?> FindAsync(Guid companyId, Guid sessionId, Guid id, bool tracked, CancellationToken ct) { var q = db.SalesMeetingChangeProposals.Where(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == id); return await (tracked ? q : q.AsNoTracking()).SingleOrDefaultAsync(ct); }
    private static IReadOnlyList<string> NormalizeSources(IReadOnlyList<string>? x) => x?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Take(100).ToArray() ?? [];
    private static string NormalizeEmail(string? value) { var x = value?.Trim().ToLowerInvariant(); if (string.IsNullOrWhiteSpace(x) || !x.Contains('@') || x.Length > 256) throw Validation("proposedValue", "A valid recipient email is required."); return x; }
    private static T Parse<T>(Func<T> parse, string field) { try { return parse(); } catch (ArgumentOutOfRangeException e) { throw Validation(field, e.Message); } }
    private static void EnsureExpected(SalesMeetingChangeProposal x, long expected) { if (x.ConcurrencyVersion != expected) throw Conflict(SalesMeetingChangeProposalProblemCodes.Conflict, "The proposal changed after it was opened. Refresh and review it again."); }
    private async Task SaveAsync(CancellationToken ct) { try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict(SalesMeetingChangeProposalProblemCodes.Conflict, "The proposal changed concurrently. Refresh and try again."); } }
    private void AddAudit(Guid companyId, Guid userId, string action, Guid id, string outcome, string rationale, string? correlation, SalesMeetingChangeProposal x)
    {
        SalesMeetingChangeTelemetry.RecordTransition(action, outcome, x.Status.ToStorageValue());
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.User, userId, action, "sales_meeting_change_proposal", id.ToString("D"), outcome, rationale, ["sales meeting evidence", "sales change policy"], new Dictionary<string, string?> { ["sessionId"] = x.SessionId.ToString("D"), ["targetType"] = x.TargetType.ToStorageValue(), ["targetId"] = x.TargetId.ToString("D"), ["field"] = x.Field.ToStorageValue(), ["policyVersion"] = x.PolicyVersion, ["evidenceVersionHash"] = x.EvidenceVersionHash, ["approvalRequestId"] = x.ApprovalRequestId?.ToString("D"), ["status"] = x.Status.ToStorageValue(), ["beforeValue"] = x.ExecutedBeforeValueJson ?? x.BeforeValueJson, ["afterValue"] = x.ExecutedAfterValueJson ?? x.ProposedValueJson }, correlation));
    }
    private DateTime Now() => time.GetUtcNow().UtcDateTime;
    private static SalesMeetingChangeProposalValidationException Validation(string field, string message) => new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] });
    private static SalesMeetingChangeProposalConflictException Conflict(string code, string message) => new(code, message);
    private sealed record ValidatedValue(SalesMeetingChangeValueKind Kind, object Value);
    private sealed record Captured(string Json, string Version, string Label);
}
