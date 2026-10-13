using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Agents;

public sealed class AgentBriefFormModel
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public string Get(string category) =>
        _values.TryGetValue(category, out var value) ? value : string.Empty;

    public void Set(string category, string? value)
    {
        _values[category] = value ?? string.Empty;
    }

    public Dictionary<string, string> ToDictionary() =>
        AgentBriefEditorState.Categories.ToDictionary(
            category => category.Key,
            category => Get(category.Key).Trim(),
            StringComparer.OrdinalIgnoreCase);

    public static AgentBriefFormModel FromProfile(AgentOperatingProfileViewModel profile)
    {
        var model = new AgentBriefFormModel();
        if (!profile.CommunicationProfile.TryGetValue("briefing", out var node) ||
            node is not JsonObject briefing)
        {
            return model;
        }

        foreach (var category in AgentBriefEditorState.Categories)
        {
            if (briefing[category.Key] is JsonValue value &&
                value.TryGetValue<string>(out var content))
            {
                model.Set(category.Key, content);
            }
        }

        return model;
    }
}

