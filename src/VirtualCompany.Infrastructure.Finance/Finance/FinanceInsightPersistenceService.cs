using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceInsightPersistenceService : IFinanceInsightPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IFinanceAgentInsightRepository _repository;
    private readonly TimeProvider _timeProvider;

    public FinanceInsightPersistenceService(
        IFinanceAgentInsightRepository repository,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ReconcileAsync(
        FinancialCheckContext context,
        IReadOnlyList<string> executedCheckCodes,
        IReadOnlyList<FinancialCheckResult> currentResults,
        CancellationToken cancellationToken)
    {
        var normalizedCodes = executedCheckCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedCodes.Length == 0)
        {
            return;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var existing = await _repository.ListByCheckCodesAsync(context.CompanyId, normalizedCodes, cancellationToken);
        var existingByIdentity = existing.ToDictionary(BuildIdentity, StringComparer.OrdinalIgnoreCase);
        var activeIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in currentResults)
        {
            var identity = BuildIdentity(
                result.CheckCode,
                result.ConditionKey,
                result.EntityType,
                result.EntityId);
            var affectedEntitiesJson = JsonSerializer.Serialize(result.AffectedEntities, JsonOptions);
            var observedUtc = NormalizeObservedUtc(result.ObservedAtUtc, context.AsOfUtc);

            if (existingByIdentity.TryGetValue(identity, out var insight))
            {
                if (result.IsActive)
                {
                    activeIdentities.Add(identity);
                    insight.Refresh(
                        result.EntityType,
                        result.EntityId,
                        result.Severity,
                        result.Message,
                        result.Recommendation,
                        result.Confidence,
                        result.PrimaryEntity?.DisplayName,
                        affectedEntitiesJson,
                        result.MetadataJson,
                        observedUtc,
                        utcNow);
                }
                else if (insight.Status == FinanceInsightStatus.Active)
                {
                    insight.MarkResolved(observedUtc, utcNow);
                }

                continue;
            }

            if (!result.IsActive)
            {
                continue;
            }

            activeIdentities.Add(identity);
            var createdInsight = new FinanceAgentInsight(
                Guid.NewGuid(),
                context.CompanyId,
                result.CheckCode,
                result.ConditionKey,
                result.EntityType,
                result.EntityId,
                result.Severity,
                result.Message,
                result.Recommendation,
                result.Confidence,
                result.PrimaryEntity?.DisplayName,
                affectedEntitiesJson,
                result.MetadataJson,
                FinanceInsightStatus.Active,
                observedUtc,
                utcNow,
                utcNow);
            await _repository.AddAsync(createdInsight, cancellationToken);
            existingByIdentity[identity] = createdInsight;
        }

        // Checks emit only active conditions by default, so any previously-active identity
        // not seen in the current batch should be resolved rather than duplicated.
        foreach (var activeInsight in existing.Where(x =>
                     x.Status == FinanceInsightStatus.Active &&
                     normalizedCodes.Contains(x.CheckCode, StringComparer.OrdinalIgnoreCase) &&
                     !activeIdentities.Contains(BuildIdentity(x))))
        {
            activeInsight.MarkResolved(context.AsOfUtc, utcNow);
        }

        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FinanceInsightDto>> ListAsync(
        Guid companyId,
        string? entityType,
        string? entityId,
        bool includeResolved,
        CancellationToken cancellationToken)
    {
        var insights = await _repository.ListAsync(companyId, includeResolved, cancellationToken);
        var normalizedEntityType = NormalizeFilter(entityType);
        var normalizedEntityId = NormalizeFilter(entityId);

        return insights
            .Select(Map)
            .Where(x => MatchesEntityFilter(x, normalizedEntityType, normalizedEntityId))
            .ToArray();
    }

    private static FinanceInsightDto Map(FinanceAgentInsight insight)
    {
        var definition = FinancialCheckDefinitions.Resolve(insight.CheckCode);
        var affectedEntities = DeserializeAffectedEntities(insight.AffectedEntitiesJson);
        var primaryEntity = affectedEntities.FirstOrDefault(x => x.IsPrimary) ??
            new FinanceInsightEntityReferenceDto(
                insight.EntityType,
                insight.EntityId,
                insight.EntityDisplayName,
                true);

        return new FinanceInsightDto(
            insight.Id,
            insight.CheckCode,
            definition.Name,
            insight.ConditionKey,
            insight.Severity.ToStorageValue(),
            insight.Message,
            insight.Recommendation,
            insight.Status.ToStorageValue(),
            insight.Confidence,
            primaryEntity,
            affectedEntities,
            insight.EntityType,
            insight.EntityId,
            insight.ObservedUtc,
            insight.CreatedUtc,
            insight.UpdatedUtc,
            insight.ResolvedUtc,
            string.Equals(insight.MetadataJson, "{}", StringComparison.Ordinal) ? null : insight.MetadataJson);
    }

    private static bool MatchesEntityFilter(
        FinanceInsightDto insight,
        string? entityType,
        string? entityId)
    {
        if (entityType is null && entityId is null)
        {
            return true;
        }

        return (insight.PrimaryEntity is not null && MatchesEntity(insight.PrimaryEntity, entityType, entityId)) ||
               insight.AffectedEntities.Any(x => MatchesEntity(x, entityType, entityId));
    }

    private static bool MatchesEntity(
        FinanceInsightEntityReferenceDto entity,
        string? entityType,
        string? entityId) =>
        (entityType is null || string.Equals(entity.EntityType, entityType, StringComparison.OrdinalIgnoreCase)) &&
        (entityId is null || string.Equals(entity.EntityId, entityId, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static IReadOnlyList<FinanceInsightEntityReferenceDto> DeserializeAffectedEntities(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<FinanceInsightEntityReferenceDto>>(payload, JsonOptions) ?? [];
    }

    private static string BuildIdentity(FinanceAgentInsight insight) =>
        BuildIdentity(insight.CheckCode, insight.ConditionKey, insight.EntityType, insight.EntityId);

    private static string BuildIdentity(
        string checkCode,
        string conditionKey,
        string entityType,
        string entityId) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{checkCode.Trim().ToLowerInvariant()}|{conditionKey.Trim().ToLowerInvariant()}|{entityType.Trim().ToLowerInvariant()}|{entityId.Trim()}");

    private static DateTime NormalizeObservedUtc(DateTime? observedUtc, DateTime fallbackUtc) =>
        observedUtc.HasValue
            ? (observedUtc.Value.Kind == DateTimeKind.Utc ? observedUtc.Value : observedUtc.Value.ToUniversalTime())
            : (fallbackUtc.Kind == DateTimeKind.Utc ? fallbackUtc : fallbackUtc.ToUniversalTime());
}
