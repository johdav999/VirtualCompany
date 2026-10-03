using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingJournalsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "journalId")] public Guid? JournalId { get; set; }
    [SupplyParameterFromQuery(Name = "periodId")] public Guid? RequestedPeriodId { get; set; }
    private IReadOnlyList<AccountingJournalResponse> Journals { get; set; } = [];
    private IReadOnlyList<ManualJournalDraftResponse> Drafts { get; set; } = [];
    private AccountingJournalResponse? Selected { get; set; }
    private DateOnly? From { get; set; }
    private DateOnly? To { get; set; }
    private string Search { get; set; } = string.Empty;
    private string PostingType { get; set; } = string.Empty;
    private int TotalCount { get; set; }
    private bool IsListLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsSaving { get; set; }
    private bool ShowReversal { get; set; }
    private string ReversalReason { get; set; } = string.Empty;
    private string? ListError { get; set; }
    private string? ActionError { get; set; }
    private string? ActionMessage { get; set; }
    private Guid? LoadedCompany;
    private int readVersion;
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (LoadedCompany != AccessState.CompanyId) { ++readVersion; Journals = []; Drafts = []; Selected = null; LoadedCompany = AccessState.CompanyId; }
        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId) await LoadAsync(companyId, true);
    }

    private async Task LoadAsync(Guid companyId, bool selectFirst)
    {
        var read = ++readVersion; var location = Navigation.Uri;
        IsListLoading = true; ListError = null;
        if (selectFirst) Selected = null;
        try
        {
            if (RequestedPeriodId is Guid periodId)
            {
                var period = await FinanceApiClient.GetAccountingPeriodAsync(companyId, periodId);
                if (read != readVersion || location != Navigation.Uri) return;
                if (period is null) throw new InvalidOperationException("The requested period is unavailable in this company.");
                From = period.StartDate; To = period.EndDate;
            }
            var journalsTask = FinanceApiClient.ListAccountingJournalsAsync(companyId, From, To, 0, 100, Search, null, PostingType);
            var draftsTask = FinanceApiClient.ListManualJournalDraftsAsync(companyId, null, 0, 50);
            await Task.WhenAll(journalsTask, draftsTask);
            if (read != readVersion || location != Navigation.Uri) return;
            var result = await journalsTask;
            Journals = result?.Items ?? []; TotalCount = result?.TotalCount ?? 0;
            Drafts = (await draftsTask)?.Items.Where(item => item.Status != "posted" && item.Status != "discarded").ToArray() ?? [];
            if (JournalId.HasValue && Selected?.Id != JournalId.Value)
                await SelectJournalAsync(JournalId.Value);
            else if (selectFirst && Selected is null && Journals.FirstOrDefault() is { } first)
                await SelectJournalAsync(first.Id);
        }
        catch (Exception ex) when (ex is FinanceApiException or InvalidOperationException or HttpRequestException)
        { if (read == readVersion) { ListError = ex.Message; Journals = []; Drafts = []; Selected = null; } }
        finally { if (read == readVersion) IsListLoading = false; }
    }

    private async Task SelectJournalAsync(Guid id)
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        IsDetailLoading = true; ActionError = null; ShowReversal = false;
        var read = readVersion; var location = Navigation.Uri; Selected = null;
        try
        {
            var journal = await FinanceApiClient.GetAccountingJournalAsync(companyId, id);
            if (read != readVersion || location != Navigation.Uri) return;
            Selected = journal;
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsDetailLoading = false; }
    }

    private Task HandleRowKeyAsync(KeyboardEventArgs args, Guid id) => args.Key is "Enter" or " " ? SelectJournalAsync(id) : Task.CompletedTask;
    private Task ReloadAsync() => AccessState.CompanyId is Guid id ? LoadAsync(id, Selected is null) : Task.CompletedTask;
    private async Task ClearFiltersAsync() { Search = PostingType = string.Empty; From = To = null; await ReloadAsync(); }
    private void ToggleReversal() { ShowReversal = !ShowReversal; ReversalReason = string.Empty; }

    private async Task ReverseAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId || Selected is null || string.IsNullOrWhiteSpace(ReversalReason)) return;
        IsSaving = true; ActionError = null;
        try
        {
            var result = await FinanceApiClient.ReverseAccountingJournalAsync(companyId, Selected.Id, new()
            {
                FiscalPeriodId = Selected.FiscalPeriodId, VoucherSeriesCode = Selected.VoucherSeriesCode,
                PostingDate = DateOnly.FromDateTime(DateTime.Today), Reason = ReversalReason,
                SourceVersion = Selected.SourceVersion ?? "1", IdempotencyKey = $"journal-reversal:{Selected.Id:N}:{Guid.NewGuid():N}"
            });
            ActionMessage = FinanceText["JournalReversed"]; ShowReversal = false;
            await LoadAsync(companyId, false); await SelectJournalAsync(result.Journal.Id);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSaving = false; }
    }

    private string AdjustmentPath(Guid id) => BuildAccountingPath($"{FinanceRoutes.AccountingManualJournal}?originalJournalId={id:D}");
    private string? SourceInvoicePath(AccountingJournalResponse journal) =>
        string.Equals(journal.SourceType, "customer_invoice", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(journal.SourceId, out var invoiceId)
            ? BuildAccountingPath(FinanceRoutes.BuildInvoiceDetailPath(invoiceId, AccessState.CompanyId))
            : null;
    private string Amount(decimal value) => value == 0 ? "—" : LocalMoney.Format(value, Selected?.BaseCurrency ?? "USD");
    private string StatusText(string? status) => FinanceText[AccountingPresentation.JournalStatusResourceKey(status)];
    private string StatusClass(string? status) => AccountingPresentation.JournalStatusClass(status);
    private string EntryTypeText(string? type) => FinanceText[AccountingPresentation.EntryTypeResourceKey(type)];
    private string SourceText(string? source) => FinanceText[AccountingPresentation.JournalSourceResourceKey(source)];
    private string ActionText(string action) => AccountingPresentation.Humanize(action);
}
