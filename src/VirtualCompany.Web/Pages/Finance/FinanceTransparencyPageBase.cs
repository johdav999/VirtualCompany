using Microsoft.AspNetCore.Components;
using System.Globalization;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public abstract class FinanceTransparencyPageBase : FinancePageBase
{
    [Inject] protected IFinanceSandboxAdminService SandboxAdminService { get; set; } = default!;

    protected bool HasTransparencyAccess =>
        AccessState.IsAllowed &&
        FinanceAccess.CanAccessSandboxAdmin(AccessState.MembershipRole);

    protected Guid CompanyScopeId => AccessState.CompanyId!.Value;

    protected string BuildFinanceHomeHref() =>
        FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, AccessState.CompanyId);

    protected string BuildEventDetailHref(Guid eventId) =>
        FinanceRoutes.BuildTransparencyEventDetailPath(eventId, AccessState.CompanyId);

    protected string BuildExecutionDetailHref(Guid executionId) =>
        FinanceRoutes.BuildTransparencyToolExecutionDetailPath(executionId, AccessState.CompanyId);

    protected string BuildApprovalHref(Guid approvalRequestId) =>
        $"/approvals?companyId={CompanyScopeId:D}&approvalId={approvalRequestId:D}";

    protected string BuildTaskHref(Guid taskId) =>
        $"/tasks?companyId={CompanyScopeId:D}&taskId={taskId:D}";

    protected string BuildWorkflowHref(Guid workflowInstanceId) =>
        $"/workflows?companyId={CompanyScopeId:D}&workflowInstanceId={workflowInstanceId:D}";

    protected string? BuildFinanceEntityHref(string? entityType, string? entityId)
    {
        if (!Guid.TryParse(entityId, out var parsedEntityId))
        {
            return null;
        }

        return NormalizeToken(entityType) switch
        {
            "finance_transaction" or "transaction" => FinanceRoutes.BuildTransactionDetailPath(parsedEntityId, CompanyScopeId),
            "finance_invoice" or "invoice" => FinanceRoutes.BuildInvoiceDetailPath(parsedEntityId, CompanyScopeId),
            "finance_anomaly" or "anomaly" => FinanceRoutes.BuildAnomalyDetailPath(parsedEntityId, CompanyScopeId),
            "finance_alert" or "alert" => FinanceRoutes.BuildAlertDetailPath(parsedEntityId, CompanyScopeId),
            _ => null
        };
    }

    protected string? BuildOriginatingEntityHref(FinanceTransparencyToolExecutionDetailViewModel detail) =>
        detail.OriginatingEntityId is Guid entityId
            ? BuildFinanceEntityHref(detail.OriginatingEntityType, entityId.ToString("D", CultureInfo.InvariantCulture))
            : null;

    protected string? BuildRelatedRecordHref(FinanceTransparencyRelatedRecordViewModel record) =>
        NormalizeToken(record.TargetType) switch
        {
            "approval_request" => Guid.TryParse(record.TargetId, out var approvalRequestId) ? BuildApprovalHref(approvalRequestId) : null,
            "tool_execution" or "tool_execution_attempt" or "agent_tool_execution" => Guid.TryParse(record.TargetId, out var executionId) ? BuildExecutionDetailHref(executionId) : null,
            "audit_event" => Guid.TryParse(record.TargetId, out var eventId) ? BuildEventDetailHref(eventId) : null,
            "work_task" or "task" => Guid.TryParse(record.TargetId, out var taskId) ? BuildTaskHref(taskId) : null,
            "workflow_instance" => Guid.TryParse(record.TargetId, out var workflowInstanceId) ? BuildWorkflowHref(workflowInstanceId) : null,
            _ => BuildFinanceEntityHref(record.TargetType, record.TargetId)
        };

    protected static string BuildRelatedRecordTitle(FinanceTransparencyRelatedRecordViewModel record)
    {
        var label = FormatLabel(record.RelationshipType);
        return string.Equals(label, "n/a", StringComparison.OrdinalIgnoreCase)
            ? FormatValue(record.DisplayText)
            : $"{label}: {FormatValue(record.DisplayText)}";
    }

    protected static string FormatLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "n/a"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    protected static string FormatTimestamp(DateTime value) =>
        value == default
            ? "Unknown time"
            : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    protected static string FormatValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "n/a" : value.Trim();

    private static string NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
}
