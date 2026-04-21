using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ReconciliationSuggestionRecord : ICompanyOwnedEntity
{
    private const int RecordTypeMaxLength = 32;
    private const int MatchTypeMaxLength = 32;

    private ReconciliationSuggestionRecord()
    {
    }

    public ReconciliationSuggestionRecord(
        Guid id,
        Guid companyId,
        string sourceRecordType,
        Guid sourceRecordId,
        string targetRecordType,
        Guid targetRecordId,
        string matchType,
        decimal confidenceScore,
        IDictionary<string, JsonNode?>? ruleBreakdown,
        Guid createdByUserId,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (sourceRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceRecordId is required.", nameof(sourceRecordId));
        }

        if (targetRecordId == Guid.Empty)
        {
            throw new ArgumentException("TargetRecordId is required.", nameof(targetRecordId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SourceRecordType = NormalizeRecordType(sourceRecordType, nameof(sourceRecordType));
        SourceRecordId = sourceRecordId;
        TargetRecordType = NormalizeRecordType(targetRecordType, nameof(targetRecordType));
        TargetRecordId = targetRecordId;
        MatchType = NormalizeMatchType(matchType, nameof(matchType));
        ConfidenceScore = NormalizeConfidenceScore(confidenceScore, nameof(confidenceScore));
        RuleBreakdown = CloneNodes(ruleBreakdown);
        Status = ReconciliationSuggestionStatuses.Open;
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string SourceRecordType { get; private set; } = null!;
    public Guid SourceRecordId { get; private set; }
    public string TargetRecordType { get; private set; } = null!;
    public Guid TargetRecordId { get; private set; }
    public string MatchType { get; private set; } = null!;
    public decimal ConfidenceScore { get; private set; }
    public Dictionary<string, JsonNode?> RuleBreakdown { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime? AcceptedUtc { get; private set; }
    public DateTime? RejectedUtc { get; private set; }
    public DateTime? SupersededUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;
    public User UpdatedByUser { get; private set; } = null!;
    public ICollection<ReconciliationResultRecord> AcceptedResults { get; } = new List<ReconciliationResultRecord>();

    public void Accept(Guid updatedByUserId, DateTime? acceptedUtc = null)
    {
        EnsureActionableTransition(nameof(Accept));
        UpdatedByUserId = NormalizeUserId(updatedByUserId, nameof(updatedByUserId));
        AcceptedUtc = EntityTimestampNormalizer.NormalizeUtc(acceptedUtc ?? DateTime.UtcNow, nameof(acceptedUtc));
        UpdatedUtc = AcceptedUtc.Value;
        Status = ReconciliationSuggestionStatuses.Accepted;
    }

    public void Reject(Guid updatedByUserId, DateTime? rejectedUtc = null)
    {
        EnsureActionableTransition(nameof(Reject));
        UpdatedByUserId = NormalizeUserId(updatedByUserId, nameof(updatedByUserId));
        RejectedUtc = EntityTimestampNormalizer.NormalizeUtc(rejectedUtc ?? DateTime.UtcNow, nameof(rejectedUtc));
        UpdatedUtc = RejectedUtc.Value;
        Status = ReconciliationSuggestionStatuses.Rejected;
    }

    public void Supersede(Guid updatedByUserId, DateTime? supersededUtc = null)
    {
        EnsureActionableTransition(nameof(Supersede));
        UpdatedByUserId = NormalizeUserId(updatedByUserId, nameof(updatedByUserId));
        SupersededUtc = EntityTimestampNormalizer.NormalizeUtc(supersededUtc ?? DateTime.UtcNow, nameof(supersededUtc));
        UpdatedUtc = SupersededUtc.Value;
        Status = ReconciliationSuggestionStatuses.Superseded;
    }

    private void EnsureActionableTransition(string operation)
    {
        if (!ReconciliationSuggestionStatuses.IsActionable(Status))
        {
            throw new InvalidOperationException($"Cannot {operation.ToLowerInvariant()} suggestion in status '{Status}'.");
        }
    }

    private static Guid NormalizeUserId(Guid value, string name) =>
        value == Guid.Empty
            ? throw new ArgumentException($"{name} is required.", name)
            : value;

    private static string NormalizeRecordType(string value, string name)
    {
        var normalized = ReconciliationRecordTypes.Normalize(value);
        if (!ReconciliationRecordTypes.IsSupported(normalized))
        {
            throw new ArgumentOutOfRangeException(name, value, "Unsupported reconciliation record type.");
        }

        return normalized.Length <= RecordTypeMaxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {RecordTypeMaxLength} characters or fewer.");
    }

    private static string NormalizeMatchType(string value, string name)
    {
        var normalized = ReconciliationMatchTypes.Normalize(value);
        if (!ReconciliationMatchTypes.IsSupported(normalized))
        {
            throw new ArgumentOutOfRangeException(name, value, "Unsupported reconciliation match type.");
        }

        return normalized.Length <= MatchTypeMaxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {MatchTypeMaxLength} characters or fewer.");
    }

    private static decimal NormalizeConfidenceScore(decimal value, string name)
    {
        var normalized = decimal.Round(value, 4, MidpointRounding.AwayFromZero);
        if (normalized < 0m || normalized > 1m)
        {
            throw new ArgumentOutOfRangeException(name, "ConfidenceScore must be between 0 and 1.");
        }

        return normalized;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class ReconciliationResultRecord : ICompanyOwnedEntity
{
    private const int RecordTypeMaxLength = 32;
    private const int MatchTypeMaxLength = 32;

    private ReconciliationResultRecord()
    {
    }

    public ReconciliationResultRecord(
        Guid id,
        Guid companyId,
        Guid acceptedSuggestionId,
        string sourceRecordType,
        Guid sourceRecordId,
        string targetRecordType,
        Guid targetRecordId,
        string matchType,
        decimal confidenceScore,
        IDictionary<string, JsonNode?>? ruleBreakdown,
        Guid createdByUserId,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (acceptedSuggestionId == Guid.Empty)
        {
            throw new ArgumentException("AcceptedSuggestionId is required.", nameof(acceptedSuggestionId));
        }

        if (sourceRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceRecordId is required.", nameof(sourceRecordId));
        }

        if (targetRecordId == Guid.Empty)
        {
            throw new ArgumentException("TargetRecordId is required.", nameof(targetRecordId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AcceptedSuggestionId = acceptedSuggestionId;
        SourceRecordType = NormalizeRecordType(sourceRecordType, nameof(sourceRecordType));
        SourceRecordId = sourceRecordId;
        TargetRecordType = NormalizeRecordType(targetRecordType, nameof(targetRecordType));
        TargetRecordId = targetRecordId;
        MatchType = NormalizeMatchType(matchType, nameof(matchType));
        ConfidenceScore = NormalizeConfidenceScore(confidenceScore, nameof(confidenceScore));
        RuleBreakdown = CloneNodes(ruleBreakdown);
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AcceptedSuggestionId { get; private set; }
    public string SourceRecordType { get; private set; } = null!;
    public Guid SourceRecordId { get; private set; }
    public string TargetRecordType { get; private set; } = null!;
    public Guid TargetRecordId { get; private set; }
    public string MatchType { get; private set; } = null!;
    public decimal ConfidenceScore { get; private set; }
    public Dictionary<string, JsonNode?> RuleBreakdown { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public Company Company { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;
    public User UpdatedByUser { get; private set; } = null!;
    public ReconciliationSuggestionRecord AcceptedSuggestion { get; private set; } = null!;

    private static string NormalizeRecordType(string value, string name)
    {
        var normalized = ReconciliationRecordTypes.Normalize(value);
        if (!ReconciliationRecordTypes.IsSupported(normalized))
        {
            throw new ArgumentOutOfRangeException(name, value, "Unsupported reconciliation record type.");
        }

        return normalized.Length <= RecordTypeMaxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {RecordTypeMaxLength} characters or fewer.");
    }

    private static string NormalizeMatchType(string value, string name)
    {
        var normalized = ReconciliationMatchTypes.Normalize(value);
        if (!ReconciliationMatchTypes.IsSupported(normalized))
        {
            throw new ArgumentOutOfRangeException(name, value, "Unsupported reconciliation match type.");
        }

        return normalized.Length <= MatchTypeMaxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {MatchTypeMaxLength} characters or fewer.");
    }

    private static decimal NormalizeConfidenceScore(decimal value, string name)
    {
        var normalized = decimal.Round(value, 4, MidpointRounding.AwayFromZero);
        if (normalized < 0m || normalized > 1m)
        {
            throw new ArgumentOutOfRangeException(name, "ConfidenceScore must be between 0 and 1.");
        }

        return normalized;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}
