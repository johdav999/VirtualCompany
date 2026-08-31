using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingCloseWorkspacePage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApi { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "periodId")] public Guid? RequestedPeriodId { get; set; }
    [SupplyParameterFromQuery(Name = "closeInstanceId")] public Guid? RequestedCloseInstanceId { get; set; }
    [SupplyParameterFromQuery(Name = "taskId")] public Guid? RequestedTaskId { get; set; }

    private AccountingCloseWorkspaceResponse? Workspace;
    private AccountingCloseWorkspaceTaskResponse? CompletingTask;
    private Guid? LoadedCompanyId;
    private Guid? LoadedPeriodId;
    private bool WorkspaceLoading;
    private bool Acting;
    private bool ActionFailed;
    private string? WorkspaceError;
    private string? ActionMessage;
    private string CompletionNote = string.Empty;
    private string LockReason = string.Empty;

    private bool CanLock => Workspace?.Readiness is { IsReady: true, IsStale: false } &&
        Workspace.AllowedActions.Contains("lock", StringComparer.Ordinal);
    private bool CanRefreshReadiness => Workspace?.CloseInstanceId is not null &&
        Workspace.AllowedActions.Contains("refresh_readiness", StringComparer.Ordinal);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId) return;
        if (LoadedCompanyId == companyId && LoadedPeriodId == RequestedPeriodId && Workspace is not null) return;
        LoadedCompanyId = companyId; LoadedPeriodId = RequestedPeriodId;
        await LoadAsync(companyId, RequestedPeriodId, RequestedCloseInstanceId);
    }

    private async Task LoadAsync(Guid companyId, Guid? periodId, Guid? closeInstanceId = null)
    {
        WorkspaceLoading = true; WorkspaceError = null;
        try { Workspace = await FinanceApi.GetAccountingCloseWorkspaceAsync(companyId, periodId, closeInstanceId); }
        catch (FinanceApiException exception) { WorkspaceError = exception.Message; Workspace = null; }
        finally { WorkspaceLoading = false; }
    }

    private Task ReloadAsync() => AccessState.CompanyId is Guid companyId
        ? LoadAsync(companyId, Workspace?.SelectedPeriod?.FiscalPeriodId ?? RequestedPeriodId, Workspace?.CloseInstanceId)
        : Task.CompletedTask;

    private async Task ChangePeriodAsync(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out var periodId) || AccessState.CompanyId is not Guid companyId) return;
        LoadedPeriodId = periodId;
        Navigation.NavigateTo(FinanceRoutes.WithCompanyContext($"{FinanceRoutes.AccountingCloseWorkspace}?periodId={periodId:D}", companyId), replace: true);
        await LoadAsync(companyId, periodId);
    }

    private bool CanComplete(AccountingCloseWorkspaceTaskResponse task) =>
        Workspace?.AllowedActions.Contains("complete_task", StringComparer.Ordinal) == true &&
        task.AllowedActions.Contains("complete", StringComparer.Ordinal) && task.BlockingReasonCodes.Count == 0;

    private void BeginComplete(AccountingCloseWorkspaceTaskResponse task) { CompletingTask = task; CompletionNote = string.Empty; }
    private void CancelComplete() { CompletingTask = null; CompletionNote = string.Empty; }

    private async Task CompleteTaskAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || Workspace?.CloseInstanceId is not Guid closeId || CompletingTask is null) return;
        await ActAsync(async () =>
        {
            await FinanceApi.CompleteAccountingCloseTaskAsync(companyId, closeId, CompletingTask, CompletionNote);
            ActionMessage = FinanceText["CloseTaskCompleted"];
            CompletingTask = null; CompletionNote = string.Empty;
            await LoadAsync(companyId, Workspace.SelectedPeriod?.FiscalPeriodId, closeId);
        });
    }

    private async Task RefreshReadinessAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || Workspace?.CloseInstanceId is not Guid closeId || Workspace.CloseVersion is not long closeVersion) return;
        await ActAsync(async () =>
        {
            await FinanceApi.RefreshAccountingCloseReadinessAsync(companyId, closeId, closeVersion);
            ActionMessage = FinanceText["ReadinessRefreshed"];
            await LoadAsync(companyId, Workspace.SelectedPeriod?.FiscalPeriodId, closeId);
        });
    }

    private async Task LockAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || Workspace?.CloseInstanceId is not Guid closeId || Workspace.Readiness is null) return;
        Acting = true; ActionFailed = false; ActionMessage = null;
        try
        {
            await FinanceApi.LockAccountingCloseAsync(companyId, closeId, Workspace.Readiness, LockReason);
            ActionMessage = FinanceText["PeriodLockedFromWorkspace"];
            LockReason = string.Empty;
            await LoadAsync(companyId, Workspace.SelectedPeriod?.FiscalPeriodId, closeId);
        }
        catch (FinanceApiException exception)
        {
            ActionFailed = true; ActionMessage = exception.Message;
            if (string.Equals(exception.ReasonCode, "accounting_close_evidence_stale", StringComparison.OrdinalIgnoreCase) && Workspace.CloseVersion is long closeVersion)
            {
                try
                {
                    await FinanceApi.RefreshAccountingCloseReadinessAsync(companyId, closeId, closeVersion);
                    await LoadAsync(companyId, Workspace.SelectedPeriod?.FiscalPeriodId, closeId);
                    ActionMessage = FinanceText["StaleLockRejectedAndRefreshed", exception.Message];
                }
                catch (FinanceApiException refreshException) { ActionMessage = $"{exception.Message} {refreshException.Message}"; }
            }
        }
        finally { Acting = false; }
    }

    private async Task ActAsync(Func<Task> action)
    {
        Acting = true; ActionFailed = false; ActionMessage = null;
        try { await action(); }
        catch (FinanceApiException exception) { ActionFailed = true; ActionMessage = exception.Message; }
        finally { Acting = false; }
    }

    private string Scoped(string path) => FinanceRoutes.WithCompanyContext(path, AccessState.CompanyId);
    private static string Friendly(string value) => value.Replace('_', ' ').Replace('-', ' ');
    private static string StatusClass(string value) => value.ToLowerInvariant() switch
    {
        "completed" or "current" or "prepared" or "final" or "normal" => "current",
        "blocked" or "failed" or "high" or "critical" or "overdue" => "blocked",
        "attention" or "in_review" or "pending_approval" or "medium" or "stale" => "attention",
        _ => "neutral"
    };
    private static string Short(Guid? id) => id.HasValue ? id.Value.ToString("N")[..8] : "—";
    private static string ShortHash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value[..Math.Min(10, value.Length)];
    private static string Local(DateTime value) => value.ToLocalTime().ToString("g");
    private string PanelTitle(AccountingCloseWorkspacePanelResponse panel) => FinanceText[$"ClosePanel_{panel.Key}"];
}
