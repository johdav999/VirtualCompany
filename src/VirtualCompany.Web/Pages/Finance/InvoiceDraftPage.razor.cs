using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class InvoiceDraftPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Parameter] public Guid? DraftId { get; set; }

    private InvoiceDraftEditor Editor { get; set; } = InvoiceDraftEditor.Create();
    private CustomerInvoiceDraftResponse? Draft { get; set; }
    private CustomerInvoiceDraftReadinessResponse? Readiness { get; set; }
    private CustomerInvoiceDraftIssueResponse? IssueResult { get; set; }
    private IReadOnlyList<FinanceCounterpartyResponse> Customers { get; set; } = [];
    private IReadOnlyList<StatutoryDocumentSeriesResponse> Series { get; set; } = [];
    private IReadOnlyList<AccountingPeriodResponse> Periods { get; set; } = [];
    private Guid SelectedSeriesId { get; set; }
    private Guid SelectedPeriodId { get; set; }
    private string VoucherSeriesCode { get; set; } = "G";
    private string? ValidationMessage { get; set; }
    private string? WorkflowMessage { get; set; }
    private bool IsWorking { get; set; }
    private Guid? _loadedDraftId;
    private Guid? _loadedCompanyId;
    private string? _saveKey;
    private string? _submitKey;
    private string? _issueKey;

    private bool CanEditDraft => FinanceAccess.CanEdit(AccessState.MembershipRole) && Draft?.Status is not ("issued" or "discarded");
    private bool CanSubmitForApproval => CanEditDraft && Draft is not null && Draft.Blockers.Count == 0 && !IsWorking;
    private bool CanIssue => !IsWorking && Draft?.Approval is { IsCurrent: true } approval &&
        string.Equals(approval.Status, "approved", StringComparison.OrdinalIgnoreCase) &&
        Readiness?.IsAllowed == true && SelectedSeriesId != Guid.Empty && SelectedPeriodId != Guid.Empty;
    private bool IsPreviewStale => Draft?.Approval is { IsCurrent: false } || Draft is not null && Draft.ResultHash.Length == 0;

    private IReadOnlyList<DraftStep> Steps =>
    [
        new(1, FinanceText["Details"], true, Draft is not null),
        new(2, FinanceText["Lines"], false, Draft is not null),
        new(3, FinanceText["Review"], false, Readiness is not null),
        new(4, FinanceText["Approval"], false, Draft?.Approval is not null),
        new(5, FinanceText["Issue"], IssueResult is not null, IssueResult is not null)
    ];

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId ||
            _loadedCompanyId == companyId && _loadedDraftId == DraftId)
        {
            return;
        }

        _loadedCompanyId = companyId;
        _loadedDraftId = DraftId;
        await LoadReferenceDataAsync(companyId);
        if (DraftId is Guid draftId)
        {
            Draft = await FinanceApiClient.GetCustomerInvoiceDraftAsync(companyId, draftId);
            if (Draft is null)
            {
                ValidationMessage = FinanceText["InvoiceDraftNotFound"];
                return;
            }

            Editor = InvoiceDraftEditor.From(Draft);
            await RefreshReadinessAsync(companyId);
        }
    }

    private async Task LoadReferenceDataAsync(Guid companyId)
    {
        try
        {
            var customersTask = FinanceApiClient.GetCustomersAsync(companyId);
            var seriesTask = FinanceApiClient.GetStatutoryDocumentSeriesAsync(companyId);
            var yearsTask = FinanceApiClient.GetAccountingFiscalYearsAsync(companyId);
            await Task.WhenAll(customersTask, seriesTask, yearsTask);
            Customers = await customersTask;
            Series = (await seriesTask).Where(x => x.IsActive && x.DocumentType is "customer_invoice" or "invoice").ToArray();
            Periods = (await yearsTask).SelectMany(x => x.Periods).Where(x => !x.IsClosed && !x.IsReportingLocked).OrderBy(x => x.StartDate).ToArray();
            SelectedSeriesId = Series.FirstOrDefault(x => Editor.IssueDate >= x.FiscalYearStart && Editor.IssueDate <= x.FiscalYearEnd)?.Id ?? Series.FirstOrDefault()?.Id ?? Guid.Empty;
            SelectedPeriodId = Periods.FirstOrDefault(x => Editor.IssueDate >= x.StartDate && Editor.IssueDate <= x.EndDate)?.Id ?? Periods.FirstOrDefault()?.Id ?? Guid.Empty;
        }
        catch (FinanceApiException ex)
        {
            ValidationMessage = ex.Message;
        }
    }

    private void AddLine() => Editor.Lines.Add(InvoiceDraftLineEditor.Create());
    private void RemoveLine(InvoiceDraftLineEditor line) { if (Editor.Lines.Count > 1) Editor.Lines.Remove(line); }

    private async Task SaveAndPreviewAsync()
    {
        if (!CanEditDraft || AccessState.CompanyId is not Guid companyId) return;
        ValidationMessage = null; WorkflowMessage = null; IsWorking = true;
        try
        {
            ValidateEditor();
            _saveKey ??= $"invoice-draft-save:{companyId:N}:{Draft?.Id.ToString("N") ?? "new"}:{Guid.NewGuid():N}";
            var request = Editor.ToRequest(Draft?.Version ?? 0, _saveKey);
            Draft = Draft is null
                ? await FinanceApiClient.CreateCustomerInvoiceDraftAsync(companyId, request)
                : await FinanceApiClient.UpdateCustomerInvoiceDraftAsync(companyId, Draft.Id, request);
            DraftId = Draft.Id;
            Editor = InvoiceDraftEditor.From(Draft);
            var preview = await FinanceApiClient.PreviewCustomerInvoiceDraftAsync(companyId, Draft.Id, Draft.Version);
            Draft = preview.Draft;
            _saveKey = null; _submitKey = null; _issueKey = null;
            await RefreshReadinessAsync(companyId);
            WorkflowMessage = FinanceText["DraftSavedAndCalculated"];
        }
        catch (Exception ex) when (ex is FinanceApiException or InvalidOperationException)
        {
            ValidationMessage = ex.Message;
        }
        finally { IsWorking = false; }
    }

    private async Task RefreshReadinessAsync(Guid companyId)
    {
        if (Draft is null) return;
        try { Readiness = await FinanceApiClient.GetCustomerInvoiceDraftReadinessAsync(companyId, Draft.Id, Draft.Version); }
        catch (FinanceApiException ex) { ValidationMessage = ex.Message; }
    }

    private async Task SubmitForApprovalAsync()
    {
        if (!CanSubmitForApproval || Draft is null || AccessState.CompanyId is not Guid companyId) return;
        ValidationMessage = null; WorkflowMessage = null; IsWorking = true;
        try
        {
            _submitKey ??= $"invoice-draft-submit:{companyId:N}:{Draft.Id:N}:{Draft.Version}:{Guid.NewGuid():N}";
            var result = await FinanceApiClient.SubmitCustomerInvoiceDraftAsync(companyId, Draft.Id, new(Draft.Version, _submitKey));
            Draft = result.Draft; Readiness = result.Readiness; _submitKey = null;
            WorkflowMessage = FinanceText["ApprovalRequested"];
        }
        catch (FinanceApiException ex) { ValidationMessage = ex.Message; }
        finally { IsWorking = false; }
    }

    private async Task IssueAsync()
    {
        if (!CanIssue || Draft is null || AccessState.CompanyId is not Guid companyId) return;
        ValidationMessage = null; WorkflowMessage = null; IsWorking = true;
        try
        {
            _issueKey ??= $"invoice-draft-issue:{companyId:N}:{Draft.Id:N}:{Draft.Version}:{Guid.NewGuid():N}";
            IssueResult = await FinanceApiClient.IssueCustomerInvoiceDraftAsync(companyId, Draft.Id,
                new(Draft.Version, _issueKey, Draft.ResultHash, SelectedSeriesId, SelectedPeriodId, Editor.IssueDate, VoucherSeriesCode));
            _issueKey = null;
            WorkflowMessage = FinanceText["InvoiceIssuedMessage", IssueResult.DocumentNumber];
        }
        catch (FinanceApiException ex) { ValidationMessage = ex.Message; }
        finally { IsWorking = false; }
    }

    private void ValidateEditor()
    {
        if (Editor.CustomerId == Guid.Empty) throw new InvalidOperationException(FinanceText["SelectCustomerBeforeSaving"]);
        if (Editor.DueDate < Editor.IssueDate) throw new InvalidOperationException(FinanceText["DueDateAfterIssueDate"]);
        if (Editor.Lines.Count == 0 || Editor.Lines.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.UnitPrice < 0 || x.DiscountPercent is < 0 or > 100))
            throw new InvalidOperationException(FinanceText["CompleteInvoiceLines"]);
    }

    private string Money(decimal amount, string currency) => $"{currency} {amount:N2}";
    private static string FormatStatus(string status) => string.Join(' ', status.Split('_', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant() is { Length: > 0 } value ? char.ToUpperInvariant(value[0]) + value[1..] : "Unknown";
    private static string StatusTone(string status) => status.ToLowerInvariant() switch { "approved" => "status-pill--success", "rejected" or "expired" => "status-pill--danger", _ => "status-pill--warning" };
    private string BuildApprovalHref(Guid approvalId) => $"/approvals?companyId={AccessState.CompanyId:D}&approvalId={approvalId:D}";

    private sealed record DraftStep(int Number, string Label, bool IsCurrent, bool IsComplete);

    private sealed class InvoiceDraftEditor
    {
        public Guid CustomerId { get; set; }
        public DateOnly IssueDate { get; set; }
        public DateOnly DueDate { get; set; }
        public int PaymentTermDays { get; set; }
        public string Currency { get; set; } = "SEK";
        public string? BuyerReference { get; set; }
        public string? Notes { get; set; }
        public string EvidenceDocumentIds { get; set; } = string.Empty;
        public List<InvoiceDraftLineEditor> Lines { get; set; } = [];

        public static InvoiceDraftEditor Create()
        {
            var issueDate = DateOnly.FromDateTime(DateTime.Today);
            return new() { IssueDate = issueDate, DueDate = issueDate.AddDays(30), PaymentTermDays = 30, Lines = [InvoiceDraftLineEditor.Create()] };
        }

        public static InvoiceDraftEditor From(CustomerInvoiceDraftResponse draft) => new()
        {
            CustomerId = draft.CustomerId, IssueDate = draft.IssueDate, DueDate = draft.DueDate,
            PaymentTermDays = draft.PaymentTermDays, Currency = draft.Currency, BuyerReference = draft.BuyerReference,
            Notes = draft.Notes, EvidenceDocumentIds = string.Join(", ", draft.Evidence.Select(x => x.DocumentId)),
            Lines = draft.Lines.OrderBy(x => x.Sequence).Select(InvoiceDraftLineEditor.From).ToList()
        };

        public SaveCustomerInvoiceDraftApiRequest ToRequest(long expectedVersion, string idempotencyKey) => new(
            expectedVersion, idempotencyKey, CustomerId, "customer_invoice", IssueDate, IssueDate, DueDate,
            Currency.Trim().ToUpperInvariant(), "fixed_days", PaymentTermDays, BuyerReference, null, Notes, "email", "user", null,
            Lines.Select((line, index) => line.ToRequest(index + 1)).ToArray(), ParseEvidenceIds());

        private IReadOnlyList<Guid> ParseEvidenceIds() => EvidenceDocumentIds.Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out var id) ? id : throw new InvalidOperationException($"'{value}' is not a valid evidence document ID."))
            .Distinct().ToArray();
    }

    private sealed class InvoiceDraftLineEditor
    {
        public Guid Key { get; } = Guid.NewGuid();
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; } = 1m;
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public string TaxRuleKey { get; set; } = "se_domestic_sales_25";
        public string? DimensionCode { get; set; }
        public static InvoiceDraftLineEditor Create() => new();
        public static InvoiceDraftLineEditor From(CustomerInvoiceDraftLineResponse line) => new() { Description = line.Description, Quantity = line.Quantity, UnitPrice = line.UnitPrice, DiscountPercent = line.DiscountPercent, TaxRuleKey = line.TaxRuleKey, DimensionCode = line.DimensionFacts.GetValueOrDefault("cost_center") };
        public CustomerInvoiceDraftLineApiRequest ToRequest(int sequence) => new(sequence, Description.Trim(), Quantity, "each", UnitPrice, DiscountPercent, TaxRuleKey, "standard_goods_or_services", [new("operator_classified_domestic_standard_25", "invoice_editor")], string.IsNullOrWhiteSpace(DimensionCode) ? null : new Dictionary<string, string> { ["cost_center"] = DimensionCode.Trim() }, "revenue");
    }
}
