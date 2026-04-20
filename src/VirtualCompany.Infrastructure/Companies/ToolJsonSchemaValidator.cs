using System.Globalization;
using System.Text.Json.Nodes;

namespace VirtualCompany.Infrastructure.Companies;

internal static class ToolJsonSchemaValidator
{
    public static bool Validate(
        IReadOnlyDictionary<string, JsonNode?> payload,
        JsonObject schema,
        out IReadOnlyList<string> errors)
    {
        var root = new JsonObject(payload
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone()))
            .ToArray());

        return Validate(root, schema, out errors);
    }

    public static bool Validate(
        JsonNode? payload,
        JsonObject schema,
        out IReadOnlyList<string> errors)
    {
        var collected = new List<string>();
        ValidateNode(payload, schema, "$", collected);
        errors = collected;
        return collected.Count == 0;
    }

    private static void ValidateNode(JsonNode? node, JsonObject schema, string path, List<string> errors)
    {
        var allowedTypes = ReadAllowedTypes(schema);
        if (allowedTypes.Count > 0 && !allowedTypes.Any(type => MatchesType(node, type)))
        {
            errors.Add($"{path} must be {string.Join(" or ", allowedTypes)}.");
            return;
        }

        if (node is JsonObject jsonObject)
        {
            ValidateObject(jsonObject, schema, path, errors);
        }
        else if (node is JsonArray jsonArray)
        {
            ValidateArray(jsonArray, schema, path, errors);
        }
        else if (node is JsonValue jsonValue)
        {
            ValidateValue(jsonValue, schema, path, errors);
        }
    }

    private static void ValidateObject(JsonObject jsonObject, JsonObject schema, string path, List<string> errors)
    {
        var properties = schema["properties"] as JsonObject;
        var required = ReadRequired(schema);

        foreach (var propertyName in required)
        {
            if (!jsonObject.ContainsKey(propertyName) || jsonObject[propertyName] is null)
            {
                errors.Add($"{path}.{propertyName} is required.");
            }
        }

        if (properties is not null)
        {
            foreach (var (propertyName, propertySchemaNode) in properties)
            {
                if (propertySchemaNode is JsonObject propertySchema &&
                    jsonObject.TryGetPropertyValue(propertyName, out var value) &&
                    value is not null)
                {
                    ValidateNode(value, propertySchema, $"{path}.{propertyName}", errors);
                }
            }
        }

        if (IsAdditionalPropertiesDisabled(schema) && properties is not null)
        {
            var knownProperties = properties.Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var propertyName in jsonObject.Select(pair => pair.Key))
            {
                if (!knownProperties.Contains(propertyName))
                {
                    errors.Add($"{path}.{propertyName} is not allowed.");
                }
            }
        }
    }

    private static void ValidateArray(JsonArray jsonArray, JsonObject schema, string path, List<string> errors)
    {
        if (TryReadDecimal(schema, "minItems", out var minItems) && jsonArray.Count < minItems)
        {
            errors.Add($"{path} must contain at least {minItems.ToString(CultureInfo.InvariantCulture)} items.");
        }

        if (TryReadDecimal(schema, "maxItems", out var maxItems) && jsonArray.Count > maxItems)
        {
            errors.Add($"{path} must contain at most {maxItems.ToString(CultureInfo.InvariantCulture)} items.");
        }

        if (schema["items"] is not JsonObject itemSchema)
        {
            return;
        }

        for (var i = 0; i < jsonArray.Count; i++)
        {
            ValidateNode(jsonArray[i], itemSchema, $"{path}[{i}]", errors);
        }
    }

    private static void ValidateValue(JsonValue value, JsonObject schema, string path, List<string> errors)
    {
        if (schema["enum"] is JsonArray enumValues && !enumValues.Any(enumValue => JsonNode.DeepEquals(enumValue, value)))
        {
            errors.Add($"{path} must be one of the declared enum values.");
        }

        if (TryReadDecimal(schema, "minimum", out var minimum) && TryReadDecimal(value, out var number) && number < minimum)
        {
            errors.Add($"{path} must be greater than or equal to {minimum.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (TryReadDecimal(schema, "maximum", out var maximum) && TryReadDecimal(value, out number) && number > maximum)
        {
            errors.Add($"{path} must be less than or equal to {maximum.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (TryReadString(schema, "format", out var format) && value.TryGetValue<string>(out var text))
        {
            if (string.Equals(format, "date-time", StringComparison.OrdinalIgnoreCase) &&
                !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            {
                errors.Add($"{path} must be a valid date-time.");
            }

            if (string.Equals(format, "uuid", StringComparison.OrdinalIgnoreCase) &&
                !Guid.TryParse(text, out _))
            {
                errors.Add($"{path} must be a valid uuid.");
            }
        }
    }

    private static IReadOnlyList<string> ReadAllowedTypes(JsonObject schema)
    {
        if (schema["type"] is JsonValue value && value.TryGetValue<string>(out var type))
        {
            return [type];
        }

        if (schema["type"] is JsonArray array)
        {
            return array
                .OfType<JsonValue>()
                .Select(x => x.TryGetValue<string>(out var item) ? item : null)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray();
        }

        return [];
    }

    private static bool MatchesType(JsonNode? node, string type) =>
        type.ToLowerInvariant() switch
        {
            "object" => node is JsonObject,
            "array" => node is JsonArray,
            "string" => node is JsonValue value && value.TryGetValue<string>(out _),
            "boolean" => node is JsonValue value && value.TryGetValue<bool>(out _),
            "number" => node is JsonValue value && TryReadDecimal(value, out _),
            "integer" => node is JsonValue value && value.TryGetValue<int>(out _),
            "null" => node is null,
            _ => true
        };

    private static IReadOnlyList<string> ReadRequired(JsonObject schema) =>
        schema["required"] is JsonArray required
            ? required
                .OfType<JsonValue>()
                .Select(x => x.TryGetValue<string>(out var item) ? item : null)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray()
            : [];

    private static bool IsAdditionalPropertiesDisabled(JsonObject schema) =>
        schema["additionalProperties"] is JsonValue value &&
        value.TryGetValue<bool>(out var additionalProperties) &&
        !additionalProperties;

    private static bool TryReadString(JsonObject schema, string key, out string value)
    {
        value = string.Empty;
        return schema[key] is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }

    private static bool TryReadDecimal(JsonObject schema, string key, out decimal value)
    {
        value = 0;
        return schema[key] is JsonValue jsonValue && TryReadDecimal(jsonValue, out value);
    }

    private static bool TryReadDecimal(JsonValue jsonValue, out decimal value) =>
        jsonValue.TryGetValue(out value) ||
        jsonValue.TryGetValue<double>(out var doubleValue) && decimal.TryParse(
            doubleValue.ToString(CultureInfo.InvariantCulture),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);
}