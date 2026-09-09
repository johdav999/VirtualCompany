using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPresentationRuntimeService(
    VirtualCompanyDbContext db,
    TimeProvider timeProvider,
    IEnumerable<ISalesPresentationEventPublisher> publishers,
    ILogger<SalesPresentationRuntimeService> logger,
    ISalesPresentationNarrationPreemption? preemption = null) : ISalesPresentationRuntimeService
{
    private static readonly Meter Meter = new("VirtualCompany.Sales.Presentation", "1.0.0");
    private static readonly Counter<long> Commands = Meter.CreateCounter<long>("sales.presentation.commands");
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("sales.presentation.rejections");
    private static readonly Counter<long> Reconnects = Meter.CreateCounter<long>("sales.presentation.reconnects");
    private static readonly Counter<long> Conflicts = Meter.CreateCounter<long>("sales.presentation.conflicts");
    private static readonly Histogram<double> CommandLatency = Meter.CreateHistogram<double>("sales.presentation.command.latency", "ms");

    public async Task<SalesPresentationAuthoritativeSnapshotDto?> GetCurrentAsync(
        Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        ValidateIds(companyId, userId, sessionId);
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        return await LoadSnapshotAsync(companyId, sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<SalesPresentationSearchResultDto>> SearchAsync(
        Guid companyId, Guid userId, Guid sessionId, string query, CancellationToken cancellationToken)
    {
        ValidateIds(companyId, userId, sessionId);
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        var term = query?.Trim();
        if (string.IsNullOrWhiteSpace(term)) return [];
        if (term.Length > 200) throw new ArgumentOutOfRangeException(nameof(query), "Search text must be 200 characters or fewer.");

        var deck = await ActiveDeckAsync(companyId, sessionId, cancellationToken);
        if (deck is null) return [];
        var normalized = term.ToLower();
        var slides = await db.SalesPresentationSlides.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DeckId == deck.Id &&
                        x.ProcessingVersion == deck.ProcessingVersion &&
                        ((x.Title != null && x.Title.ToLower().Contains(normalized)) || x.ExtractedText.ToLower().Contains(normalized)))
            .OrderBy(x => x.SlideNumber)
            .Take(25)
            .ToListAsync(cancellationToken);
        return slides.Select(x => new SalesPresentationSearchResultDto(
            x.SlideNumber, x.Title, SafeSnippet(x.ExtractedText, term))).ToArray();
    }

    public async Task<SalesPresentationCommandResultDto?> ExecuteAsync(
        Guid companyId, Guid userId, Guid sessionId, string toolName,
        SalesPresentationCommandRequest request, string? correlationId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        ValidateIds(companyId, userId, sessionId);
        ArgumentNullException.ThrowIfNull(request);
        if (!SalesPresentationToolNames.IsMutation(toolName))
            throw new ArgumentOutOfRangeException(nameof(toolName), "Unsupported presentation command.");
        await EnsureMemberAsync(companyId, userId, cancellationToken);

        var session = await db.SalesMeetingSessions.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        var deck = await ActiveDeckAsync(companyId, sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("An active processed presentation deck was not found.");

        var before = await BuildSnapshotAsync(session, deck, cancellationToken);
        var actorType = string.IsNullOrWhiteSpace(request.ActorType)
            ? SalesPresentationCommandActorTypes.Human
            : request.ActorType.Trim().ToLowerInvariant();
        if (actorType is not (SalesPresentationCommandActorTypes.Human or SalesPresentationCommandActorTypes.Agent))
            await RejectAsync(session, userId, toolName, request, SalesPresentationRuntimeProblemCodes.InvalidCommand,
                "The presentation command actor type is invalid.", before, correlationId, cancellationToken);
        if (request.DeckId.HasValue && request.DeckId.Value != deck.Id)
            await RejectAsync(session, userId, toolName, request, SalesPresentationRuntimeProblemCodes.Conflict,
                "The command targets a deck that is no longer active.", before, correlationId, cancellationToken);
        if (actorType == SalesPresentationCommandActorTypes.Agent &&
            session.PresentationControlMode != SalesPresentationControlModes.Autonomous)
            await RejectAsync(session, userId, toolName, request, SalesPresentationRuntimeProblemCodes.InvalidCommand,
                "Alex may mutate presentation state only while organizer-approved autonomous mode is active.", before, correlationId, cancellationToken);
        if (actorType == SalesPresentationCommandActorTypes.Agent &&
            (!request.ActorId.HasValue || request.ActorId == Guid.Empty ||
             !await db.Agents.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == request.ActorId &&
                 (x.Department == "Sales" || (x.Department == "Marketing" && session.PresenterAgentId == x.Id)) && x.Status == AgentStatus.Active, cancellationToken)))
            await RejectAsync(session, userId, toolName, request, SalesPresentationRuntimeProblemCodes.InvalidCommand,
                "An active company-owned Sales agent identity is required for an agent presentation command.", before,
                correlationId, cancellationToken);
        if (request.CommandId == Guid.Empty || request.Sequence < 1 || request.ExpectedVersion < 1)
            await RejectAsync(session, userId, toolName, request, SalesPresentationRuntimeProblemCodes.InvalidCommand,
                "Command id, sequence, and expected version are required.", before, correlationId, cancellationToken);
        if (request.Sequence <= session.LastPresentationSequence)
        {
            var disposition = request.Sequence == session.LastPresentationSequence &&
                              request.CommandId == session.LastPresentationCommandId
                ? "duplicate"
                : "stale";
            Commands.Add(1, new KeyValuePair<string, object?>("command", toolName), new("disposition", disposition));
            CommandLatency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>[] { new("command", toolName) });
            return new SalesPresentationCommandResultDto(disposition, null, before);
        }

        if (request.Sequence != session.LastPresentationSequence + 1)
            await RejectAsync(session, userId, toolName, request, SalesPresentationRuntimeProblemCodes.OutOfOrder,
                "The command arrived out of order.", before, correlationId, cancellationToken);
        if (request.ExpectedVersion != session.ConcurrencyVersion)
            await RejectAsync(session, userId, toolName, request, SalesPresentationRuntimeProblemCodes.Conflict,
                "The presentation changed after the command was created.", before, correlationId, cancellationToken);

        try
        {
            var commandType = Parse(toolName);
            var target = ResolveTarget(commandType, session.CurrentSlideIndex, request.SlideNumber, deck.SlideCount);
            var marker = commandType == SalesPresentationCommandType.Pause
                ? string.IsNullOrWhiteSpace(request.ResumeMarker)
                    ? $"slide:{target}:talking-point:{request.TalkingPointIndex ?? session.CurrentTalkingPointIndex}"
                    : request.ResumeMarker.Trim()
                : request.ResumeMarker;
            session.ApplyPresentationCommand(
                commandType, request.CommandId, request.Sequence, request.ExpectedVersion,
                target, request.TalkingPointIndex, marker, userId, UtcNow());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await RejectAsync(session, userId, toolName, request, SalesPresentationRuntimeProblemCodes.InvalidCommand,
                exception.Message, before, correlationId, cancellationToken);
        }

        AddAudit(session, userId, AuditEventActions.SalesPresentationCommandAccepted,
            AuditEventOutcomes.Succeeded, "Presentation command accepted.", toolName, request, null, correlationId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            Conflicts.Add(1, new KeyValuePair<string, object?>("command", toolName));
            db.ChangeTracker.Clear();
            var current = await LoadSnapshotAsync(companyId, sessionId, cancellationToken) ?? before;
            db.AuditEvents.Add(new AuditEvent(
                Guid.NewGuid(), companyId, AuditActorTypes.User, userId,
                AuditEventActions.SalesPresentationCommandRejected, "sales_meeting_session", sessionId.ToString("D"),
                AuditEventOutcomes.Rejected, "Presentation command lost an optimistic concurrency race.",
                ["sales meeting session", "presentation deck"],
                new Dictionary<string, string?>
                {
                    ["toolName"] = toolName,
                    ["commandId"] = request.CommandId.ToString("D"),
                    ["sequence"] = request.Sequence.ToString(),
                    ["rejectionCode"] = SalesPresentationRuntimeProblemCodes.Conflict
                }, correlationId, UtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            throw new SalesPresentationRuntimeConflictException(
                SalesPresentationRuntimeProblemCodes.Conflict,
                "The presentation changed while the command was being applied.", current);
        }

        var snapshot = await BuildSnapshotAsync(session, deck, cancellationToken);
        if (actorType == SalesPresentationCommandActorTypes.Human)
            preemption?.Preempt(companyId, sessionId, snapshot.Stage.Version);
        foreach (var publisher in publishers)
            await publisher.PublishAsync(companyId, sessionId, snapshot, cancellationToken);

        Commands.Add(1, new KeyValuePair<string, object?>("command", toolName), new("disposition", "accepted"));
        CommandLatency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>[] { new("command", toolName) });
        logger.LogInformation("Applied {PresentationCommand} at sequence {Sequence} for a sales presentation session.", toolName, request.Sequence);
        return new SalesPresentationCommandResultDto("accepted", null, snapshot);
    }

    public async Task<SalesPresentationControlModeDto?> SetControlModeAsync(
        Guid companyId, Guid userId, Guid sessionId, SetSalesPresentationControlModeRequest request,
        string? correlationId, CancellationToken cancellationToken)
    {
        ValidateIds(companyId, userId, sessionId);
        ArgumentNullException.ThrowIfNull(request);
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        if (!SalesPresentationControlModes.IsValid(request.Mode))
            throw new ArgumentOutOfRangeException(nameof(request.Mode), "Presentation control mode must be manual, assisted, or autonomous.");
        var session = await db.SalesMeetingSessions.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        try
        {
            session.SetPresentationControlMode(request.Mode, request.ExpectedVersion, userId, UtcNow());
        }
        catch (InvalidOperationException exception)
        {
            var snapshot = await LoadSnapshotAsync(companyId, sessionId, cancellationToken)
                ?? throw new KeyNotFoundException("An active processed presentation deck was not found.");
            throw new SalesPresentationRuntimeConflictException(SalesPresentationRuntimeProblemCodes.Conflict, exception.Message, snapshot);
        }
        preemption?.Preempt(companyId, sessionId, session.ConcurrencyVersion);
        db.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), companyId, AuditActorTypes.User, userId, "sales.presentation.control_mode_changed",
            "sales_meeting_session", sessionId.ToString("D"), AuditEventOutcomes.Succeeded,
            "The meeting organizer changed Alex's presentation control mode.",
            ["sales meeting session", "presentation control policy"],
            new Dictionary<string, string?> { ["mode"] = session.PresentationControlMode }, correlationId, UtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        var snapshotAfter = await LoadSnapshotAsync(companyId, sessionId, cancellationToken);
        if (snapshotAfter is not null)
            foreach (var publisher in publishers)
                await publisher.PublishAsync(companyId, sessionId, snapshotAfter, cancellationToken);
        return new SalesPresentationControlModeDto(sessionId, session.PresentationControlMode,
            session.PresentationControlUpdatedByUserId, session.PresentationControlUpdatedUtc, session.ConcurrencyVersion);
    }

    public async Task RecordReconnectAsync(
        Guid companyId, Guid userId, Guid sessionId, string surface, CancellationToken cancellationToken)
    {
        ValidateIds(companyId, userId, sessionId);
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        var exists = await db.SalesMeetingSessions.AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (!exists) throw new KeyNotFoundException("Sales meeting session not found.");
        Reconnects.Add(1, new KeyValuePair<string, object?>("surface", NormalizeSurface(surface)));
        db.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), companyId, AuditActorTypes.User, userId,
            AuditEventActions.SalesPresentationClientReconnected, "sales_meeting_session", sessionId.ToString("D"),
            AuditEventOutcomes.Succeeded, "Presentation client restored its authoritative snapshot.",
            ["sales meeting session"], new Dictionary<string, string?> { ["surface"] = NormalizeSurface(surface) },
            null, UtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RejectAsync(
        SalesMeetingSession session, Guid userId, string toolName, SalesPresentationCommandRequest request,
        string code, string message, SalesPresentationAuthoritativeSnapshotDto snapshot,
        string? correlationId, CancellationToken cancellationToken)
    {
        Rejections.Add(1, new KeyValuePair<string, object?>("command", toolName), new("reason", code));
        if (code == SalesPresentationRuntimeProblemCodes.Conflict) Conflicts.Add(1);
        AddAudit(session, userId, AuditEventActions.SalesPresentationCommandRejected,
            AuditEventOutcomes.Rejected, message, toolName, request, code, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        throw new SalesPresentationRuntimeConflictException(code, message, snapshot);
    }

    private async Task<SalesPresentationAuthoritativeSnapshotDto?> LoadSnapshotAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        var deck = await ActiveDeckAsync(companyId, sessionId, cancellationToken);
        return deck is null ? null : await BuildSnapshotAsync(session, deck, cancellationToken);
    }

    private async Task<SalesPresentationAuthoritativeSnapshotDto> BuildSnapshotAsync(
        SalesMeetingSession session, SalesPresentationDeck deck, CancellationToken cancellationToken)
    {
        var slideNumber = Math.Clamp(session.CurrentSlideIndex < 1 ? 1 : session.CurrentSlideIndex, 1, deck.SlideCount);
        var slide = await db.SalesPresentationSlides.AsNoTracking().SingleAsync(
            x => x.CompanyId == session.CompanyId && x.DeckId == deck.Id &&
                 x.ProcessingVersion == deck.ProcessingVersion && x.SlideNumber == slideNumber,
            cancellationToken);
        var artifacts = await db.SalesMeetingArtifacts.AsNoTracking()
            .Where(x => x.CompanyId == session.CompanyId && x.SessionId == session.Id && x.DeckId == deck.Id &&
                        x.SlideId == slide.Id && x.ArtifactVersion == deck.BriefVersion)
            .OrderBy(x => x.Order)
            .Select(x => new SalesPresentationArtifactDto(
                x.Id, x.SlideId, x.ArtifactVersion, x.ArtifactType.ToStorageValue(), x.Section, x.Order,
                x.Content, x.Classification.ToStorageValue(), x.SourceId, x.AiRunId, x.CreatedUtc))
            .ToListAsync(cancellationToken);
        var stage = new SalesPresentationStageSnapshotDto(
            session.Id, session.Status.ToStorageValue(), session.LastPresentationSequence, session.ConcurrencyVersion,
            deck.Id, deck.Version, slide.SlideNumber, deck.SlideCount, slide.Title, slide.ExtractedText,
            null, slide.ImageWidthPixels, slide.ImageHeightPixels);
        var privateState = new SalesPresentationPrivateSnapshotDto(
            stage, session.PresentationControlMode, session.CurrentTalkingPointIndex, session.ResumeMarker, slide.SpeakerNotes, slide.Objective,
            slide.ExpectedDurationSeconds, slide.TransitionText, artifacts,
            session.PresentationControlUpdatedByUserId, session.PresentationControlUpdatedUtc);
        return new SalesPresentationAuthoritativeSnapshotDto(stage, privateState);
    }

    private Task<SalesPresentationDeck?> ActiveDeckAsync(Guid companyId, Guid sessionId, CancellationToken ct) =>
        db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.SessionId == sessionId && x.IsActive &&
                 x.Status == SalesPresentationDeckStatus.Processed && x.SlideCount > 0, ct);

    private async Task EnsureMemberAsync(Guid companyId, Guid userId, CancellationToken ct)
    {
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct))
            throw new UnauthorizedAccessException("An active company membership is required.");
    }

    private void AddAudit(
        SalesMeetingSession session, Guid userId, string action, string outcome, string rationale,
        string toolName, SalesPresentationCommandRequest request, string? rejectionCode, string? correlationId)
    {
        var agentActor = request.ActorType == SalesPresentationCommandActorTypes.Agent && request.ActorId.HasValue;
        db.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), session.CompanyId, agentActor ? AuditActorTypes.Agent : AuditActorTypes.User,
            agentActor ? request.ActorId : userId, action,
            "sales_meeting_session", session.Id.ToString("D"), outcome, rationale,
            ["sales meeting session", "presentation deck"],
            new Dictionary<string, string?>
            {
                ["toolName"] = toolName,
                ["commandId"] = request.CommandId.ToString("D"),
                ["sequence"] = request.Sequence.ToString(),
                ["expectedVersion"] = request.ExpectedVersion.ToString(),
                ["authoritativeVersion"] = session.ConcurrencyVersion.ToString(),
                ["authorizedByUserId"] = agentActor ? userId.ToString("D") : null,
                ["rejectionCode"] = rejectionCode
            }, correlationId, UtcNow()));
    }

    private static SalesPresentationCommandType Parse(string toolName) => toolName switch
    {
        SalesPresentationToolNames.Next => SalesPresentationCommandType.Next,
        SalesPresentationToolNames.Previous => SalesPresentationCommandType.Previous,
        SalesPresentationToolNames.Goto => SalesPresentationCommandType.Goto,
        SalesPresentationToolNames.Pause => SalesPresentationCommandType.Pause,
        SalesPresentationToolNames.Resume => SalesPresentationCommandType.Resume,
        _ => throw new ArgumentOutOfRangeException(nameof(toolName))
    };

    private static int? ResolveTarget(SalesPresentationCommandType command, int current, int? requested, int count) => command switch
    {
        SalesPresentationCommandType.Next when current < 1 => 1,
        SalesPresentationCommandType.Next when current < count => current + 1,
        SalesPresentationCommandType.Next => throw new InvalidOperationException("The presentation is already on its last slide."),
        SalesPresentationCommandType.Previous when current > 1 => current - 1,
        SalesPresentationCommandType.Previous => throw new InvalidOperationException("The presentation is already on its first slide."),
        SalesPresentationCommandType.Goto when requested is >= 1 && requested <= count => requested,
        SalesPresentationCommandType.Goto => throw new InvalidOperationException("The requested slide is outside the active deck."),
        SalesPresentationCommandType.Pause when current >= 1 => current,
        SalesPresentationCommandType.Pause => throw new InvalidOperationException("A current slide is required before pausing."),
        _ => null
    };

    private static string SafeSnippet(string text, string term)
    {
        var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, index < 0 ? 0 : index - 60);
        var length = Math.Min(180, text.Length - start);
        return text.Substring(start, length);
    }

    private static string NormalizeSurface(string value) =>
        string.Equals(value, "stage", StringComparison.OrdinalIgnoreCase) ? "stage" : "private";

    private static void ValidateIds(params Guid[] ids)
    {
        if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Company, user, and session identifiers are required.");
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
