using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed record SalesRoomDialogueTurn(AgentConversationBinding Binding, Guid InputId, long ConsentVersion,
    DateTime ExpiresUtc);

internal sealed partial class SalesRoomAgentWorker
{
    private async Task SpeakDialogueInOwnScopeAsync(SalesRoomDialogueTurn turn, string heard, string contentClass,
        string providerSession, ISalesRoomMediaConnection media, SalesRoomAgentWorkItem work,
        SalesRoomAgentRunControl control, long interruption, CancellationToken ct)
    {
        await using var scope = captureScopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SalesRoomAgentWorker>().SpeakDialogueAsync(turn, heard,
            contentClass, providerSession, media, work, control, interruption, ct);
    }

    internal async Task SpeakDialogueAsync(SalesRoomDialogueTurn turn, string heard, string contentClass,
        string providerSession, ISalesRoomMediaConnection media, SalesRoomAgentWorkItem work,
        SalesRoomAgentRunControl control, long interruption, CancellationToken ct)
    {
        using var companyScope = executionScopes.BeginScope(work.CompanyId);
        using var stop = await control.BeginResponseAsync(ct, interruption);
        var token = stop.Token; var b = turn.Binding;
        // Acknowledgement and grounded answer share an input turn, but the durable
        // speech command key is unique across all speech kinds in a room.
        var speechCommandId = contentClass == SalesRoomDialoguePolicy.Checking
            ? new Guid(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"checking_sources:{turn.InputId:N}"))[..16])
            : turn.InputId;
        if (b.CompanyId != work.CompanyId || b.ConversationId != work.RoomId || b.OwnerId != work.LeaseOwnerId ||
            b.OwnerGeneration != work.Generation || contentClass is not (SalesRoomDialoguePolicy.Social or SalesRoomDialoguePolicy.General or SalesRoomDialoguePolicy.Clarify or SalesRoomDialoguePolicy.Checking))
        { control.EndResponse(stop); return; }
        SalesRoomAgentSpeech? item = null;
        RealtimeBufferedSpeech? candidate = null;
        var playbackCompleted = false; var deliveryStart = 0;
        var published = 0; var claimed = false; var started = Stopwatch.GetTimestamp();
        var releaseStage = "initial_authority";
        try
        {
            await CheckAsync();
            if (await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == b.CompanyId &&
                x.RoomId == b.ConversationId && x.CommandId == speechCommandId && x.Kind == SalesRoomAgentSpeechKinds.Conversation, token)) return;
            var room = await RoomAsync(work, token);
            item = new(Guid.NewGuid(), b.CompanyId, b.ConversationId, b.SessionId, speechCommandId, b.AgentId,
                b.OwnerGeneration, b.TurnGeneration, SalesRoomAgentSpeechKinds.Conversation, room.OrganizerUserId, Now,
                responseGeneration: b.ResponseGeneration);
            item.Claim(Now); db.SalesRoomAgentSpeech.Add(item); await db.SaveChangesAsync(token);
            var generationStarted = Stopwatch.GetTimestamp();
            logger.LogInformation("MeetingTrace Stage=dialogue_generation_start RoomId={RoomId} TurnId={TurnId} SpeechId={SpeechId} ContentClass={ContentClass} SpeechPromptVersion={SpeechPromptVersion}",
                b.ConversationId, turn.InputId, item.Id, contentClass, SalesRoomDialoguePolicy.SpeechPromptVersion);
            releaseStage = "generation";
            candidate = await RunWithLeaseRenewalAsync(work, t => realtimeConversation.GenerateBufferedSpeechAsync(providerSession,
                new(b.CompanyId, room.OrganizerUserId, b.AgentId, turn.InputId, heard,
                    SalesRoomDialoguePolicy.SpeechInstructions(contentClass)), t), token);
            var buffered = Stopwatch.GetTimestamp();
            logger.LogInformation("MeetingTrace Stage=dialogue_buffered RoomId={RoomId} TurnId={TurnId} ContentClass={ContentClass} GenerationMs={GenerationMs} AudioMs={AudioMs} InputTokens={InputTokens} OutputTokens={OutputTokens} AudioItemCount={AudioItemCount}",
                b.ConversationId, turn.InputId, contentClass, Stopwatch.GetElapsedTime(generationStarted, buffered).TotalMilliseconds,
                candidate.Pcm.Length / 48, candidate.InputTokens, candidate.OutputTokens, candidate.Items?.Count ?? 1);
            if (contentClass == SalesRoomDialoguePolicy.Checking &&
                Options.AcknowledgementTextDiagnosticRoomId == b.ConversationId)
            {
                // A fresh authority/consent check precedes the opt-in diagnostic. JSON encoding
                // keeps model-supplied newlines and control characters inside one log value.
                await CheckAsync();
                logger.LogInformation("MeetingTrace Stage=acknowledgement_candidate_received RoomId={RoomId} TurnId={TurnId} CandidateTextJson={CandidateTextJson}",
                    b.ConversationId, turn.InputId, JsonSerializer.Serialize(candidate.Text));
            }
            SalesRoomBenchmarkTelemetry.RecordLatency("conversation_audio_generation", Stopwatch.GetElapsedTime(generationStarted, buffered));
            db.ChangeTracker.Clear(); room = await RoomAsync(work, token);
            room.RecordAgentAudio(work.LeaseOwnerId, work.Generation, 0, 0, 0, 0, candidate.InputTokens, candidate.OutputTokens);
            await db.SaveChangesAsync(token); // Count generated usage even when content is withheld.
            releaseStage = "generated_authority";
            await CheckAsync();
            if (!SalesRoomDialoguePolicy.Bounded(candidate.Text) || candidate.Pcm.Length is < 2 or > 960000 || candidate.Pcm.Length % 2 != 0)
                throw new WithheldSpeech("conversation_invalid", "Conversational output exceeded the release envelope.");
            releaseStage = "content_validation";
            var validationStarted = Stopwatch.GetTimestamp();
            logger.LogInformation("MeetingTrace Stage=dialogue_validation_start RoomId={RoomId} TurnId={TurnId} SpeechId={SpeechId} ContentClass={ContentClass} ValidationPromptVersion={ValidationPromptVersion} GeneratedAudioMs={GeneratedAudioMs} CandidateCharacters={CandidateCharacters}",
                b.ConversationId, turn.InputId, item.Id, contentClass, SalesRoomDialoguePolicy.ValidationPromptVersion,
                candidate.Pcm.Length / 48, candidate.Text.Length);
            var validation = await RunWithLeaseRenewalAsync(work, t => conversationReasoner.ValidateDialogueAsync(
                new(b.CompanyId, b.AgentId, b.ConversationId, b.SessionId, b.ParticipantId, turn.InputId,
                    heard, "", false, b.Slide, b.Point, b.Mode), heard, contentClass, candidate.Text, t), token);
            logger.LogInformation("MeetingTrace Stage=dialogue_validation_complete RoomId={RoomId} TurnId={TurnId} SpeechId={SpeechId} ContentClass={ContentClass} Allowed={Allowed} ValidationRunId={ValidationRunId} ValidationMs={ValidationMs} GeneratedAudioMs={GeneratedAudioMs} PublishedAudioMs=0",
                b.ConversationId, turn.InputId, item.Id, contentClass, validation.HasValue, validation,
                Stopwatch.GetElapsedTime(validationStarted).TotalMilliseconds, candidate.Pcm.Length / 48);
            if (validation is null) throw new WithheldSpeech("conversation_restricted", "The proposed reply requires clarification or verified sources.");
            logger.LogInformation("MeetingTrace Stage=dialogue_validated RoomId={RoomId} TurnId={TurnId} ContentClass={ContentClass} ValidationRunId={ValidationRunId}",
                b.ConversationId, turn.InputId, contentClass, validation);
            releaseStage = "release_authority";
            await CheckAsync(); room = await RoomAsync(work, token);
            if (room.AgentOutputAudioMilliseconds + candidate.Pcm.Length / 48 > Options.MaximumOutputAudioSeconds * 1000L)
                throw new WithheldSpeech("quota_exceeded", "This reply would exceed the room audio allowance.");
            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId, token);
            floor.AgentClaim(b.ResponseGeneration, b.TurnGeneration, Now); claimed = true;
            room.AgentReady(work.LeaseOwnerId, work.Generation);
            room.AgentSpeaking(work.LeaseOwnerId, work.Generation); await db.SaveChangesAsync(token);
            SalesRoomBenchmarkTelemetry.RecordLatency("conversation_audio_buffered_to_release", Stopwatch.GetElapsedTime(buffered));
            releaseStage = "media_alignment";
            await AlignOutputGenerationAsync(media, b.TurnGeneration, token);
            deliveryStart = media.DeliveredMilliseconds(b.TurnGeneration) ?? 0;
            await CheckAsync();
            releaseStage = "playback";
            await floorEvents.AllowPlaybackAsync(b.CompanyId, b.SessionId, new(b.ConversationId, b.ResponseGeneration), token);
            logger.LogInformation("MeetingTrace Stage=dialogue_playback_start RoomId={RoomId} TurnId={TurnId} ContentClass={ContentClass} TurnGeneration={TurnGeneration} ResponseGeneration={ResponseGeneration}",
                work.RoomId, turn.InputId, contentClass, b.TurnGeneration, b.ResponseGeneration);
            var samples = MemoryMarshal.Cast<byte, short>(candidate.Pcm).ToArray();
            for (var offset = 0; offset < samples.Length; offset += 480)
            {
                if (offset % 4800 == 0) await CheckAsync();
                var count = Math.Min(480, samples.Length - offset);
                ReadOnlyMemory<short> frame = samples.AsMemory(offset, count);
                if (count < 480) { var padded = new short[480]; frame.Span.CopyTo(padded); frame = padded; }
                if (!await media.SendAsync(b.TurnGeneration, 24000, frame, token))
                    throw new OperationCanceledException(token);
                if (offset == 0)
                    SalesRoomBenchmarkTelemetry.RecordLatency("conversation_first_media_enqueue", Stopwatch.GetElapsedTime(started));
                published += count;
            }
            await CheckAsync();
            if (!await media.CompleteSpeechAsync(b.TurnGeneration, token)) throw new OperationCanceledException(token);
            playbackCompleted = true;
            logger.LogInformation("MeetingTrace Stage=dialogue_playback_complete RoomId={RoomId} TurnId={TurnId} ContentClass={ContentClass} SentAudioMs={SentAudioMs}",
                b.ConversationId, turn.InputId, contentClass, published / 24);
            await PersistAsync(candidate, validation, null);
            // The eventual grounded answer owns this turn's delivered answer context.
            try { if (contentClass != SalesRoomDialoguePolicy.Checking) await realtimeConversation.RecordPlayedResponseAsync(providerSession, turn.InputId, candidate.Text, token); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { logger.LogInformation("Completed dialogue could not be added to provider context. Type={Type}", ex.GetType().Name); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogInformation("MeetingTrace Stage=dialogue_withheld RoomId={RoomId} TurnId={TurnId} ContentClass={ContentClass} Type={Type} Code={Code} Cancelled={Cancelled} ReleaseStage={ReleaseStage} PublishedAudioMs={PublishedAudioMs} ElapsedMs={ElapsedMs}",
                work.RoomId, turn.InputId, contentClass, ex.GetType().Name,
                ex is RealtimeAgentEventException provider ? provider.Code :
                    ex is RealtimeAgentUnavailableException unavailable ? unavailable.Code :
                    ex is WithheldSpeech withheld ? withheld.Code :
                    ex is OperationCanceledException ? "conversation_interrupted" : "release_or_transport_failure",
                stop.IsCancellationRequested, releaseStage, published / 24, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            db.ChangeTracker.Clear();
            var current = await RoomAsync(work, CancellationToken.None);
            if (claimed && current.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) && current.AgentTurnGeneration == b.TurnGeneration &&
                media.GetStatistics().TurnGeneration == b.TurnGeneration)
                await media.CancelSpeechAsync(CancellationToken.None);
            await PersistAsync(null, null, ex is OperationCanceledException ? "conversation_interrupted" : "conversation_reply_withheld");
        }
        finally
        {
            try
            {
                if (candidate is not null)
                    await realtimeConversation.FinishBufferedSpeechAsync(candidate, playbackCompleted ? candidate.Pcm.Length / 48 :
                        Math.Clamp((media.DeliveredMilliseconds(b.TurnGeneration) ?? deliveryStart) - deliveryStart, 0, published / 24),
                        playbackCompleted, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { logger.LogInformation("Output session cleanup failed: {Type}", ex.GetType().Name); }
            finally { control.EndResponse(stop); }
        }

        async Task CheckAsync()
        {
            token.ThrowIfCancellationRequested(); db.ChangeTracker.Clear();
            var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, b.CompanyId, b.ConversationId, b.ParticipantId, Now, token);
            var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId && x.Id == b.ParticipantId, token);
            if (turn.ExpiresUtc <= Now || b.Mode != "autonomous" || !Options.SemanticConversationInputEnabled || authority is null ||
                !new AgentConversation(b, AgentConversationPhase.Interpreting).Check(authority, Now).Allowed ||
                participant?.Version != turn.ConsentVersion || participant.TranscriptRetentionAllowed != true ||
                !media.IsParticipantConnected(b.ParticipantId)) throw new WithheldSpeech("conversation_stale", "Conversation authority changed.");
            var currentRoom = await RoomAsync(work, token); await EnsureConsentAsync(currentRoom, token);
            var currentFloor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId, token);
            if (currentFloor.Overlap || currentFloor.PendingTurnId is not null ||
                claimed && currentFloor.State != SalesRoomFloorStates.Agent)
                throw new WithheldSpeech("conversation_stale", "A newer floor turn superseded this reply.");
        }
        async Task PersistAsync(RealtimeBufferedSpeech? audio, Guid? validation, string? failure)
        {
            if (item is null) return;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                db.ChangeTracker.Clear(); var room = await RoomAsync(work, CancellationToken.None);
                var row = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId && x.Id == item.Id);
                if (row.Status != SalesRoomAgentSpeechStates.Processing) return;
                var owns = room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) && room.AgentTurnGeneration == b.TurnGeneration;
                long? handoff = null;
                if (audio is not null && owns)
                    row.Complete(audio.Text, JsonSerializer.Serialize(new { contentClass, validationRunId = validation, path = "buffered_realtime" }),
                        audio.ResponseId, published / 24, Now);
                else row.Fail(SalesRoomAgentSpeechStates.Withheld, failure ?? "conversation_stale", "Please clarify or use a typed question. The agent is still listening.", Now);
                if (owns)
                {
                    room.AgentSpeechCompleted(work.LeaseOwnerId, work.Generation, published / 24);
                    var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId);
                    if (claimed && floor.ResponseGeneration == b.ResponseGeneration && floor.State == SalesRoomFloorStates.Agent)
                    {
                        if (failure == "conversation_interrupted" && stop.IsCancellationRequested)
                        { floor.PauseAt(floor.ResumeOffsetMilliseconds, b.TurnGeneration, Now); handoff = floor.ResponseGeneration; }
                        else floor.AgentCompleted(floor.HostParticipantId, Now, preserveNarrationCheckpoint: true);
                    }
                    if (failure is not null)
                    {
                        if (claimed && !stop.IsCancellationRequested && media.GetStatistics().TurnGeneration > b.TurnGeneration)
                        {
                            room.PreemptAgent(work.LeaseOwnerId, work.Generation, "A failed output was flushed; listening continues.");
                            floor.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, Now);
                        }
                        room.ConversationReplyWithheld(work.LeaseOwnerId, work.Generation);
                    }
                }
                db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), b.CompanyId, "system", null,
                    "sales.browser_room.conversation_audio", "sales_room_agent_speech", row.Id.ToString(), row.Status == SalesRoomAgentSpeechStates.Spoken ? "spoken" : "withheld",
                    metadata: new Dictionary<string, string?> { ["path"] = "buffered_realtime", ["class"] = contentClass }, occurredUtc: Now));
                try
                {
                    await db.SaveChangesAsync();
                    if (handoff.HasValue) control.RecordConfirmedHandoff(stop, b.ResponseGeneration, handoff.Value);
                    return;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 2) { }
            }
        }
    }
}
