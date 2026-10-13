using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Agents;

public sealed class AgentOperatingProfileFormModel : IValidatableObject
{
    private static readonly JsonSerializerOptions JsonEditorOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static bool IsSupportedStatus(string? status) =>
        status?.Trim().ToLowerInvariant() is
            "active" or
            "paused" or
            "restricted" or
            "archived";

    [Required, StringLength(32)]
    public string Status { get; set; } = "active";

    [StringLength(4000)]
    public string? RoleBrief { get; set; }

    [StringLength(32)]
    public string AutonomyLevel { get; set; } = "level_0";

    public string ObjectivesJson { get; set; } = "{}";
    public string KpisJson { get; set; } = "{}";
    public string ToolPermissionsJson { get; set; } = "{}";
    public string DataScopesJson { get; set; } = "{}";
    public string ApprovalThresholdsJson { get; set; } = "{}";
    public string EscalationRulesJson { get; set; } = "{}";
    public string TriggerLogicJson { get; set; } = "{}";
    public string WorkingHoursJson { get; set; } = "{}";

    public static AgentOperatingProfileFormModel FromViewModel(AgentOperatingProfileViewModel model) =>
        new()
        {
            Status = NormalizeStatus(model.Status),
            AutonomyLevel = NormalizeToken(model.AutonomyLevel),
            RoleBrief = model.RoleBrief,
            ObjectivesJson = Serialize(model.Objectives),
            KpisJson = Serialize(model.Kpis),
            ToolPermissionsJson = Serialize(model.ToolPermissions),
            DataScopesJson = Serialize(model.DataScopes),
            ApprovalThresholdsJson = Serialize(model.ApprovalThresholds),
            EscalationRulesJson = Serialize(model.EscalationRules),
            TriggerLogicJson = Serialize(model.TriggerLogic),
            WorkingHoursJson = Serialize(model.WorkingHours)
        };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsSupportedStatus(Status))
        {
            yield return new ValidationResult(
                "Status must be one of active, paused, restricted, or archived.",
                [nameof(Status)]);
        }

        if (!IsSupportedAutonomyLevel(AutonomyLevel))
        {
            yield return new ValidationResult(
                "AutonomyLevel must be one of level_0, level_1, level_2, or level_3.",
                [nameof(AutonomyLevel)]);
        }
    }

    public bool TryCreateRequest(
        AgentProfileVisibilityViewModel visibility,
        out UpdateAgentOperatingProfileRequest request,
        out Dictionary<string, string[]> errors)
    {
        var collectedErrors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var objectives = visibility.CanEditObjectives ? ParseJson("Objectives", ObjectivesJson, collectedErrors) : null;
        var kpis = visibility.CanEditKpis ? ParseJson("Kpis", KpisJson, collectedErrors) : null;
        var toolPermissions = visibility.CanEditSensitiveGovernance ? ParseJson("ToolPermissions", ToolPermissionsJson, collectedErrors) : null;
        var dataScopes = visibility.CanEditSensitiveGovernance ? ParseJson("DataScopes", DataScopesJson, collectedErrors) : null;
        var approvalThresholds = visibility.CanEditSensitiveGovernance ? ParseJson("ApprovalThresholds", ApprovalThresholdsJson, collectedErrors) : null;
        var escalationRules = visibility.CanEditSensitiveGovernance ? ParseJson("EscalationRules", EscalationRulesJson, collectedErrors) : null;
        var triggerLogic = visibility.CanEditSensitiveGovernance ? ParseJson("TriggerLogic", TriggerLogicJson, collectedErrors) : null;
        var workingHours = visibility.CanEditWorkingHours ? ParseJson("WorkingHours", WorkingHoursJson, collectedErrors) : null;

        if (collectedErrors.Count > 0)
        {
            request = new UpdateAgentOperatingProfileRequest();
            errors = collectedErrors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            return false;
        }

        request = new UpdateAgentOperatingProfileRequest
        {
            Status = NormalizeStatus(Status),
            RoleBrief = visibility.CanEditRoleBrief ? RoleBrief : null,
            AutonomyLevel = visibility.CanEditSensitiveGovernance ? NormalizeToken(AutonomyLevel) : null,
            Objectives = objectives,
            Kpis = kpis,
            ToolPermissions = toolPermissions,
            DataScopes = dataScopes,
            ApprovalThresholds = approvalThresholds,
            EscalationRules = escalationRules,
            TriggerLogic = triggerLogic,
            WorkingHours = workingHours
        };

        errors = new Dictionary<string, string[]>();
        return true;
    }

    private static Dictionary<string, JsonNode?> ParseJson(
        string fieldName,
        string value,
        IDictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonNode?>>(value, JsonEditorOptions);
            if (payload is null)
            {
                AddError(errors, fieldName, $"{fieldName} must be a JSON object.");
                return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
            }

            return new Dictionary<string, JsonNode?>(payload, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            AddError(errors, fieldName, $"{fieldName} must be valid JSON.");
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Serialize(IDictionary<string, JsonNode?> payload) =>
        JsonSerializer.Serialize(payload, JsonEditorOptions);

    private static string NormalizeStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToLowerInvariant();

    private static string NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static bool IsSupportedAutonomyLevel(string? autonomyLevel) =>
        autonomyLevel?.Trim().ToLowerInvariant() is
            "level_0" or
            "level_1" or
            "level_2" or
            "level_3";

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }
}
