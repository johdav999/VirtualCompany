using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ToolExecutionAttempt : ICompanyOwnedEntity
{
    private const int ToolNameMaxLength = 100;
    private const int ScopeMaxLength = 100;

    private ToolExecutionAttempt()
    {
    }

    public ToolExecutionAttempt(
        Guid id,
        Guid companyId,
        Guid agentId,
        string toolName,
        ToolActionType actionType,
        string? scope,
        IDictionary<string, JsonNode?>? requestPayload = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        }

        ToolActionTypeValues.EnsureSupported(actionType, nameof(actionType));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        ToolName = NormalizeRequired(toolName, nameof(toolName), ToolNameMaxLength);
        ActionType = actionType;
        Scope = NormalizeOptional(scope, nameof(scope), ScopeMaxLength);
        Status = ToolExecutionStatus.Denied;
        RequestPayload = CloneNodes(requestPayload);
        PolicyDecision = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        ResultPayload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public string ToolName { get; private set; } = null!;
    public ToolActionType ActionType { get; private set; }
    public string? Scope { get; private set; }
    public ToolExecutionStatus Status { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Dictionary<string, JsonNode?> RequestPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> PolicyDecision { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ResultPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ExecutedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public void MarkDenied(IDictionary<string, JsonNode?>? policyDecision)
    {
        Status = ToolExecutionStatus.Denied;
        PolicyDecision = CloneNodes(policyDecision);
        ResultPayload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        ApprovalRequestId = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkAwaitingApproval(Guid approvalRequestId, IDictionary<string, JsonNode?>? policyDecision)
    {
        if (approvalRequestId == Guid.Empty)
        {
            throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId));
        }

        Status = ToolExecutionStatus.AwaitingApproval;
        ApprovalRequestId = approvalRequestId;
        PolicyDecision = CloneNodes(policyDecision);
        ResultPayload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkExecuted(
        IDictionary<string, JsonNode?>? policyDecision,
        IDictionary<string, JsonNode?>? resultPayload)
    {
        Status = ToolExecutionStatus.Executed;
        PolicyDecision = CloneNodes(policyDecision);
        ResultPayload = CloneNodes(resultPayload);
        ExecutedUtc = DateTime.UtcNow;
        UpdatedUtc = ExecutedUtc.Value;
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

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ApprovalRequest : ICompanyOwnedEntity
{
    private const int ToolNameMaxLength = 100;
    private const int ApprovalTargetMaxLength = 100;

    private ApprovalRequest()
    {
    }

    public ApprovalRequest(
        Guid id,
        Guid companyId,
        Guid agentId,
        Guid toolExecutionAttemptId,
        Guid requestedByUserId,
        string toolName,
        ToolActionType actionType,
        string? approvalTarget,
        IDictionary<string, JsonNode?>? thresholdContext = null,
        IDictionary<string, JsonNode?>? policyDecision = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        }

        if (toolExecutionAttemptId == Guid.Empty)
        {
            throw new ArgumentException("ToolExecutionAttemptId is required.", nameof(toolExecutionAttemptId));
        }

        if (requestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("RequestedByUserId is required.", nameof(requestedByUserId));
        }

        ToolActionTypeValues.EnsureSupported(actionType, nameof(actionType));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        ToolExecutionAttemptId = toolExecutionAttemptId;
        RequestedByUserId = requestedByUserId;
        ToolName = NormalizeRequired(toolName, nameof(toolName), ToolNameMaxLength);
        ActionType = actionType;
        ApprovalTarget = NormalizeOptional(approvalTarget, nameof(approvalTarget), ApprovalTargetMaxLength);
        Status = ApprovalRequestStatus.Pending;
        ThresholdContext = CloneNodes(thresholdContext);
        PolicyDecision = CloneNodes(policyDecision);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid ToolExecutionAttemptId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string ToolName { get; private set; } = null!;
    public ToolActionType ActionType { get; private set; }
    public string? ApprovalTarget { get; private set; }
    public ApprovalRequestStatus Status { get; private set; }
    public Dictionary<string, JsonNode?> ThresholdContext { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> PolicyDecision { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

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