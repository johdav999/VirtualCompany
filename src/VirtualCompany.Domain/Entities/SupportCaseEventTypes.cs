using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public static class SupportCaseEventTypes
{
    public const string Created = "created";
    public const string MessageReceived = "message_received";
    public const string Triaged = "triaged";
    public const string Assigned = "assigned";
    public const string StatusChanged = "status_changed";
    public const string PriorityChanged = "priority_changed";
    public const string ReplyDrafted = "reply_drafted";
    public const string ReplySent = "reply_sent";
    public const string Escalated = "escalated";
    public const string ApprovalRequested = "approval_requested";
    public const string ApprovalResolved = "approval_resolved";
    public const string InternalTaskCreated = "internal_task_created";
    public const string Resolved = "resolved";
    public const string Reopened = "reopened";
    public const string Closed = "closed";
    public const string SlaRisk = "sla_risk";
    public const string SlaBreached = "sla_breached";
    public static string Normalize(string value) => SupportCaseStatuses.NormalizeKnownForSupport(value, [Created, MessageReceived, Triaged, Assigned, StatusChanged, PriorityChanged, ReplyDrafted, ReplySent, Escalated, ApprovalRequested, ApprovalResolved, InternalTaskCreated, Resolved, Reopened, Closed, SlaRisk, SlaBreached], nameof(value));
}

