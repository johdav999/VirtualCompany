using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Companies;

internal static class GuidedDraftStatusChangePolicy
{
    internal static readonly IReadOnlySet<string> SettableStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        GuidedDraftFieldStatuses.Proposed,
        GuidedDraftFieldStatuses.NeedsWork,
        GuidedDraftFieldStatuses.Confirmed,
        GuidedDraftFieldStatuses.Conflicting,
        GuidedDraftFieldStatuses.Unknown
    };

    internal static IReadOnlyList<GuidedDraftField> Apply(
        GuidedWorkSession session,
        IGuidedArtifactDefinition definition,
        IReadOnlyCollection<string> paths,
        string? fromStatus,
        string targetStatus,
        string? explanation,
        string actorDescription)
    {
        if (!SettableStatuses.Contains(targetStatus))
            throw Validation(nameof(targetStatus), "Choose Proposed, Needs work, Confirmed, Conflict, or Unknown.");
        if (!string.IsNullOrWhiteSpace(fromStatus) &&
            fromStatus != GuidedDraftFieldStatuses.Missing &&
            !SettableStatuses.Contains(fromStatus))
            throw Validation(nameof(fromStatus), "The source status is not supported.");
        if (paths.Count > 100)
            throw Validation(nameof(paths), "No more than 100 draft fields can be updated at once.");
        if (paths.Count == 0 && string.IsNullOrWhiteSpace(fromStatus))
            throw Validation(nameof(paths), "Choose one or more fields, or supply a source status.");

        var schemas = definition.Fields.Append(GuidedWorkshopFields.Insights)
            .ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var normalizedPaths = paths.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var unsupported = normalizedPaths.Where(x => !schemas.ContainsKey(x)).ToArray();
        if (unsupported.Length > 0)
            throw Validation(nameof(paths), $"The draft does not contain: {string.Join(", ", unsupported)}.");

        IEnumerable<GuidedDraftField> candidates = normalizedPaths.Length == 0
            ? session.Fields
            : normalizedPaths.Select(path => session.Fields.SingleOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)))
                .OfType<GuidedDraftField>();
        if (!string.IsNullOrWhiteSpace(fromStatus))
            candidates = candidates.Where(x => string.Equals(x.Status, fromStatus, StringComparison.OrdinalIgnoreCase));

        var selected = candidates.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var field in selected)
        {
            if (targetStatus != GuidedDraftFieldStatuses.Unknown && string.IsNullOrWhiteSpace(field.ValueJson))
                throw Validation(field.Path, $"{field.Label} needs a value before its review status can be changed.");
            var valueJson = targetStatus == GuidedDraftFieldStatuses.Unknown ? null : field.ValueJson;
            var detail = string.IsNullOrWhiteSpace(explanation)
                ? $"Status changed to {PlainStatus(targetStatus)} {actorDescription}."
                : explanation.Trim()[..Math.Min(explanation.Trim().Length, 1000)];
            field.Set(valueJson, targetStatus, field.SourceType, field.SourceMessageId, field.SourceMetadata, detail);
        }
        return selected;
    }

    private static string PlainStatus(string status) => status == GuidedDraftFieldStatuses.NeedsWork
        ? "Needs work"
        : status.Replace('_', ' ');

    private static GuidedWorkValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
}
