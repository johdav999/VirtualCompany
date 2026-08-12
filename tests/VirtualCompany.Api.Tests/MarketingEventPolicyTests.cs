using VirtualCompany.Application.Marketing;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingEventPolicyTests
{
    [Theory]
    [InlineData(MarketingEventTypes.ConsentIncident, "critical")]
    [InlineData(MarketingEventTypes.BrandIncident, "critical")]
    [InlineData(MarketingEventTypes.ProviderFailure, "warning")]
    [InlineData(MarketingEventTypes.ContentOverdue, "warning")]
    [InlineData(MarketingEventTypes.ExperimentThreshold, "info")]
    public void Severity_is_deterministic(string eventType,string expected) =>
        Assert.Equal(expected,MarketingEventPolicy.Severity(eventType));

    [Fact]
    public void Every_supported_event_has_a_deterministic_severity()
    {
        foreach(var eventType in MarketingEventTypes.All)
            Assert.Contains(MarketingEventPolicy.Severity(eventType),new[]{"info","warning","critical"});
    }
}
