using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Components.Finance;

public partial class AccountingSchedulesWorkspace : ComponentBase
{
    [Parameter] public Guid? CompanyId { get; set; }
    [Parameter] public bool CanManage { get; set; }
    [Parameter] public bool CanApprove { get; set; }
    private AccountingScheduleListResponse? List { get; set; }
    private AccountingScheduleResponse? Selected { get; set; }
    private AccountingSchedulePreviewResponse? Preview { get; set; }
    private IReadOnlyList<AccountingAccountListItemResponse> Accounts { get; set; } = [];
    private ManualJournalReferenceDataResponse? Reference { get; set; }
    private ScheduleDraft Draft { get; set; } = ScheduleDraft.Create();
    private bool IsLoading { get; set; }
    private bool IsActing { get; set; }
    private bool IsEditing { get; set; }
    private string DetailView { get; set; } = "overview";
    private string? Message { get; set; }
    private string? Error { get; set; }
    private string? CreateIdentity { get; set; }
    private string? SubmitIdentity { get; set; }
    private Guid? ApprovalDecisionIdentity { get; set; }
    private Guid? EditingScheduleId { get; set; }
    private Guid? LoadedCompanyId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (CompanyId is not Guid companyId || LoadedCompanyId == companyId) return;
        LoadedCompanyId = companyId;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (CompanyId is not Guid companyId) return;
        IsLoading = true; Error = null;
        try
        {
            var list = FinanceApiClient.ListAccountingSchedulesAsync(companyId);
            var accounts = FinanceApiClient.GetAccountingAccountsAsync(companyId, status: "active");
            var reference = FinanceApiClient.GetManualJournalReferenceDataAsync(companyId);
            await Task.WhenAll(list, accounts, reference);
            List = await list; Accounts = (await accounts).Where(x => x.IsPostingEnabled).ToArray(); Reference = await reference;
            if (Selected is not null) Selected = List?.Items.FirstOrDefault(x => x.Id == Selected.Id);
            Selected ??= List?.Items.FirstOrDefault();
        }
        catch (FinanceApiException exception) { Error = exception.Message; }
        finally { IsLoading = false; }
    }

    private void BeginCreate()
    {
        Draft = ScheduleDraft.Create();
        Draft.VoucherSeriesCode = Reference?.VoucherSeries.FirstOrDefault()?.Code ?? "A";
        Draft.DebitAccountId = Accounts.FirstOrDefault()?.Id ?? Guid.Empty;
        Draft.CreditAccountId = Accounts.Skip(1).FirstOrDefault()?.Id ?? Guid.Empty;
        IsEditing = true; Message = null; Error = null; CreateIdentity = null;
        EditingScheduleId = null;
    }
    private void BeginEdit()
    {
        if (Selected?.CurrentVersion?.Lines.OrderBy(x => x.Sequence).ToArray() is not { Length: 2 } lines) return;
        var debit = lines.SingleOrDefault(x => x.DebitAmount > 0); var credit = lines.SingleOrDefault(x => x.CreditAmount > 0);
        if (debit is null || credit is null) return;
        Draft = new()
        {
            Code = Selected.Code, Name = Selected.Name, ScheduleType = Selected.ScheduleType, Cadence = Selected.Cadence,
            AmountBasis = Selected.AmountBasis, ProrationRule = Selected.ProrationRule, StartDate = Selected.StartDate,
            EndDate = Selected.EndDate, OccurrenceDay = Selected.OccurrenceDay, VoucherSeriesCode = Selected.VoucherSeriesCode,
            ReversalRule = Selected.ReversalRule, Description = Selected.CurrentVersion.Description, Amount = debit.DebitAmount,
            DebitAccountId = debit.FinanceAccountId, CreditAccountId = credit.FinanceAccountId, Currency = Selected.Currency
        };
        Draft.DebitDimensionIds.UnionWith(debit.DimensionMemberIds); Draft.CreditDimensionIds.UnionWith(credit.DimensionMemberIds);
        Draft.EvidenceIds.UnionWith(Selected.CurrentVersion.Evidence.Select(x => x.DocumentId));
        EditingScheduleId = Selected.Id; CreateIdentity = null; IsEditing = true; Message = null; Error = null;
    }
    private void CancelCreate() => IsEditing = false;
    private void ToggleEvidence(Guid id, bool selected) { if (selected) Draft.EvidenceIds.Add(id); else Draft.EvidenceIds.Remove(id); }

    private async Task CreateAsync()
    {
        if (CompanyId is not Guid companyId || !CanManage) return;
        if (string.IsNullOrWhiteSpace(Draft.Code) || string.IsNullOrWhiteSpace(Draft.Name) || Draft.Amount <= 0 ||
            Draft.DebitAccountId == Guid.Empty || Draft.CreditAccountId == Guid.Empty || Draft.DebitAccountId == Draft.CreditAccountId)
        { Error = FinanceText["ScheduleValidationHelp"]; return; }
        await ActAsync(async () =>
        {
            CreateIdentity ??= EditingScheduleId is Guid editId
                ? $"schedule-update:{companyId:N}:{editId:N}:{Selected?.Version ?? 0}:{Guid.NewGuid():N}"
                : $"schedule-create:{companyId:N}:{Guid.NewGuid():N}";
            if (Draft.ScheduleType is "date_allocation" or "prepayment") Draft.AmountBasis = "total_schedule";
            if (Draft.ScheduleType == "accrual" && Draft.ReversalRule == "none") Draft.ReversalRule = "next_period_start";
            var request = new SaveAccountingScheduleApiRequest
            {
                Code = Draft.Code, Name = Draft.Name, ScheduleType = Draft.ScheduleType, Cadence = Draft.Cadence,
                AmountBasis = Draft.AmountBasis, ProrationRule = Draft.ProrationRule, StartDate = Draft.StartDate,
                EndDate = Draft.EndDate, OccurrenceDay = Draft.OccurrenceDay, TimeZoneId = "Europe/Stockholm",
                VoucherSeriesCode = Draft.VoucherSeriesCode, Currency = Draft.Currency, ReversalRule = Draft.ReversalRule,
                Description = Draft.Description, EvidenceDocumentIds = Draft.EvidenceIds.ToList(), IdempotencyKey = CreateIdentity,
                ExpectedVersion = EditingScheduleId.HasValue ? Selected?.Version ?? 0 : 0,
                Lines =
                [
                    new() { FinanceAccountId = Draft.DebitAccountId, DebitAmount = Draft.Amount, Description = Draft.Description, DimensionMemberIds = Draft.DebitDimensionIds.ToList() },
                    new() { FinanceAccountId = Draft.CreditAccountId, CreditAmount = Draft.Amount, Description = Draft.Description, DimensionMemberIds = Draft.CreditDimensionIds.ToList() }
                ]
            };
            Selected = EditingScheduleId is Guid scheduleId
                ? await FinanceApiClient.UpdateAccountingScheduleAsync(companyId, scheduleId, request)
                : await FinanceApiClient.CreateAccountingScheduleAsync(companyId, request);
            CreateIdentity = null; EditingScheduleId = null; IsEditing = false; Message = FinanceText["ScheduleSaved"];
            await LoadAsync();
        });
    }

    private async Task SelectAsync(Guid id)
    {
        if (CompanyId is not Guid companyId) return;
        try { Selected = await FinanceApiClient.GetAccountingScheduleAsync(companyId, id); Preview = null; DetailView = "overview"; Error = null; }
        catch (FinanceApiException exception) { Error = exception.Message; }
    }
    private Task SelectOnKeyAsync(string key, Guid id) => key is "Enter" or " " ? SelectAsync(id) : Task.CompletedTask;

    private Task PreviewAsync() => ActSelectedAsync(async (companyId, schedule) =>
        Preview = await FinanceApiClient.PreviewAccountingScheduleAsync(companyId, schedule.Id, schedule.Version));
    private Task SubmitAsync() => ActSelectedAsync(async (companyId, schedule) =>
    {
        SubmitIdentity ??= $"schedule-submit:{schedule.Id:N}:{schedule.Version}:{Guid.NewGuid():N}";
        Selected = await FinanceApiClient.SubmitAccountingScheduleAsync(companyId, schedule.Id, schedule.Version, SubmitIdentity);
        SubmitIdentity = null; Message = FinanceText["ScheduleSubmitted"];
    });
    private Task DecideAsync(bool approve) => ActSelectedAsync(async (companyId, schedule) =>
    {
        ApprovalDecisionIdentity ??= Guid.NewGuid();
        Selected = await FinanceApiClient.DecideAccountingScheduleApprovalAsync(companyId, schedule.Id, schedule.Version, approve,
            approve ? FinanceText["ScheduleApprovalComment"] : FinanceText["ScheduleRejectionComment"], ApprovalDecisionIdentity.Value);
        ApprovalDecisionIdentity = null;
        Message = FinanceText[approve ? "ScheduleApproved" : "ScheduleRejected"];
    });
    private Task ActivateAsync() => ActSelectedAsync(async (companyId, schedule) =>
    { Selected = await FinanceApiClient.ActivateAccountingScheduleAsync(companyId, schedule.Id, schedule.Version); Message = FinanceText["ScheduleActivated"]; });
    private Task ChangeStateAsync(string action) => ActSelectedAsync(async (companyId, schedule) =>
    { Selected = await FinanceApiClient.ChangeAccountingScheduleStateAsync(companyId, schedule.Id, action, schedule.Version); Message = FinanceText[$"ScheduleState_{action}"]; });
    private Task RegenerateAsync(AccountingScheduleOccurrenceResponse occurrence) => ActSelectedAsync(async (companyId, schedule) =>
    { Selected = await FinanceApiClient.RegenerateAccountingScheduleOccurrenceAsync(companyId, schedule.Id, occurrence.Id, occurrence.Version); Message = FinanceText["OccurrenceRegenerated"]; });

    private async Task ActSelectedAsync(Func<Guid, AccountingScheduleResponse, Task> action)
    {
        if (CompanyId is not Guid companyId || Selected is null) return;
        await ActAsync(async () => { await action(companyId, Selected); await RefreshListAsync(companyId); });
    }
    private async Task RefreshListAsync(Guid companyId)
    {
        var selectedId = Selected?.Id; List = await FinanceApiClient.ListAccountingSchedulesAsync(companyId);
        if (selectedId is Guid id) Selected = await FinanceApiClient.GetAccountingScheduleAsync(companyId, id);
    }
    private async Task ActAsync(Func<Task> action)
    {
        IsActing = true; Error = null; Message = null;
        try { await action(); }
        catch (Exception exception) when (exception is FinanceApiException or InvalidOperationException) { Error = exception.Message; }
        finally { IsActing = false; }
    }
    private string JournalUrl(Guid id) => FinanceRoutes.WithCompanyContext($"{FinanceRoutes.AccountingJournal}?entryId={id:D}", CompanyId);
    private static string Money(decimal? amount, string? currency) => amount.HasValue ? $"{amount.Value:N2} {currency}".Trim() : "—";
    private static string Friendly(string value) => string.Join(' ', value.Replace('-', '_').Split('_', StringSplitOptions.RemoveEmptyEntries)
        .Select((part, index) => index == 0 ? char.ToUpperInvariant(part[0]) + part[1..] : part));

    private sealed class ScheduleDraft
    {
        public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
        public string ScheduleType { get; set; } = "recurring_fixed"; public string Cadence { get; set; } = "monthly";
        public string AmountBasis { get; set; } = "per_occurrence"; public string ProrationRule { get; set; } = "none";
        public DateOnly StartDate { get; set; } public DateOnly? EndDate { get; set; } public int OccurrenceDay { get; set; } = 1;
        public string VoucherSeriesCode { get; set; } = "A"; public string Currency { get; set; } = "SEK"; public string ReversalRule { get; set; } = "none";
        public string Description { get; set; } = string.Empty; public decimal Amount { get; set; } = 1000m;
        public Guid DebitAccountId { get; set; } public Guid CreditAccountId { get; set; } public HashSet<Guid> EvidenceIds { get; } = [];
        public HashSet<Guid> DebitDimensionIds { get; } = []; public HashSet<Guid> CreditDimensionIds { get; } = [];
        public static ScheduleDraft Create() { var today = DateOnly.FromDateTime(DateTime.Today); return new() { StartDate = today, EndDate = today.AddMonths(11), OccurrenceDay = Math.Min(today.Day, 28) }; }
    }
}
