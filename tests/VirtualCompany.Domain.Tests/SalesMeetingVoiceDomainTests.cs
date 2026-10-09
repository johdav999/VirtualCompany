using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingVoiceDomainTests
{
    [Fact]
    public void Voice_session_orders_usage_and_revokes_consent_with_concurrency()
    {
        var now = DateTime.UtcNow;
        var voice = new SalesMeetingVoiceSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "browser_webrtc", now.AddMinutes(10), now);
        voice.Activate("openai", "call_123", "gpt-realtime", "webrtc", now.AddMinutes(10), now);
        voice.RecordEvent(1, 1200, 10, 5, now.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => voice.RecordEvent(1, 1, 1, 1, now.AddSeconds(2)));
        voice.RevokeConsent(voice.ConcurrencyVersion, now.AddSeconds(3));

        Assert.Equal(SalesMeetingVoiceSessionStatus.ConsentRevoked, voice.Status);
        Assert.Equal(1200, voice.AudioDurationMilliseconds);
        Assert.NotNull(voice.EndedUtc);
    }

    [Fact]
    public void Event_receipt_bounds_results_and_records_guardrail_outcome()
    {
        var receipt = new SalesMeetingVoiceEventReceipt(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "event-1", 1, "tool_invocation", DateTime.UtcNow);
        receipt.Complete(SalesMeetingVoiceEventOutcome.ToolRejected, null, "{\"error\":\"tool_not_allowed\"}", "tool_not_allowed");

        Assert.Equal(SalesMeetingVoiceEventOutcome.ToolRejected, receipt.Outcome);
        Assert.Equal("tool_not_allowed", receipt.ReasonCode);
    }
}
