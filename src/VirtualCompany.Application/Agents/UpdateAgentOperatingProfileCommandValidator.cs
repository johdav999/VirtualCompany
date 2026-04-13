using System.Globalization;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public static class UpdateAgentOperatingProfileCommandValidator
{
    private const int RoleBriefMaxLength = 4000;
    private const int MaxSectionCount = 25;
    private const int MaxItemsPerCollection = 50;
    private const int MaxTextValueLength = 250;
    private const int MaxIdentifierLength = 100;

    private static readonly HashSet<string> SupportedWorkingDays = new(StringComparer.OrdinalIgnoreCase)
    {
        "monday",
        "tuesday",
        "wednesday",
        "thursday",
        "friday",
        "saturday",
        "sunday"
    };

    private static readonly HashSet<string> SupportedToolPermissionActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "allowed",
        "denied",
        "actions",
        "deniedActions"
    };

    private static readonly HashSet<string> SupportedDataScopeActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "read",
        "recommend",
        "execute",
        "write"
    };

    public static void ValidateAndThrow(UpdateAgentOperatingProfileCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var objectives = AgentOperatingProfileJsonMapper.ToJsonDictionary(command.Objectives);
        var kpis = AgentOperatingProfileJsonMapper.ToJsonDictionary(command.Kpis);
        var toolPermissions = AgentOperatingProfileJsonMapper.ToJsonDictionary(command.ToolPermissions);
        var dataScopes = AgentOperatingProfileJsonMapper.ToJsonDictionary(command.DataScopes);
        var approvalThresholds = AgentOperatingProfileJsonMapper.ToJsonDictionary(command.ApprovalThresholds);
        var escalationRules = AgentOperatingProfileJsonMapper.ToJsonDictionary(command.EscalationRules);
        var triggerLogic = AgentOperatingProfileJsonMapper.ToJsonDictionary(command.TriggerLogic);
        var workingHours = command.WorkingHours;

        if (!AgentStatusValues.TryParse(command.Status, out _))
        {
            AddError(errors, nameof(command.Status), AgentStatusValues.BuildValidationMessage(command.Status));
        }

        if (!string.IsNullOrWhiteSpace(command.AutonomyLevel) &&
            !AgentAutonomyLevelValues.TryParse(command.AutonomyLevel, out _))
        {
            AddError(errors, nameof(command.AutonomyLevel), AgentAutonomyLevelValues.BuildValidationMessage(command.AutonomyLevel));
        }

        AddOptional(errors, nameof(command.RoleBrief), command.RoleBrief, RoleBriefMaxLength);
        ValidateObjectives(errors, nameof(command.Objectives), objectives);
        ValidateKpis(errors, nameof(command.Kpis), kpis);
        ValidateToolPermissions(errors, nameof(command.ToolPermissions), toolPermissions);
        ValidateDataScopes(errors, nameof(command.DataScopes), dataScopes);
        ValidateApprovalThresholds(errors, nameof(command.ApprovalThresholds), approvalThresholds);
        ValidateEscalationRules(errors, nameof(command.EscalationRules), escalationRules);
        ValidateTriggerLogic(errors, nameof(command.TriggerLogic), triggerLogic);
        ValidateWorkingHours(errors, nameof(command.WorkingHours), workingHours);

        ThrowIfAny(errors);
    }

    private static void ValidateObjectives(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        ValidateStringCollectionMap(errors, key, payload, "objective");
    }

    private static void ValidateKpis(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            AddError(errors, key, $"{key} must contain at least one entry.");
            return;
        }

        if (payload.Count > MaxSectionCount)
        {
            AddError(errors, key, $"{key} must contain {MaxSectionCount} entries or fewer.");
        }

        foreach (var (propertyName, node) in payload)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                AddError(errors, key, $"{key} contains an empty property name.");
                continue;
            }

            var propertyPath = $"{key}.{propertyName.Trim()}";
            if (node is not JsonArray items)
            {
                AddError(errors, propertyPath, $"{propertyPath} must be an array.");
                continue;
            }

            if (items.Count == 0)
            {
                AddError(errors, propertyPath, $"{propertyPath} must contain at least one KPI.");
                continue;
            }

            if (items.Count > MaxItemsPerCollection)
            {
                AddError(errors, propertyPath, $"{propertyPath} must contain {MaxItemsPerCollection} items or fewer.");
            }

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < items.Count; index++)
            {
                var itemPath = $"{propertyPath}[{index}]";
                var item = items[index];

                if (item is JsonValue stringValue && stringValue.TryGetValue<string>(out var textValue))
                {
                    var normalized = NormalizeText(textValue);
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        AddError(errors, itemPath, "KPI name is required.");
                        continue;
                    }

                    if (normalized.Length > MaxTextValueLength)
                    {
                        AddError(errors, itemPath, $"KPI name must be {MaxTextValueLength} characters or fewer.");
                        continue;
                    }

                    if (!seenNames.Add(normalized))
                    {
                        AddError(errors, itemPath, "KPI names must be unique.");
                    }

                    continue;
                }

                if (item is not JsonObject itemObject)
                {
                    AddError(errors, itemPath, "KPI entries must be strings or objects.");
                    continue;
                }

                var namePath = $"{itemPath}.name";
                var labelPath = $"{itemPath}.label";
                var hasName = TryGetNonEmptyString(itemObject["name"], out var name);
                var hasLabel = TryGetNonEmptyString(itemObject["label"], out var label);

                if (!hasName && !hasLabel)
                {
                    AddError(errors, namePath, "KPI entries must define a non-empty name or label.");
                    continue;
                }

                var resolvedName = hasName ? name : label;
                var resolvedPath = hasName ? namePath : labelPath;

                if (resolvedName.Length > MaxTextValueLength)
                {
                    AddError(errors, resolvedPath, $"KPI name must be {MaxTextValueLength} characters or fewer.");
                    continue;
                }

                if (!seenNames.Add(resolvedName))
                {
                    AddError(errors, resolvedPath, "KPI names must be unique.");
                }
            }
        }
    }

    private static void ValidateToolPermissions(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            AddError(errors, key, $"{key} must contain at least one entry.");
            return;
        }

        foreach (var propertyName in payload.Keys)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                AddError(errors, key, $"{key} contains an empty property name.");
                continue;
            }

            if (!SupportedToolPermissionActions.Contains(propertyName))
            {
                AddError(errors, $"{key}.{propertyName}", $"{key}.{propertyName} is not supported.");
            }
        }

        var allowed = ValidateIdentifierArray(errors, $"{key}.allowed", payload, "allowed");
        var denied = ValidateIdentifierArray(errors, $"{key}.denied", payload, "denied");
        var allowedActions = ValidateIdentifierArray(errors, $"{key}.actions", payload, "actions");
        var deniedActions = ValidateIdentifierArray(errors, $"{key}.deniedActions", payload, "deniedActions");

        if (allowed.Count == 0 && denied.Count == 0)
        {
            AddError(errors, key, $"{key} must define at least one allowed or denied tool.");
            return;
        }

        var overlaps = allowed.Intersect(denied, StringComparer.OrdinalIgnoreCase).ToArray();
        if (overlaps.Length > 0)
        {
            AddError(errors, $"{key}.allowed", "The same tool cannot be both allowed and denied.");
            AddError(errors, $"{key}.denied", "The same tool cannot be both allowed and denied.");
        }

        var actionOverlaps = allowedActions.Intersect(deniedActions, StringComparer.OrdinalIgnoreCase).ToArray();
        if (actionOverlaps.Length > 0)
        {
            AddError(errors, $"{key}.actions", "The same action type cannot be both allowed and denied.");
            AddError(errors, $"{key}.deniedActions", "The same action type cannot be both allowed and denied.");
        }

        foreach (var action in allowedActions.Concat(deniedActions))
        {
            if (!ToolActionTypeValues.TryParse(action, out _))
            {
                AddError(errors, $"{key}.actions", ToolActionTypeValues.BuildValidationMessage(action));
            }
        }
    }

    private static void ValidateDataScopes(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            AddError(errors, key, $"{key} must contain at least one entry.");
            return;
        }

        foreach (var propertyName in payload.Keys)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                AddError(errors, key, $"{key} contains an empty property name.");
                continue;
            }

            if (!SupportedDataScopeActions.Contains(propertyName))
            {
                AddError(errors, $"{key}.{propertyName}", $"{key}.{propertyName} is not supported.");
            }
        }

        var readScopes = ValidateIdentifierArray(errors, $"{key}.read", payload, "read");
        var recommendScopes = ValidateIdentifierArray(errors, $"{key}.recommend", payload, "recommend");
        var executeScopes = ValidateIdentifierArray(errors, $"{key}.execute", payload, "execute");
        var writeScopes = ValidateIdentifierArray(errors, $"{key}.write", payload, "write");

        if (readScopes.Count == 0 && recommendScopes.Count == 0 && executeScopes.Count == 0 && writeScopes.Count == 0)
        {
            AddError(errors, key, $"{key} must define at least one read, recommend, execute, or write scope.");
        }
    }

    private static void ValidateApprovalThresholds(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            AddError(errors, key, $"{key} must contain at least one entry.");
            return;
        }

        foreach (var (propertyName, node) in payload)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                AddError(errors, key, $"{key} contains an empty property name.");
                continue;
            }

            ValidateThresholdNode(errors, $"{key}.{propertyName.Trim()}", node);
        }
    }

    private static void ValidateEscalationRules(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            AddError(errors, key, $"{key} must contain at least one entry.");
            return;
        }

        var criticalRules = ValidateIdentifierArray(errors, $"{key}.critical", payload, "critical");

        var hasEscalateTo = payload.TryGetValue("escalateTo", out var escalateToNode);
        var hasValidEscalateTo = TryGetNonEmptyString(escalateToNode, out _);
        var hasRoute = hasValidEscalateTo;
        if (hasEscalateTo && !hasValidEscalateTo)
        {
            AddError(errors, $"{key}.escalateTo", "Escalation destination is required.");
        }

        if (criticalRules.Count > 0 && !hasValidEscalateTo)
        {
            AddError(errors, $"{key}.escalateTo", "Escalation destination is required when critical rules are configured.");
        }

        if (payload.TryGetValue("notifyAfterMinutes", out var notifyAfterNode) &&
            notifyAfterNode is not null &&
            !TryGetNonNegativeNumber(notifyAfterNode, out _))
        {
            AddError(errors, $"{key}.notifyAfterMinutes", "Escalation delay must be a non-negative number.");
        }

        if (payload.TryGetValue("requireApproval", out var requireApprovalNode) && requireApprovalNode is not null)
        {
            if (requireApprovalNode is not JsonObject requireApprovalObject)
            {
                AddError(errors, $"{key}.requireApproval", "Approval requirement rules must be an object.");
            }
            else
            {
                var actionRules = ValidateIdentifierArray(errors, $"{key}.requireApproval.actions", requireApprovalObject, "actions");
                var toolRules = ValidateIdentifierArray(errors, $"{key}.requireApproval.tools", requireApprovalObject, "tools");
                var scopeRules = ValidateIdentifierArray(errors, $"{key}.requireApproval.scopes", requireApprovalObject, "scopes");

                if (actionRules.Count == 0 && toolRules.Count == 0 && scopeRules.Count == 0)
                {
                    AddError(errors, $"{key}.requireApproval", "Approval requirement rules must define at least one action, tool, or scope.");
                }

                hasRoute = true;
            }
        }

        if (criticalRules.Count == 0 && !hasRoute)
        {
            AddError(errors, key, $"{key} must define at least one escalation route.");
        }
    }

    private static void ValidateTriggerLogic(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return;
        }

        var enabled = false;
        if (payload.TryGetValue("enabled", out var enabledNode) && enabledNode is not null)
        {
            if (enabledNode is not JsonValue enabledValue || !enabledValue.TryGetValue<bool>(out enabled))
            {
                AddError(errors, $"{key}.enabled", "Trigger logic enabled must be a boolean.");
            }
        }

        if (!payload.TryGetValue("conditions", out var conditionsNode) || conditionsNode is null)
        {
            if (enabled)
            {
                AddError(errors, $"{key}.conditions", "At least one trigger condition is required when trigger logic is enabled.");
            }

            return;
        }

        if (conditionsNode is not JsonArray conditions)
        {
            AddError(errors, $"{key}.conditions", "Trigger conditions must be an array.");
            return;
        }

        if (enabled && conditions.Count == 0)
        {
            AddError(errors, $"{key}.conditions", "At least one trigger condition is required when trigger logic is enabled.");
        }

        if (conditions.Count > MaxItemsPerCollection)
        {
            AddError(errors, $"{key}.conditions", $"{key}.conditions must contain {MaxItemsPerCollection} items or fewer.");
        }

        for (var index = 0; index < conditions.Count; index++)
        {
            var conditionPath = $"{key}.conditions[{index}]";
            if (conditions[index] is not JsonObject condition)
            {
                AddError(errors, conditionPath, "Trigger conditions must be objects.");
                continue;
            }

            var hasEvent = TryGetNonEmptyString(condition["event"], out var eventName);
            var hasType = TryGetNonEmptyString(condition["type"], out var typeName);

            if (!hasEvent && !hasType)
            {
                AddError(errors, $"{conditionPath}.event", "Trigger conditions must define an event or type.");
            }

            if (hasEvent && eventName.Length > MaxTextValueLength)
            {
                AddError(errors, $"{conditionPath}.event", $"Trigger event names must be {MaxTextValueLength} characters or fewer.");
            }

            if (hasType && typeName.Length > MaxTextValueLength)
            {
                AddError(errors, $"{conditionPath}.type", $"Trigger types must be {MaxTextValueLength} characters or fewer.");
            }
        }
    }

    private static void ValidateWorkingHours(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return;
        }

        if (!payload.TryGetValue("timezone", out var timezoneNode) ||
            !TryGetNonEmptyString(timezoneNode, out var timezone))
        {
            AddError(errors, $"{key}.timezone", "Timezone is required.");
        }
        else if (!IsValidTimeZoneId(timezone))
        {
            AddError(errors, $"{key}.timezone", "Timezone must be a valid timezone identifier.");
        }

        if (!payload.TryGetValue("windows", out var windowsNode) || windowsNode is null)
        {
            AddError(errors, $"{key}.windows", "Working hour windows are required.");
            return;
        }

        if (windowsNode is not JsonArray windows)
        {
            AddError(errors, $"{key}.windows", "Working hour windows must be an array.");
            return;
        }

        if (windows.Count == 0)
        {
            AddError(errors, $"{key}.windows", "At least one working hour window is required.");
            return;
        }

        if (windows.Count > MaxItemsPerCollection)
        {
            AddError(errors, $"{key}.windows", $"{key}.windows must contain {MaxItemsPerCollection} items or fewer.");
        }

        var windowsByDay = new Dictionary<string, List<(TimeOnly Start, TimeOnly End)>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < windows.Count; index++)
        {
            var windowPath = $"{key}.windows[{index}]";
            if (windows[index] is not JsonObject window)
            {
                AddError(errors, windowPath, "Working hour windows must be objects.");
                continue;
            }

            if (!TryGetNonEmptyString(window["day"], out var day))
            {
                AddError(errors, $"{windowPath}.day", "Day is required.");
                continue;
            }

            if (!SupportedWorkingDays.Contains(day))
            {
                AddError(errors, $"{windowPath}.day", "Day must be a valid weekday.");
            }

            var hasStart = TryGetNonEmptyString(window["start"], out var startText);
            var hasEnd = TryGetNonEmptyString(window["end"], out var endText);

            if (!hasStart ||
                !TimeOnly.TryParseExact(startText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            {
                AddError(errors, $"{windowPath}.start", "Start time must use HH:mm format.");
            }

            if (!hasEnd ||
                !TimeOnly.TryParseExact(endText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                AddError(errors, $"{windowPath}.end", "End time must use HH:mm format.");
            }

            if (!hasStart ||
                !hasEnd ||
                !TimeOnly.TryParseExact(startText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out start) ||
                !TimeOnly.TryParseExact(endText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
            {
                continue;
            }

            if (end <= start)
            {
                AddError(errors, $"{windowPath}.end", "End time must be later than start time.");
                continue;
            }

            if (!windowsByDay.TryGetValue(day, out var dayWindows))
            {
                dayWindows = [];
                windowsByDay[day] = dayWindows;
            }

            if (dayWindows.Any(existing => start < existing.End && end > existing.Start))
            {
                AddError(errors, windowPath, "Working hour windows cannot overlap for the same day.");
                continue;
            }

            dayWindows.Add((start, end));
        }
    }

    private static void ValidateStringCollectionMap(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload,
        string itemLabel)
    {
        if (payload is null || payload.Count == 0)
        {
            AddError(errors, key, $"{key} must contain at least one entry.");
            return;
        }

        if (payload.Count > MaxSectionCount)
        {
            AddError(errors, key, $"{key} must contain {MaxSectionCount} entries or fewer.");
        }

        foreach (var (propertyName, node) in payload)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                AddError(errors, key, $"{key} contains an empty property name.");
                continue;
            }

            var propertyPath = $"{key}.{propertyName.Trim()}";
            if (node is not JsonArray items)
            {
                AddError(errors, propertyPath, $"{propertyPath} must be an array.");
                continue;
            }

            if (items.Count == 0)
            {
                AddError(errors, propertyPath, $"{propertyPath} must contain at least one {itemLabel}.");
                continue;
            }

            if (items.Count > MaxItemsPerCollection)
            {
                AddError(errors, propertyPath, $"{propertyPath} must contain {MaxItemsPerCollection} items or fewer.");
            }

            var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < items.Count; index++)
            {
                var itemPath = $"{propertyPath}[{index}]";
                if (!TryGetNonEmptyString(items[index], out var value))
                {
                    AddError(errors, itemPath, $"{itemLabel} entries must be non-empty strings.");
                    continue;
                }

                if (value.Length > MaxTextValueLength)
                {
                    AddError(errors, itemPath, $"{itemLabel} entries must be {MaxTextValueLength} characters or fewer.");
                    continue;
                }

                if (!seenValues.Add(value))
                {
                    AddError(errors, itemPath, $"{itemLabel} entries must be unique.");
                }
            }
        }
    }

    private static List<string> ValidateIdentifierArray(
        IDictionary<string, List<string>> errors,
        string path,
        IDictionary<string, JsonNode?> payload,
        string propertyName)
    {
        if (!payload.TryGetValue(propertyName, out var node) || node is null)
        {
            return [];
        }

        if (node is not JsonArray items)
        {
            AddError(errors, path, $"{path} must be an array of identifiers.");
            return [];
        }

        if (items.Count == 0)
        {
            AddError(errors, path, $"{path} must contain at least one identifier.");
            return [];
        }

        if (items.Count > MaxItemsPerCollection)
        {
            AddError(errors, path, $"{path} must contain {MaxItemsPerCollection} items or fewer.");
        }

        var values = new List<string>(items.Count);
        var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < items.Count; index++)
        {
            var itemPath = $"{path}[{index}]";
            if (!TryGetNonEmptyString(items[index], out var identifier))
            {
                AddError(errors, itemPath, "Identifier is required.");
                continue;
            }

            if (identifier.Length > MaxIdentifierLength)
            {
                AddError(errors, itemPath, $"Identifiers must be {MaxIdentifierLength} characters or fewer.");
                continue;
            }

            if (!IsValidIdentifier(identifier))
            {
                AddError(errors, itemPath, "Identifiers may only contain letters, numbers, '-', '_', '.', ':', or '/'.");
                continue;
            }

            if (!seenValues.Add(identifier))
            {
                AddError(errors, itemPath, "Identifiers must be unique.");
                continue;
            }

            values.Add(identifier);
        }

        return values;
    }

    private static void ValidateThresholdNode(
        IDictionary<string, List<string>> errors,
        string path,
        JsonNode? node)
    {
        if (node is null)
        {
            AddError(errors, path, "Value is required.");
            return;
        }

        if (node is JsonObject jsonObject)
        {
            if (jsonObject.Count == 0)
            {
                AddError(errors, path, $"{path} must contain at least one value.");
                return;
            }

            foreach (var (propertyName, propertyValue) in jsonObject)
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    AddError(errors, path, $"{path} contains an empty property name.");
                    continue;
                }

                ValidateThresholdNode(errors, $"{path}.{propertyName.Trim()}", propertyValue);
            }

            ValidateThresholdRanges(errors, path, jsonObject);
            return;
        }

        if (node is JsonArray jsonArray)
        {
            if (jsonArray.Count == 0)
            {
                AddError(errors, path, $"{path} must contain at least one value.");
                return;
            }

            for (var index = 0; index < jsonArray.Count; index++)
            {
                ValidateThresholdNode(errors, $"{path}[{index}]", jsonArray[index]);
            }

            return;
        }

        if (!TryGetNumber(node, out var value))
        {
            AddError(errors, path, "Threshold values must be numeric.");
            return;
        }

        if (value < 0)
        {
            AddError(errors, path, "Threshold values must be non-negative.");
        }
    }

    private static void ValidateThresholdRanges(
        IDictionary<string, List<string>> errors,
        string path,
        JsonObject jsonObject)
    {
        var numericProperties = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var (propertyName, propertyValue) in jsonObject)
        {
            if (!string.IsNullOrWhiteSpace(propertyName) && TryGetNumber(propertyValue, out var numericValue))
            {
                numericProperties[propertyName.Trim()] = numericValue;
            }
        }

        foreach (var (propertyName, minValue) in numericProperties)
        {
            if (!propertyName.StartsWith("min", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = propertyName.Length == 3 ? string.Empty : propertyName[3..];
            var maxKey = $"max{suffix}";
            if (numericProperties.TryGetValue(maxKey, out var maxValue) && minValue > maxValue)
            {
                AddError(errors, $"{path}.{maxKey}", $"{path}.{maxKey} must be greater than or equal to {path}.{propertyName}.");
            }
        }
    }

    private static void AddOptional(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            AddError(errors, key, $"{key} must be {maxLength} characters or fewer.");
        }
    }

    private static void AddError(
        IDictionary<string, List<string>> errors,
        string key,
        string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private static void ThrowIfAny(Dictionary<string, List<string>> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        throw new AgentValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static bool TryGetNonEmptyString(JsonNode? node, out string value)
    {
        value = string.Empty;

        if (node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        return true;
    }

    private static bool TryGetNumber(JsonNode? node, out decimal value)
    {
        value = 0;

        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<decimal>(out var decimalValue))
        {
            value = decimalValue;
            return true;
        }

        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            value = (decimal)doubleValue;
            return true;
        }

        if (jsonValue.TryGetValue<int>(out var intValue))
        {
            value = intValue;
            return true;
        }

        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            value = longValue;
            return true;
        }

        return false;
    }

    private static bool TryGetNonNegativeNumber(JsonNode? node, out decimal value)
    {
        if (TryGetNumber(node, out value))
        {
            return value >= 0;
        }

        return false;
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!char.IsLetterOrDigit(trimmed[0]))
        {
            return false;
        }

        return trimmed.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':' or '/');
    }

    private static bool IsValidTimeZoneId(string timezone)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return string.Equals(timezone, "UTC", StringComparison.OrdinalIgnoreCase) ||
                   (timezone.Contains('/', StringComparison.Ordinal) && timezone.All(ch => char.IsLetterOrDigit(ch) || ch is '/' or '_' or '-' or '+'));
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
