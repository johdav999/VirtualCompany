using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using VirtualCompany.Web.Localization.Agents;

namespace VirtualCompany.Web.Services;

public static class AgentBriefDocumentStatusPresenter
{
    public static IReadOnlyList<AgentBriefDocumentViewModel> Deduplicate(
        IEnumerable<AgentBriefDocumentViewModel> documents) =>
        documents
            .GroupBy(BuildIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(GetReadinessRank)
                .ThenByDescending(document => document.UpdatedUtc)
                .ThenByDescending(document => document.Id)
                .First())
            .ToArray();

    public static bool IsTerminal(AgentBriefDocumentViewModel document) =>
        Is(document.IndexingStatus, "indexed") ||
        Is(document.IndexingStatus, "failed") ||
        Is(document.IngestionStatus, "failed") ||
        Is(document.IngestionStatus, "blocked");

    public static string GetLabel(AgentBriefDocumentViewModel document)
    {
        if (Is(document.IndexingStatus, "indexed"))
        {
            return "Ready";
        }

        if (Is(document.IndexingStatus, "failed") ||
            Is(document.IngestionStatus, "failed") ||
            Is(document.IngestionStatus, "blocked"))
        {
            return "Needs attention";
        }

        if (Is(document.IngestionStatus, "uploaded") || Is(document.IngestionStatus, "pending_scan"))
        {
            return "Scanning";
        }

        if (Is(document.IndexingStatus, "indexing") || Is(document.IngestionStatus, "processing"))
        {
            return "Indexing";
        }

        return "Queued";
    }

    public static string GetLabel(
        AgentBriefDocumentViewModel document,
        IStringLocalizer<AgentsResources> localizer) => localizer[GetLabelResourceKey(document)];

    public static string GetDetail(AgentBriefDocumentViewModel document)
    {
        if (!string.IsNullOrWhiteSpace(document.IndexingFailureMessage))
        {
            return document.IndexingFailureMessage;
        }

        if (!string.IsNullOrWhiteSpace(document.FailureMessage))
        {
            return string.IsNullOrWhiteSpace(document.FailureAction)
                ? document.FailureMessage
                : $"{document.FailureMessage} {document.FailureAction}";
        }

        return GetLabel(document) switch
        {
            "Ready" => "This document is indexed and available to the agent.",
            "Scanning" => "The document is passing the upload security check.",
            "Indexing" => "The document is being prepared as a grounded source.",
            "Queued" => "The document is waiting for background indexing.",
            _ => "Document processing needs attention."
        };
    }

    public static string GetDetail(
        AgentBriefDocumentViewModel document,
        IStringLocalizer<AgentsResources> localizer)
    {
        if (!string.IsNullOrWhiteSpace(document.IndexingFailureMessage))
        {
            return document.IndexingFailureMessage;
        }

        if (!string.IsNullOrWhiteSpace(document.FailureMessage))
        {
            return string.IsNullOrWhiteSpace(document.FailureAction)
                ? document.FailureMessage
                : $"{document.FailureMessage} {document.FailureAction}";
        }

        return localizer[GetDetailResourceKey(document)];
    }

    public static string GetLabelResourceKey(AgentBriefDocumentViewModel document) =>
        ResolveState(document) switch
        {
            "ready" => "DocumentReady",
            "attention" => "DocumentNeedsAttention",
            "scanning" => "DocumentScanning",
            "indexing" => "DocumentIndexing",
            _ => "DocumentQueued"
        };

    public static string GetDetailResourceKey(AgentBriefDocumentViewModel document) =>
        ResolveState(document) switch
        {
            "ready" => "DocumentDetailReady",
            "scanning" => "DocumentDetailScanning",
            "indexing" => "DocumentDetailIndexing",
            "queued" => "DocumentDetailQueued",
            _ => "DocumentDetailAttention"
        };

    private static string ResolveState(AgentBriefDocumentViewModel document)
    {
        if (Is(document.IndexingStatus, "indexed")) return "ready";
        if (Is(document.IndexingStatus, "failed") || Is(document.IngestionStatus, "failed") || Is(document.IngestionStatus, "blocked")) return "attention";
        if (Is(document.IngestionStatus, "uploaded") || Is(document.IngestionStatus, "pending_scan")) return "scanning";
        if (Is(document.IndexingStatus, "indexing") || Is(document.IngestionStatus, "processing")) return "indexing";
        return "queued";
    }

    private static bool Is(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static string BuildIdentity(AgentBriefDocumentViewModel document)
    {
        var contentIdentity = TryGetString(document.Metadata, "checksum_sha256", out var checksum)
            ? checksum
            : $"{document.OriginalFileName.Trim().ToLowerInvariant()}:{document.FileSizeBytes}";
        var category = TryGetString(document.Metadata, "briefingCategory", out var briefingCategory)
            ? briefingCategory
            : string.Empty;
        var shared = TryGetBoolean(document.Metadata, "shareWithAgentTeam", out var shareWithTeam) && shareWithTeam;
        var owner = shared
            ? "team"
            : TryGetString(document.Metadata, "agentId", out var agentId)
                ? agentId
                : document.Id.ToString("N");

        return $"{category.Trim().ToLowerInvariant()}:{owner.Trim().ToLowerInvariant()}:{contentIdentity.Trim().ToLowerInvariant()}";
    }

    private static int GetReadinessRank(AgentBriefDocumentViewModel document) =>
        Is(document.IndexingStatus, "indexed") ? 3 :
        Is(document.IngestionStatus, "processed") ? 2 :
        Is(document.IngestionStatus, "failed") ? 0 : 1;

    private static bool TryGetString(IReadOnlyDictionary<string, JsonNode?> metadata, string key, out string value)
    {
        value = string.Empty;
        if (!metadata.TryGetValue(key, out var node) ||
            node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var candidate) ||
            string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryGetBoolean(IReadOnlyDictionary<string, JsonNode?> metadata, string key, out bool value)
    {
        value = false;
        return metadata.TryGetValue(key, out var node) &&
               node is JsonValue jsonValue &&
               jsonValue.TryGetValue<bool>(out value);
    }
}
