using Microsoft.Extensions.Diagnostics.HealthChecks;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Platform;

namespace VirtualCompany.Api.Tests;

public sealed class SpeechInterruptionHealthCheckTests
{
    [Fact]
    public async Task Real_classifier_is_ready()
    {
        var result = await new SpeechInterruptionHealthCheck(new WebRtcSpeechFrameClassifierFactory())
            .CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Unavailable_classifier_fails_readiness_instead_of_falling_back_to_energy()
    {
        var result = await new SpeechInterruptionHealthCheck(new FailedFactory())
            .CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private sealed class FailedFactory : ISpeechFrameClassifierFactory
    {
        public ISpeechFrameClassifier Create() => throw new InvalidOperationException("classifier unavailable");
    }
}
