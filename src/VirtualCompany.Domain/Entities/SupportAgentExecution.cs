using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public sealed class SupportAgentExecution : ICompanyOwnedEntity
{
    private SupportAgentExecution() { }

    public SupportAgentExecution(Guid id, Guid companyId, Guid supportCaseId, Guid? agentId, string idempotencyKey)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        AgentId = SupportEntityText.NormalizeOptionalId(agentId, nameof(agentId));
        IdempotencyKey = SupportEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 200);
        Status = "running";
        CurrentStep = "started";
        Summary = "Support agent run started.";
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public Guid? AgentId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string CurrentStep { get; private set; } = null!;
    public Guid? CreatedDraftId { get; private set; }
    public string Summary { get; private set; } = null!;
    public string? FailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }

    public void MoveTo(string step, string summary)
    {
        CurrentStep = SupportEntityText.NormalizeRequired(step, nameof(step), 80);
        Summary = SupportEntityText.NormalizeRequired(summary, nameof(summary), 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Complete(Guid? createdDraftId, string summary)
    {
        CreatedDraftId = SupportEntityText.NormalizeOptionalId(createdDraftId, nameof(createdDraftId));
        Summary = SupportEntityText.NormalizeRequired(summary, nameof(summary), 1000);
        Status = "completed";
        CurrentStep = "completed";
        CompletedUtc = UpdatedUtc = DateTime.UtcNow;
        FailureSummary = null;
    }

    public void Fail(string step, string summary)
    {
        CurrentStep = SupportEntityText.NormalizeRequired(step, nameof(step), 80);
        FailureSummary = SupportEntityText.NormalizeRequired(summary, nameof(summary), 1000);
        Summary = FailureSummary;
        Status = "failed";
        CompletedUtc = UpdatedUtc = DateTime.UtcNow;
    }
}

