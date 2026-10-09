using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualCompany.Shared;

/// <summary>Uses the API's stable snake-case enum representation, accepting legacy numeric values.</summary>
public sealed class WireEnumJsonConverter : JsonStringEnumConverter
{
    public WireEnumJsonConverter() : base(JsonNamingPolicy.SnakeCaseLower) { }
}
