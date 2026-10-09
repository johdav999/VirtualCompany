using System.Text.Json;
using System.Text.Json.Serialization;
namespace VirtualCompany.Application.Support;

// Keep this new nested report contract readable by existing monthly clients, which
// use numeric enums. Native Support calendar endpoint serialization is unchanged.
public sealed class SupportQualityCalendarConverter:JsonConverter<SupportBusinessCalendarDto>
{
    private static readonly JsonSerializerOptions WriteOptions=new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ReadOptions=new(JsonSerializerDefaults.Web){Converters={new JsonStringEnumConverter()}};
    public override SupportBusinessCalendarDto Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        =>JsonSerializer.Deserialize<SupportBusinessCalendarDto>(ref reader,ReadOptions)??throw new JsonException("Support calendar evidence is missing.");
    public override void Write(Utf8JsonWriter writer,SupportBusinessCalendarDto value,JsonSerializerOptions options)
        =>JsonSerializer.Serialize(writer,value,WriteOptions);
}
