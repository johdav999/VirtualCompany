using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesMeetingClosingService(
    VirtualCompanyDbContext db,
    IAgentReasoningGateway reasoning,
    IAgentEffectiveAuthorityResolver authorityResolver,
    TimeProvider timeProvider,
    ILogger<SalesMeetingClosingService> logger) : ISalesMeetingClosingService
{
    private const string PromptVersion = "sales-meeting-closing-v1";

    public async Task<SalesMeetingClosingSnapshotDto?> PrepareAsync(Guid companyId, Guid userId, Guid sessionId,
        PrepareSalesMeetingClosingRequest request, string? correlationId, CancellationToken cancellationToken)
    {
        EnsureIds(companyId, userId, sessionId, request.GenerationRequestId, request.AgentId);
        var membership = await EnsureMemberAsync(companyId, userId, false, cancellationToken);
        var duplicate = await db.SalesMeetingMinutes.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId &&
                                       x.GenerationRequestId == request.GenerationRequestId, cancellationToken);
        if (duplicate is not null)
        {
            var duplicateInternal = await db.SalesMeetingInternalIntelligence.AsNoTracking().Include(x => x.Items)
                .SingleAsync(x => x.CompanyId == companyId && x.MinutesId == duplicate.Id, cancellationToken);
            return new(ToMinutesDto(duplicate), ToInternalDto(duplicateInternal));
        }

        var session = await db.SalesMeetingSessions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        if (session.RetentionUntilUtc <= timeProvider.GetUtcNow().UtcDateTime) throw Conflict("The meeting retention period has expired.");
        if (await db.SalesBrowserRooms.AnyAsync(x => x.CompanyId == companyId && x.MeetingSessionId == sessionId &&
            x.State != SalesBrowserRoomStates.Ended && x.State != SalesBrowserRoomStates.Ending, cancellationToken))
            throw Conflict("End the browser call before preparing or completing its closing review.");
        EnsureClosingCheckpoint(session, request.ExpectedSessionVersion, request.ExpectedCaptureVersion, request.CaptureCheckpointId);
        if (session.Status != SalesMeetingSessionStatus.Closing)
            throw Conflict("The meeting must be in closing before its closing snapshot is prepared.");
        var agent = await db.Agents.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.AgentId, cancellationToken)
            ?? throw new KeyNotFoundException("The Sales agent is unavailable in this company.");
        if (!agent.Department.Equals("Sales", StringComparison.OrdinalIgnoreCase)) throw Conflict("Closing summaries require a Sales agent.");
        var authority = await authorityResolver.ResolveAsync(companyId, agent.Id, cancellationToken);
        RequireAuthority(authority, SalesMeetingClosingToolNames.ReadEvidence, ToolActionType.Read);
        RequireAuthority(authority, SalesMeetingClosingToolNames.GenerateSummary, ToolActionType.Recommend);

        var browserRoom = await db.SalesBrowserRooms.AnyAsync(x => x.CompanyId == companyId && x.MeetingSessionId == sessionId, cancellationToken);
        var closingPrompt = browserRoom ? "sales-browser-room-closing-v1" : PromptVersion;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var latest = await db.SalesMeetingMinutes.Include(x => x.Items)
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId)
            .OrderByDescending(x => x.ArtifactVersion).FirstOrDefaultAsync(cancellationToken);
        var latestInternal = latest is null ? null : await db.SalesMeetingInternalIntelligence.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MinutesId == latest.Id, cancellationToken);
        if (latest is not null && latest.Status != SalesMeetingClosingArtifactStatus.Approved) latest.Supersede(latest.ConcurrencyVersion, now);
        if (latestInternal is not null && latestInternal.Status != SalesMeetingClosingArtifactStatus.Approved) latestInternal.Supersede(latestInternal.ConcurrencyVersion, now);
        var version = (latest?.ArtifactVersion ?? 0) + 1;

        var browserSeeds = await BuildBrowserSeedsAsync(companyId, userId, session, agent.Id, authority, request.GenerationRequestId, cancellationToken);
        var customerSeeds = await BuildCustomerSeedsAsync(companyId, sessionId, request, cancellationToken);
        customerSeeds.AddRange(browserSeeds);
        var internalSeeds = await BuildInternalSeedsAsync(companyId, sessionId, request, cancellationToken);
        var customerGeneration = await PolishAsync(companyId, userId, agent.Id, authority, customerSeeds.Select(ToSource).ToArray(),
            "Rewrite only the supplied customer-safe meeting facts as concise Minutes of Meeting statements. Return claims whose type is decision, action, outstanding_question, proposed_next_meeting, or approved_product_statement and cite the supplied source ID for every claim. Do not add objections, buying signals, competitive strategy, confidence, private notes, deal recommendations, promises, pricing, or terms.", cancellationToken, browserRoom ? $"browser-closing:{sessionId:N}" : null);
        var internalGeneration = await PolishAsync(companyId, userId, agent.Id, authority, internalSeeds.Select(ToSource).ToArray(),
            "Rewrite the supplied internal sales evidence as concise internal intelligence. Return claims whose type is objection, buying_signal, competitive_information, risk, recommendation, or proposed_deal_change and cite supplied source IDs. Do not claim that proposed sales-record changes were executed.", cancellationToken, browserRoom ? $"browser-closing:{sessionId:N}" : null);
        ApplyCustomerWording(customerSeeds, customerGeneration);
        ApplyInternalWording(internalSeeds, internalGeneration);

        var minutes = new SalesMeetingMinutes(Guid.NewGuid(), companyId, sessionId, request.GenerationRequestId,
            latest?.Id, version, session.CaptureVersion, now, agent.Id, customerGeneration?.RunId,
            customerGeneration?.ResultVersion ?? "deterministic-v1", closingPrompt, session.RetentionUntilUtc, userId, now);
        var intelligence = new SalesMeetingInternalIntelligence(Guid.NewGuid(), companyId, sessionId, minutes.Id,
            version, session.CaptureVersion, now, agent.Id, internalGeneration?.RunId,
            internalGeneration?.ResultVersion ?? "deterministic-v1", closingPrompt, session.RetentionUntilUtc, userId, now);
        AddCustomerItems(minutes, customerSeeds, now); AddInternalItems(intelligence, internalSeeds, now);
        db.SalesMeetingMinutes.Add(minutes); db.SalesMeetingInternalIntelligence.Add(intelligence);
        AddAudit(companyId, userId, AuditEventActions.SalesMeetingClosingPrepared, sessionId,
            "Customer minutes and internal intelligence were persisted as separate closing artifacts.", correlationId,
            new Dictionary<string, string?> { ["minutesId"] = minutes.Id.ToString("D"), ["internalIntelligenceId"] = intelligence.Id.ToString("D"), ["artifactVersion"] = version.ToString(), ["captureVersion"] = session.CaptureVersion.ToString() });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw Conflict("The closing generation request or artifact version already exists. Refresh the closing summary."); }
        return new(ToMinutesDto(minutes), ToInternalDto(intelligence));
    }

    public async Task<IReadOnlyList<SalesMeetingMinutesDto>> ListMinutesAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, false, cancellationToken);
        return (await db.SalesMeetingMinutes.AsNoTracking().Include(x => x.Items).Where(x => x.CompanyId == companyId && x.SessionId == sessionId)
            .OrderByDescending(x => x.ArtifactVersion).ToListAsync(cancellationToken)).Select(ToMinutesDto).ToArray();
    }

    public async Task<SalesMeetingMinutesDto?> GetMinutesAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, false, cancellationToken);
        var value = await db.SalesMeetingMinutes.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == minutesId, cancellationToken);
        return value is null ? null : ToMinutesDto(value);
    }

    public async Task<SalesMeetingCustomerMinutesPreviewDto?> GetCustomerPreviewAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, false, cancellationToken);
        var value = await db.SalesMeetingMinutes.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == minutesId, cancellationToken);
        return value is null ? null : new(value.Id, value.SessionId, value.ArtifactVersion, value.Status.ToStorageValue(),
            value.ApprovedUtc, value.Items.OrderBy(i => i.Order).Select(i => new SalesMeetingCustomerMinutesPreviewItemDto(
                i.Order, i.ItemType.ToStorageValue(), i.Content, i.OwnerLabel, i.DueUtc)).ToArray());
    }

    public async Task<SalesMeetingInternalIntelligenceDto?> GetInternalAsync(Guid companyId, Guid userId, Guid sessionId, Guid intelligenceId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, true, cancellationToken);
        var value = await db.SalesMeetingInternalIntelligence.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == intelligenceId, cancellationToken);
        return value is null ? null : ToInternalDto(value);
    }

    public async Task<SalesMeetingMinutesDto?> EditMinutesAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId,
        EditSalesMeetingMinutesRequest request, string? correlationId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, false, cancellationToken); ValidateCustomerEdits(request.Items);
        var value = await db.SalesMeetingMinutes.Include(x => x.Items).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == minutesId, cancellationToken);
        if (value is null) return null;
        try { value.EnsureEditable(request.ExpectedVersion); } catch (InvalidOperationException e) { throw Conflict(e.Message); }
        await ValidateCustomerSourcesAsync(companyId, sessionId, request.Items, cancellationToken);
        db.SalesMeetingMinutesItems.RemoveRange(value.Items); value.Items.Clear(); var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var item in request.Items.OrderBy(x => x.Order)) value.Items.Add(new(Guid.NewGuid(), companyId, value.Id, item.Order,
            SalesMeetingClosingEnumValues.ParseMinutesItemType(item.Type), item.Content, item.OwnerLabel, item.DueUtc,
            item.SourceId, item.SourceArtifactId, item.RequiresReview, now));
        value.Touch(now); AddAudit(companyId, userId, AuditEventActions.SalesMeetingMinutesEdited, value.Id, "Draft customer minutes were edited without changing meeting evidence.", correlationId);
        await SaveConflictAsync(cancellationToken); return ToMinutesDto(value);
    }

    public async Task<SalesMeetingInternalIntelligenceDto?> EditInternalAsync(Guid companyId, Guid userId, Guid sessionId, Guid intelligenceId,
        EditSalesMeetingInternalIntelligenceRequest request, string? correlationId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, true, cancellationToken); ValidateInternalEdits(request.Items);
        var value = await db.SalesMeetingInternalIntelligence.Include(x => x.Items).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == intelligenceId, cancellationToken);
        if (value is null) return null;
        try { value.EnsureEditable(request.ExpectedVersion); } catch (InvalidOperationException e) { throw Conflict(e.Message); }
        await ValidateInternalSourcesAsync(companyId, sessionId, request.Items, cancellationToken);
        db.SalesMeetingInternalIntelligenceItems.RemoveRange(value.Items); value.Items.Clear(); var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var item in request.Items.OrderBy(x => x.Order)) value.Items.Add(new(Guid.NewGuid(), companyId, value.Id, item.Order,
            SalesMeetingClosingEnumValues.ParseInternalItemType(item.Type), item.Content, item.Confidence, item.SourceId,
            item.SourceArtifactId, item.RequiresReview, now));
        value.Touch(now); AddAudit(companyId, userId, AuditEventActions.SalesMeetingInternalIntelligenceEdited, value.Id, "Draft internal meeting intelligence was edited independently of customer minutes.", correlationId);
        await SaveConflictAsync(cancellationToken); return ToInternalDto(value);
    }

    public Task<SalesMeetingMinutesDto?> SubmitMinutesAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId, long expectedVersion, string? correlationId, CancellationToken cancellationToken) =>
        ReviewMinutesAsync(companyId, userId, sessionId, minutesId, expectedVersion, false, correlationId, cancellationToken);
    public Task<SalesMeetingMinutesDto?> ApproveMinutesAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId, long expectedVersion, string? correlationId, CancellationToken cancellationToken) =>
        ReviewMinutesAsync(companyId, userId, sessionId, minutesId, expectedVersion, true, correlationId, cancellationToken);
    public Task<SalesMeetingInternalIntelligenceDto?> SubmitInternalAsync(Guid companyId, Guid userId, Guid sessionId, Guid intelligenceId, long expectedVersion, string? correlationId, CancellationToken cancellationToken) =>
        ReviewInternalAsync(companyId, userId, sessionId, intelligenceId, expectedVersion, false, correlationId, cancellationToken);
    public Task<SalesMeetingInternalIntelligenceDto?> ApproveInternalAsync(Guid companyId, Guid userId, Guid sessionId, Guid intelligenceId, long expectedVersion, string? correlationId, CancellationToken cancellationToken) =>
        ReviewInternalAsync(companyId, userId, sessionId, intelligenceId, expectedVersion, true, correlationId, cancellationToken);

    public async Task<SalesMeetingSessionResponse?> CompleteAsync(Guid companyId, Guid userId, Guid sessionId,
        CompleteSalesMeetingClosingRequest request, string? correlationId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, false, cancellationToken);
        var session = await db.SalesMeetingSessions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        if (session.RetentionUntilUtc <= timeProvider.GetUtcNow().UtcDateTime) throw Conflict("The meeting retention period has expired.");
        if (await db.SalesBrowserRooms.AnyAsync(x => x.CompanyId == companyId && x.MeetingSessionId == sessionId &&
            x.State != SalesBrowserRoomStates.Ended && x.State != SalesBrowserRoomStates.Ending, cancellationToken))
            throw Conflict("End the browser call before preparing or completing its closing review.");
        if (session.Status != SalesMeetingSessionStatus.Completed)
            EnsureClosingCheckpoint(session, request.ExpectedSessionVersion, request.ExpectedCaptureVersion, request.CaptureCheckpointId);
        var minutes = await db.SalesMeetingMinutes.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == request.MinutesId && x.ArtifactVersion == request.MinutesArtifactVersion, cancellationToken);
        var intelligence = await db.SalesMeetingInternalIntelligence.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == request.InternalIntelligenceId && x.MinutesId == request.MinutesId && x.ArtifactVersion == request.InternalArtifactVersion, cancellationToken);
        if (minutes is null || intelligence is null || minutes.EvidenceCaptureVersion != session.CaptureVersion || intelligence.EvidenceCaptureVersion != session.CaptureVersion)
            throw new SalesMeetingClosingConflictException(SalesMeetingClosingProblemCodes.CaptureNotFlushed, "Persist a closing snapshot from the current capture version before completing the meeting.");
        if (session.Status == SalesMeetingSessionStatus.Completed) return SalesMeetingSessionService.ToResponse(session);
        try { session.TransitionTo(SalesMeetingSessionStatus.Completed, null, null, null, null, userId, timeProvider.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException e) { throw Conflict(e.Message); }
        AddAudit(companyId, userId, AuditEventActions.SalesMeetingClosingCompleted, sessionId, "The meeting completed after its current capture checkpoint and separated closing artifacts were persisted.", correlationId,
            new Dictionary<string, string?> { ["captureVersion"] = session.CaptureVersion.ToString(), ["minutesId"] = minutes.Id.ToString("D"), ["internalIntelligenceId"] = intelligence.Id.ToString("D") });
        await SaveConflictAsync(cancellationToken); return SalesMeetingSessionService.ToResponse(session);
    }

    private async Task<SalesMeetingMinutesDto?> ReviewMinutesAsync(Guid companyId, Guid userId, Guid sessionId, Guid id, long expectedVersion, bool approve, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, userId, false, ct); var value = await db.SalesMeetingMinutes.Include(x => x.Items).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == id, ct); if (value is null) return null;
        try { if (approve) value.Approve(userId, expectedVersion, value.Items.Any(x => x.ItemType == SalesMeetingMinutesItemType.ApprovedProductStatement && (x.RequiresReview || string.IsNullOrWhiteSpace(x.SourceId))), timeProvider.GetUtcNow().UtcDateTime); else value.SubmitForReview(userId, expectedVersion, timeProvider.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException e) { throw Conflict(e.Message); }
        AddAudit(companyId, userId, approve ? AuditEventActions.SalesMeetingMinutesApproved : AuditEventActions.SalesMeetingMinutesSubmitted, id, approve ? "Customer minutes were approved." : "Customer minutes were submitted for review.", correlationId);
        await SaveConflictAsync(ct); return ToMinutesDto(value);
    }

    private async Task<SalesMeetingInternalIntelligenceDto?> ReviewInternalAsync(Guid companyId, Guid userId, Guid sessionId, Guid id, long expectedVersion, bool approve, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, userId, true, ct); var value = await db.SalesMeetingInternalIntelligence.Include(x => x.Items).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == id, ct); if (value is null) return null;
        try { if (approve) value.Approve(userId, expectedVersion, timeProvider.GetUtcNow().UtcDateTime); else value.SubmitForReview(userId, expectedVersion, timeProvider.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException e) { throw Conflict(e.Message); }
        AddAudit(companyId, userId, approve ? AuditEventActions.SalesMeetingInternalIntelligenceApproved : AuditEventActions.SalesMeetingInternalIntelligenceSubmitted, id, approve ? "Internal meeting intelligence was approved." : "Internal meeting intelligence was submitted for review.", correlationId);
        await SaveConflictAsync(ct); return ToInternalDto(value);
    }

    private async Task<List<CustomerSeed>> BuildCustomerSeedsAsync(Guid companyId, Guid sessionId, PrepareSalesMeetingClosingRequest request, CancellationToken ct)
    {
        var result = new List<CustomerSeed>();
        var commitments = await db.SalesMeetingObservations.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Category == SalesMeetingObservationCategory.Commitment && x.ReviewState == SalesMeetingReviewState.Reviewed).OrderBy(x => x.Sequence).ToListAsync(ct);
        result.AddRange(commitments.Select(x => new CustomerSeed(SalesMeetingMinutesItemType.Decision, x.Content, null, null, $"observation:{x.Id:N}", null, false)));
        var actions = await db.SalesMeetingActionItems.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Status != SalesMeetingActionItemStatus.Dismissed && x.ReviewState != SalesMeetingReviewState.Rejected).OrderBy(x => x.Sequence).ToListAsync(ct);
        result.AddRange(actions.Select(x => new CustomerSeed(SalesMeetingMinutesItemType.Action, x.Title, x.OwnerLabel, x.DueUtc, $"action-item:{x.Id:N}", null, x.ReviewState != SalesMeetingReviewState.Reviewed)));
        var questions = await db.SalesMeetingQuestions.AsNoTracking().Include(x => x.Evidence).Where(x => x.CompanyId == companyId && x.SessionId == sessionId).OrderBy(x => x.Sequence).ToListAsync(ct);
        result.AddRange(questions.Where(x => x.FollowUpRequired || x.Status == SalesMeetingQuestionStatus.Unverified).Select(x => new CustomerSeed(SalesMeetingMinutesItemType.OutstandingQuestion, x.QuestionText, null, null, $"question:{x.Id:N}", null, false)));
        result.AddRange(questions.Where(x => x.Status == SalesMeetingQuestionStatus.Completed && x.Visibility == SalesMeetingAnswerVisibility.ApprovedForStage && x.Evidence.Count > 0 && x.AnswerText != null).Select(x => new CustomerSeed(SalesMeetingMinutesItemType.ApprovedProductStatement, x.AnswerText!, null, null, $"question:{x.Id:N}", null, false)));
        if (request.ProposedNextMeetingUtc.HasValue) result.Add(new(SalesMeetingMinutesItemType.ProposedNextMeeting, $"Proposed next meeting: {request.ProposedNextMeetingUtc.Value.ToUniversalTime():yyyy-MM-dd HH:mm} UTC.", null, request.ProposedNextMeetingUtc, $"host-proposal:{request.GenerationRequestId:N}", null, true));
        return result;
    }

    private async Task<List<InternalSeed>> BuildInternalSeedsAsync(Guid companyId, Guid sessionId, PrepareSalesMeetingClosingRequest request, CancellationToken ct)
    {
        var observations = await db.SalesMeetingObservations.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId && x.ReviewState != SalesMeetingReviewState.Rejected).OrderBy(x => x.Sequence).ToListAsync(ct);
        var result = new List<InternalSeed>();
        foreach (var observation in observations)
        {
            var type = MapInternal(observation.Category);
            if (type.HasValue) result.Add(new(type.Value, observation.Content, observation.Confidence,
                $"observation:{observation.Id:N}", null, observation.ReviewState != SalesMeetingReviewState.Reviewed));
        }
        foreach (var change in request.ProposedDealChanges?.Where(x => !string.IsNullOrWhiteSpace(x)).Take(50) ?? [])
            result.Add(new(SalesMeetingInternalIntelligenceItemType.ProposedDealChange, change.Trim(), null, $"host-proposal:{request.GenerationRequestId:N}", null, true));
        return result;
    }

    private async Task<AgentReasoningResult?> PolishAsync(Guid companyId, Guid userId, Guid agentId, AgentEffectiveAuthorityDto authority, IReadOnlyList<AgentAiSource> sources, string instruction, CancellationToken ct, string? correlation = null)
    {
        if (sources.Count == 0) return null;
        try { return await reasoning.ReasonAsync(new AgentReasoningRequest(companyId, agentId, AgentCapabilityIds.SalesMeetingClosingSummary, "1.0.0", PromptVersion, "1.0.0", instruction, sources, ["recommend"], [SalesMeetingClosingToolNames.ReadEvidence, SalesMeetingClosingToolNames.GenerateSummary], userId, CorrelationId: correlation, IncludeClaims: true, EffectiveAuthorityVersion: authority.AuthorityVersion, EffectiveAuthorityHash: authority.AuthorityHash), ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { logger.LogWarning(e, "Meeting closing wording generation failed safely for company {CompanyId}.", companyId); return null; }
    }

    private static void ApplyCustomerWording(List<CustomerSeed> seeds, AgentReasoningResult? result)
    {
        if (result?.Status != AgentAiRunStatuses.Completed) return;
        var allowed = seeds.Select(x => x.SourceId).ToHashSet(StringComparer.Ordinal);
        foreach (var claim in result.Claims.Where(x => x.SourceIds.Count == 1 && x.SourceIds.All(allowed.Contains) &&
                                                        x.Confidence >= .5m && !string.IsNullOrWhiteSpace(x.Text)))
        {
            SalesMeetingMinutesItemType type; try { type = SalesMeetingClosingEnumValues.ParseMinutesItemType(claim.Type); } catch { continue; }
            foreach (var sourceId in claim.SourceIds)
            {
                var index = seeds.FindIndex(x => x.SourceId == sourceId && x.Type == type); if (index < 0) continue;
                seeds[index] = seeds[index] with { Content = claim.Text.Trim(), RequiresReview = seeds[index].RequiresReview || claim.Confidence < .75m }; break;
            }
        }
    }

    private static void ApplyInternalWording(List<InternalSeed> seeds, AgentReasoningResult? result)
    {
        if (result?.Status != AgentAiRunStatuses.Completed) return;
        var allowed = seeds.Select(x => x.SourceId).ToHashSet(StringComparer.Ordinal);
        foreach (var claim in result.Claims.Where(x => x.SourceIds.Count == 1 && x.SourceIds.All(allowed.Contains) &&
                                                        x.Confidence >= .5m && !string.IsNullOrWhiteSpace(x.Text)))
        {
            SalesMeetingInternalIntelligenceItemType type; try { type = SalesMeetingClosingEnumValues.ParseInternalItemType(claim.Type); } catch { continue; }
            foreach (var sourceId in claim.SourceIds)
            {
                var index = seeds.FindIndex(x => x.SourceId == sourceId && x.Type == type); if (index < 0) continue;
                seeds[index] = seeds[index] with { Content = claim.Text.Trim(), Confidence = claim.Confidence, RequiresReview = seeds[index].RequiresReview || claim.Confidence < .75m }; break;
            }
        }
    }

    private static void AddCustomerItems(SalesMeetingMinutes minutes, IEnumerable<CustomerSeed> seeds, DateTime now) { var order = 0; foreach (var x in seeds) minutes.Items.Add(new(Guid.NewGuid(), minutes.CompanyId, minutes.Id, order++, x.Type, x.Content, x.OwnerLabel, x.DueUtc, x.SourceId, x.SourceArtifactId, x.RequiresReview, now)); }
    private static void AddInternalItems(SalesMeetingInternalIntelligence value, IEnumerable<InternalSeed> seeds, DateTime now) { var order = 0; foreach (var x in seeds) value.Items.Add(new(Guid.NewGuid(), value.CompanyId, value.Id, order++, x.Type, x.Content, x.Confidence, x.SourceId, x.SourceArtifactId, x.RequiresReview, now)); }
    private static AgentAiSource ToSource(CustomerSeed x) => new(x.SourceId, x.Type.ToStorageValue(), x.Type.ToStorageValue(), x.Content);
    private static AgentAiSource ToSource(InternalSeed x) => new(x.SourceId, x.Type.ToStorageValue(), x.Type.ToStorageValue(), x.Content);
    private static SalesMeetingInternalIntelligenceItemType? MapInternal(SalesMeetingObservationCategory value) => value switch { SalesMeetingObservationCategory.Objection => SalesMeetingInternalIntelligenceItemType.Objection, SalesMeetingObservationCategory.BuyingSignal => SalesMeetingInternalIntelligenceItemType.BuyingSignal, SalesMeetingObservationCategory.CompetitiveReference => SalesMeetingInternalIntelligenceItemType.CompetitiveInformation, SalesMeetingObservationCategory.PainPoint => SalesMeetingInternalIntelligenceItemType.Risk, SalesMeetingObservationCategory.InternalSalesIntelligence => SalesMeetingInternalIntelligenceItemType.Recommendation, SalesMeetingObservationCategory.ProductInterest => SalesMeetingInternalIntelligenceItemType.Recommendation, SalesMeetingObservationCategory.CustomerNeed => SalesMeetingInternalIntelligenceItemType.Risk, _ => null };

    private async Task ValidateCustomerSourcesAsync(Guid companyId, Guid sessionId, IReadOnlyList<EditSalesMeetingMinutesItemRequest> items, CancellationToken ct)
    {
        var artifactIds = items.Where(x => x.SourceArtifactId.HasValue).Select(x => x.SourceArtifactId!.Value).Distinct().ToArray();
        if (artifactIds.Length > 0 && await db.SalesMeetingArtifacts.AsNoTracking().CountAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && artifactIds.Contains(x.Id), ct) != artifactIds.Length) throw Conflict("A customer-minutes source artifact is not available in this meeting.");
        var productQuestionIds = items.Where(x => SalesMeetingClosingEnumValues.ParseMinutesItemType(x.Type) == SalesMeetingMinutesItemType.ApprovedProductStatement && !x.RequiresReview).Select(x => ParseSourceGuid(x.SourceId, "question:")).ToArray();
        if (productQuestionIds.Any(x => !x.HasValue)) throw Conflict("Approved product statements require an approved sourced meeting answer.");
        var ids = productQuestionIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        if (ids.Length > 0 && await db.SalesMeetingQuestions.AsNoTracking().CountAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && ids.Contains(x.Id) && x.Status == SalesMeetingQuestionStatus.Completed && x.Visibility == SalesMeetingAnswerVisibility.ApprovedForStage, ct) != ids.Length) throw Conflict("Approved product statements require an approved sourced meeting answer.");
    }
    private async Task ValidateInternalSourcesAsync(Guid companyId, Guid sessionId, IReadOnlyList<EditSalesMeetingInternalItemRequest> items, CancellationToken ct)
    {
        var ids = items.Where(x => x.SourceArtifactId.HasValue).Select(x => x.SourceArtifactId!.Value).Distinct().ToArray();
        if (ids.Length > 0 && await db.SalesMeetingArtifacts.AsNoTracking().CountAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && ids.Contains(x.Id), ct) != ids.Length) throw Conflict("An internal-intelligence source artifact is not available in this meeting.");
    }
    private static Guid? ParseSourceGuid(string value, string prefix) => value.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(value[prefix.Length..], "N", out var id) ? id : null;
    private static void ValidateCustomerEdits(IReadOnlyList<EditSalesMeetingMinutesItemRequest> items) { if (items.Count > 200 || items.Select(x => x.Order).Distinct().Count() != items.Count) throw Validation(nameof(items), "Customer minutes require unique item order values and at most 200 items."); foreach (var x in items) { _ = SalesMeetingClosingEnumValues.ParseMinutesItemType(x.Type); if (string.IsNullOrWhiteSpace(x.Content) || string.IsNullOrWhiteSpace(x.SourceId)) throw Validation(nameof(items), "Every customer-minutes item requires content and a source ID."); } }
    private static void ValidateInternalEdits(IReadOnlyList<EditSalesMeetingInternalItemRequest> items) { if (items.Count > 200 || items.Select(x => x.Order).Distinct().Count() != items.Count) throw Validation(nameof(items), "Internal intelligence requires unique item order values and at most 200 items."); foreach (var x in items) { _ = SalesMeetingClosingEnumValues.ParseInternalItemType(x.Type); if (string.IsNullOrWhiteSpace(x.Content) || string.IsNullOrWhiteSpace(x.SourceId)) throw Validation(nameof(items), "Every internal-intelligence item requires content and a source ID."); } }
    private static void EnsureClosingCheckpoint(SalesMeetingSession session, long sessionVersion, long captureVersion, Guid? checkpoint) { if (session.ConcurrencyVersion != sessionVersion) throw Conflict("The meeting session changed after closing was opened."); if (session.CaptureVersion != captureVersion || session.LastCaptureBatchId != checkpoint) throw new SalesMeetingClosingConflictException(SalesMeetingClosingProblemCodes.CaptureNotFlushed, "Flush pending meeting capture and use the authoritative capture checkpoint before closing."); }
    private async Task<CompanyMembership> EnsureMemberAsync(Guid companyId, Guid userId, bool internalArtifact, CancellationToken ct) { EnsureIds(companyId, userId); var membership = await db.CompanyMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct) ?? throw new UnauthorizedAccessException("An active company membership is required."); if (internalArtifact && membership.Role == CompanyMembershipRole.Accountant) throw new SalesMeetingClosingConflictException(SalesMeetingClosingProblemCodes.InternalAccessDenied, "This membership cannot access internal sales intelligence."); return membership; }
    private static void RequireAuthority(AgentEffectiveAuthorityDto authority, string tool, ToolActionType action) { var decision = authority.Find(tool, action, "sales"); if (decision is null || !decision.IsUsable) throw Conflict($"Alex is not authorized to use {tool} for this meeting."); }
    private async Task SaveConflictAsync(CancellationToken ct) { try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("The closing artifact changed after it was opened. Refresh and try again."); } catch (DbUpdateException) { throw Conflict("The closing artifact conflicts with an existing version. Refresh and try again."); } }
    private void AddAudit(Guid companyId, Guid userId, string action, Guid subjectId, string rationale, string? correlationId, IReadOnlyDictionary<string, string?>? metadata = null) => db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.User, userId, action, "sales_meeting_closing", subjectId.ToString("D"), AuditEventOutcomes.Succeeded, rationale, ["meeting closing", "separated artifacts"], metadata ?? new Dictionary<string, string?>(), Normalize(correlationId), timeProvider.GetUtcNow().UtcDateTime));
    internal static SalesMeetingMinutesDto ToMinutesDto(SalesMeetingMinutes x) => new(x.Id, x.SessionId, x.PreviousVersionId, x.ArtifactVersion, x.Status.ToStorageValue(), x.EvidenceCaptureVersion, x.EvidenceCutoffUtc, x.GeneratorAgentId, x.AiRunId, x.GeneratorVersion, x.PromptVersion, x.RetentionUntilUtc, x.ReviewedUtc, x.ApprovedUtc, x.UpdatedUtc, x.ConcurrencyVersion, x.IsEvidenceStale, x.EvidenceStaleUtc, x.EvidenceStaleReason, x.Items.OrderBy(i => i.Order).Select(i => new SalesMeetingMinutesItemDto(i.Id, i.Order, i.ItemType.ToStorageValue(), i.Content, i.OwnerLabel, i.DueUtc, i.SourceId, i.SourceArtifactId, i.RequiresReview)).ToArray());
    internal static SalesMeetingInternalIntelligenceDto ToInternalDto(SalesMeetingInternalIntelligence x) => new(x.Id, x.SessionId, x.MinutesId, x.ArtifactVersion, x.Status.ToStorageValue(), x.EvidenceCaptureVersion, x.EvidenceCutoffUtc, x.GeneratorAgentId, x.AiRunId, x.GeneratorVersion, x.PromptVersion, x.RetentionUntilUtc, x.ReviewedUtc, x.ApprovedUtc, x.UpdatedUtc, x.ConcurrencyVersion, x.IsEvidenceStale, x.EvidenceStaleUtc, x.EvidenceStaleReason, x.Items.OrderBy(i => i.Order).Select(i => new SalesMeetingInternalIntelligenceItemDto(i.Id, i.Order, i.ItemType.ToStorageValue(), i.Content, i.Confidence, i.SourceId, i.SourceArtifactId, i.RequiresReview)).ToArray());
    private static SalesMeetingClosingValidationException Validation(string field, string message) => new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] });
    private static SalesMeetingClosingConflictException Conflict(string message) => new(SalesMeetingClosingProblemCodes.Conflict, message);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 128)];
    private static void EnsureIds(params Guid[] ids) { if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Required identifiers cannot be empty."); }
    private sealed record CustomerSeed(SalesMeetingMinutesItemType Type, string Content, string? OwnerLabel, DateTime? DueUtc, string SourceId, Guid? SourceArtifactId, bool RequiresReview);
    private sealed record InternalSeed(SalesMeetingInternalIntelligenceItemType Type, string Content, decimal? Confidence, string SourceId, Guid? SourceArtifactId, bool RequiresReview);
}
