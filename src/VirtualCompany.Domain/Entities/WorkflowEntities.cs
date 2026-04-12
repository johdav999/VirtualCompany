using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class WorkflowDefinition
{
    private const int CodeMaxLength = 100;
    private const int NameMaxLength = 200;
    private const int DepartmentMaxLength = 100;

    // Versioned definitions are immutable after creation; only activation metadata can change.
    private WorkflowDefinition()
    {
    }

    public WorkflowDefinition(
        Guid id,
        Guid? companyId,
        string code,
        string name,
        string? department,
        WorkflowTriggerType triggerType,
        int version,
        IDictionary<string, JsonNode?> definitionJson,
        bool active = true)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version must be greater than zero.");
        }

        if (definitionJson.Count == 0)
        {
            throw new ArgumentException("DefinitionJson is required.", nameof(definitionJson));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Code = NormalizeCode(code);
        Name = NormalizeRequired(name, nameof(name), NameMaxLength);
        Department = NormalizeOptional(department, nameof(department), DepartmentMaxLength);
        TriggerType = triggerType;
        Version = version;
        DefinitionJson = CloneNodes(definitionJson);
        Active = active;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid? CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Department { get; private set; }
    public int Version { get; private set; }
    public WorkflowTriggerType TriggerType { get; private set; }
    public Dictionary<string, JsonNode?> DefinitionJson { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Active { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company? Company { get; private set; }
    public ICollection<WorkflowTrigger> Triggers { get; } = new List<WorkflowTrigger>();
    public ICollection<WorkflowInstance> Instances { get; } = new List<WorkflowInstance>();
    public ICollection<WorkflowException> Exceptions { get; } = new List<WorkflowException>();

    public void SetActive(bool active)
    {
        if (Active == active)
        {
            return;
        }

        Active = active;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeCode(string value) =>
        NormalizeRequired(value, nameof(Code), CodeMaxLength).ToUpperInvariant();

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

public sealed class WorkflowTrigger : ICompanyOwnedEntity
{
    private const int EventNameMaxLength = 200;

    private WorkflowTrigger()
    {
    }

    public WorkflowTrigger(
        Guid id,
        Guid companyId,
        Guid definitionId,
        string eventName,
        IDictionary<string, JsonNode?>? criteriaJson = null,
        bool isEnabled = true)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (definitionId == Guid.Empty)
        {
            throw new ArgumentException("DefinitionId is required.", nameof(definitionId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DefinitionId = definitionId;
        EventName = NormalizeRequired(eventName, nameof(eventName), EventNameMaxLength);
        CriteriaJson = CloneNodes(criteriaJson);
        IsEnabled = isEnabled;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public string EventName { get; private set; } = null!;
    public Dictionary<string, JsonNode?> CriteriaJson { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsEnabled { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public WorkflowDefinition Definition { get; private set; } = null!;

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

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkflowInstance : ICompanyOwnedEntity
{
    private WorkflowInstance()
    {
        InputPayload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        ContextJson = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
    }

    public WorkflowInstance(
        Guid id,
        Guid companyId,
        Guid definitionId,
        Guid? triggerId,
        IDictionary<string, JsonNode?>? inputPayload = null,
        WorkflowTriggerType triggerSource = WorkflowTriggerType.Manual,
        string? triggerRef = null,
        string? currentStep = null,
        IDictionary<string, JsonNode?>? contextJson = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (definitionId == Guid.Empty)
        {
            throw new ArgumentException("DefinitionId is required.", nameof(definitionId));
        }

        if (triggerId == Guid.Empty)
        {
            throw new ArgumentException("TriggerId cannot be empty.", nameof(triggerId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DefinitionId = definitionId;
        TriggerId = triggerId;
        TriggerSource = triggerSource;
        TriggerRef = NormalizeOptional(triggerRef, nameof(triggerRef), 200);
        CurrentStep = NormalizeOptional(currentStep, nameof(currentStep), 200);
        State = WorkflowInstanceStatusValues.DefaultStatus;
        InputPayload = CloneNodes(inputPayload);
        ContextJson = CloneNodes(contextJson);
        StartedUtc = DateTime.UtcNow;
        UpdatedUtc = StartedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid? TriggerId { get; private set; }
    public WorkflowTriggerType TriggerSource { get; private set; }
    public string? TriggerRef { get; private set; }
    public WorkflowInstanceStatus State { get; private set; }
    public WorkflowInstanceStatus Status => State;
    public string? CurrentStep { get; private set; }
    public Dictionary<string, JsonNode?> InputPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ContextJson { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> OutputPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime StartedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public WorkflowDefinition Definition { get; private set; } = null!;
    public WorkflowTrigger? Trigger { get; private set; }
    public ICollection<WorkTask> Tasks { get; } = new List<WorkTask>();
    public ICollection<WorkflowException> Exceptions { get; } = new List<WorkflowException>();

    public void UpdateState(
        WorkflowInstanceStatus state,
        string? currentStep,
        IDictionary<string, JsonNode?>? outputPayload = null)
    {
        State = state;
        CurrentStep = NormalizeOptional(currentStep, nameof(currentStep), 200);
        if (outputPayload is not null)
        {
            OutputPayload = CloneNodes(outputPayload);
        }

        UpdatedUtc = DateTime.UtcNow;
        CompletedUtc = state is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed or WorkflowInstanceStatus.Cancelled
            ? UpdatedUtc
            : null;
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

public sealed class WorkflowException : ICompanyOwnedEntity
{
    private const int StepKeyMaxLength = 200;
    private const int TitleMaxLength = 200;
    private const int DetailsMaxLength = 4000;
    private const int ErrorCodeMaxLength = 100;
    private const int ResolutionNotesMaxLength = 2000;

    private WorkflowException()
    {
        TechnicalDetailsJson = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
    }

    public WorkflowException(
        Guid id,
        Guid companyId,
        Guid workflowInstanceId,
        Guid workflowDefinitionId,
        string? stepKey,
        WorkflowExceptionType exceptionType,
        string title,
        string details,
        string? errorCode = null,
        IDictionary<string, JsonNode?>? technicalDetailsJson = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (workflowInstanceId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowInstanceId is required.", nameof(workflowInstanceId));
        }

        if (workflowDefinitionId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowDefinitionId is required.", nameof(workflowDefinitionId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        WorkflowInstanceId = workflowInstanceId;
        WorkflowDefinitionId = workflowDefinitionId;
        StepKey = NormalizeOptional(stepKey, nameof(stepKey), StepKeyMaxLength) ?? "instance";
        ExceptionType = exceptionType;
        Status = WorkflowExceptionStatus.Open;
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        Details = NormalizeRequired(details, nameof(details), DetailsMaxLength);
        ErrorCode = NormalizeOptional(errorCode, nameof(errorCode), ErrorCodeMaxLength);
        TechnicalDetailsJson = CloneNodes(technicalDetailsJson);
        OccurredUtc = DateTime.UtcNow;
        UpdatedUtc = OccurredUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public Guid WorkflowDefinitionId { get; private set; }
    public string StepKey { get; private set; } = null!;
    public WorkflowExceptionType ExceptionType { get; private set; }
    public WorkflowExceptionStatus Status { get; private set; }
    public string Title { get; private set; } = null!;
    public string Details { get; private set; } = null!;
    public string? ErrorCode { get; private set; }
    public Dictionary<string, JsonNode?> TechnicalDetailsJson { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime OccurredUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ReviewedUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public Company Company { get; private set; } = null!;
    public WorkflowInstance WorkflowInstance { get; private set; } = null!;
    public WorkflowDefinition Definition { get; private set; } = null!;

    public void MarkReviewed(Guid reviewedByUserId, string? resolutionNotes = null)
    {
        if (reviewedByUserId == Guid.Empty)
        {
            throw new ArgumentException("ReviewedByUserId is required.", nameof(reviewedByUserId));
        }

        if (Status != WorkflowExceptionStatus.Open)
        {
            throw new InvalidOperationException("Only open workflow exceptions can be reviewed.");
        }

        Status = WorkflowExceptionStatus.Reviewed;
        ReviewedByUserId = reviewedByUserId;
        ResolutionNotes = NormalizeOptional(resolutionNotes, nameof(resolutionNotes), ResolutionNotesMaxLength);
        ReviewedUtc = DateTime.UtcNow;
        UpdatedUtc = ReviewedUtc.Value;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return NormalizeOptional(value, name, maxLength)!;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}
