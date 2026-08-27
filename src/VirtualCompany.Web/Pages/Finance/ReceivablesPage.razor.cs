using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class ReceivablesPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "view")] public string? RequestedView { get; set; }

    private string View => RequestedView?.ToLowerInvariant() switch { "drafts" => "drafts", "recurring" => "recurring", "operations" => "operations", _ => "collections" };
    private bool IsWorkspaceLoading { get; set; }
    private bool IsActionBusy { get; set; }
    private string? WorkspaceError { get; set; }
    private string? ActionMessage { get; set; }
    private string SearchText { get; set; } = string.Empty;
    private string QueueFilter { get; set; } = "all";
    private CustomerAgingResponse? Aging { get; set; }
    private CustomerCollectionMetricsResponse? Metrics { get; set; }
    private IReadOnlyList<CustomerCollectionCaseResponse> Cases { get; set; } = [];
    private IReadOnlyList<CustomerStatementResponse> Statements { get; set; } = [];
    private IReadOnlyList<CustomerInvoiceDraftResponse> Drafts { get; set; } = [];
    private IReadOnlyList<CustomerInvoiceScheduleResponse> Schedules { get; set; } = [];
    private NativeReceivablesReadinessResponse? Readiness { get; set; }
    private CustomerAgingItemResponse? SelectedAgingItem { get; set; }
    private CustomerInvoiceScheduleResponse? SelectedSchedule { get; set; }
    private CustomerInvoiceSchedulePreviewResponse? SchedulePreview { get; set; }
    private CustomerReminderDraftResponse? PreparedReminder { get; set; }
    private string? ActionPanel { get; set; }
    private decimal ActionAmount { get; set; }
    private string ActionReason { get; set; } = string.Empty;
    private DateOnly PromiseDueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(7));
    private bool ConfirmEndSchedule { get; set; }
    private Guid? _loadedCompany;
    private string? _loadedView;
    private string? _actionIdempotencyKey;

    private bool CanManage => FinanceAccess.CanEdit(AccessState.MembershipRole);
    private string Currency => Aging?.Currency ?? Metrics?.Currency ?? "SEK";
    private decimal AgingOverdue => (Aging?.Days1To30 ?? 0) + (Aging?.Days31To60 ?? 0) + (Aging?.Days61To90 ?? 0) + (Aging?.DaysOver90 ?? 0);
    private decimal DueThisWeek => Aging?.Items.Where(x => x.DueDate >= DateOnly.FromDateTime(DateTime.Today) && x.DueDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(7))).Sum(x => x.OpenAmount) ?? 0;
    private int PromisesDueCount => Aging?.Items.Count(x => x.PromiseDueDate.HasValue && x.PromiseDueDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(7))) ?? 0;
    private decimal PromisesDueAmount => Aging?.Items.Where(x => x.PromiseDueDate.HasValue && x.PromiseDueDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(7))).Sum(x => x.OpenAmount) ?? 0;
    private CustomerCollectionCaseResponse? SelectedCase => SelectedAgingItem is null ? null : Cases.FirstOrDefault(x => x.InvoiceId == SelectedAgingItem.InvoiceId);
    private IReadOnlyList<CustomerStatementResponse> CustomerStatements => SelectedAgingItem is null ? [] : Statements.Where(x => x.CustomerId == SelectedAgingItem.CustomerId).OrderByDescending(x => x.CreatedUtc).ToArray();
    private IReadOnlyList<CustomerAgingItemResponse> FilteredAgingItems => (Aging?.Items ?? [])
        .Where(x => string.IsNullOrWhiteSpace(SearchText) || x.CustomerName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || x.InvoiceNumber.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
        .Where(x => QueueFilter switch { "promise" => x.PromiseDueDate.HasValue, "dispute" => x.IsDisputed, "hold" => x.IsOnHold, _ => true })
        .OrderByDescending(Priority).ThenByDescending(x => x.DaysOverdue).ToArray();
    private IReadOnlyList<TimelineItem> Timeline
    {
        get
        {
            if (SelectedAgingItem is null) return [];
            var items = new List<TimelineItem>();
            if (SelectedCase is { } collectionCase)
            {
                items.Add(new(FinanceText["CollectionCaseOpened"], FriendlyStatus(collectionCase.Status), collectionCase.CreatedUtc));
                if (collectionCase.UpdatedUtc != collectionCase.CreatedUtc) items.Add(new(FinanceText["CollectionCaseUpdated"], CaseSummary(collectionCase), collectionCase.UpdatedUtc));
            }
            items.AddRange(CustomerStatements.Select(x => new TimelineItem(FinanceText["StatementCreated"], x.FileName, x.CreatedUtc)));
            return items.OrderByDescending(x => x.AtUtc).ToArray();
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId || _loadedCompany == companyId && _loadedView == View) return;
        _loadedCompany = companyId; _loadedView = View;
        await LoadAsync(companyId);
    }

    private async Task LoadAsync(Guid companyId)
    {
        IsWorkspaceLoading = true; WorkspaceError = null;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var agingTask = FinanceApiClient.GetCustomerAgingAsync(companyId, today, TimeZoneInfo.Local.Id, take: 200);
            var metricsTask = FinanceApiClient.GetCustomerCollectionMetricsAsync(companyId, today);
            var casesTask = FinanceApiClient.GetCustomerCollectionCasesAsync(companyId, take: 200);
            var statementsTask = FinanceApiClient.GetCustomerStatementsAsync(companyId, take: 200);
            var draftsTask = FinanceApiClient.GetCustomerInvoiceDraftsAsync(companyId, take: 200);
            var schedulesTask = FinanceApiClient.GetCustomerInvoiceSchedulesAsync(companyId, take: 200);
            var readinessTask = FinanceApiClient.GetNativeReceivablesReadinessAsync(companyId);
            await Task.WhenAll(agingTask, metricsTask, casesTask, statementsTask, draftsTask, schedulesTask, readinessTask);
            Aging = await agingTask; Metrics = await metricsTask; Cases = (await casesTask)?.Items ?? []; Statements = (await statementsTask)?.Items ?? [];
            Drafts = (await draftsTask)?.Items ?? []; Schedules = (await schedulesTask)?.Items ?? [];
            Readiness = await readinessTask;
            if (SelectedAgingItem is null || Aging?.Items.All(x => x.InvoiceId != SelectedAgingItem.InvoiceId) == true) SelectedAgingItem = Aging?.Items.OrderByDescending(Priority).FirstOrDefault();
            if (SelectedSchedule is null || Schedules.All(x => x.Id != SelectedSchedule.Id)) SelectedSchedule = Schedules.FirstOrDefault();
            if (View == "recurring" && SelectedSchedule is not null) await LoadSchedulePreviewAsync(companyId, SelectedSchedule.Id);
        }
        catch (FinanceApiException ex) { WorkspaceError = ex.Message; }
        finally { IsWorkspaceLoading = false; }
    }

    private async Task ReloadAsync() { if (AccessState.CompanyId is Guid companyId) await LoadAsync(companyId); }

    private string ReadinessTitle(string key) => key switch
    {
        "stale_approvals" => FinanceText["ReceivablesCheckStaleApprovals"],
        "numbering_gaps" => FinanceText["ReceivablesCheckNumberGaps"],
        "render_failures" => FinanceText["ReceivablesCheckRenderFailures"],
        "delivery_ambiguity" => FinanceText["ReceivablesCheckDeliveryAmbiguity"],
        "recurring_blockers" => FinanceText["ReceivablesCheckRecurringBlockers"],
        "electronic_invoice_rejections" => FinanceText["ReceivablesCheckElectronicRejections"],
        "refund_reconciliation" => FinanceText["ReceivablesCheckRefundReconciliation"],
        "receivables_control" => FinanceText["ReceivablesCheckControlDifference"],
        "overdue_collection_follow_ups" => FinanceText["ReceivablesCheckOverdueFollowUps"],
        "document_archive_failures" => FinanceText["ReceivablesCheckArchiveFailures"],
        _ => FinanceText["ReceivablesOperationalCheck"]
    };

    private string ReadinessStatusLabel(string status) => status switch
    {
        "blocking" => FinanceText["Blocking"],
        "attention" => FinanceText["NeedsAttention"],
        _ => FinanceText["Healthy"]
    };

    private string ReadinessValue(NativeReceivablesReadinessSignalResponse signal) => signal.Amount.HasValue
        ? Money(signal.Amount.Value, signal.Currency ?? Currency)
        : signal.Count.ToString(CultureInfo.CurrentCulture);
    private void SelectAgingItem(CustomerAgingItemResponse item) { SelectedAgingItem = item; PreparedReminder = null; ActionPanel = null; ActionMessage = null; }
    private async Task SelectScheduleAsync(CustomerInvoiceScheduleResponse schedule) { SelectedSchedule = schedule; ConfirmEndSchedule = false; if (AccessState.CompanyId is Guid companyId) await LoadSchedulePreviewAsync(companyId, schedule.Id); }
    private async Task LoadSchedulePreviewAsync(Guid companyId, Guid scheduleId) { try { SchedulePreview = await FinanceApiClient.PreviewCustomerInvoiceScheduleAsync(companyId, scheduleId, 6); } catch (FinanceApiException ex) { WorkspaceError = ex.Message; } }

    private async Task ChangeScheduleStatusAsync(string action)
    {
        if (!CanManage || SelectedSchedule is null || AccessState.CompanyId is not Guid companyId) return;
        IsActionBusy = true; WorkspaceError = null;
        try
        {
            var key = $"invoice-schedule-{action}:{companyId:N}:{SelectedSchedule.Id:N}:{SelectedSchedule.Version}:{Guid.NewGuid():N}";
            SelectedSchedule = await FinanceApiClient.ChangeCustomerInvoiceScheduleStatusAsync(companyId, SelectedSchedule.Id, action, new(SelectedSchedule.Version, key));
            ActionMessage = action switch { "pause" => FinanceText["SchedulePaused"], "resume" => FinanceText["ScheduleResumed"], _ => FinanceText["ScheduleEnded"] };
            ConfirmEndSchedule = false; await LoadAsync(companyId);
        }
        catch (FinanceApiException ex) { WorkspaceError = ex.Message; }
        finally { IsActionBusy = false; }
    }

    private async Task GenerateStatementAsync()
    {
        if (!CanManage || SelectedAgingItem is null || AccessState.CompanyId is not Guid companyId) return;
        IsActionBusy = true; WorkspaceError = null;
        try
        {
            _actionIdempotencyKey ??= $"customer-statement:{companyId:N}:{SelectedAgingItem.CustomerId:N}:{DateOnly.FromDateTime(DateTime.Today):yyyyMMdd}:{Guid.NewGuid():N}";
            var today = DateOnly.FromDateTime(DateTime.Today);
            var result = await FinanceApiClient.GenerateCustomerStatementAsync(companyId, new(SelectedAgingItem.CustomerId, today.AddMonths(-3), today, TimeZoneInfo.Local.Id, CultureInfo.CurrentUICulture.Name, SelectedAgingItem.Currency, _actionIdempotencyKey));
            _actionIdempotencyKey = null; ActionMessage = FinanceText["StatementCreatedMessage", result.FileName]; await LoadAsync(companyId);
        }
        catch (FinanceApiException ex) { WorkspaceError = ex.Message; }
        finally { IsActionBusy = false; }
    }

    private async Task PrepareReminderAsync()
    {
        if (!CanManage || SelectedAgingItem is null || AccessState.CompanyId is not Guid companyId) return;
        IsActionBusy = true; WorkspaceError = null;
        try
        {
            _actionIdempotencyKey ??= $"customer-reminder-prepare:{companyId:N}:{SelectedAgingItem.InvoiceId:N}:{Guid.NewGuid():N}";
            PreparedReminder = await FinanceApiClient.PrepareCustomerReminderAsync(companyId, SelectedAgingItem.InvoiceId, new(null, CustomerStatements.FirstOrDefault()?.Id, _actionIdempotencyKey));
            _actionIdempotencyKey = null; ActionMessage = FinanceText["ReminderPreparedMessage"];
        }
        catch (FinanceApiException ex) { WorkspaceError = ex.Message; }
        finally { IsActionBusy = false; }
    }

    private async Task QueueReminderAsync()
    {
        if (!CanManage || PreparedReminder is null || AccessState.CompanyId is not Guid companyId) return;
        IsActionBusy = true; WorkspaceError = null;
        try
        {
            var key = $"customer-reminder-send:{companyId:N}:{PreparedReminder.Id:N}:{PreparedReminder.Version}:{Guid.NewGuid():N}";
            var delivery = await FinanceApiClient.SendCustomerReminderAsync(companyId, PreparedReminder.Id, new(PreparedReminder.Version, PreparedReminder.SourceHash, key));
            ActionMessage = FinanceText["ReminderQueuedMessage", FriendlyStatus(delivery.Status)]; PreparedReminder = null; await LoadAsync(companyId);
        }
        catch (FinanceApiException ex) { WorkspaceError = ex.Message; }
        finally { IsActionBusy = false; }
    }

    private async Task RecordDisputeAsync()
    {
        if (!CanManage || SelectedAgingItem is null || AccessState.CompanyId is not Guid companyId || ActionAmount <= 0 || string.IsNullOrWhiteSpace(ActionReason)) return;
        IsActionBusy = true; WorkspaceError = null;
        try
        {
            var key = $"customer-dispute:{companyId:N}:{SelectedAgingItem.InvoiceId:N}:{Guid.NewGuid():N}";
            await FinanceApiClient.RecordCustomerDisputeAsync(companyId, SelectedAgingItem.InvoiceId, new(ActionAmount, ActionReason.Trim(), null, DateTime.UtcNow.AddDays(7), SelectedCase?.Version, key));
            ActionMessage = FinanceText["DisputeRecorded"]; CloseActionPanel(); await LoadAsync(companyId);
        }
        catch (FinanceApiException ex) { WorkspaceError = ex.Message; }
        finally { IsActionBusy = false; }
    }

    private async Task RecordPromiseAsync()
    {
        if (!CanManage || SelectedAgingItem is null || AccessState.CompanyId is not Guid companyId || ActionAmount <= 0) return;
        IsActionBusy = true; WorkspaceError = null;
        try
        {
            var key = $"customer-promise:{companyId:N}:{SelectedAgingItem.InvoiceId:N}:{Guid.NewGuid():N}";
            await FinanceApiClient.RecordCustomerPromiseAsync(companyId, SelectedAgingItem.InvoiceId, new(ActionAmount, PromiseDueDate, null, PromiseDueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), SelectedCase?.Version, key));
            ActionMessage = FinanceText["PromiseRecorded"]; CloseActionPanel(); await LoadAsync(companyId);
        }
        catch (FinanceApiException ex) { WorkspaceError = ex.Message; }
        finally { IsActionBusy = false; }
    }

    private void CloseActionPanel() { ActionPanel = null; ActionAmount = 0; ActionReason = string.Empty; }
    private static int Priority(CustomerAgingItemResponse x) => (x.IsDisputed ? 10000 : 0) + (x.IsOnHold ? 8000 : 0) + (x.PromiseDueDate.HasValue ? 6000 : 0) + Math.Max(0, x.DaysOverdue);
    private static string AgingTone(CustomerAgingItemResponse item) => item.DaysOverdue > 60 ? "row-icon--danger" : item.DaysOverdue > 30 ? "row-icon--warning" : string.Empty;
    private string CollectionStatusLabel(CustomerAgingItemResponse item) => item.IsDisputed ? FinanceText["Disputed"] : item.IsOnHold ? FinanceText["OnHold"] : item.PromiseDueDate.HasValue ? FinanceText["PromiseDue"] : item.ReminderStage > 0 ? FinanceText["ReminderInProgress"] : FinanceText["NeedsFollowUp"];
    private static string CollectionStatusTone(CustomerAgingItemResponse item) => item.IsDisputed || item.IsOnHold ? "status-pill--danger" : item.PromiseDueDate.HasValue || item.DaysOverdue > 30 ? "status-pill--warning" : "status-pill--neutral";
    private string NextActionLabel(CustomerAgingItemResponse item) => string.IsNullOrWhiteSpace(item.RecommendedAction) ? FinanceText["ReviewNextAction"] : item.RecommendedAction;
    private string AgeLabel(CustomerAgingItemResponse item) => item.DaysOverdue > 0 ? FinanceText["DaysOverdue", item.DaysOverdue] : FinanceText["NotYetDue"];
    private static string FriendlyStatus(string? value) => string.IsNullOrWhiteSpace(value) ? "Not set" : string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant() is { Length: > 0 } label ? char.ToUpperInvariant(label[0]) + label[1..] : "Not set";
    private string FriendlyOptionalStatus(string? value) => string.IsNullOrWhiteSpace(value) ? FinanceText["None"] : FriendlyStatus(value);
    private static string StatusTone(string? status) => status?.ToLowerInvariant() switch { "active" or "approved" or "completed" or "kept" => "status-pill--success", "blocked" or "failed" or "rejected" or "ended" => "status-pill--danger", "paused" or "pending" or "submitted" or "awaiting_approval" => "status-pill--warning", _ => "status-pill--neutral" };
    private string FriendlyCadence(string value) => value switch { "monthly" => FinanceText["Monthly"], "weekly" => FinanceText["Weekly"], "quarterly" => FinanceText["Quarterly"], "yearly" => FinanceText["Yearly"], _ => FriendlyStatus(value) };
    private string FriendlyDelivery(string value) => value switch { "email" => FinanceText["EmailWithPdf"], "electronic" or "e_invoice" => FinanceText["ElectronicInvoice"], _ => FriendlyStatus(value) };
    private static string FormatDateTime(DateTime value) => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    private string FormatOptionalDate(DateTime? value) => value.HasValue ? FormatDateTime(value.Value) : FinanceText["NotScheduled"];
    private static string Money(decimal amount, string currency) => $"{currency} {amount:N2}";
    private static string FriendlyCitation(string value) => value.Replace('_', ' ').Replace(":", " · ", StringComparison.Ordinal);
    private string CaseSummary(CustomerCollectionCaseResponse value) => value.IsOnHold ? FinanceText["CollectionOnHold"] : !string.IsNullOrWhiteSpace(value.DisputeStatus) ? FinanceText["DisputeStatusLabel", FriendlyStatus(value.DisputeStatus)] : !string.IsNullOrWhiteSpace(value.PromiseStatus) ? FinanceText["PromiseStatusLabel", FriendlyStatus(value.PromiseStatus)] : FriendlyStatus(value.Status);
    private string BuildApprovalHref(Guid approvalId) => $"/approvals?companyId={AccessState.CompanyId:D}&approvalId={approvalId:D}";
    private sealed record TimelineItem(string Title, string Detail, DateTime AtUtc);
}
