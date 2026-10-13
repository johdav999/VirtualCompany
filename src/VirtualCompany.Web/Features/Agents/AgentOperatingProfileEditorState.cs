using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Agents;

public sealed class AgentOperatingProfileEditorState
{
    public AgentOperatingProfileFormModel ProfileModel { get; set; } = new();
    public Dictionary<string, string[]> ProfileErrors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ProfileErrorMessage { get; set; }
    public string? ProfileSuccessMessage { get; set; }
    public bool IsSavingProfile { get; set; }
    public IReadOnlyList<string> GetProfileErrors(string key)
    {
        var matched = ProfileErrors
            .Where(entry =>
                string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith($"{key}.", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith($"{key}[", StringComparison.OrdinalIgnoreCase))
            .SelectMany(entry => entry.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return matched.Length == 0
            ? []
            : matched;
    }

}
