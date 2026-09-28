using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

// Sales profile of the shared, provider-neutral controller. All authority comes from scoped
// database records, not a transcript/tool payload. Existing records remain the only floor owner.
internal static class SalesRoomConversationPolicy
{
    internal static async Task<AgentConversation?> BeginInputAsync(VirtualCompanyDbContext db, SalesRoomAgentOptions options,
        Guid company, Guid room, Guid participant, Guid inputId, DateTime now, CancellationToken ct)
    {
        if (!options.HybridConversationEnabled) return null;
        var authority = await LoadAsync(db, options, company, room, participant, now, ct)
            ?? throw new SalesRoomAgentException("conversation_unavailable", "The conversation binding is no longer available.");
        var conversation = new AgentConversation(authority.Binding, AgentConversationPhase.Presenting);
        var decision = conversation.Authorize(authority, AgentConversationAction.Interpret, now);
        if (!decision.Allowed) throw new SalesRoomAgentException(decision.Code, "The conversation changed. Refresh before asking again.");
        conversation.Heard(inputId, conversation.Version, authority, now);
        conversation.Retrieve(conversation.Version, authority, now);
        return conversation;
    }

    internal static async Task<AgentConversationDecision> RecheckAsync(VirtualCompanyDbContext db,
        SalesRoomAgentOptions options, AgentConversation conversation, DateTime now, CancellationToken ct)
    {
        var b = conversation.Binding;
        var current = await LoadAsync(db, options, b.CompanyId, b.ConversationId, b.ParticipantId, now, ct);
        return current is null ? new(false, "conversation_unavailable") : conversation.Check(current, now);
    }

    internal static async Task<AgentConversationAuthority?> LoadAsync(VirtualCompanyDbContext db,
        SalesRoomAgentOptions options, Guid company, Guid roomId, Guid participantId, DateTime now, CancellationToken ct)
    {
        var room = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == roomId, ct);
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == company && x.RoomId == roomId, ct);
        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == company && x.RoomId == roomId && x.Id == participantId, ct);
        if (room?.AgentId is not Guid agent || room.MeetingSessionId is not Guid session ||
            room.AgentLeaseOwnerId is not Guid owner || floor is null || participant is null) return null;
        var allConsent = !await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == company && x.RoomId == roomId && x.State == SalesRoomParticipantStates.Admitted && !x.AiProcessingAllowed, ct);
        return new(new(company, agent, roomId, session, participantId, participant.Generation, owner,
                room.AgentGeneration, room.AgentTurnGeneration, floor.ResponseGeneration, floor.ControlMode,
                AgentConversation.PolicyVersion, floor.PresentationVersion, floor.SlideNumber, floor.TalkingPointIndex,
                floor.ResumeOffsetMilliseconds), options.HybridConversationEnabled,
            participant.State == SalesRoomParticipantStates.Admitted && participant.ExpiresUtc > now,
            allConsent && participant.AiProcessingAllowed,
            room.State == SalesBrowserRoomStates.Live && room.IsAgentOwner(owner, room.AgentGeneration, now) &&
                room.AgentStartedUtc > now.AddMinutes(-options.MaximumSessionMinutes) &&
                room.AgentLastErrorCode != "host_takeover",
            SalesRoomOperationsPolicy.AudioLimitProblem(room, options) is null && !options.EmergencyDisabled &&
                (options.MaximumSpendPerCallUsd > 0
                    ? SalesRoomOperationsPolicy.EstimatedSpend(room, options) < options.MaximumSpendPerCallUsd
                    : !options.Enabled),
            participant.Id == floor.HostParticipantId || participant.Id == floor.PreauthorizedCoHostParticipantId,
            room.ExpiresUtc);
    }

    internal static bool Supported(SalesMeetingQuestion question) =>
        question.IsSafeNoEvidenceLimitation ||
        question.Status is SalesMeetingQuestionStatus.Completed or SalesMeetingQuestionStatus.PartiallySupported &&
        question.Evidence.Count > 0 && !string.IsNullOrWhiteSpace(question.AnswerText);

    internal static bool Released(SalesMeetingQuestion question) =>
        question.Visibility == SalesMeetingAnswerVisibility.ApprovedForStage && question.StageApprovedUtc.HasValue;
}
