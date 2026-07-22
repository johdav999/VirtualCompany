using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;
public sealed class FinanceSeedAnomaly : ICompanyOwnedEntity
{
    private FinanceSeedAnomaly()
    {
    }

    public FinanceSeedAnomaly(
        Guid id,
        Guid companyId,
        string anomalyType,
        string scenarioProfile,
        IReadOnlyCollection<Guid> affectedRecordIds,
        string expectedDetectionMetadataJson)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (affectedRecordIds.Count == 0)
        {
            throw new ArgumentException("At least one affected record id is required.", nameof(affectedRecordIds));
        }

        if (affectedRecordIds.Any(x => x == Guid.Empty))
        {
            throw new ArgumentException("Affected record ids cannot contain empty values.", nameof(affectedRecordIds));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AnomalyType = NormalizeRequired(anomalyType, nameof(anomalyType), 64);
        ScenarioProfile = NormalizeRequired(scenarioProfile, nameof(scenarioProfile), 64);
        AffectedRecordIdsJson = new JsonArray(affectedRecordIds.Select(id => JsonValue.Create(id)).Cast<JsonNode?>().ToArray()).ToJsonString();
        ExpectedDetectionMetadataJson = NormalizeRequired(expectedDetectionMetadataJson, nameof(expectedDetectionMetadataJson), 4000);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string AnomalyType { get; private set; } = null!;
    public string ScenarioProfile { get; private set; } = null!;
    public string AffectedRecordIdsJson { get; private set; } = null!;
    public string ExpectedDetectionMetadataJson { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public IReadOnlyList<Guid> GetAffectedRecordIds() =>
        JsonNode.Parse(AffectedRecordIdsJson)?.AsArray()
            .Select(x => x?.GetValue<Guid>() ?? Guid.Empty)
            .Where(x => x != Guid.Empty)
            .ToArray() ?? [];

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

