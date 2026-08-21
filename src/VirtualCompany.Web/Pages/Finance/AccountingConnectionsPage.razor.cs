using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingConnectionsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private AccountingAuthorityReadModelResponse? Authority { get; set; }
    private IReadOnlyList<AccountingPeriodResponse> AvailablePeriods { get; set; } = [];
    private AccountingAuthorityChangePreviewResponse? Preview { get; set; }
    private bool ShowAuthorityChange { get; set; }
    private bool IsActing { get; set; }
    private string TargetAuthority { get; set; } = "external_provider";
    private string? TargetProviderKey { get; set; }
    private Guid EffectiveFiscalPeriodId { get; set; }
    private string AuthorityChangeReason { get; set; } = "Move the authoritative books at a reviewed accounting-period boundary.";
    private bool OpeningBalancesReconciled { get; set; }
    private bool TrialBalanceReconciled { get; set; }
    private bool SourceMappingsReconciled { get; set; }
    private int ConflictCount { get; set; }
    private string CutoverSummary { get; set; } = "Reviewed against the selected authority source.";
    private string? ActionMessage { get; set; }
    private string? ActionError { get; set; }
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private AccountingAuthorityPeriodResponse? CurrentCutover =>
        Authority?.Periods.FirstOrDefault(x => x.Authority == "migration" && !x.CompletedUtc.HasValue);
    private string CurrentAuthorityLabel => Authority?.CurrentPeriod?.AuthorityLabel ?? "Not configured";
    private string CurrentEffectivePeriod => Authority?.CurrentPeriod is null
        ? "Not available"
        : $"{Authority.CurrentPeriod.EffectiveFrom:yyyy-MM} onward";

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId)
        {
            await LoadAsync(companyId);
        }
    }

    private async Task LoadAsync(Guid companyId)
    {
        ActionError = null;
        try
        {
            var authorityTask = FinanceApiClient.GetAccountingAuthorityAsync(companyId);
            var yearsTask = FinanceApiClient.GetAccountingFiscalYearsAsync(companyId);
            await Task.WhenAll(authorityTask, yearsTask);
            Authority = await authorityTask;
            AvailablePeriods = (await yearsTask).SelectMany(x => x.Periods)
                .Where(x => x.StartDate > (Authority?.CurrentPeriod?.EffectiveFrom ?? DateOnly.MinValue))
                .OrderBy(x => x.StartDate)
                .ToArray();
            EffectiveFiscalPeriodId = AvailablePeriods.FirstOrDefault()?.Id ?? Guid.Empty;
            TargetProviderKey ??= Authority?.Providers.FirstOrDefault()?.ProviderKey;
            LoadCutoverForm();
        }
        catch (FinanceApiException exception)
        {
            ActionError = exception.Message;
        }
    }

    private Task ReloadAsync() => AccessState.CompanyId is Guid companyId ? LoadAsync(companyId) : Task.CompletedTask;

    private void ToggleAuthorityChange()
    {
        ShowAuthorityChange = !ShowAuthorityChange;
        Preview = null;
        ActionError = null;
    }

    private async Task PreviewAuthorityChangeAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || EffectiveFiscalPeriodId == Guid.Empty) return;
        await ActAsync(async () =>
        {
            Preview = await FinanceApiClient.PreviewAccountingAuthorityChangeAsync(companyId, new()
            {
                EffectiveFiscalPeriodId = EffectiveFiscalPeriodId,
                TargetAuthority = TargetAuthority,
                ProviderKey = TargetAuthority == "external_provider" ? TargetProviderKey : null
            });
        });
    }

    private async Task StartAuthorityChangeAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || Preview?.IsAllowed != true) return;
        await ActAsync(async () =>
        {
            Authority = await FinanceApiClient.StartAccountingAuthorityChangeAsync(companyId, new()
            {
                EffectiveFiscalPeriodId = Preview.EffectiveFiscalPeriodId,
                TargetAuthority = Preview.TargetAuthority,
                ProviderKey = Preview.ProviderKey,
                Reason = AuthorityChangeReason,
                PreviewToken = Preview.PreviewToken,
                ExpectedCurrentVersion = Preview.ExpectedCurrentVersion
            });
            ShowAuthorityChange = false;
            Preview = null;
            LoadCutoverForm();
            ActionMessage = "The controlled authority cutover was started. Normal posting and export are paused for the affected period until reconciliation completes.";
        });
    }

    private async Task SaveCutoverValidationAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || CurrentCutover is null) return;
        await ActAsync(async () =>
        {
            Authority = await FinanceApiClient.RecordAccountingCutoverValidationAsync(companyId, CurrentCutover.Id, new()
            {
                OpeningBalancesReconciled = OpeningBalancesReconciled,
                TrialBalanceReconciled = TrialBalanceReconciled,
                SourceMappingsReconciled = SourceMappingsReconciled,
                ConflictCount = ConflictCount,
                Summary = CutoverSummary,
                ExpectedVersion = CurrentCutover.Version
            });
            LoadCutoverForm();
            ActionMessage = CurrentCutover?.IsCutoverReady == true
                ? "All cutover checks passed. The authority change is ready to complete."
                : "The cutover checks were saved. Resolve the remaining items before completion.";
        });
    }

    private async Task CompleteCutoverAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || CurrentCutover?.IsCutoverReady != true) return;
        await ActAsync(async () =>
        {
            Authority = await FinanceApiClient.CompleteAccountingAuthorityCutoverAsync(companyId, CurrentCutover.Id,
                new() { ExpectedVersion = CurrentCutover.Version });
            ActionMessage = "The authority cutover completed. Each accounting period now follows exactly one authoritative book.";
        });
    }

    private async Task ReconcileExportAsync(AccountingProviderExportResponse export, bool succeeded)
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        await ActAsync(async () =>
        {
            await FinanceApiClient.ReconcileAccountingProviderExportAsync(companyId, export.Id, new()
            {
                ProviderConfirmedSuccess = succeeded,
                ProviderExternalId = succeeded ? export.ProviderExternalId : null,
                Summary = succeeded
                    ? "The provider record was verified and linked to this export."
                    : "The provider confirmed that no business action was created.",
                ExpectedVersion = export.Version
            });
            await LoadAsync(companyId);
            ActionMessage = succeeded
                ? "The provider success was reconciled without reposting the local journal."
                : "The unknown outcome was reconciled as not sent.";
        });
    }

    private async Task ActAsync(Func<Task> action)
    {
        IsActing = true;
        ActionError = null;
        ActionMessage = null;
        try { await action(); }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
        finally { IsActing = false; }
    }

    private void LoadCutoverForm()
    {
        if (CurrentCutover is null) return;
        OpeningBalancesReconciled = CurrentCutover.OpeningBalancesReconciled;
        TrialBalanceReconciled = CurrentCutover.TrialBalanceReconciled;
        SourceMappingsReconciled = CurrentCutover.SourceMappingsReconciled;
        ConflictCount = CurrentCutover.ConflictCount;
        CutoverSummary = CurrentCutover.ValidationSummary ?? CutoverSummary;
    }

    private static string PeriodRange(AccountingAuthorityPeriodResponse period) => period.EffectiveTo.HasValue
        ? $"{period.EffectiveFrom:yyyy-MM} to {period.EffectiveTo:yyyy-MM}"
        : $"{period.EffectiveFrom:yyyy-MM} onward";
    private static string AuthorityIcon(string authority) => authority switch { "internal_ledger" => "◇", "external_provider" => "▣", _ => "↔" };
    private static string ProviderMark(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0])).ToUpperInvariant()[..1];
    private static string SourceLabel(string source) => source.Replace('_', ' ');
    private static string ExportBadge(string status) => status switch
    {
        "exported" => "status-badge status-badge-success",
        "reconciliation_required" or "failed" => "status-badge status-badge-danger",
        "awaiting_approval" or "approved" or "executing" => "status-badge status-badge-warning",
        _ => "status-badge"
    };

    private static RenderFragment CutoverCheck(string label, bool complete) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "accounting-cutover-check");
        builder.OpenElement(2, "span");
        builder.AddContent(3, complete ? "✓" : "!");
        builder.CloseElement();
        builder.AddContent(4, label);
        builder.OpenElement(5, "strong");
        builder.AddContent(6, complete ? "Complete" : "Needs review");
        builder.CloseElement();
        builder.CloseElement();
    };
}
