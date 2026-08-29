using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class StatementImportsPage
{
    private const long MaximumFileBytes = 20 * 1024 * 1024;
    [Inject] private FinanceApiClient FinanceClient { get; set; } = default!;
    private StatementImportWorkspaceResponse? Workspace { get; set; }
    private StatementImportJobResponse? SelectedJob { get; set; }
    private IBrowserFile? SelectedFile { get; set; }
    private Guid? SelectedBankAccountId { get; set; }
    private Guid? SelectedCsvProfileId { get; set; }
    private string? ActionMessage { get; set; }
    private string? ActionError { get; set; }
    private bool IsBusy { get; set; }
    private bool ShowProfileEditor { get; set; }
    private CsvProfileEditor Profile { get; set; } = new();
    private bool CanManage => FinanceAccess.CanManageFinanceIntegrations(AccessState.MembershipRole);
    private bool CanPreview => CanManage && !IsBusy && SelectedFile is not null && SelectedBankAccountId.HasValue;
    private bool CanCommit => CanManage && !IsBusy && SelectedJob is { ErrorRowCount: 0, AcceptedRowCount: > 0 } &&
        SelectedJob.Status is "ready_to_import" or "preview_ready" or "partially_imported" or "importing";
    private bool CanCreateProfile => CanManage && !IsBusy && !string.IsNullOrWhiteSpace(Profile.Name) &&
        !string.IsNullOrWhiteSpace(Profile.BookingDateColumn) && !string.IsNullOrWhiteSpace(Profile.ReferenceColumn) &&
        (!string.IsNullOrWhiteSpace(Profile.AmountColumn) || !string.IsNullOrWhiteSpace(Profile.DebitColumn) && !string.IsNullOrWhiteSpace(Profile.CreditColumn));
    private bool ControlTotalsMatch => SelectedJob?.OpeningBalance is not null && SelectedJob.ClosingBalance is not null &&
        SelectedJob.CalculatedClosingBalance == SelectedJob.ClosingBalance;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId)
        {
            try { Workspace = await FinanceClient.GetStatementImportWorkspaceAsync(companyId); SelectedBankAccountId ??= Workspace.Accounts.FirstOrDefault()?.Id; }
            catch (FinanceApiException exception) { ActionError = exception.Message; }
        }
    }
    private void SelectFile(InputFileChangeEventArgs args) { SelectedFile = args.File; ActionError = null; ActionMessage = null; }
    private void ResetPreview() { SelectedFile = null; SelectedJob = null; ActionError = null; ActionMessage = null; }
    private void ToggleProfileEditor() => ShowProfileEditor = !ShowProfileEditor;
    private async Task PreviewAsync()
    {
        if (!CanPreview || AccessState.CompanyId is not Guid companyId || SelectedFile is null || SelectedBankAccountId is not Guid accountId) return;
        await MutateAsync(async () =>
        {
            if (SelectedFile.Size > MaximumFileBytes) throw new FinanceApiException(FinanceText["StatementFileTooLarge", 20]);
            await using var stream = SelectedFile.OpenReadStream(MaximumFileBytes);
            var profile = Workspace?.CsvProfiles.FirstOrDefault(x => x.Id == SelectedCsvProfileId);
            SelectedJob = await FinanceClient.PreviewStatementImportAsync(companyId, accountId, SelectedFile.Name,
                SelectedFile.ContentType, SelectedFile.Size, stream, profile?.Id, profile?.Version);
            ActionMessage = SelectedJob.Status == "status_only" ? FinanceText["PaymentStatusValidated"] :
                SelectedJob.ErrorRowCount > 0 ? FinanceText["PreviewNeedsAttention"] : FinanceText["PreviewReady"];
            await ReloadWorkspaceAsync(companyId);
        });
    }
    private async Task CommitAsync()
    {
        if (!CanCommit || AccessState.CompanyId is not Guid companyId || SelectedJob is null) return;
        await MutateAsync(async () =>
        {
            for (var batch = 0; batch < 20 && SelectedJob is { Status: not "completed" }; batch++)
            {
                SelectedJob = await FinanceClient.CommitStatementImportAsync(companyId, SelectedJob.Id, SelectedJob.Version);
                if (SelectedJob.Status is not ("partially_imported" or "importing")) break;
            }
            ActionMessage = SelectedJob.Status == "completed" ? FinanceText["StatementImportCompleted"] : FinanceText["StatementImportSavedForResume"];
            await ReloadWorkspaceAsync(companyId);
        });
    }
    private async Task SkipRowAsync(StatementImportRowResponse row)
    {
        if (!CanSkip(row) || AccessState.CompanyId is not Guid companyId || SelectedJob is null) return;
        await MutateAsync(async () =>
        {
            SelectedJob = await FinanceClient.SkipStatementImportRowAsync(companyId, SelectedJob.Id, row.Id,
                SelectedJob.Version, FinanceText["StatementRowExcludedReason"]);
            ActionMessage = FinanceText["StatementRowSkipped"];
            await ReloadWorkspaceAsync(companyId);
        });
    }
    private async Task CreateProfileAsync()
    {
        if (!CanCreateProfile || AccessState.CompanyId is not Guid companyId) return;
        await MutateAsync(async () =>
        {
            var created = await FinanceClient.CreateStatementCsvProfileAsync(companyId, new(Profile.Name.Trim(),
                Profile.Delimiter, Profile.CultureName.Trim(), Profile.DateFormat.Trim(), Profile.HasHeader,
                Profile.BookingDateColumn.Trim(), Null(Profile.ValueDateColumn), Null(Profile.AmountColumn),
                Null(Profile.DebitColumn), Null(Profile.CreditColumn), Null(Profile.CurrencyColumn),
                Profile.ReferenceColumn.Trim(), Null(Profile.CounterpartyColumn), Null(Profile.ExternalReferenceColumn),
                Null(Profile.AccountIdentifierColumn), Null(Profile.DefaultCurrency)));
            await ReloadWorkspaceAsync(companyId); SelectedCsvProfileId = created.Id; ShowProfileEditor = false;
            Profile = new(); ActionMessage = FinanceText["CsvProfileSaved"];
        });
    }
    private async Task SelectJobAsync(Guid id)
    {
        if (AccessState.CompanyId is not Guid companyId || IsBusy) return;
        IsBusy = true; ActionError = null;
        try { SelectedJob = await FinanceClient.GetStatementImportJobAsync(companyId, id); }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
        finally { IsBusy = false; }
    }
    private async Task ReloadWorkspaceAsync(Guid companyId) => Workspace = await FinanceClient.GetStatementImportWorkspaceAsync(companyId);
    private async Task MutateAsync(Func<Task> action)
    {
        if (!CanManage || IsBusy) return; IsBusy = true; ActionError = null; ActionMessage = null;
        try { await action(); } catch (Exception exception) { ActionError = exception.Message; } finally { IsBusy = false; }
    }
    private bool CanSkip(StatementImportRowResponse row) => CanManage && !IsBusy && SelectedJob is not null && row.Outcome is "error" or "duplicate";
    private string FormatDate(DateTime? value) => value.HasValue ? LocalDateTime.Date(DateOnly.FromDateTime(value.Value)) : "—";
    private string FormatAmount(StatementImportRowResponse row) => row.Amount.HasValue ? LocalMoney.Format(row.Amount.Value, row.Currency ?? SelectedJob?.Currency ?? "SEK") : "—";
    private string FormatMoney(decimal? value) => value.HasValue ? LocalMoney.Format(value.Value, SelectedJob?.Currency ?? "SEK") : "—";
    private static int Progress(StatementImportJobResponse job) => job.AcceptedRowCount == 0 ? (job.Status == "completed" ? 100 : 0) : Math.Clamp((int)Math.Round(job.ImportedRowCount * 100m / job.AcceptedRowCount), 0, 100);
    private static string FormatBytes(long value) => value >= 1024 * 1024 ? $"{value / 1024d / 1024d:0.0} MB" : $"{Math.Max(1, value / 1024d):0} KB";
    private string JobStatusLabel(string value) => value switch { "ready_to_import" => FinanceText["ReadyToImport"], "preview_ready" => FinanceText["PreviewReadyStatus"], "attention_required" => FinanceText["NeedsReview"], "partially_imported" => FinanceText["PartiallyImported"], "importing" => FinanceText["InProgress"], "completed" => FinanceText["Completed"], "status_only" => FinanceText["StatusOnly"], "failed" => FinanceText["Failed"], _ => FinanceText["Pending"] };
    private static string JobStatusClass(string value) => $"statement-status statement-status--{value.Replace('_', '-')}";
    private string RowStatusLabel(string value) => value switch { "accepted" => FinanceText["Accepted"], "duplicate" => FinanceText["Duplicate"], "error" => FinanceText["Issue"], "payment_status" => FinanceText["PaymentStatus"], "imported" => FinanceText["Imported"], "skipped" => FinanceText["Skipped"], _ => value };
    private static string RowStatusClass(string value) => $"row-status row-status--{value.Replace('_', '-')}";
    private string IssueLabel(StatementImportIssueResponse issue) => issue.RowNumber.HasValue ? FinanceText["RowIssue", issue.RowNumber.Value] : FinanceText["FileIssue"];
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private sealed class CsvProfileEditor
    {
        public string Name { get; set; } = string.Empty; public string Delimiter { get; set; } = ";";
        public string CultureName { get; set; } = "sv-SE"; public string DateFormat { get; set; } = "yyyy-MM-dd";
        public bool HasHeader { get; set; } = true; public string BookingDateColumn { get; set; } = string.Empty;
        public string? ValueDateColumn { get; set; } public string? AmountColumn { get; set; }
        public string? DebitColumn { get; set; } public string? CreditColumn { get; set; }
        public string? CurrencyColumn { get; set; } public string ReferenceColumn { get; set; } = string.Empty;
        public string? CounterpartyColumn { get; set; } public string? ExternalReferenceColumn { get; set; }
        public string? AccountIdentifierColumn { get; set; } public string? DefaultCurrency { get; set; }
    }
}
