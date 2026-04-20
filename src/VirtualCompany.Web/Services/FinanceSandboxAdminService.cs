using VirtualCompany.Shared;

namespace VirtualCompany.Web.Services;

public interface IFinanceSandboxAdminService
{
    Task<FinanceSandboxDatasetGenerationViewModel?> GetDatasetGenerationAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<FinanceSandboxSeedGenerationViewModel> GenerateSeedDatasetAsync(FinanceSandboxSeedGenerationCommand command, CancellationToken cancellationToken = default);
    Task<FinanceSandboxAnomalyInjectionViewModel?> GetAnomalyInjectionAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<FinanceSandboxAnomalyDetailViewModel?> GetAnomalyDetailAsync(Guid companyId, Guid anomalyId, CancellationToken cancellationToken = default);
    Task<FinanceSandboxAnomalyDetailViewModel> InjectAnomalyAsync(FinanceSandboxAnomalyInjectionCommand command, CancellationToken cancellationToken = default);
    Task<FinanceSandboxSimulationControlsViewModel?> GetSimulationControlsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<FinanceSandboxSimulationDiagnosticsViewModel?> GetSimulationDiagnosticsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<FinanceSandboxProgressionRunViewModel> AdvanceSimulationAsync(FinanceSandboxSimulationAdvanceCommand command, CancellationToken cancellationToken = default);
    Task<FinanceSandboxProgressionRunViewModel> StartProgressionRunAsync(FinanceSandboxSimulationAdvanceCommand command, CancellationToken cancellationToken = default);
    Task<FinanceTransparencyToolManifestListViewModel?> GetTransparencyToolManifestsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<FinanceTransparencyExecutionHistoryViewModel?> GetTransparencyToolExecutionsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<FinanceTransparencyToolExecutionDetailViewModel?> GetTransparencyToolExecutionDetailAsync(Guid companyId, Guid executionId, CancellationToken cancellationToken = default);
    Task<FinanceTransparencyEventStreamViewModel?> GetTransparencyEventsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<FinanceTransparencyEventDetailViewModel?> GetTransparencyEventDetailAsync(Guid companyId, Guid eventId, CancellationToken cancellationToken = default);
    Task<FinanceSandboxToolExecutionVisibilityViewModel?> GetToolExecutionVisibilityAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<FinanceSandboxDomainEventsViewModel?> GetDomainEventsAsync(Guid companyId, CancellationToken cancellationToken = default);
}

public sealed class FinanceSandboxAdminService : IFinanceSandboxAdminService
{
    private readonly FinanceApiClient _financeApiClient;

    public FinanceSandboxAdminService(FinanceApiClient financeApiClient)
    {
        _financeApiClient = financeApiClient;
    }

    public async Task<FinanceSandboxDatasetGenerationViewModel?> GetDatasetGenerationAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetSandboxDatasetGenerationAsync(companyId, cancellationToken);
        return response is null ? null : new FinanceSandboxDatasetGenerationViewModel
        {
            ProfileName = response.ProfileName,
            LastGeneratedUtc = response.LastGeneratedUtc,
            CoverageSummary = response.CoverageSummary,
            AvailableProfiles = response.AvailableProfiles
        };
    }

    public async Task<FinanceSandboxSeedGenerationViewModel> GenerateSeedDatasetAsync(FinanceSandboxSeedGenerationCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GenerateSandboxSeedDatasetAsync(command.CompanyId, new FinanceSandboxSeedGenerationRequest
        {
            CompanyId = command.CompanyId,
            SeedValue = command.SeedValue,
            AnchorDateUtc = command.AnchorDateUtc,
            GenerationMode = command.GenerationMode
        }, cancellationToken);

        return new FinanceSandboxSeedGenerationViewModel
        {
            CompanyId = response.CompanyId,
            SeedValue = response.SeedValue,
            AnchorDateUtc = response.AnchorDateUtc,
            GenerationMode = response.GenerationMode,
            Succeeded = response.Succeeded,
            CreatedCount = response.CreatedCount,
            UpdatedCount = response.UpdatedCount,
            Message = response.Message,
            Errors = response.Errors.Select(MapSeedGenerationIssue).ToArray(),
            Warnings = response.Warnings.Select(MapSeedGenerationIssue).ToArray(),
            ReferentialIntegrityErrors = response.ReferentialIntegrityErrors.Select(MapSeedGenerationIssue).ToArray()
        };
    }

    public async Task<FinanceSandboxAnomalyInjectionViewModel?> GetAnomalyInjectionAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetSandboxAnomalyInjectionAsync(companyId, cancellationToken);
        return response is null ? null : new FinanceSandboxAnomalyInjectionViewModel
        {
            Mode = response.Mode,
            LastInjectedUtc = response.LastInjectedUtc,
            Observation = response.Observation,
            AvailableScenarioProfiles = response.AvailableScenarioProfiles.Select(MapScenarioProfile).ToArray(),
            RegistryEntries = response.RegistryEntries.Select(MapAnomalyRegistryItem).ToArray(),
            ActiveScenarios = response.ActiveScenarios
        };
    }

    public async Task<FinanceSandboxAnomalyDetailViewModel?> GetAnomalyDetailAsync(Guid companyId, Guid anomalyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetSandboxAnomalyDetailAsync(companyId, anomalyId, cancellationToken);
        return response is null ? null : MapAnomalyDetail(response);
    }

    public async Task<FinanceSandboxAnomalyDetailViewModel> InjectAnomalyAsync(FinanceSandboxAnomalyInjectionCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.InjectSandboxAnomalyAsync(command.CompanyId, new FinanceSandboxAnomalyInjectionRequest
        {
            CompanyId = command.CompanyId,
            ScenarioProfileCode = command.ScenarioProfileCode
        }, cancellationToken);

        return MapAnomalyDetail(response);
    }

    public async Task<FinanceSandboxProgressionRunViewModel> AdvanceSimulationAsync(FinanceSandboxSimulationAdvanceCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.AdvanceSandboxSimulationAsync(command.CompanyId, new FinanceSandboxSimulationAdvanceRequest
        {
            CompanyId = command.CompanyId,
            IncrementHours = command.IncrementHours,
            ExecutionStepHours = command.ExecutionStepHours,
            Accelerated = command.Accelerated
        }, cancellationToken);

        return MapProgressionRun(response);
    }

    public async Task<FinanceSandboxProgressionRunViewModel> StartProgressionRunAsync(FinanceSandboxSimulationAdvanceCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.StartSandboxProgressionRunAsync(command.CompanyId, new FinanceSandboxSimulationAdvanceRequest
        {
            CompanyId = command.CompanyId,
            IncrementHours = command.IncrementHours,
            ExecutionStepHours = command.ExecutionStepHours,
            Accelerated = command.Accelerated
        }, cancellationToken);

        return MapProgressionRun(response);
    }

    public async Task<FinanceSandboxSimulationControlsViewModel?> GetSimulationControlsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetSandboxSimulationControlsAsync(companyId, cancellationToken);
        return response is null ? null : new FinanceSandboxSimulationControlsViewModel
        {
            ClockMode = response.ClockMode,
            ReferenceUtc = response.ReferenceUtc,
            CheckpointLabel = response.CheckpointLabel,
            Observation = response.Observation,
            CurrentRun = response.CurrentRun is null ? null : MapProgressionRun(response.CurrentRun),
            RunHistory = response.RunHistory.Select(MapProgressionRun).ToArray()
        };
    }

    public async Task<FinanceSandboxSimulationDiagnosticsViewModel?> GetSimulationDiagnosticsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetCompanySimulationStateAsync(companyId, cancellationToken);
        return response is null ? null : new FinanceSandboxSimulationDiagnosticsViewModel
        {
            CompanyId = response.CompanyId,
            Status = response.Status,
            CurrentSimulatedDateTime = response.CurrentSimulatedDateTime,
            LastProgressionTimestamp = response.LastProgressionTimestamp,
            GenerationEnabled = response.GenerationEnabled,
            Seed = response.Seed,
            ActiveSessionId = response.ActiveSessionId,
            StartSimulatedDateTime = response.StartSimulatedDateTime,
            UiVisible = response.UiVisible,
            BackendExecutionEnabled = response.BackendExecutionEnabled,
            BackgroundJobsEnabled = response.BackgroundJobsEnabled,
            DisabledReason = response.DisabledReason,
            RecentHistory = response.RecentHistory.Select(MapSimulationRunHistory).ToArray()
        };
    }

    public async Task<FinanceTransparencyToolManifestListViewModel?> GetTransparencyToolManifestsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetTransparencyToolManifestsAsync(companyId, cancellationToken);
        return response is null ? null : new FinanceTransparencyToolManifestListViewModel
        {
            Summary = response.Summary,
            Items = response.Items.Select(MapTransparencyToolManifestItem).ToArray()
        };
    }

    public async Task<FinanceTransparencyExecutionHistoryViewModel?> GetTransparencyToolExecutionsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetTransparencyToolExecutionsAsync(companyId, cancellationToken);
        return response is null ? null : new FinanceTransparencyExecutionHistoryViewModel
        {
            Summary = response.Summary,
            Items = response.Items.Select(MapTransparencyToolExecutionItem).ToArray()
        };
    }

    public async Task<FinanceTransparencyToolExecutionDetailViewModel?> GetTransparencyToolExecutionDetailAsync(Guid companyId, Guid executionId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetTransparencyToolExecutionDetailAsync(companyId, executionId, cancellationToken);
        return response is null ? null : MapTransparencyToolExecutionDetail(response);
    }

    public async Task<FinanceTransparencyEventStreamViewModel?> GetTransparencyEventsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetTransparencyEventsAsync(companyId, cancellationToken);
        return response is null ? null : new FinanceTransparencyEventStreamViewModel
        {
            Summary = response.Summary,
            Items = response.Items.Select(MapTransparencyEventItem).ToArray()
        };
    }

    public async Task<FinanceSandboxToolExecutionVisibilityViewModel?> GetToolExecutionVisibilityAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetSandboxToolExecutionVisibilityAsync(companyId, cancellationToken);
        return response is null ? null : new FinanceSandboxToolExecutionVisibilityViewModel
        {
            Summary = response.Summary,
            Items = response.Items.Select(MapToolExecutionItem).ToArray()
        };
    }

    public async Task<FinanceSandboxDomainEventsViewModel?> GetDomainEventsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetSandboxDomainEventsAsync(companyId, cancellationToken);
        return response is null ? null : new FinanceSandboxDomainEventsViewModel
        {
            Summary = response.Summary,
            Items = response.Items.Select(MapDomainEventItem).ToArray()
        };
    }

    public async Task<FinanceTransparencyEventDetailViewModel?> GetTransparencyEventDetailAsync(Guid companyId, Guid eventId, CancellationToken cancellationToken = default)
    {
        var response = await _financeApiClient.GetTransparencyEventDetailAsync(companyId, eventId, cancellationToken);
        return response is null ? null : MapTransparencyEventDetail(response);
    }

    private static FinanceSandboxSeedGenerationIssueViewModel MapSeedGenerationIssue(FinanceSandboxSeedGenerationIssueResponse response) =>
        new()
        {
            Code = response.Code,
            Message = response.Message
        };

    private static FinanceSandboxAnomalyScenarioProfileViewModel MapScenarioProfile(FinanceSandboxAnomalyScenarioProfileResponse response) =>
        new()
        {
            Code = response.Code,
            Name = response.Name,
            Description = response.Description
        };

    private static FinanceSandboxBackendMessageViewModel MapBackendMessage(FinanceSandboxBackendMessageResponse response) =>
        new()
        {
            Severity = response.Severity,
            Code = response.Code,
            Message = response.Message
        };

    private static FinanceTransparencyToolManifestItemViewModel MapTransparencyToolManifestItem(FinanceTransparencyToolManifestItemResponse response) =>
        new()
        {
            ToolName = response.ToolName,
            VersionMetadata = response.VersionMetadata,
            Version = response.Version,
            ContractSummary = response.ContractSummary,
            SchemaSummary = response.SchemaSummary,
            ManifestSource = response.ManifestSource,
            ProviderAdapterId = response.ProviderAdapterId,
            ProviderAdapterName = response.ProviderAdapterName,
            ProviderAdapterIdentity = response.ProviderAdapterIdentity
        };

    private static FinanceTransparencyToolExecutionListItemViewModel MapTransparencyToolExecutionItem(FinanceTransparencyToolExecutionListItemResponse response) =>
        new()
        {
            ExecutionId = response.ExecutionId,
            ToolName = response.ToolName,
            ToolVersion = response.ToolVersion,
            LifecycleState = response.LifecycleState,
            RequestSummary = response.RequestSummary,
            ResponseSummary = response.ResponseSummary,
            ExecutionTimestampUtc = response.ExecutionTimestampUtc,
            CorrelationId = response.CorrelationId
        };

    private static FinanceTransparencyToolExecutionDetailViewModel MapTransparencyToolExecutionDetail(FinanceTransparencyToolExecutionDetailResponse response) =>
        new()
        {
            ExecutionId = response.ExecutionId,
            ToolName = response.ToolName,
            ToolVersion = response.ToolVersion,
            LifecycleState = response.LifecycleState,
            RequestSummary = response.RequestSummary,
            ResponseSummary = response.ResponseSummary,
            ExecutionTimestampUtc = response.ExecutionTimestampUtc,
            CorrelationId = response.CorrelationId,
            ApprovalRequestDisplay = response.ApprovalRequestDisplay,
            ApprovalRequestId = response.ApprovalRequestId,
            OriginatingEntityType = response.OriginatingEntityType,
            OriginatingFinanceActionDisplay = response.OriginatingFinanceActionDisplay,
            OriginatingEntityId = response.OriginatingEntityId,
            OriginatingEntityReference = response.OriginatingEntityReference,
            TaskId = response.TaskId,
            WorkflowInstanceId = response.WorkflowInstanceId,
            RelatedRecords = response.RelatedRecords.Select(MapTransparencyRelatedRecord).ToArray()
        };

    private static FinanceTransparencyEventListItemViewModel MapTransparencyEventItem(FinanceTransparencyEventListItemResponse response) =>
        new()
        {
            Id = response.Id,
            EventType = response.EventType,
            OccurredAtUtc = response.OccurredAtUtc,
            CorrelationId = response.CorrelationId,
            AffectedEntityType = response.AffectedEntityType,
            AffectedEntityId = response.AffectedEntityId,
            EntityReference = response.EntityReference
            ,
            PayloadSummary = response.PayloadSummary,
            HasTriggerTrace = response.HasTriggerTrace
        };

    private static FinanceTransparencyTriggerTraceItemViewModel MapTransparencyTraceItem(FinanceTransparencyTriggerTraceItemResponse response) =>
        new()
        {
            SourceType = response.SourceType,
            SourceId = response.SourceId,
            DisplayName = response.DisplayName,
            Reference = response.Reference
        };

    private static FinanceSandboxToolExecutionItemViewModel MapToolExecutionItem(FinanceSandboxToolExecutionItemResponse response) =>
        new()
        {
            Name = response.Name,
            Visibility = response.Visibility,
            LastStatus = response.LastStatus
        };

    private static FinanceSandboxAnomalyRegistryItemViewModel MapAnomalyRegistryItem(FinanceSandboxAnomalyRegistryItemResponse response) =>
        new()
        {
            Id = response.Id,
            Type = response.Type,
            Status = response.Status,
            ScenarioProfileCode = response.ScenarioProfileCode,
            ScenarioProfileName = response.ScenarioProfileName,
            AffectedRecordType = response.AffectedRecordType,
            AffectedRecordId = response.AffectedRecordId,
            AffectedRecordReference = response.AffectedRecordReference,
            CreatedUtc = response.CreatedUtc,
            Messages = response.Messages.Select(MapBackendMessage).ToArray()
        };

    private static FinanceSandboxAnomalyDetailViewModel MapAnomalyDetail(FinanceSandboxAnomalyDetailResponse response) =>
        new()
        {
            Id = response.Id,
            Type = response.Type,
            Status = response.Status,
            ScenarioProfileCode = response.ScenarioProfileCode,
            ScenarioProfileName = response.ScenarioProfileName,
            AffectedRecordType = response.AffectedRecordType,
            AffectedRecordId = response.AffectedRecordId,
            AffectedRecordReference = response.AffectedRecordReference,
            CreatedUtc = response.CreatedUtc,
            ExpectedDetectionMetadataJson = response.ExpectedDetectionMetadataJson,
            Messages = response.Messages.Select(MapBackendMessage).ToArray()
        };

    private static FinanceSandboxSimulationRunHistoryViewModel MapSimulationRunHistory(FinanceCompanySimulationRunHistoryResponse response) =>
        new()
        {
            SessionId = response.SessionId,
            Status = response.Status,
            StartedUtc = response.StartedUtc,
            CompletedUtc = response.CompletedUtc,
            LastUpdatedUtc = response.LastUpdatedUtc,
            GenerationEnabled = response.GenerationEnabled,
            Seed = response.Seed,
            StartSimulatedDateTime = response.StartSimulatedDateTime,
            CurrentSimulatedDateTime = response.CurrentSimulatedDateTime,
            InjectedAnomalies = response.InjectedAnomalies.ToArray(),
            Warnings = response.Warnings.ToArray(),
            Errors = response.Errors.ToArray(),
            StatusTransitions = response.StatusTransitions.Select(MapSimulationStatusTransition).ToArray(),
            DayLogs = response.DayLogs.Select(MapSimulationDayLog).ToArray()
        };

    private static FinanceSandboxSimulationStatusTransitionViewModel MapSimulationStatusTransition(FinanceCompanySimulationStatusTransitionResponse response) =>
        new()
        {
            Status = response.Status,
            TransitionedUtc = response.TransitionedUtc,
            Message = response.Message ?? string.Empty
        };

    private static FinanceSandboxSimulationDayLogViewModel MapSimulationDayLog(FinanceCompanySimulationDayLogResponse response) =>
        new()
        {
            SimulatedDateUtc = response.SimulatedDateUtc,
            TransactionsGenerated = response.TransactionsGenerated,
            InvoicesGenerated = response.InvoicesGenerated,
            BillsGenerated = response.BillsGenerated,
            RecurringExpenseInstancesGenerated = response.RecurringExpenseInstancesGenerated,
            AlertsGenerated = response.AlertsGenerated,
            InjectedAnomalies = response.InjectedAnomalies.ToArray(),
            Warnings = response.Warnings.ToArray(),
            Errors = response.Errors.ToArray()
        };

    private static FinanceSandboxProgressionRunViewModel MapProgressionRun(FinanceSandboxProgressionRunSummaryResponse response) =>
        new()
        {
            RunType = response.RunType,
            Status = response.Status,
            StartedUtc = response.StartedUtc,
            CompletedUtc = response.CompletedUtc,
            AdvancedHours = response.AdvancedHours,
            ExecutionStepHours = response.ExecutionStepHours,
            TransactionsGenerated = response.TransactionsGenerated,
            InvoicesGenerated = response.InvoicesGenerated,
            BillsGenerated = response.BillsGenerated,
            RecurringExpenseInstancesGenerated = response.RecurringExpenseInstancesGenerated,
            EventsEmitted = response.EventsEmitted,
            Messages = response.Messages.Select(MapBackendMessage).ToArray(),
            Steps = response.Steps.Select(MapProgressionStep).ToArray()
        };

    private static FinanceSandboxProgressionRunStepViewModel MapProgressionStep(FinanceSandboxProgressionRunStepResponse response) =>
        new()
        {
            WindowStartUtc = response.WindowStartUtc,
            WindowEndUtc = response.WindowEndUtc,
            TransactionsGenerated = response.TransactionsGenerated,
            InvoicesGenerated = response.InvoicesGenerated,
            BillsGenerated = response.BillsGenerated,
            RecurringExpenseInstancesGenerated = response.RecurringExpenseInstancesGenerated,
            EventsEmitted = response.EventsEmitted
        };

    private static FinanceSandboxDomainEventItemViewModel MapDomainEventItem(FinanceSandboxDomainEventItemResponse response) =>
        new()
        {
            EventType = response.EventType,
            Status = response.Status,
            OccurredAtUtc = response.OccurredAtUtc
        };

    private static FinanceTransparencyEventDetailViewModel MapTransparencyEventDetail(FinanceTransparencyEventDetailResponse response) =>
        new()
        {
            Id = response.Id,
            EventType = response.EventType,
            OccurredAtUtc = response.OccurredAtUtc,
            CorrelationId = response.CorrelationId,
            EntityType = response.EntityType,
            EntityId = response.EntityId,
            EntityReference = response.EntityReference,
            PayloadSummary = response.PayloadSummary,
            RelatedRecords = response.RelatedRecords.Select(MapTransparencyRelatedRecord).ToArray(),
            TriggerConsumptionTrace = response.TriggerConsumptionTrace.Select(MapTransparencyTraceItem).ToArray()
        };

    private static FinanceTransparencyRelatedRecordViewModel MapTransparencyRelatedRecord(FinanceTransparencyRelatedRecordResponse response) =>
        new()
        {
            RelationshipType = response.RelationshipType,
            TargetType = response.TargetType,
            TargetId = response.TargetId,
            DisplayText = response.DisplayText,
            Reference = response.Reference,
            ResolutionSource = response.ResolutionSource
        };
}

public sealed record FinanceSandboxSectionState<TSection>(bool IsLoading, string? ErrorMessage, TSection? Value)
    where TSection : class
{
    public bool IsEmpty => !IsLoading && string.IsNullOrWhiteSpace(ErrorMessage) && Value is null;

    public static FinanceSandboxSectionState<TSection> Loading() => new(true, null, null);
    public static FinanceSandboxSectionState<TSection> Success(TSection? value) => new(false, null, value);
    public static FinanceSandboxSectionState<TSection> Failure(string errorMessage) => new(false, errorMessage, null);
}

public sealed class FinanceSandboxSeedGenerationCommand
{
    public Guid CompanyId { get; set; }
    public int SeedValue { get; set; }
    public DateTime AnchorDateUtc { get; set; }
    public string GenerationMode { get; set; } = FinanceSandboxSeedGenerationModes.Refresh;
}

public sealed class FinanceSandboxSeedGenerationViewModel
{
    public Guid CompanyId { get; set; }
    public int SeedValue { get; set; }
    public DateTime AnchorDateUtc { get; set; }
    public string GenerationMode { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<FinanceSandboxSeedGenerationIssueViewModel> Errors { get; set; } = [];
    public IReadOnlyList<FinanceSandboxSeedGenerationIssueViewModel> Warnings { get; set; } = [];
    public IReadOnlyList<FinanceSandboxSeedGenerationIssueViewModel> ReferentialIntegrityErrors { get; set; } = [];
}

public sealed class FinanceSandboxSeedGenerationIssueViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class FinanceSandboxDatasetGenerationViewModel
{
    public string ProfileName { get; set; } = string.Empty;
    public DateTime LastGeneratedUtc { get; set; }
    public string CoverageSummary { get; set; } = string.Empty;
    public IReadOnlyList<string> AvailableProfiles { get; set; } = [];
}

public sealed class FinanceSandboxAnomalyInjectionViewModel
{
    public string Mode { get; set; } = string.Empty;
    public DateTime LastInjectedUtc { get; set; }
    public string Observation { get; set; } = string.Empty;
    public IReadOnlyList<string> ActiveScenarios { get; set; } = [];
    public IReadOnlyList<FinanceSandboxAnomalyScenarioProfileViewModel> AvailableScenarioProfiles { get; set; } = [];
    public IReadOnlyList<FinanceSandboxAnomalyRegistryItemViewModel> RegistryEntries { get; set; } = [];
}

public sealed class FinanceSandboxAnomalyInjectionCommand
{
    public Guid CompanyId { get; set; }
    public string ScenarioProfileCode { get; set; } = string.Empty;
}

public sealed class FinanceSandboxAnomalyScenarioProfileViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class FinanceSandboxBackendMessageViewModel
{
    public string Severity { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class FinanceSandboxAnomalyRegistryItemViewModel
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
    public IReadOnlyList<FinanceSandboxBackendMessageViewModel> Messages { get; set; } = [];
}

public sealed class FinanceSandboxAnomalyDetailViewModel
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
    public IReadOnlyList<FinanceSandboxBackendMessageViewModel> Messages { get; set; } = [];
}

public sealed class FinanceSandboxSimulationAdvanceCommand
{
    public Guid CompanyId { get; set; }
    public int IncrementHours { get; set; }
    public int? ExecutionStepHours { get; set; }
    public bool Accelerated { get; set; }
}

public sealed class FinanceSandboxSimulationControlsViewModel
{
    public string ClockMode { get; set; } = string.Empty;
    public DateTime ReferenceUtc { get; set; }
    public string CheckpointLabel { get; set; } = string.Empty;
    public string Observation { get; set; } = string.Empty;
    public FinanceSandboxProgressionRunViewModel? CurrentRun { get; set; }
    public IReadOnlyList<FinanceSandboxProgressionRunViewModel> RunHistory { get; set; } = [];
}

public sealed class FinanceSandboxSimulationDiagnosticsViewModel
{
    public Guid CompanyId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? CurrentSimulatedDateTime { get; set; }
    public DateTime? LastProgressionTimestamp { get; set; }
    public bool? GenerationEnabled { get; set; }
    public int? Seed { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public DateTime? StartSimulatedDateTime { get; set; }
    public bool UiVisible { get; set; } = true;
    public bool BackendExecutionEnabled { get; set; } = true;
    public bool BackgroundJobsEnabled { get; set; } = true;
    public string? DisabledReason { get; set; }
    public IReadOnlyList<FinanceSandboxSimulationRunHistoryViewModel> RecentHistory { get; set; } = [];
}

public sealed class FinanceSandboxSimulationRunHistoryViewModel
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
    public bool GenerationEnabled { get; set; }
    public int Seed { get; set; }
    public DateTime StartSimulatedDateTime { get; set; }
    public DateTime? CurrentSimulatedDateTime { get; set; }
    public IReadOnlyList<string> InjectedAnomalies { get; set; } = [];
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public IReadOnlyList<string> Errors { get; set; } = [];
    public IReadOnlyList<FinanceSandboxSimulationStatusTransitionViewModel> StatusTransitions { get; set; } = [];
    public IReadOnlyList<FinanceSandboxSimulationDayLogViewModel> DayLogs { get; set; } = [];
}

public sealed class FinanceSandboxSimulationStatusTransitionViewModel
{
    public string Status { get; set; } = string.Empty;
    public DateTime TransitionedUtc { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class FinanceSandboxSimulationDayLogViewModel
{
    public DateTime SimulatedDateUtc { get; set; }
    public int TransactionsGenerated { get; set; }
    public int InvoicesGenerated { get; set; }
    public int BillsGenerated { get; set; }
    public int RecurringExpenseInstancesGenerated { get; set; }
    public int AlertsGenerated { get; set; }
    public IReadOnlyList<string> InjectedAnomalies { get; set; } = [];
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public IReadOnlyList<string> Errors { get; set; } = [];
    public int GeneratedRecordCount => TransactionsGenerated + InvoicesGenerated + BillsGenerated + RecurringExpenseInstancesGenerated;
}

public sealed class FinanceSandboxProgressionRunViewModel
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
    public IReadOnlyList<FinanceSandboxBackendMessageViewModel> Messages { get; set; } = [];
    public IReadOnlyList<FinanceSandboxProgressionRunStepViewModel> Steps { get; set; } = [];

    public int GeneratedRecordCount =>
        TransactionsGenerated +
        InvoicesGenerated +
        BillsGenerated +
        RecurringExpenseInstancesGenerated;
}

public sealed class FinanceSandboxProgressionRunStepViewModel
{
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public int TransactionsGenerated { get; set; }
    public int InvoicesGenerated { get; set; }
    public int BillsGenerated { get; set; }
    public int RecurringExpenseInstancesGenerated { get; set; }
    public int EventsEmitted { get; set; }
}

public sealed class FinanceTransparencyToolManifestListViewModel
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceTransparencyToolManifestItemViewModel> Items { get; set; } = [];
}

public sealed class FinanceTransparencyToolManifestItemViewModel
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

public sealed class FinanceTransparencyExecutionHistoryViewModel
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceTransparencyToolExecutionListItemViewModel> Items { get; set; } = [];
}

public sealed class FinanceTransparencyToolExecutionListItemViewModel
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

public sealed class FinanceTransparencyToolExecutionDetailViewModel
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
    public IReadOnlyList<FinanceTransparencyRelatedRecordViewModel> RelatedRecords { get; set; } = [];
}

public sealed class FinanceSandboxToolExecutionVisibilityViewModel
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceSandboxToolExecutionItemViewModel> Items { get; set; } = [];
}

public sealed class FinanceSandboxToolExecutionItemViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public string LastStatus { get; set; } = string.Empty;
}

public sealed class FinanceSandboxDomainEventsViewModel
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceSandboxDomainEventItemViewModel> Items { get; set; } = [];
}

public sealed class FinanceSandboxDomainEventItemViewModel
{
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class FinanceTransparencyEventStreamViewModel
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceTransparencyEventListItemViewModel> Items { get; set; } = [];
}

public sealed class FinanceTransparencyEventListItemViewModel
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

public sealed class FinanceTransparencyEventDetailViewModel
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string EntityReference { get; set; } = string.Empty;
    public string PayloadSummary { get; set; } = string.Empty;
    public IReadOnlyList<FinanceTransparencyRelatedRecordViewModel> RelatedRecords { get; set; } = [];
    public IReadOnlyList<FinanceTransparencyTriggerTraceItemViewModel> TriggerConsumptionTrace { get; set; } = [];
}

public sealed class FinanceTransparencyTriggerTraceItemViewModel
{
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class FinanceTransparencyRelatedRecordViewModel
{
    public string RelationshipType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string ResolutionSource { get; set; } = string.Empty;
}
