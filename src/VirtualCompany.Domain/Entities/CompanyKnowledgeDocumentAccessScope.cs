using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VirtualCompany.Domain.Entities;

public sealed class CompanyKnowledgeDocumentAccessScope
{
    public const string CompanyVisibility = "company";

    private static readonly HashSet<string> UnsupportedScopedReferenceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "agent_id",
        "agent_ids",
        "department_id",
        "department_ids",
        "membership_id",
        "membership_ids",
        "role_id",
        "role_ids",
        "user_id",
        "user_ids"
    };

    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = string.Empty;

    [JsonPropertyName("company_id")]
    public Guid CompanyId { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonNode?> AdditionalProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public CompanyKnowledgeDocumentAccessScope()
    {
    }

    public CompanyKnowledgeDocumentAccessScope(Guid companyId, string visibility, Dictionary<string, JsonNode?>? additionalProperties = null)
    {
        if (!TryCreate(companyId, visibility, additionalProperties, out var scope, out var errors))
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(additionalProperties));
        }

        Visibility = scope!.Visibility;
        CompanyId = scope.CompanyId;
        AdditionalProperties = scope.AdditionalProperties;
    }

    public CompanyKnowledgeDocumentAccessScope Clone() =>
        new(CompanyId, Visibility, CloneDictionary(AdditionalProperties));

    public CompanyKnowledgeDocumentAccessScope NormalizeForCompany(Guid companyId)
    {
        var raw = CloneDictionary(AdditionalProperties);
        raw["visibility"] = JsonValue.Create(Visibility);

        if (CompanyId != Guid.Empty)
        {
            raw["company_id"] = JsonValue.Create(CompanyId.ToString("D"));
        }

        if (!TryNormalizeForCompany(companyId, raw, out var normalized, out var errors))
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(companyId));
        }

        return normalized!;
    }

    public static bool TryNormalizeForCompany(
        Guid companyId,
        Dictionary<string, JsonNode?>? value,
        out CompanyKnowledgeDocumentAccessScope? accessScope,
        out IReadOnlyList<string> errors)
    {
        accessScope = null;
        var validationErrors = new List<string>();

        if (value is null || value.Count == 0)
        {
            validationErrors.Add("AccessScope is required and must include tenant-aware visibility metadata.");
            errors = validationErrors;
            return false;
        }

        string? visibility = null;
        Guid? suppliedCompanyId = null;
        var additionalProperties = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in value)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var key = pair.Key.Trim();
            if (key.Equals("visibility", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetStringValue(pair.Value, out var parsedVisibility))
                {
                    validationErrors.Add("AccessScope.visibility is required.");
                }
                else
                {
                    visibility = parsedVisibility;
                }

                continue;
            }

            if (IsCompanyIdentifierKey(key))
            {
                if (!TryGetGuidValue(pair.Value, out var parsedCompanyId))
                {
                    validationErrors.Add("AccessScope.company_id must be a valid company identifier when supplied.");
                }
                else
                {
                    suppliedCompanyId = parsedCompanyId;
                }

                continue;
            }

            if (IsTenantIdentifierKey(key))
            {
                validationErrors.Add("AccessScope.tenant_id is not allowed. The company context is resolved from the route.");
                continue;
            }

            additionalProperties[key] = pair.Value?.DeepClone();
        }

        if (suppliedCompanyId.HasValue && suppliedCompanyId.Value != companyId)
        {
            validationErrors.Add("AccessScope.company_id must match the current company context.");
        }

        if (validationErrors.Count == 0 &&
            !TryCreate(companyId, visibility, additionalProperties, out accessScope, out var modelErrors))
        {
            validationErrors.AddRange(modelErrors);
        }

        errors = validationErrors;
        return errors.Count == 0;
    }

    private static bool TryCreate(
        Guid companyId,
        string? visibility,
        Dictionary<string, JsonNode?>? additionalProperties,
        out CompanyKnowledgeDocumentAccessScope? accessScope,
        out IReadOnlyList<string> errors)
    {
        accessScope = null;
        var validationErrors = new List<string>();

        var normalizedVisibility = NormalizeVisibility(visibility);
        if (companyId == Guid.Empty)
        {
            validationErrors.Add("AccessScope must belong to a company.");
        }

        if (normalizedVisibility is null)
        {
            validationErrors.Add("AccessScope.visibility is required.");
        }
        else if (!string.Equals(normalizedVisibility, CompanyVisibility, StringComparison.Ordinal))
        {
            validationErrors.Add("AccessScope.visibility must be 'company' for tenant-scoped knowledge documents.");
        }

        var normalizedProperties = CloneDictionary(additionalProperties);
        ValidateAdditionalProperties(companyId, normalizedProperties, validationErrors);

        if (validationErrors.Count > 0)
        {
            errors = validationErrors;
            return false;
        }

        accessScope = new CompanyKnowledgeDocumentAccessScope
        {
            Visibility = normalizedVisibility!,
            CompanyId = companyId,
            AdditionalProperties = normalizedProperties
        };

        errors = Array.Empty<string>();
        return true;
    }

    private static void ValidateAdditionalProperties(
        Guid companyId,
        IReadOnlyDictionary<string, JsonNode?> additionalProperties,
        ICollection<string> errors)
    {
        foreach (var pair in additionalProperties)
        {
            ValidateAdditionalProperty(companyId, pair.Key, pair.Value, errors);
        }
    }

    private static void ValidateAdditionalProperty(
        Guid companyId,
        string key,
        JsonNode? value,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (UnsupportedScopedReferenceKeys.Contains(key))
        {
            errors.Add($"AccessScope.{key} is not supported for tenant-scoped knowledge documents.");
        }

        if (IsTenantIdentifierKey(key))
        {
            errors.Add("AccessScope must not contain tenant_id references.");
        }
        else if (IsCompanyIdentifierKey(key))
        {
            if (!TryGetGuidValue(value, out var scopedCompanyId) || scopedCompanyId != companyId)
            {
                errors.Add("AccessScope cannot contain cross-tenant company references.");
            }
        }

        switch (value)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    ValidateAdditionalProperty(companyId, property.Key, property.Value, errors);
                }

                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    switch (item)
                    {
                        case JsonObject arrayObject:
                            foreach (var property in arrayObject)
                            {
                                ValidateAdditionalProperty(companyId, property.Key, property.Value, errors);
                            }

                            break;
                        case JsonArray nestedArray:
                            ValidateAdditionalProperty(companyId, key, nestedArray, errors);
                            break;
                    }
                }

                break;
        }
    }

    private static string? NormalizeVisibility(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static bool TryGetStringValue(JsonNode? value, out string parsedValue)
    {
        parsedValue = string.Empty;
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        parsedValue = rawValue.Trim();
        return true;
    }

    private static bool TryGetGuidValue(JsonNode? value, out Guid guid)
    {
        guid = Guid.Empty;
        return TryGetStringValue(value, out var rawValue) && Guid.TryParse(rawValue, out guid);
    }

    private static bool IsCompanyIdentifierKey(string key) =>
        key.Equals("company_id", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("companyId", StringComparison.OrdinalIgnoreCase);

    private static bool IsTenantIdentifierKey(string key) =>
        key.Equals("tenant_id", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("tenantId", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, JsonNode?> CloneDictionary(Dictionary<string, JsonNode?>? value)
    {
        var clone = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if (value is null)
        {
            return clone;
        }

        foreach (var pair in value)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            clone[pair.Key.Trim()] = pair.Value?.DeepClone();
        }

        return clone;
    }
}
