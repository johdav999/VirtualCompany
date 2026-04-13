using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class WorkTask : ICompanyOwnedEntity
{
    private const int TypeMaxLength = 100;
    private const int TitleMaxLength = 200;
    private const int DescriptionMaxLength = 4000;
    private const int ActorTypeMaxLength = 64;
    private const int RationaleSummaryMaxLength = 2000;
    private const int CorrelationIdMaxLength = 128;

    private WorkTask()
    {
    }

    public WorkTask(
        Guid id,
        Guid companyId,
        string type,
        string title,
        string? description,
        WorkTaskPriority priority,
        Guid? assignedAgentId,
        Guid? parentTaskId,
        string createdByActorType,
        Guid? createdByActorId,
        IDictionary<string, JsonNode?>? inputPayload = null,
        Guid? workflowInstanceId = null,
        IDictionary<string, JsonNode?>? outputPayload = null,
        string? rationaleSummary = null,
        decimal? confidenceScore = null,
        string? correlationId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (assignedAgentId == Guid.Empty)
        {
            throw new ArgumentException("AssignedAgentId cannot be empty.", nameof(assignedAgentId));
        }

        if (parentTaskId == Guid.Empty)
        {
            throw new ArgumentException("ParentTaskId cannot be empty.", nameof(parentTaskId));
        }

        if (createdByActorId == Guid.Empty)
        {
            throw new ArgumentException("CreatedByActorId cannot be empty.", nameof(createdByActorId));
        }

        if (workflowInstanceId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowInstanceId cannot be empty.", nameof(workflowInstanceId));
        }

        if (confidenceScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceScore), "ConfidenceScore must be between 0 and 1.");
        }

        _ = priority.ToStorageValue();

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Type = NormalizeRequired(type, nameof(type), TypeMaxLength);
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        Description = NormalizeOptional(description, nameof(description), DescriptionMaxLength);
        Priority = priority;
        Status = WorkTaskStatusValues.DefaultStatus;
        AssignedAgentId = assignedAgentId;
        ParentTaskId = parentTaskId;
        CreatedByActorType = NormalizeRequired(createdByActorType, nameof(createdByActorType), ActorTypeMaxLength);
        CreatedByActorId = createdByActorId;
        InputPayload = CloneNodes(inputPayload);
        OutputPayload = CloneNodes(outputPayload);
        RationaleSummary = NormalizeOptional(rationaleSummary, nameof(rationaleSummary), RationaleSummaryMaxLength);
        ConfidenceScore = confidenceScore;
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        WorkflowInstanceId = workflowInstanceId;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public Guid? ParentTaskId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }
    public string CreatedByActorType { get; private set; } = null!;
    public Guid? CreatedByActorId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public WorkTaskPriority Priority { get; private set; }
    public WorkTaskStatus Status { get; private set; }
    public DateTime? DueUtc { get; private set; }
    public Dictionary<string, JsonNode?> InputPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> OutputPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? RationaleSummary { get; private set; }
    public decimal? ConfidenceScore { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent? AssignedAgent { get; private set; }
    public WorkTask? ParentTask { get; private set; }
    public ICollection<WorkTask> Subtasks { get; } = new List<WorkTask>();
    public WorkflowInstance? WorkflowInstance { get; private set; }
    public ICollection<ConversationTaskLink> ConversationLinks { get; } = new List<ConversationTaskLink>();

    public void SetDueDate(DateTime? dueUtc)
    {
        DueUtc = dueUtc;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void AssignTo(Guid? agentId)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AssignedAgentId cannot be empty.", nameof(agentId));
        }

        if (AssignedAgentId == agentId)
        {
            return;
        }

        AssignedAgentId = agentId;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void LinkToParent(Guid parentTaskId)
    {
        if (parentTaskId == Guid.Empty)
        {
            throw new ArgumentException("ParentTaskId is required.", nameof(parentTaskId));
        }

        if (parentTaskId == Id)
        {
            throw new InvalidOperationException("A task cannot be its own parent.");
        }

        ParentTaskId = parentTaskId;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetCorrelationId(string? correlationId)
    {
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateStatus(
        WorkTaskStatus status,
        IDictionary<string, JsonNode?>? outputPayload = null,
        string? rationaleSummary = null,
        decimal? confidenceScore = null)
    {
        WorkTaskStatusValues.EnsureSupported(status, nameof(status));

        if (confidenceScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceScore), "ConfidenceScore must be between 0 and 1.");
        }

        Status = status;
        OutputPayload = CloneNodes(outputPayload);
        RationaleSummary = NormalizeOptional(rationaleSummary, nameof(rationaleSummary), RationaleSummaryMaxLength);
        ConfidenceScore = confidenceScore;
        CompletedUtc = status == WorkTaskStatus.Completed ? DateTime.UtcNow : null;
        UpdatedUtc = DateTime.UtcNow;
    }

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

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}
