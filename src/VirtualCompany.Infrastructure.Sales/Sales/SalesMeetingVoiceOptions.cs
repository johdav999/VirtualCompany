using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingVoiceOptions
{
    public const string SectionName = "SalesMeetingVoice";

    public bool Enabled { get; set; }
    public bool PilotApproved { get; set; }
    public string MediaRoute { get; set; } = "browser_webrtc";
    public int MaximumSessionMinutes { get; set; } = 30;
    public int MaximumReconnects { get; set; } = 2;
    public int MaximumAudioSeconds { get; set; } = 1800;
    public int MaximumInputTokens { get; set; } = 50_000;
    public int MaximumOutputTokens { get; set; } = 10_000;
}

public sealed class ConfiguredMeetingMediaAdapter(IOptions<SalesMeetingVoiceOptions> configured) : IMeetingMediaAdapter
{
    public Task<MeetingMediaAdapterHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        var options = configured.Value;
        var approvedRoute = options.PilotApproved && string.Equals(options.MediaRoute, "browser_webrtc", StringComparison.Ordinal);
        var available = options.Enabled && approvedRoute;
        var reason = !options.Enabled ? "feature_disabled" : !options.PilotApproved ? "pilot_not_approved" : !approvedRoute ? "media_route_not_approved" : null;
        return Task.FromResult(new MeetingMediaAdapterHealth(options.Enabled, options.PilotApproved, available,
            options.MediaRoute, available ? "available" : "degraded", reason,
            available ? null : "Voice media is unavailable; typed and host-mediated meeting controls remain available."));
    }

    public MeetingMediaEvent Normalize(RealtimeAgentEvent value)
    {
        var speaker = value.Type == RealtimeAgentEventTypes.AgentTranscriptCompleted ? "agent" : "participant";
        return new MeetingMediaEvent(value.EventId, value.Sequence, value.Type, speaker,
            value.SpeakerLabel, value.Text, value.ToolCallId, value.ToolName, value.ToolArgumentsJson,
            value.ResponseId, value.AudioDurationMilliseconds, value.InputTokens, value.OutputTokens,
            value.ErrorCode, value.ErrorSummary);
    }
}
