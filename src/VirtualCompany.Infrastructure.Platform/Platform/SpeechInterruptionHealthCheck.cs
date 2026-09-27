using Microsoft.Extensions.Diagnostics.HealthChecks;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Infrastructure.Platform;

internal sealed class SpeechInterruptionHealthCheck(ISpeechFrameClassifierFactory classifiers) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var classifier = classifiers.Create();
            var silence = new short[480];
            return Task.FromResult(classifier.IsSpeech(silence, 24_000)
                ? HealthCheckResult.Unhealthy("Speech classifier identified silence as speech.")
                : HealthCheckResult.Healthy("Speech classifier is available."));
        }
        catch
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Speech classifier is unavailable; automatic interruption must remain disabled."));
        }
    }
}
