using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceReadService
{
    private static readonly JsonSerializerOptions InsightReadModelSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<NormalizedFinanceInsightsDto> GetNormalizedInsightsAsync(
        GetNormalizedFinanceInsightsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        var repository = new FinanceAgentInsightRepository(_dbContext);
        var insights = await repository.QueryAsync(query, cancellationToken);
        var normalizedEntityType = NormalizeInsightFilter(query.EntityType);
        var normalizedEntityId = NormalizeInsightFilter(query.EntityId);

        var items = insights
            .Select(MapNormalizedInsight)
            .Where(x => MatchesNormalizedEntityFilter(x, normalizedEntityType, normalizedEntityId))
            .ToArray();

        return new NormalizedFinanceInsightsDto(query.CompanyId, items);
    }

    private static NormalizedFinanceInsightDto MapNormalizedInsight(FinanceAgentInsight insight)
    {
        var definition = FinancialCheckDefinitions.Resolve(insight.CheckCode);
        var affectedEntities = DeserializeInsightEntities(insight.AffectedEntitiesJson);
        var entityReference = affectedEntities.FirstOrDefault(x => x.IsPrimary) ??
            new FinanceInsightEntityReferenceDto(
                insight.EntityType,
                insight.EntityId,
                insight.EntityDisplayName,
                true);

        return new NormalizedFinanceInsightDto(
            insight.Id,
            insight.Severity.ToStorageValue(),
            insight.Message,
            insight.Recommendation,
            entityReference,
            insight.Status.ToStorageValue(),
            insight.CreatedUtc,
            insight.UpdatedUtc,
            insight.CheckCode,
            definition.Name,
            insight.ConditionKey,
            affectedEntities,
            insight.ObservedUtc,
            insight.ResolvedUtc);
    }

    private static IReadOnlyList<FinanceInsightEntityReferenceDto> DeserializeInsightEntities(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<FinanceInsightEntityReferenceDto>>(payload, InsightReadModelSerializerOptions) ?? [];
    }

    private static bool MatchesNormalizedEntityFilter(NormalizedFinanceInsightDto insight, string? entityType, string? entityId)
    {
        if (entityType is null && entityId is null)
        {
            return true;
        }

        return MatchesEntityReference(insight.EntityReference, entityType, entityId) ||
               insight.AffectedEntities.Any(x => MatchesEntityReference(x, entityType, entityId));
    }

    private static bool MatchesEntityReference(FinanceInsightEntityReferenceDto entity, string? entityType, string? entityId) =>
        (entityType is null || string.Equals(entity.EntityType, entityType, StringComparison.OrdinalIgnoreCase)) &&
        (entityId is null || string.Equals(entity.EntityId, entityId, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeInsightFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}