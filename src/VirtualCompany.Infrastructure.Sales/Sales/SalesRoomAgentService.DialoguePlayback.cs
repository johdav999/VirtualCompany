using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesRoomAgentService
{
    // This binding is created by the worker after a confirmed input and (if needed) a
    // completed output handoff. Neither identities nor playback offsets come from the model.
    internal async Task<SalesRoomConversationToolResult> ExecuteDialoguePlaybackAsync(
        SalesRoomDialogueTurn turn, string action, CancellationToken ct)
    {
        var b = turn.Binding;
        var command = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"dialogue-playback:{b.CompanyId:N}:{b.ConversationId:N}:{b.OwnerGeneration}:{turn.InputId:N}"))[..16]);
        try
        {
            RequireAgentAdmission();
            var attempt = 0;
            var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (attempt++ > 0) db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId && x.Id == b.ParticipantId, ct);
                if (participant?.MemberUserId is not Guid user) throw new UnauthorizedAccessException();
                var (room, _, floor) = await ControllerAsync(b.CompanyId, user, b.ConversationId, true, ct);
                // A single confirmed turn can never queue two different playback actions.
                if (await ReplayAsync(b.CompanyId, b.ConversationId, command, "dialogue_" + action, ct))
                    return (Room: room, Response: floor.ResponseGeneration, Replay: true);
                var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, b.CompanyId,
                    b.ConversationId, b.ParticipantId, Now, ct);
                if (!Options.SemanticConversationInputEnabled || turn.InputId == Guid.Empty || turn.ExpiresUtc <= Now ||
                    participant.Version != turn.ConsentVersion || !participant.TranscriptRetentionAllowed ||
                    !SalesRoomDialoguePolicy.IsPlayback(action) || authority?.ControllerAllowed != true || b.Mode != "autonomous" ||
                    !new AgentConversation(b, AgentConversationPhase.Interpreting).Check(authority, Now).Allowed ||
                    floor.Overlap || floor.PendingTurnId is not null || floor.State == SalesRoomFloorStates.Agent)
                    throw Error("conversation_stale", "The playback request is no longer authorized.");
                await RequireConsentAsync(room, ct);
                var session = await SessionAsync(room, ct);
                if (session.PresentationControlMode != "autonomous" || session.ConcurrencyVersion != b.PresentationVersion)
                    throw Error("conversation_stale", "The presentation changed before execution.");
                if (await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == b.CompanyId &&
                    x.RoomId == b.ConversationId && (x.Status == SalesRoomAgentSpeechStates.Queued ||
                    x.Status == SalesRoomAgentSpeechStates.Processing), ct))
                    throw Error("conversation_stale", "Another speech operation is still pending.");
                if (action == SalesRoomDialoguePolicy.Pause)
                {
                    floor.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, Now);
                    room.AgentReady(b.OwnerId, b.OwnerGeneration); // Pause narration, not the listening session.
                }
                else
                {
                    if (!await AudienceReadyAsync(room, session, ct))
                        throw Error("floor_not_ready", "Wait for the audience to render the current slide.");
                    if (action == SalesRoomDialoguePolicy.Start)
                    {
                        if (session.Status == SalesMeetingSessionStatus.Answering)
                            session.ResumeBrowserNarration(session.ConcurrencyVersion, floor.SlideNumber, floor.TalkingPointIndex,
                                floor.ResumeMarker, user, Now);
                        session.ApplyPresentationCommand(SalesPresentationCommandType.Goto, command,
                            session.LastPresentationSequence + 1, session.ConcurrencyVersion, 1, 1, null, user, Now,
                            allowInterruptedNavigation: true);
                        floor.PresentationMoved(participant.Id, session.ConcurrencyVersion, 1, 1, null, Now);
                    }
                    var source = await CurrentNarrationAsync(room, session, ct, floor.TalkingPointIndex)
                        ?? throw Error("release_required", "Approved narration is unavailable at that position.");
                    if (!await (from segment in db.SalesNarrationSegments.IgnoreQueryFilters()
                        join asset in db.SalesNarrationAssets.IgnoreQueryFilters() on segment.AssetId equals asset.Id
                        where segment.CompanyId == b.CompanyId && asset.CompanyId == b.CompanyId && segment.Id == source.SegmentId &&
                            asset.Status == SalesNarrationAsset.Ready && asset.StorageKey != null && asset.DurationMilliseconds > floor.ResumeOffsetMilliseconds
                        select asset.Id).AnyAsync(ct))
                        throw Error("release_required", "Approved audio is not playable at the saved position.");
                    room.ResumeAgent(b.OwnerId, b.OwnerGeneration);
                    RestoreResumePosition(session, floor, participant.Id, user, room.AgentTurnGeneration, Now);
                    db.SalesRoomAgentSpeech.Add(new(Guid.NewGuid(), b.CompanyId, room.Id, session.Id, command,
                        b.AgentId, b.OwnerGeneration, room.AgentTurnGeneration, SalesRoomAgentSpeechKinds.Narration, user, Now,
                        source.RevisionId, source.SegmentId, null, floor.ResumeOffsetMilliseconds, floor.ResponseGeneration));
                }
                Record(room, command, "dialogue_" + action, user, new { turn.InputId, action, b });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return (Room: room, Response: floor.ResponseGeneration, Replay: false);
            });
            if (!result.Replay && action != SalesRoomDialoguePolicy.Pause)
            {
                try
                {
                    var snapshot = await presentation.GetCurrentAsync(b.CompanyId, result.Room.OrganizerUserId, b.SessionId, ct);
                    if (snapshot is null)
                        throw new InvalidOperationException("The committed playback snapshot could not be loaded for publication.");
                    foreach (var publisher in presentationEvents) await publisher.PublishAsync(b.CompanyId, b.SessionId, snapshot, ct);
                    await floorEvents.AllowPlaybackAsync(b.CompanyId, b.SessionId, new(b.ConversationId, result.Response), ct);
                    await commands.SignalAsync(new(b.CompanyId, b.ConversationId, b.OwnerId, b.OwnerGeneration, "wake"), ct);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    // The durable speech queue is already committed. The worker polls it and
                    // the conductor republishes stage state; never report rollback or enqueue twice.
                    SalesRoomBenchmarkTelemetry.RecordConversationFailure("playback_notification_failed");
                    return new(true, "presentation_queued_notification_pending",
                        "Playback is saved but its live notification failed. The worker will recheck the saved operation; audio has not been confirmed.");
                }
            }
            return new(true, action == SalesRoomDialoguePolicy.Pause ? "presentation_paused" : "presentation_queued",
                action == SalesRoomDialoguePolicy.Pause ? "Narration is paused; listening remains active." :
                "Playback accepted at the server position. Queued is not completed; audience and authority are rechecked before audio.");
        }
        catch (UnauthorizedAccessException) { return new(false, "participant_denied", "Only the current controller can change playback."); }
        catch (SalesRoomAgentException ex) { return new(false, ex.Code, ex.Message); }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException or InvalidOperationException)
        { db.ChangeTracker.Clear(); return new(false, "conversation_changed", "Playback state changed. Please repeat the request."); }
    }
}
