using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Briefings;

public sealed class BriefingInsightAggregationService : IBriefingInsightAggregationService
{
    public AggregatedBriefingPayloadDto Aggregate(BriefingInsightAggregationRequest request)
    {
        if (request.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(request));
        }

        var scopedContributions = request.Contributions
            .Where(contribution => IsInScope(contribution, request.CompanyId, request.TenantId))
            .Select(NormalizeContribution)
            .Where(contribution => !string.IsNullOrWhiteSpace(contribution.Topic) || !string.IsNullOrWhiteSpace(contribution.Narrative))
            .ToList();

        var sections = scopedContributions
            .GroupBy(contribution => ResolveGroupingKey(contribution))
            .OrderBy(group => SortRank(group.Key.GroupingType))
            .ThenBy(group => group.Key.GroupingValue, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildSection(group.Key, group.ToList()))
            .ToList();

        return new AggregatedBriefingPayloadDto(
            request.CompanyId,
            request.TenantId,
            BuildNarrative(sections),
            sections);
    }

    private static bool IsInScope(BriefingInsightContributionDto contribution, Guid companyId, Guid? tenantId)
    {
        if (contribution.CompanyId != companyId)
        {
            return false;
        }

        return !tenantId.HasValue ||
               !contribution.TenantId.HasValue ||
               contribution.TenantId == tenantId;
    }

    private static BriefingInsightContributionDto NormalizeContribution(BriefingInsightContributionDto contribution) =>
        contribution with
        {
            TimestampUtc = NormalizeUtc(contribution.TimestampUtc),
            Topic = NormalizeWhitespace(contribution.Topic),
            Narrative = NormalizeWhitespace(contribution.Narrative),
            Assessment = NormalizeOptional(contribution.Assessment),
            EventCorrelationId = NormalizeOptional(contribution.EventCorrelationId),
            Metadata = CloneNodes(contribution.Metadata),
            ConfidenceMetadata = CloneNodes(contribution.ConfidenceMetadata)
        };

    private static GroupingKey ResolveGroupingKey(BriefingInsightContributionDto contribution)
    {
        if (contribution.CompanyEntityId is { } companyEntityId && companyEntityId != Guid.Empty)
        {
            return new GroupingKey(BriefingInsightGroupingTypes.CompanyEntity, companyEntityId.ToString("N"));
        }

        if (contribution.WorkflowInstanceId is { } workflowInstanceId && workflowInstanceId != Guid.Empty)
        {
            return new GroupingKey(BriefingInsightGroupingTypes.Workflow, workflowInstanceId.ToString("N"));
        }

        if (contribution.TaskId is { } taskId && taskId != Guid.Empty)
        {
            return new GroupingKey(BriefingInsightGroupingTypes.Task, taskId.ToString("N"));
        }

        if (!string.IsNullOrWhiteSpace(contribution.EventCorrelationId))
        {
            return new GroupingKey(BriefingInsightGroupingTypes.EventCorrelation, contribution.EventCorrelationId.Trim().ToLowerInvariant());
        }

        var topicKey = NormalizeKey(contribution.Topic);
        return new GroupingKey(BriefingInsightGroupingTypes.Topic, string.IsNullOrWhiteSpace(topicKey) ? "untitled" : topicKey);
    }

    private static AggregatedBriefingSectionDto BuildSection(GroupingKey groupingKey, IReadOnlyList<BriefingInsightContributionDto> contributions)
    {
        var groupingType = groupingKey.GroupingType;
        var orderedContributions = contributions
            .OrderByDescending(contribution => contribution.TimestampUtc)
            .ThenBy(contribution => contribution.AgentId)
            .ThenBy(contribution => contribution.SourceReference.EntityType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(contribution => contribution.SourceReference.EntityId)
            .ToList();
        var viewpoints = BuildConflictViewpoints(orderedContributions);
        var isConflicting = viewpoints.Count > 1;
        var title = ResolveTitle(orderedContributions);

        return new AggregatedBriefingSectionDto(
            $"{groupingType}:{groupingKey.GroupingValue}",
            title,
            groupingType,
            groupingKey.GroupingValue,
            BuildSectionNarrative(title, orderedContributions, viewpoints),
            isConflicting,
            orderedContributions,
            isConflicting ? viewpoints : [],
            DeduplicateReferences(orderedContributions.Select(x => x.SourceReference)));
    }

    private static IReadOnlyList<BriefingConflictViewpointDto> BuildConflictViewpoints(IReadOnlyList<BriefingInsightContributionDto> contributions) =>
        contributions
            .Where(contribution => !string.IsNullOrWhiteSpace(contribution.Assessment))
            .GroupBy(contribution => NormalizeKey(contribution.Assessment!), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new BriefingConflictViewpointDto(
                group.First().Assessment!,
                group.Select(contribution => contribution.AgentId).Where(agentId => agentId != Guid.Empty).Distinct().OrderBy(agentId => agentId).ToList(),
                group.Select(contribution => contribution.Narrative).Where(narrative => !string.IsNullOrWhiteSpace(narrative)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList(),
                DeduplicateReferences(group.Select(contribution => contribution.SourceReference))))
            .ToList();

    private static string BuildSectionNarrative(
        string title,
        IReadOnlyList<BriefingInsightContributionDto> contributions,
        IReadOnlyList<BriefingConflictViewpointDto> viewpoints)
    {
        if (viewpoints.Count > 1)
        {
            var labels = string.Join("; ", viewpoints.Select(viewpoint => $"{viewpoint.Assessment}: {string.Join(", ", viewpoint.Narratives.Take(2))}"));
            return $"Agents disagree on {title}. Viewpoints: {labels}.";
        }

        var summaries = contributions
            .Select(contribution => contribution.Narrative)
            .Where(narrative => !string.IsNullOrWhiteSpace(narrative))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return summaries.Count == 0
            ? title
            : string.Join(" ", summaries);
    }

    private static string BuildNarrative(IReadOnlyList<AggregatedBriefingSectionDto> sections)
    {
        if (sections.Count == 0)
        {
            return "No cross-agent briefing insights were available for this scope.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Aggregated {sections.Count.ToString(CultureInfo.InvariantCulture)} briefing section(s).");
        foreach (var section in sections.Take(8))
        {
            var marker = section.IsConflicting ? "Conflict detected" : "Aligned";
            builder.AppendLine($"- {section.Title}: {marker}. {section.Narrative}");
        }

        return builder.ToString().Trim();
    }

    private static string ResolveTitle(IReadOnlyList<BriefingInsightContributionDto> contributions)
    {
        var topic = contributions
            .Select(contribution => contribution.Topic)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (!string.IsNullOrWhiteSpace(topic))
        {
            return topic;
        }

        var reference = contributions[0].SourceReference;
        return string.IsNullOrWhiteSpace(reference.Label)
            ? $"{reference.EntityType} {reference.EntityId:N}"
            : reference.Label;
    }

    private static IReadOnlyList<BriefingSourceReferenceDto> DeduplicateReferences(IEnumerable<BriefingSourceReferenceDto> references) =>
        references
            .Where(reference => reference.EntityId != Guid.Empty && !string.IsNullOrWhiteSpace(reference.EntityType))
            .GroupBy(reference => new { EntityType = reference.EntityType.Trim().ToLowerInvariant(), reference.EntityId })
            .Select(group => group.First())
            .OrderBy(reference => reference.EntityType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.EntityId)
            .ToList();

    private static int SortRank(string groupingType) =>
        groupingType switch
        {
            BriefingInsightGroupingTypes.CompanyEntity => 0,
            BriefingInsightGroupingTypes.Workflow => 1,
            BriefingInsightGroupingTypes.Task => 2,
            BriefingInsightGroupingTypes.EventCorrelation => 3,
            _ => 4
        };

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizeOptional(string? value)
    {
        var normalized = NormalizeWhitespace(value ?? string.Empty);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeKey(string value) =>
        NormalizeWhitespace(value).ToLowerInvariant();

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private sealed record GroupingKey(string GroupingType, string GroupingValue)
    {
        public override string ToString() => GroupingValue;
    }
}
