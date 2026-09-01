using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class ManualJournalWorkbenchPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Parameter] public Guid? DraftId { get; set; }
    [SupplyParameterFromQuery] public Guid? OriginalJournalId { get; set; }
    private ManualJournalDraftResponse? Draft { get; set; }
    private AccountingJournalResponse? OriginalJournal { get; set; }
    private ManualJournalReferenceDataResponse ReferenceData { get; set; } = new();
    private IReadOnlyList<AccountingAccountListItemResponse> Accounts { get; set; } = [];
    private IReadOnlyList<AccountingPeriodResponse> Periods { get; set; } = [];
    private ManualJournalModel Model { get; set; } = ManualJournalModel.CreateDefault();
    private ManualJournalPreviewResponse? Preview { get; set; }
    private Guid SelectedEvidenceId { get; set; }
    private Guid? LoadedDraftId { get; set; }
    private bool ReferencesLoaded { get; set; }
    private bool IsSaving { get; set; }
    private bool HasVersionConflict { get; set; }
    private string? ActionError { get; set; }
    private string? ActionMessage { get; set; }
    private string SaveKey { get; set; } = NewKey("save");
    private string SubmitKey { get; set; } = NewKey("submit");
    private string PostKey { get; set; } = NewKey("post");
    private string DiscardKey { get; set; } = NewKey("discard");
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private bool CanEdit => CanManageAccounting && (Draft is null || Draft.Status is "draft" or "rejected" or "approval_expired");
    private bool CanSubmit => CanEdit && Draft is not null && IsBalanced && DebitTotal > 0 && Model.Explanation.Trim().Length >= 8 && Model.EvidenceDocumentIds.Count > 0 && Model.FiscalPeriodId != Guid.Empty && !string.IsNullOrWhiteSpace(Model.VoucherSeriesCode);
    private decimal DebitTotal => Model.Lines.Sum(line => line.DebitAmount);
    private decimal CreditTotal => Model.Lines.Sum(line => line.CreditAmount);
    private decimal Difference => Math.Abs(DebitTotal - CreditTotal);
    private bool IsBalanced => Difference == 0 && Model.Lines.Count >= 2 && Model.Lines.All(line => line.FinanceAccountId != Guid.Empty && (line.DebitAmount == 0) != (line.CreditAmount == 0));
    private string StatusClass => AccountingPresentation.JournalStatusClass(Draft?.Status);
    private string StatusText => FinanceText[AccountingPresentation.JournalStatusResourceKey(Draft?.Status)];
    private IEnumerable<ManualJournalEvidenceOptionResponse> AvailableEvidence => ReferenceData.EvidenceDocuments.Where(item => !Model.EvidenceDocumentIds.Contains(item.DocumentId));

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId) return;
        if (!ReferencesLoaded) await LoadReferencesAsync(companyId);
        if (DraftId.HasValue && LoadedDraftId != DraftId) await LoadDraftAsync(companyId, DraftId.Value);
        else if (!DraftId.HasValue && LoadedDraftId.HasValue) ResetNew();
        if (!DraftId.HasValue && OriginalJournalId.HasValue && OriginalJournal?.Id != OriginalJournalId) await LoadOriginalAsync(companyId, OriginalJournalId.Value);
    }

    private async Task LoadReferencesAsync(Guid companyId)
    {
        try
        {
            var accountsTask = FinanceApiClient.GetAccountingAccountsAsync(companyId, status: "active");
            var yearsTask = FinanceApiClient.GetAccountingFiscalYearsAsync(companyId);
            var referenceTask = FinanceApiClient.GetManualJournalReferenceDataAsync(companyId);
            await Task.WhenAll(accountsTask, yearsTask, referenceTask);
            Accounts = await accountsTask;
            Periods = (await yearsTask).SelectMany(year => year.Periods).Where(period => !period.IsClosed && !period.IsReportingLocked).OrderBy(period => period.StartDate).ToArray();
            ReferenceData = await referenceTask ?? new();
            Model.FiscalPeriodId = Periods.FirstOrDefault(period => period.StartDate <= Model.PostingDate && period.EndDate >= Model.PostingDate)?.Id ?? Periods.FirstOrDefault()?.Id ?? Guid.Empty;
            Model.VoucherSeriesCode = ReferenceData.VoucherSeries.FirstOrDefault()?.Code ?? string.Empty;
            Model.Currency = Accounts.FirstOrDefault()?.Currency ?? "USD";
            ReferencesLoaded = true;
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
    }

    private async Task LoadDraftAsync(Guid companyId, Guid draftId)
    {
        try
        {
            Draft = await FinanceApiClient.GetManualJournalDraftAsync(companyId, draftId);
            if (Draft is null) { ActionError = FinanceText["ManualJournalNotFound"]; return; }
            LoadedDraftId = draftId; Model = ManualJournalModel.From(Draft); Preview = null; HasVersionConflict = false;
            if (Draft.OriginalLedgerEntryId.HasValue) OriginalJournal = await FinanceApiClient.GetAccountingJournalAsync(companyId, Draft.OriginalLedgerEntryId.Value);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
    }

    private async Task LoadOriginalAsync(Guid companyId, Guid journalId)
    {
        try
        {
            OriginalJournal = await FinanceApiClient.GetAccountingJournalAsync(companyId, journalId);
            if (OriginalJournal is null) return;
            Model.OriginalLedgerEntryId = journalId; Model.CorrectionReason = string.Empty;
            Model.Currency = OriginalJournal.BaseCurrency; Model.FiscalPeriodId = Periods.FirstOrDefault()?.Id ?? OriginalJournal.FiscalPeriodId;
            Model.VoucherSeriesCode = OriginalJournal.VoucherSeriesCode;
            Model.Explanation = FinanceText["AdjustmentExplanation", OriginalJournal.EntryNumber];
            Model.Lines = OriginalJournal.Lines.Select(line => new ManualJournalLineModel { FinanceAccountId = line.FinanceAccountId, DebitAmount = line.DebitAmount, CreditAmount = line.CreditAmount, Description = line.Description, TaxCode = line.TaxFacts.GetValueOrDefault("taxCode") }).ToList();
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
    }

    private void ResetNew() { Draft = null; OriginalJournal = null; LoadedDraftId = null; Model = ManualJournalModel.CreateDefault(); Preview = null; }
    private void MarkDirty() { Preview = null; ActionMessage = null; SaveKey = NewKey("save"); SubmitKey = NewKey("submit"); }
    private void NormalizeCurrency() { Model.Currency = Model.Currency.Trim().ToUpperInvariant(); MarkDirty(); }
    private void AddLine() { Model.Lines.Add(new()); MarkDirty(); }
    private void RemoveLine(ManualJournalLineModel line) { Model.Lines.Remove(line); MarkDirty(); }
    private void AddEvidence() { if (SelectedEvidenceId != Guid.Empty && !Model.EvidenceDocumentIds.Contains(SelectedEvidenceId)) Model.EvidenceDocumentIds.Add(SelectedEvidenceId); SelectedEvidenceId = Guid.Empty; MarkDirty(); }
    private void RemoveEvidence(Guid id) { Model.EvidenceDocumentIds.Remove(id); MarkDirty(); }

    private async Task SaveAsync()
    {
        if (!CanEdit || AccessState.CompanyId is not Guid companyId) return;
        await MutateAsync(async () =>
        {
            var request = BuildRequest();
            if (Draft is null)
            {
                Draft = Model.OriginalLedgerEntryId.HasValue
                    ? await FinanceApiClient.CreateAdjustingJournalDraftAsync(companyId, Model.OriginalLedgerEntryId.Value, request)
                    : await FinanceApiClient.CreateManualJournalDraftAsync(companyId, request);
                LoadedDraftId = Draft.Id; Model = ManualJournalModel.From(Draft);
                Navigation.NavigateTo(FinanceRoutes.BuildManualJournalDraftPath(Draft.Id, companyId), replace: true);
                ActionMessage = FinanceText["DraftCreated"];
            }
            else { Draft = await FinanceApiClient.UpdateManualJournalDraftAsync(companyId, Draft.Id, request); Model = ManualJournalModel.From(Draft); ActionMessage = FinanceText["DraftSaved"]; }
            SaveKey = NewKey("save");
        });
    }

    private async Task PreviewAsync()
    {
        await SaveAsync();
        if (Draft is null || AccessState.CompanyId is not Guid companyId || ActionError is not null) return;
        await MutateAsync(async () => { Preview = await FinanceApiClient.PreviewManualJournalDraftAsync(companyId, Draft.Id, Draft.Version); ActionMessage = FinanceText["PreviewComplete"]; });
    }

    private async Task SubmitAsync()
    {
        await PreviewAsync();
        if (Draft is null || Preview?.Policy.IsAllowed != true || Preview.PostingPreview.IsValid != true || AccessState.CompanyId is not Guid companyId) return;
        await MutateAsync(async () => { var result = await FinanceApiClient.SubmitManualJournalDraftAsync(companyId, Draft.Id, Draft.Version, SubmitKey); Draft = result.Draft; Model = ManualJournalModel.From(Draft); ActionMessage = FinanceText["SubmittedForApproval"]; SubmitKey = NewKey("submit"); });
    }

    private async Task PostAsync()
    {
        if (Draft?.Status != "approved" || AccessState.CompanyId is not Guid companyId) return;
        await MutateAsync(async () => { var result = await FinanceApiClient.PostManualJournalDraftAsync(companyId, Draft.Id, Draft.Version, PostKey); Draft = result.Draft; ActionMessage = FinanceText["JournalPosted", result.Journal.EntryNumber]; PostKey = NewKey("post"); });
    }

    private async Task DiscardAsync()
    {
        if (Draft is null || AccessState.CompanyId is not Guid companyId) return;
        await MutateAsync(async () => { Draft = await FinanceApiClient.DiscardManualJournalDraftAsync(companyId, Draft.Id, Draft.Version, DiscardKey); ActionMessage = FinanceText["DraftDiscarded"]; DiscardKey = NewKey("discard"); });
    }

    private Task ReloadDraftAsync() => DraftId.HasValue && AccessState.CompanyId is Guid id ? LoadDraftAsync(id, DraftId.Value) : Task.CompletedTask;
    private async Task MutateAsync(Func<Task> action) { IsSaving = true; ActionError = null; HasVersionConflict = false; try { await action(); } catch (ManualJournalConflictApiException ex) { ActionError = ex.Message; HasVersionConflict = true; } catch (FinanceApiException ex) { ActionError = ex.Message; } finally { IsSaving = false; } }

    private SaveManualJournalDraftApiRequest BuildRequest() => new()
    {
        ExpectedVersion = Draft?.Version ?? 0, IdempotencyKey = SaveKey, FiscalPeriodId = Model.FiscalPeriodId,
        VoucherSeriesCode = Model.VoucherSeriesCode, DocumentDate = Model.DocumentDate, PostingDate = Model.PostingDate,
        Explanation = Model.Explanation, Currency = Model.Currency, EvidenceDocumentIds = [.. Model.EvidenceDocumentIds],
        OriginalLedgerEntryId = Model.OriginalLedgerEntryId, CorrectionReason = Model.CorrectionReason,
        SourceRecords = Model.SourceRecords.Select(source => new ManualJournalSourceReferenceApiRequest
            { SourceType = source.SourceType, RecordId = source.RecordId, SourceVersion = source.SourceVersion }).ToList(),
        Lines = Model.Lines.Select(line => new ManualJournalLineApiRequest { FinanceAccountId = line.FinanceAccountId,
            DebitAmount = line.DebitAmount, CreditAmount = line.CreditAmount, Description = line.Description,
            TaxFacts = string.IsNullOrWhiteSpace(line.TaxCode) ? null : new Dictionary<string, string> { ["taxCode"] = line.TaxCode.Trim() },
            DimensionFacts = ParseDimensions(Model.DimensionInput) }).ToList()
    };
    private static Dictionary<string, string>? ParseDimensions(string input) { var values = input.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Split('=', 2, StringSplitOptions.TrimEntries)).Where(parts => parts.Length == 2 && parts.All(part => part.Length > 0)).ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase); return values.Count == 0 ? null : values; }
    private static string NewKey(string action) => $"manual-journal:{action}:{Guid.NewGuid():N}";

    private sealed class ManualJournalModel
    {
        public Guid FiscalPeriodId { get; set; } public string VoucherSeriesCode { get; set; } = string.Empty;
        public DateOnly DocumentDate { get; set; } public DateOnly PostingDate { get; set; } public string Explanation { get; set; } = string.Empty;
        public string Currency { get; set; } = "USD"; public List<ManualJournalLineModel> Lines { get; set; } = [];
        public List<Guid> EvidenceDocumentIds { get; set; } = []; public Guid? OriginalLedgerEntryId { get; set; }
        public List<ManualJournalSourceReferenceResponse> SourceRecords { get; set; } = [];
        public string? CorrectionReason { get; set; } public string DimensionInput { get; set; } = string.Empty;
        public static ManualJournalModel CreateDefault() { var today = DateOnly.FromDateTime(DateTime.Today); return new() { DocumentDate = today, PostingDate = today, Lines = [new(), new()] }; }
        public static ManualJournalModel From(ManualJournalDraftResponse draft) => new() { FiscalPeriodId = draft.FiscalPeriodId, VoucherSeriesCode = draft.VoucherSeriesCode, DocumentDate = draft.DocumentDate, PostingDate = draft.PostingDate, Explanation = draft.Explanation, Currency = draft.Currency, EvidenceDocumentIds = draft.Evidence.Select(item => item.DocumentId).ToList(), SourceRecords = [.. draft.SourceRecords], OriginalLedgerEntryId = draft.OriginalLedgerEntryId, CorrectionReason = draft.CorrectionReason, Lines = draft.Lines.Select(line => new ManualJournalLineModel { FinanceAccountId = line.FinanceAccountId, DebitAmount = line.DebitAmount, CreditAmount = line.CreditAmount, Description = line.Description, TaxCode = line.TaxFacts.GetValueOrDefault("taxCode") }).ToList(), DimensionInput = draft.Lines.SelectMany(line => line.DimensionFacts).Distinct().Select(pair => $"{pair.Key}={pair.Value}").Aggregate(string.Empty, (current, value) => string.IsNullOrEmpty(current) ? value : $"{current}; {value}") };
    }
    private sealed class ManualJournalLineModel { public Guid FinanceAccountId { get; set; } public decimal DebitAmount { get; set; } public decimal CreditAmount { get; set; } public string? Description { get; set; } public string? TaxCode { get; set; } }
}
