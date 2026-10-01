using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

internal static class SalesRoomConversationStatusProjection
{
    internal static SalesRoomConversationStatusView Project(SalesBrowserRoom room, SalesRoomFloor? floor,
        SalesRoomAgentSpeech? currentSpeech, SalesRoomAgentAnswerView? answer,
        bool enabled, RealtimeAgentHealth? provider, string? effectiveErrorCode, string? effectiveError,
        DateTime now, bool continuous = false)
    {
        if (room.State is SalesBrowserRoomStates.Ending or SalesBrowserRoomStates.Ended || room.ExpiresUtc <= now)
            return new("expired", "expired", "This room has ended. Book a new meeting to use room AI again.");

        var quota = effectiveErrorCode is "quota_exceeded" or "duration_limit" or "spend_limit" or
            SalesRoomAgentProblemCodes.QuotaExceeded or SalesRoomAgentProblemCodes.SpendLimit;
        if (quota) return new("paused", "quota", effectiveError ?? "The room AI allowance is exhausted.");

        if (room.AgentHealth == SalesRoomAgentHealthStates.Stopped)
            return new("stopped", "stopped", effectiveError ?? "Alex has stopped. Check consent and use Start agent to reconnect.");

        var confirmedHandoff = continuous && room.AgentLastErrorCode == "human_speaking" &&
            room.AgentVoiceHealth == "healthy" && room.AgentLeaseExpiresUtc > now && floor?.ControlMode == "autonomous";
        if (room.AgentHealth == SalesRoomAgentHealthStates.Paused && !confirmedHandoff)
            return new("paused", ClassifyFailure(effectiveErrorCode),
                effectiveError ?? "AI is paused. Human calling and manual slides remain available.");

        if (!enabled)
            return new("legacy", "off", effectiveErrorCode == "conversation_disabled"
                ? effectiveError ?? "Realtime conversation was turned off. Restart the agent for approved-answer mode."
                : "Realtime conversation is off. Approved answers and manual controls remain available.");
        if (provider?.Available != true)
            return new("unavailable", provider?.Enabled == true && provider.Configured ? "provider_unavailable" : "configuration",
                provider?.Enabled == true && provider.Configured
                    ? "Realtime voice is unavailable. Use approved answers and manual controls."
                    : "Realtime voice is not configured. Use approved answers and manual controls.");
        if (room.AgentHealth is SalesRoomAgentHealthStates.NotStarted or
            SalesRoomAgentHealthStates.Unavailable)
            return new("not_started", "available", "Start the agent after all participants have consented.");
        if (room.AgentLeaseExpiresUtc is null || room.AgentLeaseExpiresUtc <= now)
            return new("unavailable", "lease_expired",
                "The agent connection expired. Restart the agent; human calling and manual slides remain available.");

        if (room.AgentHealth == SalesRoomAgentHealthStates.Starting)
        {
            if (effectiveErrorCode == "provider_reconnecting")
                return new("reconnecting", "reconnecting", effectiveError);
            return new("connecting", "available", "The agent is connecting. Wait for the voice track before asking aloud.");
        }
        if (room.AgentHealth == SalesRoomAgentHealthStates.Ready && effectiveErrorCode is not null && continuous)
            return new("listening", "available", effectiveError);
        if (room.AgentHealth == SalesRoomAgentHealthStates.Ready && effectiveErrorCode is "question_rejected" or "conversation_reply_withheld")
            return new("ready", "available", effectiveError);

        if (floor?.PendingTurnId is not null && floor.PendingTurnState is
            SalesRoomPendingTurnStates.ConfirmationRequired or SalesRoomPendingTurnStates.HostInvocationRequired)
            return new("awaiting_approval", "available", null);
        if (floor?.PendingQuestionId is Guid pendingQuestion && answer?.QuestionId == pendingQuestion &&
            answer.Status is not ("completed" or "partially_supported" or "failed"))
            return new("checking_sources", "available", null);
        if (floor?.PendingQuestionId is Guid failedQuestion && answer?.QuestionId == failedQuestion &&
            answer.Status == "failed")
            return new(continuous ? "listening" : "paused", continuous ? "available" : "paused",
                "Answer generation failed. Retry the question or continue the human meeting.");
        if (currentSpeech is { Kind: SalesRoomAgentSpeechKinds.Answer or SalesRoomAgentSpeechKinds.Limitation,
                Status: SalesRoomAgentSpeechStates.Queued or SalesRoomAgentSpeechStates.Processing })
            return new("answering", "available", null);
        if (currentSpeech is { Kind: SalesRoomAgentSpeechKinds.Bridge or SalesRoomAgentSpeechKinds.Conversation,
                Status: SalesRoomAgentSpeechStates.Queued or SalesRoomAgentSpeechStates.Processing })
            return new("answering", "available", null);
        if (currentSpeech is { Kind: SalesRoomAgentSpeechKinds.Narration,
                Status: SalesRoomAgentSpeechStates.Queued } && floor?.State == SalesRoomFloorStates.Agent)
            return new("resuming", "available", null);
        if (floor?.PendingQuestionId is Guid releasedQuestion && answer?.QuestionId == releasedQuestion &&
            answer.Status is "completed" or "partially_supported" &&
            floor.PendingTurnState == SalesRoomPendingTurnStates.Authorized && currentSpeech is null)
            return new("awaiting_recovery", "available",
                "The answer is ready but automatic speech has not started. Review the evidence and use the host recovery control.");
        if (!continuous && currentSpeech is { Kind: SalesRoomAgentSpeechKinds.Bridge or SalesRoomAgentSpeechKinds.Answer,
                Status: SalesRoomAgentSpeechStates.Spoken, CompletedUtc: DateTime completed } &&
            floor is { State: SalesRoomFloorStates.Host, PendingTurnId: null } &&
            floor.ControlMode == "autonomous" &&
            currentSpeech.AgentGeneration == room.AgentGeneration &&
            currentSpeech.TurnGeneration == floor.TurnGeneration &&
            currentSpeech.ResponseGeneration == floor.ResponseGeneration &&
            completed <= now && completed.AddSeconds(45) > now)
            return new("listening_for_reply", "available", null);
        if (floor?.State == SalesRoomFloorStates.Agent && room.AgentHealth == SalesRoomAgentHealthStates.Speaking)
            return new("presenting", "available", null);
        if (floor?.PendingTurnId is not null)
            return new("checking_sources", "available", null);
        return new(continuous ? floor?.State == SalesRoomFloorStates.Paused ? "presentation_paused" : "listening" : "ready", "available", null);
    }

    private static string ClassifyFailure(string? code) => code switch
    {
        "voice_unavailable" or "transcription_unavailable" or "session_expired" or
            "provider_unavailable" or "provider_error" => "provider_unavailable",
        "conversation_unavailable" or "unsupported_configuration" or "credentials_missing" => "configuration",
        _ => "paused"
    };
}
