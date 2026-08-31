using System.Globalization;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public static class AgentAuthorityTransparencyPresenter
{
    private static readonly string[] ActionOrder = ["read", "recommend", "execute"];

    public static IReadOnlyList<AgentAuthorityCapabilityRow> SelectCapabilityRows(
        AgentCapabilityCatalogViewModel catalog,
        int maximumRows = 6)
    {
        if (maximumRows <= 0)
        {
            return [];
        }

        var candidates = catalog.EffectiveTools
            .Where(item => !string.IsNullOrWhiteSpace(item.ToolName))
            .OrderBy(item => StateRank(item.State))
            .ThenBy(item => item.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selected = new List<EffectiveAgentToolAuthorityViewModel>(maximumRows);

        foreach (var action in ActionOrder)
        {
            selected.AddRange(candidates
                .Where(item => Is(item.ActionType, action))
                .Take(2));
        }

        selected.AddRange(candidates.Where(item => !selected.Contains(item)));

        return selected
            .Distinct()
            .Take(maximumRows)
            .Select(item => new AgentAuthorityCapabilityRow(
                item.ToolName,
                Normalize(item.ActionType),
                FirstMeaningful(item.ActorPermission, item.RequiredFinancePermissions.FirstOrDefault(), "membership.access"),
                Normalize(item.State),
                Normalize(item.ReasonCode),
                FirstMeaningful(item.ApprovalBehavior, ApprovalRequirement(item)),
                FirstMeaningful(item.IntegrationState, IntegrationState(item.State))))
            .ToArray();
    }

    public static AgentApprovalPreview? CreateApprovalPreview(
        IEnumerable<ApprovalRequestViewModel> approvals,
        Guid agentId)
    {
        var approval = approvals
            .Where(item => Is(item.RequestedByActorType, "agent") && item.RequestedByActorId == agentId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();
        if (approval is null)
        {
            return null;
        }

        var binding = ObjectAt(approval.ThresholdContext, "approvalBinding");
        var risk = ObjectAt(approval.ThresholdContext, "riskClassification");
        var toolName = StringAt(approval.ThresholdContext, "toolName");
        var actionType = StringAt(approval.ThresholdContext, "actionType");
        var evidenceAt = DateAt(binding, "issuedUtc") ?? approval.CreatedAt;
        var expiresAt = DateAt(approval.ThresholdContext, "expiresUtc") ?? DateAt(binding, "expiresUtc");
        var executionId = GuidAt(approval.ThresholdContext, "toolExecutionAttemptId")
            ?? GuidAt(approval.ThresholdContext, "toolExecutionId");
        var targetLabel = FirstMeaningful(
            approval.DisplayTitle,
            approval.AffectedDataSummary,
            approval.AffectedEntities.FirstOrDefault()?.Label,
            approval.DisplayReference);

        return new AgentApprovalPreview(
            approval.Id,
            Normalize(approval.Status),
            approval.TargetEntityType,
            approval.TargetEntityId,
            targetLabel,
            toolName,
            actionType,
            evidenceAt,
            StringAt(risk, "riskTier"),
            approval.RequiredRole,
            approval.RequiredUserId,
            BoolAt(binding, "segregationRequired"),
            expiresAt,
            executionId);
    }

    public static string StateLabelKey(string state) => Normalize(state) switch
    {
        "available" => "AuthorityStateAvailable",
        "approval_required" => "AuthorityStateApprovalRequired",
        "configuration_required" => "AuthorityStateConfigurationRequired",
        "permission_denied" => "AuthorityStatePermissionDenied",
        "integration_unavailable" => "AuthorityStateIntegrationUnavailable",
        "not_implemented" => "AuthorityStateNotImplemented",
        _ => "AuthorityStateUnavailable"
    };

    public static string StateExplanationKey(string reasonCode) => Normalize(reasonCode) switch
    {
        "authority_available" => "AuthorityExplanationAvailable",
        "authority_approval_required" => "AuthorityExplanationApprovalRequired",
        "authority_agent_inactive" => "AuthorityExplanationAgentInactive",
        "authority_explicitly_denied" => "AuthorityExplanationExplicitlyDenied",
        "authority_action_denied" => "AuthorityExplanationActionDenied",
        "authority_scope_denied" => "AuthorityExplanationScopeDenied",
        "authority_configuration_required" => "AuthorityExplanationConfigurationRequired",
        "authority_integration_unavailable" => "AuthorityExplanationIntegrationUnavailable",
        "authority_not_implemented" => "AuthorityExplanationNotImplemented",
        "effective_authority_stale" => "AuthorityExplanationStale",
        _ => "AuthorityExplanationUnavailable"
    };

    public static string StateTone(string state) => Normalize(state) switch
    {
        "available" => "is-ready",
        "approval_required" => "is-review",
        "configuration_required" or "integration_unavailable" => "is-setup",
        _ => "is-blocked"
    };

    public static string ApprovalStatusLabelKey(string status) => Normalize(status) switch
    {
        "pending" => "ApprovalStatusPending",
        "approved" => "ApprovalStatusApproved",
        "rejected" => "ApprovalStatusRejected",
        "expired" => "ApprovalStatusExpired",
        "cancelled" => "ApprovalStatusCancelled",
        "stale" => "ApprovalStatusStale",
        "superseded" => "ApprovalStatusSuperseded",
        "revoked" => "ApprovalStatusRevoked",
        _ => "ApprovalStatusUnknown"
    };

    public static string ApprovalStatusTone(string status) => Normalize(status) switch
    {
        "pending" => "is-review",
        "approved" => "is-ready",
        "rejected" or "expired" or "stale" or "superseded" or "revoked" => "is-blocked",
        _ => "is-setup"
    };

    public static bool RequiresFreshReview(string status) => Normalize(status) is
        "expired" or "stale" or "superseded" or "revoked";

    public static string? BuildTargetHref(AgentApprovalPreview preview, Guid companyId) =>
        Normalize(preview.TargetEntityType) switch
        {
            "finance_transaction" or "transaction" => FinanceRoutes.BuildTransactionDetailPath(preview.TargetEntityId, companyId),
            "finance_bill" or "bill" => FinanceRoutes.BuildBillDetailPath(preview.TargetEntityId, companyId),
            "finance_payment" or "payment" => FinanceRoutes.BuildPaymentDetailPath(preview.TargetEntityId, companyId),
            "finance_invoice" or "invoice" => FinanceRoutes.BuildInvoiceDetailPath(preview.TargetEntityId, companyId),
            "finance_anomaly" or "anomaly" => FinanceRoutes.BuildAnomalyDetailPath(preview.TargetEntityId, companyId),
            "finance_alert" or "alert" => FinanceRoutes.BuildAlertDetailPath(preview.TargetEntityId, companyId),
            "task" or "work_task" => $"/tasks?companyId={companyId:D}&taskId={preview.TargetEntityId:D}",
            _ => null
        };

    private static string ApprovalRequirement(EffectiveAgentToolAuthorityViewModel item) =>
        Normalize(item.State) == "approval_required"
            ? "required"
            : Normalize(item.ActionType) == "execute"
                ? "policy_dependent"
                : "not_required";

    private static string IntegrationState(string state) => Normalize(state) switch
    {
        "integration_unavailable" => "unavailable",
        "configuration_required" => "setup_required",
        "not_implemented" => "not_available",
        _ => "ready"
    };

    private static int StateRank(string state) => Normalize(state) switch
    {
        "available" => 0,
        "approval_required" => 1,
        "configuration_required" => 2,
        "integration_unavailable" => 3,
        "permission_denied" => 4,
        _ => 5
    };

    private static JsonObject? ObjectAt(IReadOnlyDictionary<string, JsonNode?> values, string key) =>
        values.TryGetValue(key, out var node) ? node as JsonObject : null;

    private static string? StringAt(IReadOnlyDictionary<string, JsonNode?> values, string key) =>
        values.TryGetValue(key, out var node) ? StringValue(node) : null;

    private static string? StringAt(JsonObject? values, string key) =>
        values is not null && values.TryGetPropertyValue(key, out var node) ? StringValue(node) : null;

    private static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static DateTime? DateAt(IReadOnlyDictionary<string, JsonNode?> values, string key) =>
        values.TryGetValue(key, out var node) ? DateValue(node) : null;

    private static DateTime? DateAt(JsonObject? values, string key) =>
        values is not null && values.TryGetPropertyValue(key, out var node) ? DateValue(node) : null;

    private static DateTime? DateValue(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<DateTime>(out var date))
        {
            return date;
        }

        return value.TryGetValue<string>(out var text) &&
               DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date)
            ? date
            : null;
    }

    private static Guid? GuidAt(IReadOnlyDictionary<string, JsonNode?> values, string key)
    {
        if (!values.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<Guid>(out var id))
        {
            return id;
        }

        return value.TryGetValue<string>(out var text) && Guid.TryParse(text, out id) ? id : null;
    }

    private static bool BoolAt(JsonObject? values, string key) =>
        values is not null &&
        values.TryGetPropertyValue(key, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<bool>(out var result) &&
        result;

    private static string FirstMeaningful(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool Is(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
}

public sealed record AgentAuthorityCapabilityRow(
    string ToolName,
    string ActionMode,
    string ActorPermission,
    string State,
    string ReasonCode,
    string ApprovalRequirement,
    string IntegrationState);

public sealed record AgentApprovalPreview(
    Guid ApprovalId,
    string Status,
    string TargetEntityType,
    Guid TargetEntityId,
    string TargetSummary,
    string? ToolName,
    string? ActionType,
    DateTime EvidenceAt,
    string? RiskTier,
    string? RequiredRole,
    Guid? RequiredUserId,
    bool RequiresIndependentApproval,
    DateTime? ExpiresAt,
    Guid? ToolExecutionId);
