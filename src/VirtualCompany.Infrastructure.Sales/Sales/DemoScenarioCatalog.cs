using System.Reflection;
using System.Text.Json;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class DemoScenarioCatalog : IDemoScenarioCatalog
{
    private static readonly HashSet<string> AllowedCommands =
    [
        "demo.sales.qualify_lead",
        "demo.sales.convert_lead",
        "demo.sales.move_deal_to_proposal"
    ];

    private static readonly HashSet<string> AllowedEntityTypes =
    [
        "customer_company",
        "contact",
        "lead"
    ];

    private readonly IReadOnlyDictionary<(string Key, int Version), DemoScenarioDefinitionDto> _definitions;

    public DemoScenarioCatalog()
    {
        var assembly = typeof(DemoScenarioCatalog).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(x => x.Contains(".DemoScenarios.", StringComparison.Ordinal) && x.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (names.Length == 0)
            throw new InvalidOperationException("No embedded demo scenario specifications were found.");

        _definitions = names
            .Select(name => Parse(ReadResource(assembly, name)))
            .ToDictionary(x => (x.ScenarioKey, x.ScenarioVersion));
    }

    public IReadOnlyList<DemoScenarioDefinitionDto> List() =>
        _definitions.Values.OrderBy(x => x.ScenarioKey).ThenBy(x => x.ScenarioVersion).ToArray();

    public DemoScenarioDefinitionDto Get(string scenarioKey, int scenarioVersion)
    {
        var key = NormalizeKey(scenarioKey);
        return _definitions.TryGetValue((key, scenarioVersion), out var definition)
            ? definition
            : throw new DemoScenarioException(
                DemoScenarioProblemCodes.InvalidSpecification,
                $"Demo scenario '{key}' version {scenarioVersion} is not available.");
    }

    public static DemoScenarioDefinitionDto Parse(string json)
    {
        ScenarioDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ScenarioDocument>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException exception)
        {
            throw new DemoScenarioException(DemoScenarioProblemCodes.InvalidSpecification, $"The demo scenario JSON is invalid: {exception.Message}");
        }

        if (document is null || document.SchemaVersion != 1)
            throw Invalid("Only demo scenario schema version 1 is supported.");
        var key = NormalizeKey(document.ScenarioKey);
        if (document.ScenarioVersion < 1) throw Invalid("ScenarioVersion must be greater than zero.");
        Required(document.Title, "Title", 160);
        Required(document.CompanyName, "CompanyName", 200);
        if (document.InitialState is null) throw Invalid("InitialState is required.");
        ValidateInitialState(document.InitialState);

        var entities = document.SeededEntities ?? [];
        if (entities.Count != AllowedEntityTypes.Count ||
            entities.Any(x => !AllowedEntityTypes.Contains(Normalize(x.EntityType)) || x.Count != 1) ||
            entities.Select(x => Normalize(x.EntityType)).Distinct(StringComparer.Ordinal).Count() != AllowedEntityTypes.Count)
            throw Invalid("The scenario must define exactly one customer_company, contact, and lead seed record.");

        var roles = (document.AllowedRoles ?? []).Select(Normalize).ToArray();
        if (roles.Length == 0 || roles.Any(x => x is not ("owner" or "admin" or "manager")))
            throw Invalid("AllowedRoles may contain only owner, admin, and manager.");

        var actions = document.Actions ?? [];
        if (actions.Count != AllowedCommands.Count ||
            !actions.Select(x => x.StepNumber).SequenceEqual(Enumerable.Range(1, actions.Count)) ||
            actions.Any(x => !AllowedCommands.Contains(Normalize(x.CommandName))) ||
            actions.Select(x => Normalize(x.CommandName)).Distinct(StringComparer.Ordinal).Count() != AllowedCommands.Count)
            throw Invalid("Actions must be the ordered, unique allowlisted demo commands.");
        foreach (var action in actions)
        {
            Required(action.DisplayName, "Action.DisplayName", 120);
            Required(action.ExpectedVisibleOutcome, "Action.ExpectedVisibleOutcome", 500);
        }

        var resetRules = RequiredList(document.ResetRules, "ResetRules");
        var validations = RequiredList(document.ValidationChecks, "ValidationChecks");
        var disabled = RequiredList(document.DisabledIntegrations, "DisabledIntegrations");

        return new DemoScenarioDefinitionDto(
            document.SchemaVersion,
            key,
            document.ScenarioVersion,
            document.Title.Trim(),
            document.CompanyName.Trim(),
            new DemoScenarioInitialStateDto(
                document.InitialState.CustomerCompanyName.Trim(),
                document.InitialState.CustomerIndustry.Trim(),
                document.InitialState.ContactName.Trim(),
                document.InitialState.ContactEmail.Trim().ToLowerInvariant(),
                document.InitialState.ContactTitle.Trim(),
                document.InitialState.LeadTitle.Trim(),
                document.InitialState.EstimatedValue,
                document.InitialState.Currency.Trim().ToUpperInvariant(),
                document.InitialState.SeedTimestampUtc.ToUniversalTime()),
            entities.Select(x => new DemoScenarioSeededEntityDto(Normalize(x.EntityType), NormalizeKey(x.LogicalKey), x.Count)).ToArray(),
            roles,
            actions.Select(x => new DemoScenarioActionDto(x.StepNumber, Normalize(x.CommandName), x.DisplayName.Trim(), x.ExpectedVisibleOutcome.Trim())).ToArray(),
            resetRules,
            validations,
            disabled);
    }

    private static void ValidateInitialState(InitialStateDocument value)
    {
        Required(value.CustomerCompanyName, "InitialState.CustomerCompanyName", 200);
        Required(value.CustomerIndustry, "InitialState.CustomerIndustry", 120);
        Required(value.ContactName, "InitialState.ContactName", 160);
        Required(value.ContactEmail, "InitialState.ContactEmail", 256);
        if (!value.ContactEmail.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
            throw Invalid("Synthetic contact email must use the reserved .invalid top-level domain.");
        Required(value.ContactTitle, "InitialState.ContactTitle", 120);
        Required(value.LeadTitle, "InitialState.LeadTitle", 200);
        if (value.EstimatedValue <= 0) throw Invalid("InitialState.EstimatedValue must be greater than zero.");
        if (Required(value.Currency, "InitialState.Currency", 3).Length != 3) throw Invalid("InitialState.Currency must be a three-letter code.");
        if (value.SeedTimestampUtc == default || value.SeedTimestampUtc.Kind != DateTimeKind.Utc)
            throw Invalid("InitialState.SeedTimestampUtc must be an explicit UTC timestamp.");
    }

    private static IReadOnlyList<string> RequiredList(List<string>? values, string field)
    {
        if (values is null || values.Count == 0) throw Invalid($"{field} must contain at least one item.");
        return values.Select(x => Required(x, field, 500)).ToArray();
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded demo scenario '{name}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Required(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid($"{field} is required.");
        var result = value.Trim();
        return result.Length <= maxLength ? result : throw Invalid($"{field} exceeds {maxLength} characters.");
    }

    private static string Normalize(string? value) => Required(value, "Value", 160).ToLowerInvariant();
    private static string NormalizeKey(string? value) => Normalize(value);
    private static DemoScenarioException Invalid(string message) => new(DemoScenarioProblemCodes.InvalidSpecification, message);

    private sealed class ScenarioDocument
    {
        public int SchemaVersion { get; set; }
        public string ScenarioKey { get; set; } = "";
        public int ScenarioVersion { get; set; }
        public string Title { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public InitialStateDocument? InitialState { get; set; }
        public List<SeededEntityDocument>? SeededEntities { get; set; }
        public List<string>? AllowedRoles { get; set; }
        public List<ActionDocument>? Actions { get; set; }
        public List<string>? ResetRules { get; set; }
        public List<string>? ValidationChecks { get; set; }
        public List<string>? DisabledIntegrations { get; set; }
    }

    private sealed class InitialStateDocument
    {
        public string CustomerCompanyName { get; set; } = "";
        public string CustomerIndustry { get; set; } = "";
        public string ContactName { get; set; } = "";
        public string ContactEmail { get; set; } = "";
        public string ContactTitle { get; set; } = "";
        public string LeadTitle { get; set; } = "";
        public decimal EstimatedValue { get; set; }
        public string Currency { get; set; } = "";
        public DateTime SeedTimestampUtc { get; set; }
    }

    private sealed class SeededEntityDocument
    {
        public string EntityType { get; set; } = "";
        public string LogicalKey { get; set; } = "";
        public int Count { get; set; }
    }

    private sealed class ActionDocument
    {
        public int StepNumber { get; set; }
        public string CommandName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ExpectedVisibleOutcome { get; set; } = "";
    }
}

