using System.Text;

namespace VirtualCompany.Application.Agents;

public static class AgentCommunicationProfilePromptRenderer
{
    public const string SectionTitle = "Agent identity profile";

    public static string Render(AgentCommunicationProfileDto profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new StringBuilder();
        builder.Append("Tone: ");
        builder.AppendLine(string.IsNullOrWhiteSpace(profile.Tone) ? "professional, clear, helpful" : profile.Tone.Trim());
        builder.Append("Persona: ");
        builder.AppendLine(string.IsNullOrWhiteSpace(profile.Persona) ? "reliable business assistant" : profile.Persona.Trim());
        builder.Append("Profile source: ");
        builder.AppendLine(string.IsNullOrWhiteSpace(profile.ProfileSource) ? AgentCommunicationProfileSources.Fallback : profile.ProfileSource.Trim());
        builder.Append("Fallback profile: ");
        builder.AppendLine(profile.IsFallback ? "true" : "false");
        AppendListSection(builder, "Style directives", profile.StyleDirectives);
        AppendListSection(builder, "Communication rules", profile.CommunicationRules);
        AppendListSection(builder, "Forbidden tone rules", profile.ForbiddenToneRules);
        builder.AppendLine("Apply this identity profile to every user-facing response, task output, summary, and generated artifact.");
        return builder.ToString().TrimEnd();
    }

    public static IReadOnlyDictionary<string, string> ToStableDirectiveMap(AgentCommunicationProfileDto profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tone"] = string.IsNullOrWhiteSpace(profile.Tone) ? "professional, clear, helpful" : profile.Tone.Trim(),
            ["persona"] = string.IsNullOrWhiteSpace(profile.Persona) ? "reliable business assistant" : profile.Persona.Trim(),
            ["styleDirectives"] = JoinOrNone(profile.StyleDirectives),
            ["communicationRules"] = JoinOrNone(profile.CommunicationRules),
            ["forbiddenToneRules"] = JoinOrNone(profile.ForbiddenToneRules),
            ["profileSource"] = string.IsNullOrWhiteSpace(profile.ProfileSource) ? AgentCommunicationProfileSources.Fallback : profile.ProfileSource.Trim(),
            ["isFallback"] = profile.IsFallback ? "true" : "false"
        };
    }

    private static void AppendListSection(StringBuilder builder, string label, IReadOnlyList<string> values)
    {
        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(JoinOrNone(values));
    }

    private static string JoinOrNone(IEnumerable<string> values)
    {
        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();

        return normalized.Length == 0 ? "none" : string.Join("; ", normalized);
    }
}