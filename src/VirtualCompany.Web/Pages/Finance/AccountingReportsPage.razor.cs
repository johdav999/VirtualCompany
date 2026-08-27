using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VirtualCompany.Shared;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingReportsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "view")] public string? RequestedView { get; set; }
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
    private IReadOnlyList<VatFilingPeriodResponse> VatFilingPeriods { get; set; } = [];
    private IReadOnlyList<VatReturnResponse> VatReturns { get; set; } = [];
    private Guid? SelectedVatFilingPeriodId { get; set; }
    private VatReturnResponse? CurrentVatReturn => VatReturns
        .Where(x => x.FilingPeriodId == SelectedVatFilingPeriodId && !x.IsSuperseded)
        .OrderByDescending(x => x.Version)
        .FirstOrDefault();
    private Guid? SelectedAccountId { get; set; }
    private GeneralLedgerAccountResponse? SelectedLedgerAccount { get; set; }
    private string View { get; set; } = "trial";
    private bool IsReportLoading { get; set; }
    private bool IsActing { get; set; }
    private string CloseReason { get; set; } = string.Empty;
    private string ReopenReason { get; set; } = "";
    private string? ActionMessage { get; set; }
    private string? ActionError { get; set; }
    private string? VatError { get; set; }
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private bool CanReopen => AccessState.MembershipRole is "owner" or "admin";
    private string? Currency => TrialBalance?.Accounts.Select(x => x.Currency).Distinct().Count() == 1 ? TrialBalance.Accounts.FirstOrDefault()?.Currency : null;
    private string CloseSummary => CloseValidation is null ? FinanceText["NotReviewedYet"] : CloseValidation.IsReadyToClose ? FinanceText["ReadyToClose"] : FinanceText["CloseIssueTypes", CloseValidation.BlockingIssues.Count];
    private string LauraAdvice => CloseValidation?.IsReadyToClose == true ? FinanceText["LauraCloseReadyAdvice"] : CloseValidation is null ? FinanceText["LauraCloseReviewAdvice"] : FinanceText["LauraCloseBlockedAdvice", CloseValidation.BlockingIssues.Count];

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (string.Equals(RequestedView, "vat", StringComparison.OrdinalIgnoreCase)) View = "vat";
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
            var vatPeriods = FinanceApiClient.GetVatFilingPeriodsAsync(companyId);
            await Task.WhenAll(trial, ledger, profit, balance, tax, control, history, exports, vatPeriods);
            TrialBalance = await trial; GeneralLedger = await ledger; ProfitAndLoss = await profit; BalanceSheet = await balance;
            TaxSummary = await tax; ControlAccounts = await control; History = await history; Exports = await exports;
            VatFilingPeriods = await vatPeriods;
            SelectedVatFilingPeriodId = VatFilingPeriods.FirstOrDefault(x => x.FiscalPeriodId == SelectedPeriodId)?.Id
                ?? VatFilingPeriods.FirstOrDefault()?.Id;
            await LoadVatReturnsAsync(companyId);
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
    private async Task ChangeVatPeriodAsync(Guid filingPeriodId) => await ActAsync(async companyId => { SelectedVatFilingPeriodId = filingPeriodId; await LoadVatReturnsAsync(companyId); });
    private async Task CalculateVatAsync() => await ActAsync(async companyId =>
    {
        var filingPeriodId = await EnsureVatFilingPeriodAsync(companyId);
        var current = CurrentVatReturn;
        await FinanceApiClient.CalculateVatReturnAsync(companyId, filingPeriodId, current?.Id,
            $"vat-calculate:{companyId:N}:{filingPeriodId:N}:{current?.Version ?? 0}:{DateTime.UtcNow:yyyyMMddHHmm}");
        ActionMessage = FinanceText["VatCalculatedMessage"];
        await LoadVatReturnsAsync(companyId);
    });
    private async Task RequestVatApprovalAsync() => await ActAsync(async companyId =>
    {
        if (CurrentVatReturn?.InputHash is not { Length: > 0 } inputHash) return;
        await FinanceApiClient.RequestVatReturnApprovalAsync(companyId, CurrentVatReturn.Id, inputHash);
        ActionMessage = FinanceText["VatApprovalRequestedMessage"];
        await LoadVatReturnsAsync(companyId);
    });
    private async Task FinalizeVatAsync() => await ActAsync(async companyId =>
    {
        if (CurrentVatReturn?.InputHash is not { Length: > 0 } inputHash) return;
        await FinanceApiClient.FinalizeVatReturnAsync(companyId, CurrentVatReturn.Id, inputHash);
        ActionMessage = FinanceText["VatFinalizedMessage"];
        await LoadVatReturnsAsync(companyId);
    });
    private async Task CreateVatCorrectionAsync(VatReturnCorrectionDraft draft) => await ActAsync(async companyId =>
    {
        if (CurrentVatReturn is null) return;
        await FinanceApiClient.CreateVatReturnCorrectionAsync(companyId, CurrentVatReturn.Id, draft.Reason,
            draft.EvidenceReference, $"vat-correction:{CurrentVatReturn.Id:N}:{CurrentVatReturn.Version + 1}:{Guid.NewGuid():N}");
        ActionMessage = FinanceText["VatCorrectionCreatedMessage"];
        await LoadVatReturnsAsync(companyId);
    });
    private async Task RequestStatutoryExportAsync(string exportType) => await ActAsync(async companyId =>
    {
        await FinanceApiClient.RequestAccountingExportAsync(companyId, SelectedPeriodId,
            $"statutory-export:{companyId:N}:{SelectedPeriodId:N}:{exportType}:{Guid.NewGuid():N}", exportType);
        ActionMessage = FinanceText["ExportQueuedMessage"];
        await RefreshExportsCoreAsync(companyId);
    });
    private Task RetryStatutoryExportAsync(AccountingExportJobResponse job) => RequestStatutoryExportAsync(job.ExportType);
    private async Task RefreshExportsAsync() => await ActAsync(RefreshExportsCoreAsync);
    private async Task RefreshExportsCoreAsync(Guid companyId) => Exports = await FinanceApiClient.GetAccountingExportsAsync(companyId, SelectedPeriodId);
    private async Task LoadVatReturnsAsync(Guid companyId)
    {
        VatError = null;
        try { VatReturns = SelectedVatFilingPeriodId is Guid filingPeriodId ? await FinanceApiClient.GetVatReturnsAsync(companyId, filingPeriodId) : []; }
        catch (FinanceApiException exception) { VatReturns = []; VatError = exception.Message; }
    }
    private async Task<Guid> EnsureVatFilingPeriodAsync(Guid companyId)
    {
        if (SelectedVatFilingPeriodId is Guid existing) return existing;
        var period = SelectedPeriod ?? throw new InvalidOperationException(FinanceText["VatFiscalPeriodRequired"]);
        var created = await FinanceApiClient.CreateVatFilingPeriodAsync(companyId, period.Name, period.StartDate, period.EndDate, period.Id);
        VatFilingPeriods = [.. VatFilingPeriods, created];
        SelectedVatFilingPeriodId = created.Id;
        return created.Id;
    }
    private async Task ValidateCloseCoreAsync(Guid companyId) => CloseValidation = await FinanceApiClient.ValidateAccountingPeriodCloseAsync(companyId, SelectedPeriodId);
    private async Task ReloadAsync(Guid companyId) { var years = await FinanceApiClient.GetAccountingFiscalYearsAsync(companyId); Periods = years.SelectMany(x => x.Periods).OrderByDescending(x => x.StartDate).ToList(); await LoadReportsAsync(companyId); CloseValidation = await FinanceApiClient.ValidateAccountingPeriodCloseAsync(companyId, SelectedPeriodId); }
    private async Task ActAsync(Func<Guid, Task> action) { if (AccessState.CompanyId is not Guid companyId) return; IsActing = true; ActionError = null; ActionMessage = null; try { await action(companyId); } catch (Exception exception) when (exception is FinanceApiException or InvalidOperationException) { ActionError = exception.Message; } finally { IsActing = false; } }
    private string ExportDownloadUrl(Guid id) => AccessState.CompanyId is Guid companyId ? FinanceApiClient.GetAccountingExportDownloadUrl(companyId, id) : "#";
    private string VatPackageDownloadUrl => AccessState.CompanyId is Guid companyId && CurrentVatReturn is not null ? FinanceApiClient.GetVatReturnPackageDownloadUrl(companyId, CurrentVatReturn.Id) : "#";
    private string JournalEntryUrl(Guid ledgerEntryId) => FinanceRoutes.WithCompanyContext($"{FinanceRoutes.AccountingJournal}?entryId={ledgerEntryId:D}", AccessState.CompanyId);
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
