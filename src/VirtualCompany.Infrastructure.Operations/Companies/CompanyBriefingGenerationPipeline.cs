using System.Globalization;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyBriefingGenerationPipeline : IBriefingGenerationPipeline
{
    private readonly ICompanyBriefingService _briefingService;

    public CompanyBriefingGenerationPipeline(ICompanyBriefingService briefingService)
    {
        _briefingService = briefingService;
    }

    public Task<CompanyBriefingGenerationResult> GenerateAsync(
        BriefingGenerationJobContext job,
        CancellationToken cancellationToken)
    {
        var briefingType = ResolveBriefingType(job).ToStorageValue();

        return _briefingService.GenerateAsync(
            job.CompanyId,
            new GenerateCompanyBriefingCommand(
                briefingType,
                NowUtc: ResolveGenerationNowUtc(job),
                Force: string.Equals(job.TriggerType, CompanyBriefingUpdateJobTriggerTypeValues.EventDriven, StringComparison.OrdinalIgnoreCase),
                CorrelationId: job.CorrelationId),
            cancellationToken);
    }

    private static DateTime ResolveGenerationNowUtc(BriefingGenerationJobContext job)
    {
        if (IsScheduledTrigger(job.TriggerType) &&
            TryGetUtcMetadata(job.SourceMetadata, "periodEndUtc", out var scheduledPeriodEndUtc))
        {
            return scheduledPeriodEndUtc;
        }

        return DateTime.UtcNow;
    }

    private static CompanyBriefingType ResolveBriefingType(BriefingGenerationJobContext job) =>
        CompanyBriefingTypeValues.TryParse(job.BriefingType, out var briefingType)
            ? briefingType
            : CompanyBriefingType.Daily;

    private static bool IsScheduledTrigger(string triggerType) =>
        string.Equals(triggerType, CompanyBriefingUpdateJobTriggerTypeValues.Daily, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(triggerType, CompanyBriefingUpdateJobTriggerTypeValues.Weekly, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetUtcMetadata(
        Dictionary<string, JsonNode?> metadata,
        string key,
        out DateTime value)
    {
        value = default;
        if (!metadata.TryGetValue(key, out var node) || node is not JsonValue jsonValue)
        {
            return false;
        }

        try
        {
            if (jsonValue.TryGetValue<DateTime>(out var dateTime))
            {
                value = NormalizeUtc(dateTime);
                return true;
            }

            if (jsonValue.TryGetValue<string>(out var text) &&
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dateTime))
            {
                value = NormalizeUtc(dateTime);
                return true;
            }
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}