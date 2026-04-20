using System.Text.Json.Serialization;
namespace VirtualCompany.Shared;

public sealed class FinanceSandboxDatasetGenerationResponse
{
    public string ProfileName { get; set; } = string.Empty;
    public DateTime LastGeneratedUtc { get; set; }
    public string CoverageSummary { get; set; } = string.Empty;
    public IReadOnlyList<string> AvailableProfiles { get; set; } = [];
}

public static class FinanceSandboxSeedGenerationModes
{
    public const string Refresh = "refresh";
    public const string RefreshWithAnomalies = "refresh_with_anomalies";

    private static readonly string[] AllowedModeValues =
    [
        Refresh,
        RefreshWithAnomalies
    ];

    private static readonly HashSet<string> AllowedModeSet = new(AllowedModeValues, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => AllowedModeValues;

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AllowedModeSet.Contains(Normalize(value));
}

public sealed class FinanceSandboxSeedGenerationRequest
{
    public Guid CompanyId { get; set; }
    public int SeedValue { get; set; }
    public DateTime AnchorDateUtc { get; set; }
    public string GenerationMode { get; set; } = FinanceSandboxSeedGenerationModes.Refresh;
}

public sealed class FinanceSandboxSeedGenerationResponse
{
    public Guid CompanyId { get; set; }
    public int SeedValue { get; set; }
    public DateTime AnchorDateUtc { get; set; }
    public string GenerationMode { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<FinanceSandboxSeedGenerationIssueResponse> Errors { get; set; } = [];
    public IReadOnlyList<FinanceSandboxSeedGenerationIssueResponse> Warnings { get; set; } = [];
    public IReadOnlyList<FinanceSandboxSeedGenerationIssueResponse> ReferentialIntegrityErrors { get; set; } = [];
}

public sealed class FinanceSandboxSeedGenerationIssueResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class FinanceSandboxAnomalyScenarioProfileResponse
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class FinanceSandboxBackendMessageResponse
{
    public string Severity { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class FinanceSandboxAnomalyRegistryItemResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ScenarioProfileCode { get; set; } = string.Empty;
    public string ScenarioProfileName { get; set; } = string.Empty;
    public string AffectedRecordType { get; set; } = string.Empty;
    public Guid? AffectedRecordId { get; set; }
    public string AffectedRecordReference { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public IReadOnlyList<FinanceSandboxBackendMessageResponse> Messages { get; set; } = [];
}

public sealed class FinanceSandboxAnomalyDetailResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ScenarioProfileCode { get; set; } = string.Empty;
    public string ScenarioProfileName { get; set; } = string.Empty;
    public string AffectedRecordType { get; set; } = string.Empty;
    public Guid? AffectedRecordId { get; set; }
    public string AffectedRecordReference { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public string ExpectedDetectionMetadataJson { get; set; } = string.Empty;
    public IReadOnlyList<FinanceSandboxBackendMessageResponse> Messages { get; set; } = [];
}

public sealed class FinanceSandboxAnomalyInjectionRequest
{
    public Guid CompanyId { get; set; }
    public string ScenarioProfileCode { get; set; } = string.Empty;
}

public sealed class FinanceSandboxAnomalyInjectionResponse
{
    public string Mode { get; set; } = string.Empty;
    public DateTime LastInjectedUtc { get; set; }
    public string Observation { get; set; } = string.Empty;
    public IReadOnlyList<string> ActiveScenarios { get; set; } = [];
    public IReadOnlyList<FinanceSandboxAnomalyScenarioProfileResponse> AvailableScenarioProfiles { get; set; } = [];
    public IReadOnlyList<FinanceSandboxAnomalyRegistryItemResponse> RegistryEntries { get; set; } = [];
}

public sealed class FinanceSandboxProgressionRunStepResponse
{
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public int TransactionsGenerated { get; set; }
    public int InvoicesGenerated { get; set; }
    public int BillsGenerated { get; set; }
    public int RecurringExpenseInstancesGenerated { get; set; }
    public int EventsEmitted { get; set; }
}

public sealed class FinanceSandboxProgressionRunSummaryResponse
{
    public string RunType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public int AdvancedHours { get; set; }
    public int ExecutionStepHours { get; set; }
    public int TransactionsGenerated { get; set; }
    public int InvoicesGenerated { get; set; }
    public int BillsGenerated { get; set; }
    public int RecurringExpenseInstancesGenerated { get; set; }
    public int EventsEmitted { get; set; }
    public IReadOnlyList<FinanceSandboxBackendMessageResponse> Messages { get; set; } = [];
    public IReadOnlyList<FinanceSandboxProgressionRunStepResponse> Steps { get; set; } = [];
}

public sealed class FinanceSandboxSimulationControlsResponse
{
    public string ClockMode { get; set; } = string.Empty;
    public DateTime ReferenceUtc { get; set; }
    public string CheckpointLabel { get; set; } = string.Empty;
    public string Observation { get; set; } = string.Empty;
    public FinanceSandboxProgressionRunSummaryResponse? CurrentRun { get; set; }
    public IReadOnlyList<FinanceSandboxProgressionRunSummaryResponse> RunHistory { get; set; } = [];
}

public sealed class FinanceSandboxSimulationAdvanceRequest
{
    public Guid CompanyId { get; set; }
    public int IncrementHours { get; set; }
    public int? ExecutionStepHours { get; set; }
    [JsonPropertyName("accelerated")]
    public bool Accelerated { get; set; }
}

public sealed class FinanceSandboxToolExecutionVisibilityResponse
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceSandboxToolExecutionItemResponse> Items { get; set; } = [];
}

public sealed class FinanceSandboxToolExecutionItemResponse
{
    public string Name { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public string LastStatus { get; set; } = string.Empty;
}

public sealed class FinanceSandboxDomainEventsResponse
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceSandboxDomainEventItemResponse> Items { get; set; } = [];
}

public sealed class FinanceSandboxDomainEventItemResponse
{
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class FinanceTransparencyEventStreamResponse
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceTransparencyEventListItemResponse> Items { get; set; } = [];
}

public sealed class FinanceTransparencyEventListItemResponse
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string AffectedEntityType { get; set; } = string.Empty;
    public string AffectedEntityId { get; set; } = string.Empty;
    public string EntityReference { get; set; } = string.Empty;
    public string PayloadSummary { get; set; } = string.Empty;
    public bool HasTriggerTrace { get; set; }
}

public sealed class FinanceTransparencyEventDetailResponse
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string EntityReference { get; set; } = string.Empty;
    public string PayloadSummary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceTransparencyRelatedRecordResponse> RelatedRecords { get; set; } = [];
    public IReadOnlyList<FinanceTransparencyTriggerTraceItemResponse> TriggerConsumptionTrace { get; set; } = [];
}

public sealed class FinanceTransparencyTriggerTraceItemResponse
{
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class FinanceTransparencyToolManifestListResponse
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceTransparencyToolManifestItemResponse> Items { get; set; } = [];
}

public sealed class FinanceTransparencyToolManifestItemResponse
{
    public string ToolName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string VersionMetadata { get; set; } = string.Empty;
    public string ContractSummary { get; set; } = string.Empty;
    public string SchemaSummary { get; set; } = string.Empty;
    public string ManifestSource { get; set; } = string.Empty;
    public string ProviderAdapterId { get; set; } = string.Empty;
    public string ProviderAdapterName { get; set; } = string.Empty;
    public string ProviderAdapterIdentity { get; set; } = string.Empty;
}

public sealed class FinanceTransparencyToolExecutionHistoryResponse
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceTransparencyToolExecutionListItemResponse> Items { get; set; } = [];
}

public sealed class FinanceTransparencyToolExecutionListItemResponse
{
    public Guid ExecutionId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string ToolVersion { get; set; } = string.Empty;
    public string LifecycleState { get; set; } = string.Empty;
    public string RequestSummary { get; set; } = string.Empty;
    public string ResponseSummary { get; set; } = string.Empty;
    public DateTime ExecutionTimestampUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class FinanceTransparencyToolExecutionDetailResponse
{
    public Guid ExecutionId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string ToolVersion { get; set; } = string.Empty;
    public string LifecycleState { get; set; } = string.Empty;
    public string RequestSummary { get; set; } = string.Empty;
    public string ResponseSummary { get; set; } = string.Empty;
    public DateTime ExecutionTimestampUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
    public string ApprovalRequestDisplay { get; set; } = string.Empty;
    public string OriginatingEntityType { get; set; } = string.Empty;
    public Guid? OriginatingEntityId { get; set; }
    public string OriginatingFinanceActionDisplay { get; set; } = string.Empty;
    public string OriginatingEntityReference { get; set; } = string.Empty;
    public Guid? TaskId { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public IReadOnlyList<FinanceTransparencyRelatedRecordResponse> RelatedRecords { get; set; } = [];
}

public sealed class FinanceTransparencyRelatedRecordResponse
{
    public string RelationshipType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string ResolutionSource { get; set; } = string.Empty;
}
