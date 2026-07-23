using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Infrastructure.Finance;

public static class SimulationEventDeterministicIdentity
{
    public static Guid CreateEventId(
        Guid companyId,
        int seed,
        DateTime startSimulatedUtc,
        DateTime simulationDateUtc,
        string eventType,
        string sourceEntityType,
        Guid? sourceEntityId,
        int sequenceNumber,
        string? deterministicConfigurationJson = null)
    {
        var material = BuildMaterial(companyId, seed, startSimulatedUtc, simulationDateUtc, eventType, sourceEntityType, sourceEntityId, sequenceNumber, deterministicConfigurationJson);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    public static string CreateDeterministicKey(
        Guid companyId,
        int seed,
        DateTime startSimulatedUtc,
        DateTime simulationDateUtc,
        string eventType,
        string sourceEntityType,
        Guid? sourceEntityId,
        int sequenceNumber,
        string? deterministicConfigurationJson = null)
    {
        var material = BuildMaterial(companyId, seed, startSimulatedUtc, simulationDateUtc, eventType, sourceEntityType, sourceEntityId, sequenceNumber, deterministicConfigurationJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static string BuildMaterial(
        Guid companyId,
        int seed,
        DateTime startSimulatedUtc,
        DateTime simulationDateUtc,
        string eventType,
        string sourceEntityType,
        Guid? sourceEntityId,
        int sequenceNumber,
        string? deterministicConfigurationJson)
    {
        if (sequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "Sequence number must be positive.");
        }

        return string.Join(
            "|",
            companyId.ToString("N"),
            seed.ToString(CultureInfo.InvariantCulture),
            NormalizeUtc(startSimulatedUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            NormalizeUtc(simulationDateUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            eventType.Trim().ToLowerInvariant(),
            sourceEntityType.Trim().ToLowerInvariant(),
            sourceEntityId?.ToString("N") ?? string.Empty,
            sequenceNumber.ToString(CultureInfo.InvariantCulture),
            Canonicalize(deterministicConfigurationJson));
    }

    private static string Canonicalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            return CanonicalizeNode(JsonNode.Parse(json));
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }

    private static string CanonicalizeNode(JsonNode? node) =>
        node switch
        {
            null => "null",
            JsonValue value => value.ToJsonString(),
            JsonArray array => $"[{string.Join(",", array.Select(CanonicalizeNode))}]",
            JsonObject obj => $"{{{string.Join(",", obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{JsonValue.Create(x.Key)!.ToJsonString()}:{CanonicalizeNode(x.Value)}"))}}}",
            _ => node.ToJsonString()
        };

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}