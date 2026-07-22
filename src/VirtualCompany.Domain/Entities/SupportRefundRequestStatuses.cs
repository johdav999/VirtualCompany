using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public static class SupportRefundRequestStatuses
{
    public const string PendingApproval = "pending_approval";
    public const string Approved = "approved";
    public const string Queued = "queued";
    public const string PendingFinanceApproval = "pending_finance_approval";
    public const string Executing = "executing";
    public const string ReconciliationRequired = "reconciliation_required";
    public const string Completed = "completed";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
    public const string Executed = "executed";
    public const string Failed = "failed";
}

