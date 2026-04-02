using System.Globalization;
using System.Text;

namespace VirtualCompany.Application.Context;

public static class GroundedContextPromptReadyMapper
{
    private static readonly IReadOnlyDictionary<string, int> RelevantRecordSourcePriority =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["knowledge"] = 0,
            ["memory"] = 1,
            ["recent_tasks"] = 2,
            ["relevant_records"] = 3
        };

    private static readonly IReadOnlyDictionary<string, int> SectionPriority =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [GroundedContextSourceTypes.ApprovalRequest] = 0,
            [GroundedContextSourceTypes.AgentRecord] = 1,
            [GroundedContextSourceTypes.CompanyRecord] = 2
        };

    public static NormalizedGroundedContextDto Normalize(
        DateTime generatedAtUtc,
        RetrievalSectionDto knowledgeSection,
        RetrievalSectionDto memorySection,
        RetrievalSectionDto recentTaskSection,
        RetrievalSectionDto relevantRecordsSection,
        IReadOnlyList<RetrievalSourceReferenceDto> sourceReferences)
    {
        var normalizedSourceReferences = NormalizeSourceReferences(sourceReferences);
        var sourceLookup = BuildSourceLookup(normalizedSourceReferences);

        var documents = NormalizeDocuments(knowledgeSection, sourceLookup);
        var memory = NormalizeMemory(memorySection, sourceLookup);
        var recentTasks = NormalizeRecentTasks(recentTaskSection, sourceLookup);
        var relevantRecords = NormalizeRelevantRecords(relevantRecordsSection, sourceLookup);

        var sections = new GroundedContextSectionDescriptorDto[]
        {
            new(documents.Id, documents.Title, 0, documents.Items.Count),
            new(memory.Id, memory.Title, 1, memory.Items.Count),
            new(recentTasks.Id, recentTasks.Title, 2, recentTasks.Items.Count),
            new(relevantRecords.Id, relevantRecords.Title, 3, relevantRecords.Items.Count)
        };

        return new NormalizedGroundedContextDto(
            sections,
            documents,
            memory,
            recentTasks,
            relevantRecords,
            normalizedSourceReferences,
            new GroundedContextSectionCountsDto(
                documents.Items.Count,
                memory.Items.Count,
                recentTasks.Items.Count,
                relevantRecords.Items.Count,
                normalizedSourceReferences.Count));
    }

    private static DocumentContextSectionDto NormalizeDocuments(
        RetrievalSectionDto section,
        IReadOnlyDictionary<string, RetrievalSourceReferenceDto> sourceLookup)
    {
        var items = section.Items
            .Select(item => CreateDocumentItem(section.Id, item, sourceLookup))
            .Where(static item => item is not null)
            .Cast<DocumentContextItemDto>()
            .OrderByDescending(item => item.RelevanceScore ?? 0d)
            .ThenBy(item => item.DocumentId, StringComparer.Ordinal)
            .ThenBy(item => item.ChunkIndex ?? int.MaxValue)
            .ThenBy(item => item.ChunkId, StringComparer.Ordinal)
            .ToArray();

        return new DocumentContextSectionDto(section.Id, section.Title, items);
    }

    private static MemoryContextSectionDto NormalizeMemory(
        RetrievalSectionDto section,
        IReadOnlyDictionary<string, RetrievalSourceReferenceDto> sourceLookup)
    {
        var items = section.Items
            .Select(item => CreateMemoryItem(section.Id, item, sourceLookup))
            .Where(static item => item is not null)
            .Cast<MemoryContextItemDto>()
            .OrderByDescending(item => item.RelevanceScore ?? 0d)
            .ThenByDescending(item => item.Salience ?? 0d)
            .ThenByDescending(item => item.ValidFromUtc ?? item.CreatedUtc ?? DateTime.MinValue)
            .ThenByDescending(item => item.CreatedUtc)
            .ThenBy(item => item.MemoryId, StringComparer.Ordinal)
            .ToArray();

        return new MemoryContextSectionDto(section.Id, section.Title, items);
    }

    private static RecentTaskContextSectionDto NormalizeRecentTasks(
        RetrievalSectionDto section,
        IReadOnlyDictionary<string, RetrievalSourceReferenceDto> sourceLookup)
    {
        var items = section.Items
            .Select(item => CreateRecentTaskItem(section.Id, item, sourceLookup))
            .Where(static item => item is not null)
            .Cast<RecentTaskContextItemDto>()
            .OrderByDescending(item => item.RelevanceScore ?? 0d)
            .ThenByDescending(item => item.TimestampUtc)
            .ThenBy(item => item.AttemptId, StringComparer.Ordinal)
            .ToArray();

        return new RecentTaskContextSectionDto(section.Id, section.Title, items);
    }

    private static RelevantRecordContextSectionDto NormalizeRelevantRecords(
        RetrievalSectionDto section,
        IReadOnlyDictionary<string, RetrievalSourceReferenceDto> sourceLookup)
    {
        var items = section.Items
            .Select(item => CreateRelevantRecordItem(section.Id, item, sourceLookup))
            .Where(static item => item is not null)
            .Cast<RelevantRecordContextItemDto>()
            .OrderByDescending(item => item.RelevanceScore ?? 0d)
            .ThenByDescending(item => item.TimestampUtc)
            .ThenBy(item => RelevantRecordSourcePriority.TryGetValue(item.RecordType, out var priority) ? priority : int.MaxValue)
            .ThenBy(item => item.RecordId, StringComparer.Ordinal)
            .ToArray();

        return new RelevantRecordContextSectionDto(section.Id, section.Title, items);
    }

    private static DocumentContextItemDto? CreateDocumentItem(
        string sectionId,
        RetrievalItemDto item,
        IReadOnlyDictionary<string, RetrievalSourceReferenceDto> sourceLookup)
    {
        var excerpt = NormalizeOptional(item.Content);
        if (excerpt is null)
        {
            return null;
        }

        var metadata = NormalizeMetadata(item.Metadata);
        return new DocumentContextItemDto(
            item.SourceId,
            GetMetadata(metadata, "documentId") ?? item.SourceId,
            NormalizeOptional(item.Title) ?? GetMetadata(metadata, "documentTitle") ?? item.SourceId,
            excerpt,
            GetMetadata(metadata, "documentType"),
            GetMetadata(metadata, "sourceType"),
            GetMetadata(metadata, "sourceRef") ?? GetMetadata(metadata, "chunkSourceReference"),
            ParseNullableInt(GetMetadata(metadata, "chunkIndex")),
            item.RelevanceScore,
            CreateSource(sectionId, item, sourceLookup));
    }

    private static MemoryContextItemDto? CreateMemoryItem(
        string sectionId,
        RetrievalItemDto item,
        IReadOnlyDictionary<string, RetrievalSourceReferenceDto> sourceLookup)
    {
        var summary = NormalizeOptional(item.Content);
        if (summary is null)
        {
            return null;
        }

        var metadata = NormalizeMetadata(item.Metadata);
        return new MemoryContextItemDto(
            item.SourceId,
            NormalizeOptional(item.Title) ?? item.SourceId,
            summary,
            GetMetadata(metadata, "memoryType"),
            GetMetadata(metadata, "scope"),
            ParseNullableDouble(GetMetadata(metadata, "salience")),
            item.TimestampUtc,
            ParseNullableDateTime(GetMetadata(metadata, "validFromUtc")),
            ParseNullableDateTime(GetMetadata(metadata, "validToUtc")),
            item.RelevanceScore,
            CreateSource(sectionId, item, sourceLookup));
    }

    private static RecentTaskContextItemDto? CreateRecentTaskItem(
        string sectionId,
        RetrievalItemDto item,
        IReadOnlyDictionary<string, RetrievalSourceReferenceDto> sourceLookup)
    {
        var summary = NormalizeOptional(item.Content);
        if (summary is null)
        {
            return null;
        }

        var metadata = NormalizeMetadata(item.Metadata);
        return new RecentTaskContextItemDto(
            item.SourceId,
            NormalizeOptional(item.Title) ?? item.SourceId,
            summary,
            GetMetadata(metadata, "toolName"),
            GetMetadata(metadata, "actionType"),
            GetMetadata(metadata, "status"),
            GetMetadata(metadata, "scope"),
            GetMetadata(metadata, "taskId"),
            GetMetadata(metadata, "approvalRequestId"),
            item.TimestampUtc,
            item.RelevanceScore,
            CreateSource(sectionId, item, sourceLookup));
    }

    private static RelevantRecordContextItemDto? CreateRelevantRecordItem(
        string sectionId,
        RetrievalItemDto item,
        IReadOnlyDictionary<string, RetrievalSourceReferenceDto> sourceLookup)
    {
        var summary = NormalizeOptional(item.Content);
        if (summary is null)
        {
            return null;
        }

        var fields = NormalizeMetadata(item.Metadata);
        return new RelevantRecordContextItemDto(
            item.SourceId,
            GetMetadata(fields, "sourceEntityType") ?? item.SourceType,
            NormalizeOptional(item.Title) ?? item.SourceId,
            summary,
            fields,
            item.TimestampUtc,
            item.RelevanceScore,
            CreateSource(sectionId, item, sourceLookup));
    }

    private static GroundedContextItemSourceDto CreateSource(
        string sectionId,
        RetrievalItemDto item,
        IReadOnlyDictionary<string, RetrievalSourceReferenceDto> sourceLookup)
    {
        var lookupKey = BuildSourceKey(sectionId, item.SourceType, item.SourceId);
        if (!sourceLookup.TryGetValue(lookupKey, out var sourceReference))
        {
            var fallbackMetadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["retrievalSection"] = sectionId
            };

            return new GroundedContextItemSourceDto(
                item.SourceType,
                item.SourceId,
                NormalizeOptional(item.Title) ?? item.SourceId,
                null,
                null,
                item.RelevanceScore,
                item.TimestampUtc,
                fallbackMetadata);
        }

        var metadata = NormalizeMetadata(sourceReference.Metadata);
        return new GroundedContextItemSourceDto(
            sourceReference.SourceType,
            sourceReference.SourceId,
            NormalizeOptional(sourceReference.Title) ?? item.SourceId,
            sourceReference.Rank > 0 ? sourceReference.Rank : null,
            sourceReference.SectionRank > 0
                ? sourceReference.SectionRank
                : ParseNullableInt(GetMetadata(metadata, "retrievalSectionRank")),
            sourceReference.Score,
            sourceReference.TimestampUtc,
            metadata);
    }

    private static IReadOnlyList<RetrievalSourceReferenceDto> NormalizeSourceReferences(
        IReadOnlyList<RetrievalSourceReferenceDto> sourceReferences)
    {
        sourceReferences
            .OrderBy(reference => GetSectionPriority(NormalizeOptional(reference.SectionId) ?? GetMetadata(reference.Metadata, "retrievalSection")))
            .ThenBy(reference => GetSectionRank(reference))
            .ThenBy(reference => reference.Rank > 0 ? reference.Rank : int.MaxValue)
            .ThenBy(reference => reference.SourceType, StringComparer.Ordinal)
            .ThenBy(reference => reference.SourceId, StringComparer.Ordinal)
            .Select(reference => new RetrievalSourceReferenceDto(
                reference.SourceType,
                reference.SourceId,
                NormalizeOptional(reference.Title) ?? reference.SourceId,
                NormalizeOptional(reference.ParentSourceType) ?? GetMetadata(reference.Metadata, "parentSourceType"),
                NormalizeOptional(reference.ParentSourceId) ?? GetMetadata(reference.Metadata, "parentSourceId"),
                NormalizeOptional(reference.ParentTitle),
                NormalizeOptional(reference.SectionId) ?? GetMetadata(reference.Metadata, "retrievalSection") ?? string.Empty,
                NormalizeOptional(reference.SectionTitle) ?? GetMetadata(reference.Metadata, "retrievalSectionTitle") ?? string.Empty,
                GetSectionRank(reference),
                NormalizeOptional(reference.Locator),
                reference.Rank,
                reference.Score,
                NormalizeOptional(reference.Snippet) ?? string.Empty,
                reference.TimestampUtc,
                NormalizeMetadata(reference.Metadata)))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, RetrievalSourceReferenceDto> BuildSourceLookup(
        IReadOnlyList<RetrievalSourceReferenceDto> sourceReferences) =>
        sourceReferences
            .GroupBy(reference => BuildSourceKey(reference.SectionId, reference.SourceType, reference.SourceId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(reference => GetSectionRank(reference))
                    .ThenBy(reference => reference.Rank > 0 ? reference.Rank : int.MaxValue)
                    .ThenBy(reference => reference.SourceType, StringComparer.Ordinal)
                    .ThenBy(reference => reference.SourceId, StringComparer.Ordinal)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

    private static int GetSectionPriority(string? sectionId) =>
        sectionId is not null && SectionPriority.TryGetValue(sectionId, out var priority) ? priority : int.MaxValue;

    private static int GetSectionRank(RetrievalSourceReferenceDto reference) =>
        reference.SectionRank > 0 ? reference.SectionRank : ParseNullableInt(GetMetadata(reference.Metadata, "retrievalSectionRank")) ?? 0;
    private static string BuildSourceKey(string sectionId, string sourceType, string sourceId) =>
        $"{sectionId}|{sourceType}|{sourceId}";

    private static IReadOnlyDictionary<string, string?> NormalizeMetadata(IReadOnlyDictionary<string, string?> metadata)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[pair.Key.Trim()] = NormalizeOptional(pair.Value);
        }

        return normalized;
    }

    private static string? GetMetadata(IReadOnlyDictionary<string, string?> metadata, string key) =>
        metadata.TryGetValue(key, out var value)
            ? value
            : null;

    private static int? ParseNullableInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? ParseNullableDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ParseNullableDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return CollapseWhitespace(value);
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            previousWasWhitespace = false;
            builder.Append(character);
        }

        return builder.ToString();
    }
}
