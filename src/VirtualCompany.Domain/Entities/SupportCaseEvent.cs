using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public sealed class SupportCaseEvent : ICompanyOwnedEntity
{
    private SupportCaseEvent()
    {
    }

    public SupportCaseEvent(Guid id, Guid companyId, Guid supportCaseId, string eventType, string summary, string actorType, Guid? actorId, DateTime occurredUtc, JsonObject? metadata = null)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        EventType = SupportCaseEventTypes.Normalize(eventType);
        Summary = SupportEntityText.NormalizeRequired(summary, nameof(summary), 1000);
        ActorType = SupportEntityText.NormalizeRequired(actorType, nameof(actorType), 64);
        ActorId = SupportEntityText.NormalizeOptionalId(actorId, nameof(actorId));
        OccurredUtc = SupportEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        Metadata = metadata is null ? [] : JsonNode.Parse(metadata.ToJsonString())!.AsObject();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string ActorType { get; private set; } = null!;
    public Guid? ActorId { get; private set; }
    public JsonObject Metadata { get; private set; } = [];
    public DateTime OccurredUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;
}

