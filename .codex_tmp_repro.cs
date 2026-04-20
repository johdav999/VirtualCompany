using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;

var scope = new CompanyKnowledgeDocumentAccessScope(
    Guid.Parse("4e030f2f-789a-49f0-8385-8e1c67f499c8"),
    CompanyKnowledgeDocumentAccessScope.CompanyVisibility,
    new System.Collections.Generic.Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
    {
        ["restricted"] = JsonValue.Create(true),
        ["roles"] = new JsonArray(JsonValue.Create("owner"))
    });

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var json = JsonSerializer.Serialize(scope, options);
Console.WriteLine(json);
var roundTrip = JsonSerializer.Deserialize<CompanyKnowledgeDocumentAccessScope>(json, options);
Console.WriteLine(roundTrip is null ? "null" : "ok");
