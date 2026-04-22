namespace VirtualCompany.Shared;

public static class FinanceCompanySimulationStatusValues
{
    public const string NotStarted = "not_started";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Stopped = "stopped";
}

public sealed class FinanceCompanySimulationStatusTransitionResponse
{
    public string Status { get; set; } = string.Empty;
    public DateTime TransitionedUtc { get; set; }
    public string? Message { get; set; }
}

public sealed class FinanceCompanySimulationDayLogResponse
{
    public DateTime SimulatedDateUtc { get; set; }
    public int TransactionsGenerated { get; set; }
    public int InvoicesGenerated { get; set; }
    public int BillsGenerated { get; set; }
    public int AssetPurchasesGenerated { get; set; }
    public int RecurringExpenseInstancesGenerated { get; set; }
    public int AlertsGenerated { get; set; }
    public List<string> InjectedAnomalies { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public int GeneratedRecordCount =>
        TransactionsGenerated + InvoicesGenerated + BillsGenerated + AssetPurchasesGenerated + RecurringExpenseInstancesGenerated;
}

public sealed class FinanceCompanySimulationRunHistoryResponse
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
    public List<string> InjectedAnomalies { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<FinanceCompanySimulationStatusTransitionResponse> StatusTransitions { get; set; } = [];
    public List<FinanceCompanySimulationDayLogResponse> DayLogs { get; set; } = [];
}

public sealed class FinanceCompanySimulationStateResponse
{
    public Guid CompanyId { get; set; }
    public string Status { get; set; } = FinanceCompanySimulationStatusValues.NotStarted;
    public DateTime? CurrentSimulatedDateTime { get; set; }
    public DateTime? LastProgressionTimestamp { get; set; }
    public bool? GenerationEnabled { get; set; }
    public int? Seed { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public DateTime? StartSimulatedDateTime { get; set; }
    public bool CanStart { get; set; }
    public bool CanPause { get; set; }
    public bool CanStop { get; set; }
    public bool CanToggleGeneration { get; set; }
    public bool UiVisible { get; set; } = true;
    public bool BackendExecutionEnabled { get; set; } = true;
    public bool BackgroundJobsEnabled { get; set; } = true;
    public string? DisabledReason { get; set; }
    public List<FinanceCompanySimulationRunHistoryResponse> RecentHistory { get; set; } = [];
    public bool SupportsStepForwardOneDay { get; set; }
    public bool SupportsRefresh { get; set; } = true;
    public string? DeterministicConfigurationJson { get; set; }

    public static FinanceCompanySimulationStateResponse NotStarted(Guid companyId) =>
        new()
        {
            CompanyId = companyId,
            Status = FinanceCompanySimulationStatusValues.NotStarted,
            CanStart = true,
            CanPause = false,
            CanStop = false,
            CanToggleGeneration = true,
            UiVisible = true,
            BackendExecutionEnabled = true,
            BackgroundJobsEnabled = true,
            DisabledReason = null,
            SupportsStepForwardOneDay = true,
            SupportsRefresh = true,
            GenerationEnabled = true
        };
}

public sealed class FinanceCompanySimulationStartRequest
{
    public DateTime StartSimulatedDateTime { get; set; }
    public bool GenerationEnabled { get; set; }
    public int Seed { get; set; }
    public string? DeterministicConfigurationJson { get; set; }
}

public sealed class FinanceCompanySimulationUpdateRequest
{
    public bool? GenerationEnabled { get; set; }
    public string? DeterministicConfigurationJson { get; set; }
}