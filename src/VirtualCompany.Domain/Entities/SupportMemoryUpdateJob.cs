using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public sealed class SupportMemoryUpdateJob : ICompanyOwnedEntity
{
    private SupportMemoryUpdateJob() { }
    public SupportMemoryUpdateJob(Guid id, Guid companyId, Guid supportCaseId, string eventKey)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        EventKey = SupportEntityText.NormalizeRequired(eventKey, nameof(eventKey), 200);
        Status = "pending";
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public string EventKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public string? SafeFailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public void Start() { if (Status == "completed" || Status == "skipped") return; Status = "processing"; AttemptCount++; SafeFailureSummary = null; UpdatedUtc = DateTime.UtcNow; }
    public void Complete(bool skipped = false) { Status = skipped ? "skipped" : "completed"; CompletedUtc = UpdatedUtc = DateTime.UtcNow; SafeFailureSummary = null; }
    public void Fail(string summary) { Status = "failed"; SafeFailureSummary = SupportEntityText.NormalizeRequired(summary, nameof(summary), 1000); UpdatedUtc = DateTime.UtcNow; }
}

