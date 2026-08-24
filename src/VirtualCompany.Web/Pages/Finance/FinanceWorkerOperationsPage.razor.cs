using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class FinanceWorkerOperationsPage
{
    [Inject] private FinanceApiClient FinanceClient { get; set; } = default!;
    private FinanceWorkerOperationsResponse? Operations { get; set; }
    private FinanceWorkerWorkItemResponse? Selected { get; set; }
    private bool IsOperationsLoading { get; set; }
    private bool IsActionRunning { get; set; }
    private string? StatusFilter { get; set; }
    private string? WorkerFilter { get; set; }
    private string? Search { get; set; }
    private string OperatorReason { get; set; } = string.Empty;
    private string? ActionError { get; set; }
    private string? ActionSuccess { get; set; }
    private bool IsBusy => IsOperationsLoading || IsActionRunning;
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private bool CanRunRetry => CanManageAccounting && Selected?.AllowedActions.CanRetry == true && !IsBusy && HasOperatorReason;
    private bool CanRunStop => CanManageAccounting && Selected?.AllowedActions.CanStop == true && !IsBusy && HasOperatorReason;
    private bool CanRunAcknowledge => CanManageAccounting && Selected?.AllowedActions.CanAcknowledge == true && !IsBusy && HasOperatorReason;
    private bool HasOperatorReason => !string.IsNullOrWhiteSpace(OperatorReason);
    private long AttentionCount => Operations is null ? 0 : Operations.Health.ExhaustedFailureCount + Operations.Health.PoisonWorkCount + Operations.Health.ReconciliationRequiredCount;
    private int ReadyWorkerCount => Operations?.Workers.Count(x => x.IsConfigured && (!x.IsEnabled || x.IsConfigured)) ?? 0;
    private string HealthLabel => FinanceText[Operations?.Health.Status == "ready" ? "AllConfigured" : "NeedsAttention"];
    private string OldestAge => Operations?.Health.OldestQueuedUtc is DateTime oldest ? FormatAge(oldest) : "—";
    private string OldestCreatedLabel => Operations?.Health.OldestQueuedUtc is DateTime oldest ? FinanceText["CreatedValue", FormatDate(oldest)] : FinanceText["NoQueuedWork"];
    private string FailureSummary => string.IsNullOrWhiteSpace(Selected?.SafeFailureSummary) ? FinanceText["NoSafeFailure"] : Selected.SafeFailureSummary;
    private string FailureCodeLabel => string.IsNullOrWhiteSpace(Selected?.FailureCode) ? FinanceText["NoFailureRecorded"] : Humanize(Selected.FailureCode);
    private string RetryLabel => Selected?.NextRetryUtc is DateTime next ? FinanceText["NextRetryValue", FormatDate(next)] : Selected?.LeaseExpiresUtc is DateTime lease ? FinanceText["LeaseEndsValue", FormatDate(lease)] : FinanceText["NoAutomaticRetry"];
    private string ReconciliationHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.AccountingConnections, AccessState.CompanyId);
    private IReadOnlyList<FinanceWorkerWorkItemResponse> FilteredItems => (Operations?.WorkItems ?? [])
        .Where(x => string.IsNullOrWhiteSpace(Search) || x.WorkerName.Contains(Search, StringComparison.OrdinalIgnoreCase) || x.WorkReference.Contains(Search, StringComparison.OrdinalIgnoreCase))
        .ToList();

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (AccessState.IsAllowed) await LoadOperationsAsync();
    }

    private async Task LoadOperationsAsync()
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        IsOperationsLoading = true; ActionError = null;
        try
        {
            var selectedId = Selected?.Id;
            Operations = await FinanceClient.GetWorkerOperationsAsync(companyId, StatusFilter, WorkerFilter);
            Selected = selectedId.HasValue ? Operations?.WorkItems.FirstOrDefault(x => x.Id == selectedId) : Operations?.WorkItems.FirstOrDefault();
        }
        catch (Exception ex) { ActionError = ex.Message; }
        finally { IsOperationsLoading = false; }
    }

    private void Select(FinanceWorkerWorkItemResponse item) { Selected = item; OperatorReason = string.Empty; ActionError = null; ActionSuccess = null; }

    private async Task RunActionAsync(string action)
    {
        if (AccessState.CompanyId is not Guid companyId || Selected is null || !HasOperatorReason) return;
        IsActionRunning = true; ActionError = null; ActionSuccess = null;
        try
        {
            var request = new FinanceWorkerOperatorActionApiRequest(Selected.Version, OperatorReason.Trim());
            var updated = action switch
            {
                "retry" => await FinanceClient.RetryWorkerExecutionAsync(companyId, Selected.Id, request),
                "stop" => await FinanceClient.StopWorkerExecutionAsync(companyId, Selected.Id, request),
                _ => await FinanceClient.AcknowledgeWorkerExecutionAsync(companyId, Selected.Id, request)
            };
            ActionSuccess = FinanceText[action switch { "retry" => "WorkRetryQueued", "stop" => "FutureWorkStopped", _ => "FailureAcknowledged" }];
            OperatorReason = string.Empty;
            await LoadOperationsAsync();
            Selected = Operations?.WorkItems.FirstOrDefault(x => x.Id == updated.Id) ?? updated;
        }
        catch (Exception ex) { ActionError = ex.Message; }
        finally { IsActionRunning = false; }
    }

    private async Task ClearFiltersAsync() { Search = null; StatusFilter = null; WorkerFilter = null; await LoadOperationsAsync(); }
    private string FormatDate(DateTime value) => LocalDateTime.DateTime(value);
    private string ShortReference(string value) => string.IsNullOrWhiteSpace(value) ? FinanceText["NotAvailable"] : value.Length <= 12 ? value : $"{value[..8]}…";
    private string FormatAge(DateTime value) { var age = DateTime.UtcNow - value.ToUniversalTime(); return age.TotalDays >= 1 ? FinanceText["AgeDaysHours", (int)age.TotalDays, age.Hours] : age.TotalHours >= 1 ? FinanceText["AgeHoursMinutes", (int)age.TotalHours, age.Minutes] : FinanceText["AgeMinutes", Math.Max(0, age.Minutes)]; }
    private string FormatDuration(long milliseconds) => TimeSpan.FromMilliseconds(milliseconds) is var duration && duration.TotalMinutes >= 1 ? FinanceText["DurationMinutesSeconds", (int)duration.TotalMinutes, duration.Seconds] : FinanceText["DurationSeconds", duration.Seconds];
    private static string Humanize(string value) => string.Join(" ", value.Replace('.', '_').Split('_', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    private string AttemptLabel(string value) => Humanize(value) switch { "succeeded" => FinanceText["Succeeded"], "retry scheduled" => FinanceText["RetryScheduled"], "lease expired" => FinanceText["RecoveredAfterProcessStop"], "blocked" => FinanceText["Blocked"], _ => Humanize(value) };
    private string StatusLabel(string status) => FinanceText[status switch
    {
        "queued" => "Queued",
        "in_progress" => "InProgress",
        "retry_scheduled" => "RetryScheduled",
        "needs_attention" => "NeedsAttention",
        "completed" => "Completed",
        "stopped" => "Stopped",
        _ => "Unknown"
    }];
    private static string StatusClass(string status) => $"finance-work-recovery__status finance-work-recovery__status--{status.Replace('_', '-')}";
}
