using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class FinanceAgentWorkbenchPage : FinancePageBase
{
    private static readonly HashSet<string> SupportedReferenceTypes = new(StringComparer.OrdinalIgnoreCase)
    { "invoice", "bill", "customer", "supplier", "fiscal_period", "migration" };

    [Inject] private FinanceApiClient FinanceApi { get; set; } = default!;
    [Inject] private AgentApiClient AgentApi { get; set; } = default!;
    [Inject] private ILogger<FinanceAgentWorkbenchPage> Logger { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "agentId")] public Guid? AgentId { get; set; }
    [SupplyParameterFromQuery(Name = "runId")] public Guid? RunId { get; set; }
    [SupplyParameterFromQuery(Name = "referenceType")] public string? ReferenceType { get; set; }
    [SupplyParameterFromQuery(Name = "referenceValue")] public string? ReferenceValue { get; set; }
    [SupplyParameterFromQuery(Name = "request")] public string? InitialRequest { get; set; }

    protected CompanyAgentSummaryViewModel? CurrentAgent { get; private set; }
    protected FinanceConversationRunViewModel? CurrentRun { get; private set; }
    protected IReadOnlyList<FinanceConversationRunViewModel> RecentRuns { get; private set; } = [];
    protected string RequestText { get; set; } = string.Empty;
    protected string? ActionError { get; private set; }
    protected string? ConflictMessage { get; private set; }
    protected string Announcement { get; private set; } = string.Empty;
    protected bool IsBusy { get; private set; }
    private CancellationTokenSource? pollingCts;
    private bool initializedRequest;

    protected IReadOnlyList<string> ExampleIntents =>
    [
        FinanceText["IntentOverdueInvoices"], FinanceText["IntentCashOutlook"],
        FinanceText["IntentSupplierBills"], FinanceText["IntentExplainTransaction"]
    ];
    protected IReadOnlyList<FinancePlanningEvidenceViewModel> Evidence => CurrentRun?.Revisions
        .OrderByDescending(x => x.Revision).FirstOrDefault()?.Evidence
        .GroupBy(x => $"{x.EntityType}:{x.EntityId}", StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray() ?? [];
    protected IReadOnlyList<FinancePlanningEvidenceViewModel> FreshEvidence => Evidence.Where(x => x.IsFresh).ToArray();
    protected IReadOnlyList<FinancePlanningEvidenceViewModel> StaleEvidence => Evidence.Where(x => !x.IsFresh).ToArray();
    protected FinanceConversationRunStepViewModel? CheckpointStep => CurrentRun?.Steps
        .OrderBy(x => x.Order).FirstOrDefault(x => x.Status is "awaiting_confirmation" or "awaiting_approval")
        ?? CurrentRun?.Steps.OrderByDescending(x => x.Order).FirstOrDefault(x => x.ActionType == "execute");
    protected bool CanCancel => FinanceConversationRunUiState.CanCancel(CurrentRun?.Status);
    protected bool CanSubmit => !IsBusy && CurrentAgent is not null && !string.IsNullOrWhiteSpace(RequestText) && RequestText.Trim().Length <= 8000;
    protected bool HasVisibleReference => SupportedReferenceTypes.Contains(ReferenceType ?? string.Empty) &&
                                          !string.IsNullOrWhiteSpace(ReferenceValue) && ReferenceValue.Trim().Length <= 200;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        await StopPollingAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId) return;

        ActionError = null;
        try
        {
            var roster = await AgentApi.GetRosterAsync(companyId);
            CurrentAgent = AgentId is Guid selected
                ? roster.FirstOrDefault(x => x.Id == selected && IsVisibleFinanceAgent(x))
                : roster.FirstOrDefault(IsVisibleFinanceAgent);
            if (CurrentAgent is null)
            {
                ActionError = FinanceText["FinanceAgentUnavailable"];
                return;
            }
            AgentId = CurrentAgent.Id;
            if (!initializedRequest)
            {
                RequestText = !string.IsNullOrWhiteSpace(InitialRequest) && InitialRequest.Length <= 8000
                    ? InitialRequest.Trim()
                    : HasVisibleReference ? DefaultReferenceRequest() : string.Empty;
                initializedRequest = true;
            }
            await LoadRunsAsync(companyId, CurrentAgent.Id, RunId);
        }
        catch (Exception ex) when (ex is FinanceApiException or InvalidOperationException)
        {
            Logger.LogWarning(ex, "Finance agent workbench could not load for company {CompanyId}.", companyId);
            ActionError = ex.Message;
        }
    }

    protected async Task SubmitRequestAsync()
    {
        if (!CanSubmit || AccessState.CompanyId is not Guid companyId || CurrentAgent is null) return;
        IsBusy = true; ActionError = null; ConflictMessage = null;
        try
        {
            var references = HasVisibleReference
                ? new[] { new FinancePlanningReferenceApiRequest(ReferenceType!.Trim().ToLowerInvariant(), ReferenceValue!.Trim()) }
                : null;
            if (CurrentRun?.Status == "awaiting_clarification")
            {
                CurrentRun = await FinanceApi.SupersedeConversationRunAsync(companyId, CurrentAgent.Id, CurrentRun.Id,
                    new SupersedeFinanceConversationRunApiRequest(RequestText.Trim(), NewIdempotencyKey(),
                        FinanceText["ClarificationSupersessionReason"], References: references));
            }
            else
            {
                CurrentRun = await FinanceApi.StartConversationRunAsync(companyId, CurrentAgent.Id,
                    new StartFinanceConversationRunApiRequest(RequestText.Trim(), NewIdempotencyKey(), References: references));
            }
            RunId = CurrentRun.Id;
            Announcement = FinanceText["RunStateAnnouncement", StatusLabel(CurrentRun.Status)];
            await LoadRunsAsync(companyId, CurrentAgent.Id, CurrentRun.Id);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsBusy = false; }
    }

    protected async Task ConfirmStepAsync(FinanceConversationRunStepViewModel step)
    {
        if (CurrentRun is null || AccessState.CompanyId is not Guid companyId || CurrentAgent is null) return;
        IsBusy = true; ActionError = null; ConflictMessage = null;
        try
        {
            CurrentRun = await FinanceApi.ConfirmConversationRunStepAsync(companyId, CurrentAgent.Id,
                CurrentRun.Id, step.StepId, step.Version);
            Announcement = FinanceText["ConfirmationSubmittedAnnouncement", StatusLabel(CurrentRun.Status)];
            BeginPolling();
        }
        catch (FinanceApiException ex)
        {
            await ReloadAfterConflictAsync(ex.Message);
        }
        finally { IsBusy = false; }
    }

    protected async Task CancelRunAsync()
    {
        if (CurrentRun is null || AccessState.CompanyId is not Guid companyId || CurrentAgent is null) return;
        IsBusy = true; ActionError = null; ConflictMessage = null;
        try
        {
            CurrentRun = await FinanceApi.CancelConversationRunAsync(companyId, CurrentAgent.Id, CurrentRun.Id,
                FinanceText["UserCancellationReason"]);
            Announcement = FinanceText["RunCancelledAnnouncement"];
            await StopPollingAsync();
        }
        catch (FinanceApiException ex) { await ReloadAfterConflictAsync(ex.Message); }
        finally { IsBusy = false; }
    }

    protected async Task RefreshAsync()
    {
        if (CurrentRun is null || AccessState.CompanyId is not Guid companyId || CurrentAgent is null) return;
        IsBusy = true; ActionError = null;
        try
        {
            CurrentRun = await FinanceApi.GetConversationRunAsync(companyId, CurrentAgent.Id, CurrentRun.Id);
            ConflictMessage = null;
            Announcement = CurrentRun is null ? FinanceText["RunUnavailable"] : FinanceText["RunStateAnnouncement", StatusLabel(CurrentRun.Status)];
            BeginPolling();
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsBusy = false; }
    }

    protected async Task SelectRunAsync(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out var selected) || AccessState.CompanyId is not Guid companyId || CurrentAgent is null) return;
        CurrentRun = await FinanceApi.GetConversationRunAsync(companyId, CurrentAgent.Id, selected);
        RunId = selected;
        Announcement = CurrentRun is null ? FinanceText["RunUnavailable"] : FinanceText["RunStateAnnouncement", StatusLabel(CurrentRun.Status)];
        BeginPolling();
    }

    protected void SelectIntent(string intent) => RequestText = intent;
    protected void ClearReference() { ReferenceType = null; ReferenceValue = null; }

    private async Task LoadRunsAsync(Guid companyId, Guid agentId, Guid? selectedRunId)
    {
        var list = await FinanceApi.ListConversationRunsAsync(companyId, agentId, 20);
        RecentRuns = list?.Items ?? [];
        CurrentRun = selectedRunId is Guid runId
            ? await FinanceApi.GetConversationRunAsync(companyId, agentId, runId)
            : RecentRuns.FirstOrDefault();
        if (CurrentRun is not null) RunId = CurrentRun.Id;
        BeginPolling();
    }

    private void BeginPolling()
    {
        StopPolling();
        if (CurrentRun is null || !FinanceConversationRunUiState.ShouldPoll(CurrentRun.Status)) return;
        pollingCts = new CancellationTokenSource();
        _ = PollAsync(pollingCts.Token);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        if (AccessState.CompanyId is not Guid companyId || CurrentAgent is null || CurrentRun is null) return;
        var runId = CurrentRun.Id;
        for (var attempt = 0; attempt < 40 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                var refreshed = await FinanceApi.GetConversationRunAsync(companyId, CurrentAgent.Id, runId, cancellationToken);
                if (refreshed is null) return;
                await InvokeAsync(() =>
                {
                    var changed = CurrentRun?.Version != refreshed.Version || CurrentRun?.Status != refreshed.Status;
                    CurrentRun = refreshed;
                    if (changed) Announcement = FinanceText["RunStateAnnouncement", StatusLabel(refreshed.Status)];
                    StateHasChanged();
                });
                if (!FinanceConversationRunUiState.ShouldPoll(refreshed.Status)) return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (FinanceApiException ex)
            {
                await InvokeAsync(() => { ActionError = ex.Message; StateHasChanged(); });
                return;
            }
        }
    }

    private async Task ReloadAfterConflictAsync(string message)
    {
        ConflictMessage = FinanceText["ConflictRefreshMessage", message];
        if (AccessState.CompanyId is Guid companyId && CurrentAgent is not null && CurrentRun is not null)
            CurrentRun = await FinanceApi.GetConversationRunAsync(companyId, CurrentAgent.Id, CurrentRun.Id);
        Announcement = FinanceText["RunChangedAnnouncement"];
    }

    private void StopPolling()
    {
        var previous = Interlocked.Exchange(ref pollingCts, null);
        if (previous is null) return;
        previous.Cancel();
        previous.Dispose();
    }

    private Task StopPollingAsync() { StopPolling(); return Task.CompletedTask; }

    public async ValueTask DisposeAsync() => await StopPollingAsync();

    private bool IsVisibleFinanceAgent(CompanyAgentSummaryViewModel agent) =>
        string.Equals(agent.Department, "Finance", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(agent.Status, "archived", StringComparison.OrdinalIgnoreCase);
    private string DefaultReferenceRequest() => ReferenceType?.ToLowerInvariant() switch
    {
        "invoice" => FinanceText["ReferenceRequestInvoice"], "bill" => FinanceText["ReferenceRequestBill"],
        "customer" => FinanceText["ReferenceRequestCustomer"], "supplier" => FinanceText["ReferenceRequestSupplier"],
        "fiscal_period" => FinanceText["ReferenceRequestPeriod"], "migration" => FinanceText["ReferenceRequestMigration"],
        _ => string.Empty
    };
    private string NewIdempotencyKey() => $"finance-workbench-{Guid.NewGuid():N}";
    protected IReadOnlyList<FinancePlanningEvidenceViewModel> EvidenceForStep(FinanceConversationRunStepViewModel step) => Evidence;
    protected string? BuildEvidenceHref(FinancePlanningEvidenceViewModel evidence) => evidence.EntityType.ToLowerInvariant() switch
    {
        var type when type.Contains("invoice") && Guid.TryParse(evidence.EntityId, out var id) => FinanceRoutes.BuildInvoiceDetailPath(id, AccessState.CompanyId),
        var type when type.Contains("bill") && Guid.TryParse(evidence.EntityId, out var id) => FinanceRoutes.BuildBillDetailPath(id, AccessState.CompanyId),
        var type when type.Contains("customer") && Guid.TryParse(evidence.EntityId, out var id) => FinanceRoutes.BuildCustomerBillingPath(id, AccessState.CompanyId),
        _ => null
    };
    protected string BuildApprovalHref(Guid approvalId) => $"/work?companyId={AccessState.CompanyId:D}&tab=approvals&approvalId={approvalId:D}";
    protected string BuildAuditHref() => $"/history?companyId={AccessState.CompanyId:D}&agentId={CurrentAgent?.Id:D}";
    protected string StatusLabel(string status) => status.ToLowerInvariant() switch
    {
        "planned" => FinanceText["RunStatusPlanned"], "awaiting_clarification" => FinanceText["RunStatusAwaitingClarification"],
        "ready" => FinanceText["RunStatusReady"], "executing" => FinanceText["RunStatusExecuting"],
        "awaiting_confirmation" => FinanceText["RunStatusAwaitingConfirmation"], "awaiting_approval" => FinanceText["RunStatusAwaitingApproval"],
        "queued" => FinanceText["RunStatusQueued"], "reconciling" => FinanceText["RunStatusReconciling"],
        "completed" => FinanceText["RunStatusCompleted"], "partially_completed" => FinanceText["RunStatusPartiallyCompleted"],
        "cancelled" => FinanceText["RunStatusCancelled"], "stale" => FinanceText["RunStatusStale"], "blocked" => FinanceText["RunStatusBlocked"],
        "failed" => FinanceText["RunStatusFailed"], _ => Humanize(status)
    };
    protected string StatusCss(string status) => status.ToLowerInvariant() switch
    {
        "completed" => "is-success", "partially_completed" or "awaiting_confirmation" or "awaiting_approval" or "queued" => "is-warning",
        "cancelled" or "stale" or "failed" or "blocked" => "is-danger", "executing" or "reconciling" => "is-progress", _ => "is-neutral"
    };
    protected string StepCss(string status) => status is "completed" or "blocked" or "cancelled" or "stale" or "failed" ? "is-terminal" : StatusCss(status);
    protected string ModeCss(string action) => action.ToLowerInvariant() switch { "execute" => "is-execute", "recommend" => "is-recommend", _ => "is-read" };
    protected string ActionLabel(string action) => action.ToLowerInvariant() switch { "execute" => FinanceText["Execute"], "recommend" => FinanceText["Recommend"], _ => FinanceText["Read"] };
    protected string StepTitle(FinanceConversationRunStepViewModel step) => step.ActionType.ToLowerInvariant() switch
    { "execute" => FinanceText["ExecuteStepTitle"], "recommend" => FinanceText["RecommendStepTitle"], _ => FinanceText["ReadStepTitle"] };
    protected string OutcomeIcon(string status) => status.ToLowerInvariant() switch { "completed" => "bi bi-check2", "failed" or "stale" => "bi bi-exclamation-triangle", _ => "bi bi-hourglass-split" };
    protected string CheckpointDescription(FinanceConversationRunStepViewModel step) => step.Status == "awaiting_approval" ? FinanceText["ApprovalCheckpointDescription"] : FinanceText["ConfirmationCheckpointDescription"];
    protected string CheckpointRequirement(FinanceConversationRunStepViewModel step) => step.Status == "awaiting_approval" ? FinanceText["IndependentApprovalRequired"] : FinanceText["ExactConfirmationRequired"];
    protected string ActualEffect(FinanceConversationRunStepViewModel step) => step.Status switch
    {
        "completed" => FinanceText["EffectCompleted"], "partially_completed" => FinanceText["EffectPartiallyCompleted"],
        "queued" or "reconciling" => FinanceText["EffectPendingReconciliation"], "failed" or "blocked" or "stale" => FinanceText["EffectNotCompleted"],
        _ => FinanceText["EffectNotStarted"]
    };
    protected static string Humanize(string value) => string.Join(' ', value.Replace(':', '_').Split('_', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
