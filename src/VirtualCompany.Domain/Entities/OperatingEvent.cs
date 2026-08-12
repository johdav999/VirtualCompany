using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class OperatingEvent : ICompanyOwnedEntity
{
    private OperatingEvent() { }
    public OperatingEvent(Guid id, Guid companyId, string eventType, string sourceType,
        string sourceId, int sourceVersion, DateTime observedUtc, OperatingEventMateriality materiality,
        string deduplicationKey, string correlationId, Guid? affectedGoalId = null,
        IDictionary<string, JsonNode?>? payload = null)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId));
        if (sourceVersion < 1) throw new ArgumentOutOfRangeException(nameof(sourceVersion));
        if (affectedGoalId == Guid.Empty) throw new ArgumentException("Affected goal id cannot be empty.", nameof(affectedGoalId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EventType = OperatingCycle.Text(eventType, nameof(eventType), 100);
        SourceType = OperatingCycle.Text(sourceType, nameof(sourceType), 100);
        SourceId = OperatingCycle.Text(sourceId, nameof(sourceId), 200);
        SourceVersion = sourceVersion;
        ObservedUtc = observedUtc.ToUniversalTime();
        Materiality = materiality;
        DeduplicationKey = OperatingCycle.Text(deduplicationKey, nameof(deduplicationKey), 200);
        CorrelationId = OperatingCycle.Text(correlationId, nameof(correlationId), 128);
        AffectedGoalId = affectedGoalId;
        Payload = OperatingPlan.Clone(payload);
        Status = OperatingEventStatus.Pending;
        CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string SourceType { get; private set; } = null!;
    public string SourceId { get; private set; } = null!;
    public int SourceVersion { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public OperatingEventMateriality Materiality { get; private set; }
    public OperatingEventStatus Status { get; private set; }
    public string DeduplicationKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public Guid? AffectedGoalId { get; private set; }
    public Dictionary<string, JsonNode?> Payload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SuppressionReason { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? ProcessedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public CompanyGoal? AffectedGoal { get; private set; }
    public void MarkProcessed() { if (Status != OperatingEventStatus.Pending) return; Status = OperatingEventStatus.Processed; ProcessedUtc = DateTime.UtcNow; }
    public void Suppress(string reason) { if (Status != OperatingEventStatus.Pending) return; Status = OperatingEventStatus.Suppressed; SuppressionReason = OperatingCycle.Text(reason, nameof(reason), 500); ProcessedUtc = DateTime.UtcNow; }
    public void Coalesce(string reason) { if (Status != OperatingEventStatus.Pending) return; Status = OperatingEventStatus.Coalesced; SuppressionReason = OperatingCycle.Text(reason, nameof(reason), 500); ProcessedUtc = DateTime.UtcNow; }
}
