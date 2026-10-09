using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VirtualCompany.Shared.Contracts.Documents;

[JsonConverter(typeof(CompanyKnowledgeDocumentAccessScopeWireConverter))]
public class CompanyKnowledgeDocumentAccessScopeDto
{
    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = string.Empty;

    [JsonPropertyName("company_id")]
    public Guid CompanyId { get; init; }

    public JsonObject AdditionalProperties { get; init; } = [];
}

public sealed class CompanyKnowledgeDocumentAccessScopeWireConverter : JsonConverter<CompanyKnowledgeDocumentAccessScopeDto>
{
    public override CompanyKnowledgeDocumentAccessScopeDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("CompanyKnowledgeDocumentAccessScope must be a JSON object.");

        var visibility = string.Empty;
        var companyId = Guid.Empty;
        var additional = new JsonObject();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals("visibility"))
                visibility = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : string.Empty;
            else if (property.NameEquals("company_id"))
            {
                if (property.Value.ValueKind == JsonValueKind.String && Guid.TryParse(property.Value.GetString(), out var parsed)) companyId = parsed;
            }
            else additional[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }
        return new() { Visibility = visibility, CompanyId = companyId, AdditionalProperties = additional };
    }

    public override void Write(Utf8JsonWriter writer, CompanyKnowledgeDocumentAccessScopeDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("visibility", value.Visibility);
        writer.WriteString("company_id", value.CompanyId);
        foreach (var property in value.AdditionalProperties)
        {
            writer.WritePropertyName(property.Key);
            if (property.Value is null) writer.WriteNullValue();
            else property.Value.WriteTo(writer, options);
        }
        writer.WriteEndObject();
    }
}
