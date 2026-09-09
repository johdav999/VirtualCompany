namespace VirtualCompany.Application.Sales;

public sealed record DemoScenarioSeededEntityDto(string EntityType, string LogicalKey, int Count);

public sealed record DemoScenarioActionDto(
    int StepNumber,
    string CommandName,
    string DisplayName,
    string ExpectedVisibleOutcome);

public sealed record DemoScenarioInitialStateDto(
    string CustomerCompanyName,
    string CustomerIndustry,
    string ContactName,
    string ContactEmail,
    string ContactTitle,
    string LeadTitle,
    decimal EstimatedValue,
    string Currency,
    DateTime SeedTimestampUtc);

public sealed record DemoScenarioDefinitionDto(
    int SchemaVersion,
    string ScenarioKey,
    int ScenarioVersion,
    string Title,
    string CompanyName,
    DemoScenarioInitialStateDto InitialState,
    IReadOnlyList<DemoScenarioSeededEntityDto> SeededEntities,
    IReadOnlyList<string> AllowedRoles,
    IReadOnlyList<DemoScenarioActionDto> Actions,
    IReadOnlyList<string> ResetRules,
    IReadOnlyList<string> ValidationChecks,
    IReadOnlyList<string> DisabledIntegrations);

public sealed record ProvisionDemoScenarioRequest(
    string ScenarioKey,
    int ScenarioVersion,
    bool ConfirmSyntheticDataOnly);

public sealed record ProvisionDemoScenarioResult(
    Guid CompanyId,
    string CompanyName,
    Guid RunId,
    string ScenarioKey,
    int ScenarioVersion,
    string Status,
    bool IsDemoTenant,
    bool ExternalIntegrationsBlocked);

public sealed record LinkDemoScenarioMeetingRequest(
    Guid MeetingSessionId,
    string ScenarioKey,
    int ScenarioVersion);

public sealed record DemoScenarioAffectedRecordDto(string RecordClass, int ExistingCount, int StartingCount);

public sealed record DemoScenarioValidationDto(string Code, bool Passed, string Message);

public sealed record DemoScenarioResetPreviewDto(
    Guid CompanyId,
    string CompanyName,
    Guid RunId,
    string ScenarioKey,
    int ScenarioVersion,
    int ResetGeneration,
    IReadOnlyList<DemoScenarioAffectedRecordDto> AffectedRecords,
    IReadOnlyList<string> DisabledIntegrations,
    IReadOnlyList<DemoScenarioValidationDto> Validations,
    IReadOnlyList<string> ExpectedPostResetInvariants,
    bool AuditHistoryPreserved,
    bool CanReset,
    string PreviewToken);

public sealed record ResetDemoScenarioRequest(
    string ScenarioKey,
    int ScenarioVersion,
    string ExpectedCompanyName,
    string PreviewToken);

public sealed record DemoScenarioStatusDto(
    Guid CompanyId,
    string CompanyName,
    Guid RunId,
    string ScenarioKey,
    int ScenarioVersion,
    Guid? MeetingSessionId,
    string Status,
    int CurrentStep,
    int StepCount,
    int ResetGeneration,
    bool ExternalIntegrationsBlocked,
    DemoScenarioActionDto? NextAction,
    IReadOnlyList<DemoScenarioActionDto> Actions,
    IReadOnlyList<DemoScenarioValidationDto> Validations,
    DateTime UpdatedUtc);

public sealed record ExecuteDemoScenarioCommandRequest(string CommandName, string IdempotencyKey);

public sealed record DemoScenarioCommandResultDto(
    string Disposition,
    string? ReasonCode,
    string Message,
    string CommandName,
    int StepNumber,
    string ExpectedVisibleOutcome,
    string EntityType,
    Guid EntityId,
    DemoScenarioStatusDto Status);

public sealed record DemoExternalSideEffectDecision(bool Allowed, string ReasonCode, string Explanation);

public interface IDemoScenarioCatalog
{
    IReadOnlyList<DemoScenarioDefinitionDto> List();
    DemoScenarioDefinitionDto Get(string scenarioKey, int scenarioVersion);
}

public interface IDemoTenantExternalSideEffectPolicy
{
    Task<DemoExternalSideEffectDecision> EvaluateAsync(
        Guid companyId,
        string actionType,
        CancellationToken cancellationToken);
}

public interface IDemoScenarioService
{
    Task<ProvisionDemoScenarioResult> ProvisionAsync(
        Guid userId,
        ProvisionDemoScenarioRequest request,
        CancellationToken cancellationToken);

    Task<DemoScenarioStatusDto?> GetStatusAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<DemoScenarioStatusDto> LinkMeetingAsync(
        Guid companyId,
        Guid userId,
        LinkDemoScenarioMeetingRequest request,
        CancellationToken cancellationToken);

    Task<DemoScenarioStatusDto> StartAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<DemoScenarioResetPreviewDto> PreviewResetAsync(
        Guid companyId,
        Guid userId,
        string scenarioKey,
        int scenarioVersion,
        CancellationToken cancellationToken);

    Task<DemoScenarioStatusDto> ResetAsync(
        Guid companyId,
        Guid userId,
        ResetDemoScenarioRequest request,
        CancellationToken cancellationToken);

    Task<DemoScenarioCommandResultDto> ExecuteAsync(
        Guid companyId,
        Guid userId,
        ExecuteDemoScenarioCommandRequest request,
        CancellationToken cancellationToken);
}

public static class DemoScenarioProblemCodes
{
    public const string Disabled = "demo_scenario.disabled";
    public const string InvalidSpecification = "demo_scenario.invalid_specification";
    public const string NotDemoTenant = "demo_scenario.not_demo_tenant";
    public const string ScenarioMismatch = "demo_scenario.scenario_mismatch";
    public const string ResetPreviewStale = "demo_scenario.reset_preview_stale";
    public const string CommandNotAllowed = "demo_scenario.command_not_allowed";
    public const string CommandOutOfOrder = "demo_scenario.command_out_of_order";
    public const string MeetingMismatch = "demo_scenario.meeting_mismatch";
    public const string ExternalSideEffectBlocked = "demo_scenario.external_side_effect_blocked";
    public const string PermissionDenied = "demo_scenario.permission_denied";
}

public sealed class DemoScenarioException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
