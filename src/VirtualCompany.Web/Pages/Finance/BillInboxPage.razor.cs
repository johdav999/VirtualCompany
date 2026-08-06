using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BillInboxPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private IReadOnlyList<FinanceBillInboxRowResponse> Items { get; set; } = [];
    private bool IsListLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private string SearchTerm { get; set; } = string.Empty;
    private string SelectedStatusFilter { get; set; } = StatusFilters.All;
    private string SelectedAttentionFilter { get; set; } = AttentionFilters.All;
    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && Items.Count == 0;
    private bool HasActiveFilters => !string.IsNullOrWhiteSpace(SearchTerm) || SelectedStatusFilter != StatusFilters.All || SelectedAttentionFilter != AttentionFilters.All;
    private int NeedsReviewCount => Items.Count(x => GetStatusGroup(x.Status) == StatusFilters.NeedsReview);
    private int WaitingForApprovalCount => Items.Count(x => GetStatusGroup(x.Status) == StatusFilters.WaitingForApproval);
    private int ReadyToSendCount => Items.Count(x => GetStatusGroup(x.Status) == StatusFilters.ReadyToSend);

    private IReadOnlyList<FinanceBillInboxRowResponse> FilteredItems => Items
        .Where(MatchesSearch)
        .Where(x => SelectedStatusFilter == StatusFilters.All || GetStatusGroup(x.Status) == SelectedStatusFilter)
        .Where(MatchesAttention)
        .OrderByDescending(x => x.DetectedUtc)
        .ThenByDescending(x => x.Id)
        .ToList();

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        Items = [];
        ListErrorMessage = null;

        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId)
        {
            await LoadAsync(companyId);
        }
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadAsync(companyId);
        }
    }

    private async Task LoadAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;
        try
        {
            Items = await FinanceApiClient.GetBillInboxAsync(companyId, 200);
        }
        catch (FinanceApiException ex)
        {
            Items = [];
            ListErrorMessage = ex.Message;
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private string BuildDetailHref(Guid billId) => FinanceRoutes.BuildBillInboxDetailPath(billId, AccessState.CompanyId);
    private string FormatDate(DateTime value) => LocalDateTime.Date(DateOnly.FromDateTime(value));

    private string FormatAmount(decimal? amount, string? currency) => amount.HasValue
        ? string.IsNullOrWhiteSpace(currency) ? LocalNumber.Decimal(amount.Value) : LocalMoney.Format(amount.Value, currency)
        : FinanceText["AmountNotExtracted"];

    private void SetStatusFilter(string filter) => SelectedStatusFilter = filter;

    private void ClearFilters()
    {
        SearchTerm = string.Empty;
        SelectedStatusFilter = StatusFilters.All;
        SelectedAttentionFilter = AttentionFilters.All;
    }

    private bool MatchesSearch(FinanceBillInboxRowResponse item)
    {
        if (string.IsNullOrWhiteSpace(SearchTerm))
        {
            return true;
        }

        return item.SupplierName.Contains(SearchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
               item.BillReference.Contains(SearchTerm.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesAttention(FinanceBillInboxRowResponse item)
    {
        var hasIssues = item.ValidationWarningCount > 0 || item.DuplicateWarningCount > 0 ||
            string.Equals(item.ConfidenceLevel, "Low", StringComparison.OrdinalIgnoreCase);
        return SelectedAttentionFilter switch
        {
            AttentionFilters.Issues => hasIssues,
            AttentionFilters.NoIssues => !hasIssues,
            _ => true
        };
    }

    private BillReviewPresentation Present(FinanceBillInboxRowResponse item)
    {
        var supplier = IsUsableBusinessValue(item.SupplierName)
            ? item.SupplierName.Trim()
            : FinanceText["SupplierNotIdentified"];
        var invoiceReference = IsUsableInvoiceReference(item.BillReference)
            ? FinanceText["InvoiceReference", item.BillReference.Trim()]
            : FinanceText["InvoiceNumberMissing"];
        var (statusLabel, statusTone) = GetStatusPresentation(item.Status);
        var (checkLabel, checkTone) = GetCheckPresentation(item);
        return new(supplier, invoiceReference, statusLabel, statusTone, checkLabel, checkTone);
    }

    private (string Label, string Tone) GetStatusPresentation(string status) => GetStatusGroup(status) switch
    {
        StatusFilters.WaitingForApproval => (FinanceText["WaitingForApproval"], "approval"),
        StatusFilters.ReadyToSend => (FinanceText["ApprovedReadyToSend"], "success"),
        StatusFilters.Sent => (FinanceText["SentToFortnox"], "success"),
        StatusFilters.Rejected => (FinanceText["Rejected"], "danger"),
        _ => (FinanceText["NeedsReview"], "warning")
    };

    private (string Label, string Tone) GetCheckPresentation(FinanceBillInboxRowResponse item)
    {
        if (item.DuplicateWarningCount > 0)
        {
            return (FinanceText["PossibleDuplicate"], "danger");
        }

        if (item.ValidationWarningCount > 0)
        {
            return (FinanceText[item.ValidationWarningCount == 1 ? "FieldNeedsChecking" : "FieldsNeedChecking", item.ValidationWarningCount], "warning");
        }

        if (string.Equals(item.ConfidenceLevel, "Low", StringComparison.OrdinalIgnoreCase))
        {
            return (FinanceText["CheckExtractedDetails"], "warning");
        }

        if (string.Equals(item.ConfidenceLevel, "Medium", StringComparison.OrdinalIgnoreCase))
        {
            return (FinanceText["ReviewKeyFields"], "approval");
        }

        return (FinanceText["NoIssuesFound"], "success");
    }

    private static string GetStatusGroup(string status) => status.Trim().ToLowerInvariant() switch
    {
        "proposed for approval" => StatusFilters.WaitingForApproval,
        "approved" => StatusFilters.ReadyToSend,
        "sent to payment/exported" => StatusFilters.Sent,
        "rejected" => StatusFilters.Rejected,
        _ => StatusFilters.NeedsReview
    };

    private static bool IsUsableBusinessValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.TrimStart().StartsWith("/", StringComparison.Ordinal) &&
        !string.Equals(value.Trim(), "Unknown supplier", StringComparison.OrdinalIgnoreCase);

    private static bool IsUsableInvoiceReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !Guid.TryParse(value, out _) &&
        !value.TrimStart().StartsWith("/", StringComparison.Ordinal);

    private sealed record BillReviewPresentation(string SupplierName, string InvoiceReference, string StatusLabel, string StatusTone, string CheckLabel, string CheckTone);

    private static class StatusFilters
    {
        public const string All = "all";
        public const string NeedsReview = "needs-review";
        public const string WaitingForApproval = "waiting-for-approval";
        public const string ReadyToSend = "ready-to-send";
        public const string Sent = "sent";
        public const string Rejected = "rejected";
    }

    private static class AttentionFilters
    {
        public const string All = "all";
        public const string Issues = "issues";
        public const string NoIssues = "no-issues";
    }
}
