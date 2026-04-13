using System.Text.RegularExpressions;

namespace VirtualCompany.Application.Auditing;

public static partial class SafeAuditExplanationMapper
{
    public const string FallbackSummary = "Action completed using configured policy and available company data.";
    private const int SummaryMaxLength = 240;
    private const int DataSourceMaxLength = 120;

    private static readonly string[] UnsafeMetadataKeyFragments =
    [
        "chainofthought",
        "chain_of_thought",
        "chain-of-thought",
        "cot",
        "scratchpad",
        "hiddenreasoning",
        "hidden_reasoning",
        "internalreasoning",
        "internal_reasoning",
        "rawreasoning",
        "raw_reasoning",
        "reasoningtrace",
        "reasoning_trace",
        "deliberation",
        "debugprompt",
        "debug_prompt"
    ];

    private static readonly string[] UnsafeTextMarkers =
    [
        "chain-of-thought",
        "chain of thought",
        "hidden reasoning",
        "internal reasoning",
        "raw reasoning",
        "scratchpad",
        "private reasoning",
        "step-by-step reasoning",
        "deliberation trace"
    ];

    // Audit/explainability views must expose operational summaries only, never raw chain-of-thought.
    public static AuditSafeExplanationDto Build(
        string action,
        string outcome,
        string? rationaleSummary,
        IEnumerable<string>? dataSources = null)
    {
        var safeSummary = NormalizeSummary(rationaleSummary);
        var safeOutcome = NormalizeDisplayText(outcome, nameof(outcome), 64) ?? "recorded";
        var safeAction = NormalizeDisplayText(action, nameof(action), 128) ?? "audit action";
        var safeDataSources = NormalizeDataSources(dataSources);

        return new AuditSafeExplanationDto(
            safeSummary,
            $"The system recorded '{safeAction}' with outcome '{safeOutcome}' under configured company policy.",
            safeOutcome,
            safeDataSources);
    }

    public static string NormalizeSummary(string? rationaleSummary)
    {
        var normalized = NormalizeDisplayText(rationaleSummary, nameof(rationaleSummary), SummaryMaxLength);
        if (string.IsNullOrWhiteSpace(normalized) || ContainsUnsafeReasoningMarker(normalized))
        {
            return FallbackSummary;
        }

        return normalized;
    }

    public static Dictionary<string, string?> SanitizeMetadata(IReadOnlyDictionary<string, string?> metadata)
    {
        var safe = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in metadata)
        {
            if (!IsSafeMetadataEntry(pair.Key, pair.Value))
            {
                continue;
            }

            safe[pair.Key] = pair.Value;
        }

        return safe;
    }

    public static bool IsSafeMetadataEntry(string key, string? value)
    {
        var normalizedKey = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return false;
        }

        if (UnsafeMetadataKeyFragments.Any(fragment => normalizedKey.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(value) || !ContainsUnsafeReasoningMarker(value);
    }

    private static IReadOnlyList<string> NormalizeDataSources(IEnumerable<string>? dataSources)
    {
        if (dataSources is null)
        {
            return [];
        }

        return dataSources
            .Select(value => NormalizeDisplayText(value, nameof(dataSources), DataSourceMaxLength))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private static string? NormalizeDisplayText(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = WhitespaceRegex().Replace(value.Trim(), " ");
        if (compact.Length <= maxLength)
        {
            return compact;
        }

        var sentenceEnd = compact.LastIndexOfAny(['.', '!', '?'], maxLength - 1);
        var trimmed = sentenceEnd >= 80
            ? compact[..(sentenceEnd + 1)]
            : compact[..maxLength].TrimEnd(' ', '.', ',', ';', ':') + ".";

        return trimmed.Length > maxLength
            ? trimmed[..maxLength].TrimEnd(' ', '.', ',', ';', ':') + "."
            : trimmed;
    }

    private static bool ContainsUnsafeReasoningMarker(string value) =>
        UnsafeTextMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeKey(string key) =>
        key.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}