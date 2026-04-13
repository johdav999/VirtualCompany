using System.Text.RegularExpressions;
using VirtualCompany.Application.Context;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Auditing;

public static partial class AuditSourceReferenceDisplayFormatter
{
    private const int LabelMaxLength = 160;
    private const int SecondaryMaxLength = 220;
    private const int SnippetMaxLength = 220;

    public static AuditSourceReferenceDto Format(
        AuditDataSourceUsed source,
        string? resolvedDisplayName = null,
        string? resolvedSecondaryText = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var sourceType = NormalizeSourceType(source.SourceType);
        var entityType = NormalizeEntityType(sourceType);
        var displayName = FirstNonEmpty(source.DisplayName, resolvedDisplayName);
        var readableType = ToReadableType(sourceType);
        var fallbackName = BuildUnavailableLabel(readableType, source.SourceId);
        var primaryName = NormalizeText(displayName, LabelMaxLength) ?? fallbackName;
        var label = PrefixLabel(readableType, primaryName);
        var secondaryText = NormalizeText(
            FirstNonEmpty(resolvedSecondaryText, BuildSecondaryText(source, sourceType, readableType, primaryName)),
            SecondaryMaxLength);

        return new AuditSourceReferenceDto(
            label,
            source.SourceId,
            sourceType,
            sourceType,
            primaryName,
            secondaryText,
            entityType,
            source.SourceId,
            NormalizeText(source.Reference, SnippetMaxLength));
    }

    public static AuditSourceReferenceDto FormatLegacyDataSource(string source)
    {
        var label = NormalizeText(source, LabelMaxLength) ?? "Data source";
        return new AuditSourceReferenceDto(
            label,
            null,
            "data_source",
            "data_source",
            label,
            null,
            null,
            null,
            null);
    }

    public static AuditSourceReferenceDto FormatMetadata(string key, string value)
    {
        var label = ToDisplayName(key);
        var secondary = IsLikelyOpaque(value)
            ? null
            : NormalizeText(value, SecondaryMaxLength);

        return new AuditSourceReferenceDto(
            label,
            value,
            "metadata",
            "metadata",
            label,
            secondary,
            null,
            null,
            null);
    }

    public static string BuildExplanationLabel(AuditDataSourceUsed source, string? resolvedDisplayName = null) =>
        Format(source, resolvedDisplayName).Label;

    private static string? BuildSecondaryText(
        AuditDataSourceUsed source,
        string sourceType,
        string readableType,
        string primaryName)
    {
        if (string.IsNullOrWhiteSpace(source.Reference) || IsLikelyOpaque(source.Reference))
        {
            return sourceType switch
            {
                GroundedContextSourceTypes.KnowledgeChunk => "Section excerpt",
                "document_chunk" => "Section excerpt",
                "knowledge_document" or "document" or "company_document" => "Knowledge document",
                GroundedContextSourceTypes.RecentTask or "task" or "work_task" => "Task record",
                "workflow" or "workflow_instance" => "Workflow record",
                GroundedContextSourceTypes.ApprovalRequest => "Approval record",
                "tool_execution" or "tool_execution_attempt" or "agent_tool_execution" => "Tool execution record",
                "message" or "conversation" or "conversation_task_link" => "Conversation record",
                _ => null
            };
        }

        var reference = NormalizeText(source.Reference, SecondaryMaxLength);
        if (string.IsNullOrWhiteSpace(reference) ||
            string.Equals(reference, primaryName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reference, source.SourceId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return reference;
    }

    private static string PrefixLabel(string readableType, string primaryName) =>
        primaryName.StartsWith($"{readableType}:", StringComparison.OrdinalIgnoreCase)
            ? primaryName
            : $"{readableType}: {primaryName}";

    private static string BuildUnavailableLabel(string readableType, string? sourceId) =>
        string.IsNullOrWhiteSpace(sourceId)
            ? $"{readableType} (unavailable)"
            : $"{readableType} (deleted or inaccessible)";

    private static string NormalizeSourceType(string? sourceType) =>
        string.IsNullOrWhiteSpace(sourceType)
            ? "data_source"
            : CollapseWhitespace(sourceType.Trim()).ToLowerInvariant();

    private static string NormalizeEntityType(string sourceType) =>
        sourceType switch
        {
            GroundedContextSourceTypes.KnowledgeChunk or "document_chunk" => "knowledge_chunk",
            "knowledge_document" or "document" or "company_document" => AuditTargetTypes.CompanyDocument,
            GroundedContextSourceTypes.RecentTask or "task" => AuditTargetTypes.WorkTask,
            "workflow" => AuditTargetTypes.WorkflowInstance,
            "tool_execution" or "tool_execution_attempt" => AuditTargetTypes.AgentToolExecution,
            _ => sourceType
        };

    private static string ToReadableType(string sourceType) =>
        sourceType switch
        {
            GroundedContextSourceTypes.KnowledgeChunk or "document_chunk" => "Document",
            "knowledge_document" or "document" or "company_document" => "Document",
            GroundedContextSourceTypes.MemoryItem or "memory" => "Memory",
            GroundedContextSourceTypes.RecentTask or "task" or "work_task" => "Task",
            "workflow" or "workflow_instance" => "Workflow",
            GroundedContextSourceTypes.ApprovalRequest => "Approval",
            "tool_execution" or "tool_execution_attempt" or "agent_tool_execution" => "Tool execution",
            "message" => "Message",
            "conversation" or "conversation_task_link" => "Conversation",
            "integration_record" or "integration-originated-record" => "Integration record",
            GroundedContextSourceTypes.AgentRecord => "Agent record",
            GroundedContextSourceTypes.CompanyRecord => "Company record",
            _ => ToDisplayName(sourceType)
        };

    private static bool IsLikelyOpaque(string value)
    {
        var trimmed = value.Trim();
        return Guid.TryParse(trimmed, out _) ||
               trimmed.StartsWith("kb://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("s3://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("chunk:", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("/", StringComparison.Ordinal) ||
               trimmed.Contains('\\', StringComparison.Ordinal) ||
               trimmed.Contains("/documents/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = CollapseWhitespace(value.Trim());
        return compact.Length <= maxLength
            ? compact
            : compact[..maxLength].TrimEnd(' ', '.', ',', ';', ':') + ".";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string ToDisplayName(string value)
    {
        var spaced = SnakeCaseRegex().Replace(value, " ");
        spaced = PascalCaseRegex().Replace(spaced, "$1 $2");
        return string.Join(
            " ",
            spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => string.Concat(char.ToUpperInvariant(word[0]), word.AsSpan(1).ToString().ToLowerInvariant())));
    }

    private static string CollapseWhitespace(string value) => WhitespaceRegex().Replace(value, " ");

    [GeneratedRegex(@"[\-_]+")]
    private static partial Regex SnakeCaseRegex();

    [GeneratedRegex(@"([a-z])([A-Z])")]
    private static partial Regex PascalCaseRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}