using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceSeedTelemetry : IFinanceSeedTelemetry
{
    private readonly ILogger<FinanceSeedTelemetry> _logger;

    public FinanceSeedTelemetry(ILogger<FinanceSeedTelemetry> logger)
    {
        _logger = logger;
    }

    public Task TrackAsync(string eventName, FinanceSeedTelemetryContext context, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["TelemetryEventName"] = eventName,
            ["CompanyId"] = context.CompanyId,
            ["UserId"] = context.UserId,
            ["JobId"] = context.JobId,
            ["CorrelationId"] = context.CorrelationId,
            ["IdempotencyKey"] = context.IdempotencyKey,
            ["TriggerSource"] = context.TriggerSource,
            ["SeedStateBefore"] = context.SeedStateBefore.ToStorageValue(),
            ["SeedStateAfter"] = context.SeedStateAfter.ToStorageValue(),
            ["JobAlreadyRunning"] = context.JobAlreadyRunning,
            ["Attempt"] = context.Attempt,
            ["MaxAttempts"] = context.MaxAttempts,
            ["DurationMs"] = context.DurationMs,
            ["ErrorType"] = context.ErrorType,
            ["ErrorMessageSafe"] = context.ErrorMessageSafe,
            ["SeedMode"] = context.SeedMode,
            ["ActorType"] = context.ActorType,
            ["ActorId"] = context.ActorId
        });

        _logger.LogInformation(
            "Finance auto-seed telemetry event {EventName} recorded for company {CompanyId} and job {JobId}.",
            eventName,
            context.CompanyId,
            context.JobId);

        return Task.CompletedTask;
    }
}