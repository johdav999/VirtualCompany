using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Agents;

public sealed record AgentCommunicationProfileDto(
    string Tone,
    string Persona,
    IReadOnlyList<string> StyleDirectives,
    IReadOnlyList<string> CommunicationRules,
    IReadOnlyList<string> ForbiddenToneRules,
    string ProfileSource,
    bool IsFallback)
{
    public static AgentCommunicationProfileDto Empty { get; } = new(
        string.Empty,
        string.Empty,
        [],
        [],
        [],
        AgentCommunicationProfileSources.Explicit,
        false);
}

public static class AgentCommunicationProfileSources
{
    public const string Explicit = "explicit";
    public const string Fallback = "fallback";
}

public sealed record CommunicationProfileResolutionContext(
    Guid CompanyId,
    Guid AgentId,
    string? GenerationPath,
    string? CorrelationId);

public interface IDefaultAgentCommunicationProfileProvider
{
    AgentCommunicationProfileDto GetDefaultProfile();
}

public interface IAgentCommunicationProfileResolver
{
    AgentCommunicationProfileDto Resolve(
        IReadOnlyDictionary<string, JsonNode?>? persistedProfile,
        CommunicationProfileResolutionContext context);
}

public static class AgentCommunicationProfileJsonMapper
{
    private const int MaxTextLength = 500;
    private const int MaxListItems = 50;

    public static Dictionary<string, JsonNode?> ToJsonDictionary(AgentCommunicationProfileInput? input)
    {
        if (input is null)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        AddString(values, "tone", input.Tone);
        AddString(values, "persona", input.Persona);
        AddStringArray(values, "styleDirectives", input.StyleDirectives);
        AddStringArray(values, "communicationRules", input.CommunicationRules);
        AddStringArray(values, "forbiddenToneRules", input.ForbiddenToneRules);
        AddStringArray(values, "forbiddenPhrases", input.ForbiddenPhrases);

        if (input.AdditionalProperties is not null)
        {
            foreach (var property in input.AdditionalProperties)
            {
                if (!values.ContainsKey(property.Key))
                {
                    values[property.Key] = JsonNode.Parse(property.Value.GetRawText());
                }
            }
        }

        return values;
    }

    public static AgentCommunicationProfileDto ToDto(
        IReadOnlyDictionary<string, JsonNode?>? values,
        string source,
        bool isFallback)
    {
        if (values is null || values.Count == 0)
        {
            return AgentCommunicationProfileDto.Empty with
            {
                ProfileSource = source,
                IsFallback = isFallback
            };
        }

        return new AgentCommunicationProfileDto(
            GetString(values, "tone"),
            GetString(values, "persona"),
            GetStringList(values, "styleDirectives", "styleConstraints"),
            GetStringList(values, "communicationRules"),
            GetStringList(values, "forbiddenToneRules", "forbiddenPhrases"),
            source,
            isFallback);
    }

    public static bool HasExplicitProfile(IReadOnlyDictionary<string, JsonNode?>? values)
    {
        if (values is null || values.Count == 0)
        {
            return false;
        }

        var profile = ToDto(values, AgentCommunicationProfileSources.Explicit, false);
        return !string.IsNullOrWhiteSpace(profile.Tone) ||
               !string.IsNullOrWhiteSpace(profile.Persona) ||
               profile.StyleDirectives.Count > 0 ||
               profile.CommunicationRules.Count > 0 ||
               profile.ForbiddenToneRules.Count > 0;
    }

    public static Dictionary<string, JsonNode?> ToJsonDictionary(AgentCommunicationProfileDto profile)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tone"] = JsonValue.Create(profile.Tone),
            ["persona"] = JsonValue.Create(profile.Persona),
            ["styleDirectives"] = ToJsonArray(profile.StyleDirectives),
            ["communicationRules"] = ToJsonArray(profile.CommunicationRules),
            ["forbiddenToneRules"] = ToJsonArray(profile.ForbiddenToneRules),
            ["profileSource"] = JsonValue.Create(profile.ProfileSource),
            ["isFallback"] = JsonValue.Create(profile.IsFallback)
        };

        return values;
    }

    public static void Validate(
        IDictionary<string, List<string>> errors,
        string key,
        IReadOnlyDictionary<string, JsonNode?>? values)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        ValidateOptionalString(errors, key, values, "tone");
        ValidateOptionalString(errors, key, values, "persona");
        ValidateStringArray(errors, key, values, "styleDirectives");
        ValidateStringArray(errors, key, values, "styleConstraints");
        ValidateStringArray(errors, key, values, "communicationRules");
        ValidateStringArray(errors, key, values, "forbiddenToneRules");
        ValidateStringArray(errors, key, values, "forbiddenPhrases");
    }

    private static void AddString(IDictionary<string, JsonNode?> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = JsonValue.Create(value.Trim());
        }
    }

    private static void AddStringArray(IDictionary<string, JsonNode?> values, string key, IReadOnlyCollection<string>? items)
    {
        if (items is null || items.Count == 0)
        {
            return;
        }

        values[key] = ToJsonArray(items);
    }

    private static JsonArray ToJsonArray(IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (var item in items.Where(static x => !string.IsNullOrWhiteSpace(x)).Select(static x => x.Trim()))
        {
            array.Add(JsonValue.Create(item));
        }

        return array;
    }

    private static string GetString(IReadOnlyDictionary<string, JsonNode?> values, string key) =>
        values.TryGetValue(key, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : string.Empty;

    private static IReadOnlyList<string> GetStringList(IReadOnlyDictionary<string, JsonNode?> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!values.TryGetValue(key, out var node) || node is not JsonArray array)
            {
                continue;
            }

            return array
                .OfType<JsonValue>()
                .Select(static x => x.TryGetValue<string>(out var text) ? text.Trim() : string.Empty)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        return [];
    }

    private static void ValidateOptionalString(
        IDictionary<string, List<string>> errors,
        string key,
        IReadOnlyDictionary<string, JsonNode?> values,
        string propertyName)
    {
        if (!values.TryGetValue(propertyName, out var node) || node is null)
        {
            return;
        }

        if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
        {
            AddError(errors, key, $"{key}.{propertyName} must be a non-empty string.");
            return;
        }

        if (text.Trim().Length > MaxTextLength)
        {
            AddError(errors, key, $"{key}.{propertyName} must be {MaxTextLength} characters or fewer.");
        }
    }

    private static void ValidateStringArray(
        IDictionary<string, List<string>> errors,
        string key,
        IReadOnlyDictionary<string, JsonNode?> values,
        string propertyName)
    {
        if (!values.TryGetValue(propertyName, out var node) || node is null)
        {
            return;
        }

        if (node is not JsonArray array)
        {
            AddError(errors, key, $"{key}.{propertyName} must be an array of strings.");
            return;
        }

        if (array.Count > MaxListItems)
        {
            AddError(errors, key, $"{key}.{propertyName} must contain {MaxListItems} items or fewer.");
            return;
        }

        foreach (var item in array)
        {
            if (item is not JsonValue value ||
                !value.TryGetValue<string>(out var text) ||
                string.IsNullOrWhiteSpace(text))
            {
                AddError(errors, key, $"{key}.{propertyName} must contain non-empty string values.");
                return;
            }

            if (text.Trim().Length > MaxTextLength)
            {
                AddError(errors, key, $"{key}.{propertyName} entries must be {MaxTextLength} characters or fewer.");
                return;
            }
        }
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }
}

public sealed class AgentCommunicationProfileInput
{
    public string? Tone { get; set; }
    public string? Persona { get; set; }
    public List<string> StyleDirectives { get; set; } = [];
    public List<string> CommunicationRules { get; set; } = [];
    public List<string> ForbiddenToneRules { get; set; } = [];
    public List<string> ForbiddenPhrases { get; set; } = [];

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties { get; set; }
}
