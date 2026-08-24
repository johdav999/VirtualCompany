using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingReportsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    private List<AccountingPeriodResponse> Periods { get; set; } = [];
    private Guid SelectedPeriodId { get; set; }
    private AccountingPeriodResponse? SelectedPeriod => Periods.FirstOrDefault(x => x.Id == SelectedPeriodId);
    private TrialBalanceReportResponse? TrialBalance { get; set; }
    private GeneralLedgerReportResponse? GeneralLedger { get; set; }
    private ProfitAndLossReportResponse? ProfitAndLoss { get; set; }
    private BalanceSheetReportResponse? BalanceSheet { get; set; }
    private AccountingTaxSummaryResponse? TaxSummary { get; set; }
    private ControlAccountReconciliationResponse? ControlAccounts { get; set; }
    private ReportingPeriodCloseValidationResponse? CloseValidation { get; set; }
    private IReadOnlyList<AccountingPeriodHistoryResponse> History { get; set; } = [];
    private IReadOnlyList<AccountingExportJobResponse> Exports { get; set; } = [];
    private Guid? SelectedAccountId { get; set; }
    private GeneralLedgerAccountResponse? SelectedLedgerAccount { get; set; }
    private string View { get; set; } = "trial";
    private bool IsReportLoading { get; set; }
    private bool IsActing { get; set; }
    private string CloseReason { get; set; } = string.Empty;
    private string ReopenReason { get; set; } = "";
    private string? ActionMessage { get; set; }
    private string? ActionError { get; set; }
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private bool CanReopen => AccessState.MembershipRole is "owner" or "admin";
    private string? Currency => TrialBalance?.Accounts.Select(x => x.Currency).Distinct().Count() == 1 ? TrialBalance.Accounts.FirstOrDefault()?.Currency : null;
    private string CloseSummary => CloseValidation is null ? FinanceText["NotReviewedYet"] : CloseValidation.IsReadyToClose ? FinanceText["ReadyToClose"] : FinanceText["CloseIssueTypes", CloseValidation.BlockingIssues.Count];
    private string LauraAdvice => CloseValidation?.IsReadyToClose == true ? FinanceText["LauraCloseReadyAdvice"] : CloseValidation is null ? FinanceText["LauraCloseReviewAdvice"] : FinanceText["LauraCloseBlockedAdvice", CloseValidation.BlockingIssues.Count];

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (string.IsNullOrWhiteSpace(CloseReason)) CloseReason = FinanceText["DefaultCloseReason"];
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId) return;
        try
        {
            var years = await FinanceApiClient.GetAccountingFiscalYearsAsync(companyId);
            Periods = years.SelectMany(x => x.Periods).OrderByDescending(x => x.StartDate).ToList();
            SelectedPeriodId = Periods.FirstOrDefault(x => !x.IsClosed)?.Id ?? Periods.FirstOrDefault()?.Id ?? Guid.Empty;
            if (SelectedPeriodId != Guid.Empty) await LoadReportsAsync(companyId);
        }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
    }

    private async Task ChangePeriodAsync(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out var periodId) && AccessState.CompanyId is Guid companyId)
        {
            SelectedPeriodId = periodId; SelectedAccountId = null; SelectedLedgerAccount = null; CloseValidation = null; await LoadReportsAsync(companyId);
        }
    }

    private async Task LoadReportsAsync(Guid companyId)
    {
        IsReportLoading = true; ActionError = null;
        try
        {
            var trial = FinanceApiClient.GetAccountingTrialBalanceAsync(companyId, SelectedPeriodId);
            var ledger = FinanceApiClient.GetAccountingGeneralLedgerAsync(companyId, SelectedPeriodId);
            var profit = FinanceApiClient.GetAccountingProfitAndLossAsync(companyId, SelectedPeriodId);
            var balance = FinanceApiClient.GetAccountingBalanceSheetAsync(companyId, SelectedPeriodId);
            var tax = FinanceApiClient.GetAccountingTaxSummaryAsync(companyId, SelectedPeriodId);
            var control = FinanceApiClient.GetAccountingControlReconciliationAsync(companyId, SelectedPeriodId);
            var history = FinanceApiClient.GetAccountingPeriodHistoryAsync(companyId, SelectedPeriodId);
            var exports = FinanceApiClient.GetAccountingExportsAsync(companyId, SelectedPeriodId);
            await Task.WhenAll(trial, ledger, profit, balance, tax, control, history, exports);
            TrialBalance = await trial; GeneralLedger = await ledger; ProfitAndLoss = await profit; BalanceSheet = await balance;
            TaxSummary = await tax; ControlAccounts = await control; History = await history; Exports = await exports;
            SelectedAccountId = null; SelectedLedgerAccount = null;
        }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
        finally { IsReportLoading = false; }
    }

    private async Task SelectAccountAsync(Guid accountId)
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        SelectedAccountId = accountId;
        View = View == "trial" ? "trial" : "ledger";
        try
        {
            var detail = await FinanceApiClient.GetAccountingGeneralLedgerPageAsync(
                companyId, SelectedPeriodId, accountId, 1, 200);
            SelectedLedgerAccount = detail?.Accounts.SingleOrDefault();
        }
        catch (FinanceApiException exception)
        {
            ActionError = exception.Message;
            SelectedLedgerAccount = null;
        }
    }
    private Task HandleAccountKeyAsync(KeyboardEventArgs args, Guid accountId) =>
        args.Key is "Enter" or " " ? SelectAccountAsync(accountId) : Task.CompletedTask;
    private async Task ValidateCloseAsync() => await ActAsync(async companyId => { CloseValidation = await FinanceApiClient.ValidateAccountingPeriodCloseAsync(companyId, SelectedPeriodId); ActionMessage = FinanceText[CloseValidation.IsReadyToClose ? "AllCloseChecksPassedMessage" : "CloseReviewFinishedMessage"]; });
    private async Task ReviewTaxAsync() => await ActAsync(async companyId => { TaxSummary = await FinanceApiClient.ReviewAccountingTaxSummaryAsync(companyId, SelectedPeriodId); ActionMessage = FinanceText["TaxReviewRecordedMessage"]; await ValidateCloseCoreAsync(companyId); });
    private async Task CloseAndLockAsync() => await ActAsync(async companyId => { await FinanceApiClient.CloseAndLockAccountingPeriodAsync(companyId, SelectedPeriodId, CloseReason); ActionMessage = FinanceText["PeriodClosedMessage"]; await ReloadAsync(companyId); });
    private async Task ReopenAsync() => await ActAsync(async companyId => { await FinanceApiClient.ReopenAccountingPeriodAsync(companyId, SelectedPeriodId, ReopenReason); ActionMessage = FinanceText["PeriodReopenedMessage"]; ReopenReason = ""; await ReloadAsync(companyId); });
    private async Task RequestExportAsync() => await ActAsync(async companyId => { await FinanceApiClient.RequestAccountingExportAsync(companyId, SelectedPeriodId, $"accountant-export:{companyId:N}:{SelectedPeriodId:N}:{DateTime.UtcNow:yyyyMMddHHmm}"); ActionMessage = FinanceText["ExportQueuedMessage"]; await RefreshExportsCoreAsync(companyId); });
    private async Task RefreshExportsAsync() => await ActAsync(RefreshExportsCoreAsync);
    private async Task RefreshExportsCoreAsync(Guid companyId) => Exports = await FinanceApiClient.GetAccountingExportsAsync(companyId, SelectedPeriodId);
    private async Task ValidateCloseCoreAsync(Guid companyId) => CloseValidation = await FinanceApiClient.ValidateAccountingPeriodCloseAsync(companyId, SelectedPeriodId);
    private async Task ReloadAsync(Guid companyId) { var years = await FinanceApiClient.GetAccountingFiscalYearsAsync(companyId); Periods = years.SelectMany(x => x.Periods).OrderByDescending(x => x.StartDate).ToList(); await LoadReportsAsync(companyId); CloseValidation = await FinanceApiClient.ValidateAccountingPeriodCloseAsync(companyId, SelectedPeriodId); }
    private async Task ActAsync(Func<Guid, Task> action) { if (AccessState.CompanyId is not Guid companyId) return; IsActing = true; ActionError = null; ActionMessage = null; try { await action(companyId); } catch (FinanceApiException exception) { ActionError = exception.Message; } finally { IsActing = false; } }
    private string ExportDownloadUrl(Guid id) => $"internal/companies/{AccessState.CompanyId}/finance/accounting/exports/{id:D}/download";
    private string Friendly(string value)
    {
        var key = $"Value_{value.Replace('-', '_').Replace('.', '_')}";
        var localized = FinanceText[key];
        return localized.ResourceNotFound
            ? string.Join(' ', value.Replace('-', '_').Split('_', StringSplitOptions.RemoveEmptyEntries).Select((x, i) => i == 0 ? char.ToUpperInvariant(x[0]) + x[1..] : x))
            : localized.Value;
    }
    private static string Money(decimal? amount, string? currency) => amount.HasValue ? $"{amount.Value:N2} {(string.IsNullOrWhiteSpace(currency) ? "" : currency)}".Trim() : "—";
}
