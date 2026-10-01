using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed partial class SalesRoomAgentWorker
{
    internal async Task<SalesRoomConversationToolResult> ExecuteDialoguePlaybackCommandAsync(
        SalesRoomDialogueTurn turn, string action, CancellationToken ct)
    {
        await using var scope = captureScopes.CreateAsyncScope();
        // DI scopes do not inherit the listening worker's scoped tenant accessor.
        // The command and its subsequent filtered snapshot publication need the same tenant.
        using var company = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>()
            .BeginScope(turn.Binding.CompanyId);
        var service = (SalesRoomAgentService)scope.ServiceProvider.GetRequiredService<ISalesRoomAgentService>();
        return await service.ExecuteDialoguePlaybackAsync(turn, action, ct);
    }

    internal async Task<RealtimeConversationResponseRequest> DialogueRequestAsync(SalesRoomDialogueTurn turn, CancellationToken ct)
    {
        var b = turn.Binding;
        var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, b.CompanyId, b.ConversationId, b.ParticipantId, Now, ct);
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId, ct);
        var available = SalesRoomDialoguePolicy.Tools.Where(x => !SalesRoomDialoguePolicy.IsPlayback(x.Name)).Select(x => x.Name).ToList();
        var sessionReady = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == b.CompanyId && x.Id == b.SessionId && x.PresentationControlMode == "autonomous" &&
            x.ConcurrencyVersion == b.PresentationVersion, ct);
        if (authority?.ControllerAllowed == true && b.Mode == "autonomous" &&
            sessionReady &&
            new AgentConversation(b, AgentConversationPhase.Interpreting).Check(authority, Now).Allowed &&
            !floor.Overlap && floor.PendingTurnId is null)
        {
            var audience = await db.SalesRoomPresentationAudience.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId && x.PresentationVersion == b.PresentationVersion)
                .Select(x => x.State).ToListAsync(ct);
            var ready = audience.Count > 0 && audience.All(x => x is SalesRoomPresentationAudienceStates.Rendered or SalesRoomPresentationAudienceStates.Overridden);
            var pending = floor.State != SalesRoomFloorStates.Agent && await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AnyAsync(x =>
                x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId &&
                (x.Status == SalesRoomAgentSpeechStates.Queued || x.Status == SalesRoomAgentSpeechStates.Processing), ct);
            var selection = SalesRoomNarrationSelection.Current(db, b.CompanyId, b.SessionId, Now);
            var playable = await (from revision in selection
                join segment in db.SalesNarrationSegments.IgnoreQueryFilters() on revision.Id equals segment.RevisionId
                join asset in db.SalesNarrationAssets.IgnoreQueryFilters() on segment.AssetId equals asset.Id
                where segment.CompanyId == b.CompanyId && asset.CompanyId == b.CompanyId &&
                    asset.Status == SalesNarrationAsset.Ready && asset.StorageKey != null
                select new { segment.SlideNumber, segment.TalkingPoint, asset.DurationMilliseconds }).ToListAsync(ct);
            if (ready && !pending && playable.Any(x => x.SlideNumber == 1 && x.TalkingPoint == 1 && x.DurationMilliseconds > 0)) available.Add(SalesRoomDialoguePolicy.Start);
            if (floor.State == SalesRoomFloorStates.Agent) available.Add(SalesRoomDialoguePolicy.Pause);
            else if (ready && !pending && playable.Any(x => x.SlideNumber == b.Slide && x.TalkingPoint == Math.Max(1, b.Point) && x.DurationMilliseconds > b.OffsetMilliseconds))
                available.Add(SalesRoomDialoguePolicy.Resume);
        }
        var receipts = await (from x in db.SalesRoomAgentSpeech.IgnoreQueryFilters().AsNoTracking()
            join segment in db.SalesNarrationSegments.IgnoreQueryFilters().AsNoTracking() on x.NarrationSegmentId equals segment.Id
            where segment.CompanyId == b.CompanyId &&
            x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId && x.AgentGeneration == b.OwnerGeneration &&
            x.Kind == SalesRoomAgentSpeechKinds.Narration && x.Status == SalesRoomAgentSpeechStates.Spoken
            select new { x.CompletedUtc, x.NarrationSegmentId, ReleasedText = x.OffsetMilliseconds == 0 ? segment.Script : null, x.OffsetMilliseconds, x.DurationMilliseconds })
            .OrderByDescending(x => x.CompletedUtc).Take(3)
            .ToListAsync(ct);
        var snapshot = JsonSerializer.Serialize(new {
            slide = b.Slide, point = b.Point, savedNarrationOffsetMilliseconds = b.OffsetMilliseconds,
            state = floor.State, version = b.PresentationVersion,
            completed = receipts.Select(x => new { x.NarrationSegmentId, text = x.ReleasedText == null ? null : x.ReleasedText[..Math.Min(300, x.ReleasedText.Length)], x.OffsetMilliseconds, x.DurationMilliseconds }),
            partial = "The saved offset is a media-delivery checkpoint, not proof that the full segment was heard. Do not infer unheard words."
        });
        return new(turn.InputId, false, 512, KeepProviderContext: true, AutomaticToolChoice: true,
            AvailableTools: available, PlaybackContext: snapshot);
    }
}
