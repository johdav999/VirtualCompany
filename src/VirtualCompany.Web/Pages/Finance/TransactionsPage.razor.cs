using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;
using VirtualCompany.Shared;

namespace VirtualCompany.Web.Pages.Finance;

public partial class TransactionsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [Parameter]
    public Guid? TransactionId { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public DateTime? From { get; set; }

    [SupplyParameterFromQuery(Name = "to")]
    public DateTime? To { get; set; }

    [SupplyParameterFromQuery(Name = "category")]
    public string? Category { get; set; }

    [SupplyParameterFromQuery(Name = "flagged")]
    public string? Flagged { get; set; }

    [SupplyParameterFromQuery(Name = "account")]
    public string? Account { get; set; }

    [SupplyParameterFromQuery(Name = "search")]
    public string? Search { get; set; }

    private IReadOnlyList<FinanceTransactionResponse> Transactions { get; set; } = [];
    private FinanceTransactionDetailResponse? SelectedTransaction { get; set; }
    private bool IsListLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? DetailErrorMessage { get; set; }
    private string EditableCategory { get; set; } = string.Empty;
    private string? CategoryValidationMessage { get; set; }
    private string? CategorySaveMessage { get; set; }
    private bool IsSavingCategory { get; set; }

    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && Transactions.Count == 0;
    private bool CanEditTransactionCategory =>
        SelectedTransaction?.Permissions.CanEditTransactionCategory ?? FinanceAccess.CanEditTransactionCategory(AccessState.MembershipRole);
    private string FromInputValue => From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    private string ToInputValue => To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    private string? CategoryFilterValue => NormalizeOptionalText(Category);
    private string? AccountFilterValue => NormalizeOptionalText(Account);
    private string? SearchFilterValue => NormalizeOptionalText(Search);
    private string FlaggedFilterValue => NormalizeFlaggedState(Flagged);
    private string DashboardHref => AccessState.CompanyId is Guid companyId ? $"/dashboard?companyId={companyId:D}" : "/dashboard";
    private IReadOnlyList<FinanceTransactionResponse> DisplayedTransactions =>
        Transactions.Where(MatchesClientFilters).ToArray();
    private IReadOnlyList<TransactionRowViewModel> TransactionRows =>
        DisplayedTransactions.Select(transaction => ToRowViewModel(transaction, TransactionId == transaction.Id)).ToArray();
    private TransactionsSummaryViewModel Summary => BuildSummary(DisplayedTransactions);
    private TransactionDetailViewModel? SelectedTransactionDisplay =>
        SelectedTransaction is null ? null : ToDetailViewModel(SelectedTransaction);
    private bool IsDisplayedListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && DisplayedTransactions.Count == 0;
    private string EmptyTitle => Transactions.Count == 0
        ? "No transactions available"
        : "No transactions matched the active filters";
    private string EmptyMessage => Transactions.Count == 0
        ? "Account activity will appear here when bank or integration data is available."
        : "Adjust the date range, account, or search filter and try again.";
    private IReadOnlyList<string> CategoryOptions => Transactions
        .Select(x => x.TransactionType)
        .Append(CategoryFilterValue ?? string.Empty)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    private IReadOnlyList<string> EditableCategoryOptions => FinanceTransactionCategories.AllowedValues
        .Append(SelectedTransaction?.Category ?? string.Empty)
        .Append(CategoryFilterValue ?? string.Empty)
        .Concat(Transactions.Select(x => x.TransactionType))
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    private IReadOnlyList<string> AccountOptions => Transactions
        .Select(x => x.AccountName)
        .Append(AccountFilterValue ?? string.Empty)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    private string ClearFiltersHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.Transactions, AccessState.CompanyId);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Transactions = [];
        SelectedTransaction = null;
        ListErrorMessage = null;
        DetailErrorMessage = null;
        CategoryValidationMessage = null;
        CategorySaveMessage = null;
        EditableCategory = string.Empty;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadTransactionsAsync(companyId);

        if (TransactionId is Guid transactionId)
        {
            await LoadDetailAsync(companyId, transactionId);
        }
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadTransactionsAsync(companyId);
        if (TransactionId is Guid transactionId)
        {
            await LoadDetailAsync(companyId, transactionId);
        }
    }

    private async Task LoadTransactionsAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            Transactions = await FinanceApiClient.GetTransactionsAsync(
                companyId,
                NormalizeStartUtc(From),
                NormalizeEndExclusiveUtc(To),
                CategoryFilterValue,
                FlaggedFilterValue == "all" ? null : FlaggedFilterValue,
                200);
        }
        catch (FinanceApiException ex)
        {
            Transactions = [];
            ListErrorMessage = ex.Message;
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private async Task LoadDetailAsync(Guid companyId, Guid transactionId)
    {
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try
        {
            SelectedTransaction = await FinanceApiClient.GetTransactionDetailAsync(companyId, transactionId);
            if (SelectedTransaction is null)
            {
                DetailErrorMessage = "The selected transaction could not be found for this company.";
                EditableCategory = string.Empty;
            }
        }
        catch (FinanceApiException ex)
        {
            SelectedTransaction = null;
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            EditableCategory = SelectedTransaction?.Category ?? string.Empty;
            IsDetailLoading = false;
        }
    }

    private async Task HandleCategorySaveAsync()
    {
        CategoryValidationMessage = null;
        CategorySaveMessage = null;

        if (!CanEditTransactionCategory || AccessState.CompanyId is not Guid companyId || SelectedTransaction is null)
        {
            return;
        }

        var normalizedCategory = FinanceTransactionCategories.Normalize(EditableCategory);
        if (!FinanceTransactionCategories.IsSupported(normalizedCategory))
        {
            CategoryValidationMessage = $"Unsupported category '{EditableCategory}'. Choose one of the supported finance categories.";
            return;
        }

        IsSavingCategory = true;
        try
        {
            EditableCategory = normalizedCategory;
            await FinanceApiClient.UpdateTransactionCategoryAsync(companyId, SelectedTransaction.Id, normalizedCategory);
            await LoadTransactionsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedTransaction.Id);
            CategorySaveMessage = $"Category saved as {FormatLabel(SelectedTransaction?.Category)}.";
        }
        catch (FinanceApiValidationException ex)
        {
            CategoryValidationMessage = ResolveValidationMessage(ex, "Category");
        }
        catch (FinanceApiException ex)
        {
            CategoryValidationMessage = ex.Message;
        }
        finally
        {
            IsSavingCategory = false;
        }
    }

    private string BuildTransactionHref(Guid transactionId)
    {
        var path = FinanceRoutes.BuildTransactionDetailPath(transactionId, null);
        var query = new List<string>
        {
            $"{FinanceRoutes.CompanyIdQueryKey}={AccessState.CompanyId}"
        };

        if (From is DateTime from)
        {
            query.Add($"from={Uri.EscapeDataString(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        }

        if (To is DateTime to)
        {
            query.Add($"to={Uri.EscapeDataString(to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        }

        if (!string.IsNullOrWhiteSpace(CategoryFilterValue))
        {
            query.Add($"category={Uri.EscapeDataString(CategoryFilterValue)}");
        }

        if (!string.Equals(FlaggedFilterValue, "all", StringComparison.OrdinalIgnoreCase))
        {
            query.Add($"flagged={Uri.EscapeDataString(FlaggedFilterValue)}");
        }

        if (!string.IsNullOrWhiteSpace(AccountFilterValue))
        {
            query.Add($"account={Uri.EscapeDataString(AccountFilterValue)}");
        }

        if (!string.IsNullOrWhiteSpace(SearchFilterValue))
        {
            query.Add($"search={Uri.EscapeDataString(SearchFilterValue)}");
        }

        return $"{path}?{string.Join("&", query)}";
    }

    private string BuildDocumentHref(Guid documentId) =>
        $"/api/companies/{AccessState.CompanyId}/documents/{documentId}";

    private void NavigateToTransaction(Guid transactionId) =>
        Navigation.NavigateTo(BuildTransactionHref(transactionId));

    private bool MatchesClientFilters(FinanceTransactionResponse transaction)
    {
        if (!string.IsNullOrWhiteSpace(AccountFilterValue) &&
            !string.Equals(transaction.AccountName, AccountFilterValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchFilterValue))
        {
            return true;
        }

        var searchable = string.Join(" ",
            transaction.Description,
            transaction.CounterpartyName,
            transaction.ExternalReference,
            transaction.AccountName,
            transaction.TransactionType,
            transaction.Amount.ToString(CultureInfo.InvariantCulture),
            transaction.Currency);

        return searchable.Contains(SearchFilterValue, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCategorySelected(string option) =>
        string.Equals(CategoryFilterValue, option, StringComparison.OrdinalIgnoreCase);

    private bool IsAccountSelected(string option) =>
        string.Equals(AccountFilterValue, option, StringComparison.OrdinalIgnoreCase);

    private bool IsFlaggedSelected(string option) =>
        string.Equals(FlaggedFilterValue, option, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFlaggedState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "all";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "flagged" => "flagged",
            "not_flagged" => "not_flagged",
            _ => "all"
        };
    }

    private static string ResolveValidationMessage(FinanceApiValidationException exception, string key)
    {
        if (exception.Errors.TryGetValue(key, out var directErrors) && directErrors.Length > 0)
        {
            return directErrors[0];
        }

        var matched = exception.Errors.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
        return matched.Value is { Length: > 0 } ? matched.Value[0] : exception.Message;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? NormalizeStartUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc) : null;

    private static DateTime? NormalizeEndExclusiveUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value.Date.AddDays(1), DateTimeKind.Utc) : null;

    private static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string FormatDate(DateTime value) =>
        value == default ? "Not available" : value.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture);

    private static string FormatLabel(string? value) =>
        FinanceAnomalyPresentation.FormatLabel(value);

    private static string FormatTableDate(DateTime value) =>
        value == default ? "Not available" : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatPlainText(string? value, string fallback = "Not available") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool IsUncategorized(string? category)
    {
        var normalized = NormalizeOptionalText(category)?.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ||
            normalized is "uncategorized" or "unknown" or "unmapped" or "needs_category";
    }

    private static bool IsNeedsReview(FinanceTransactionResponse transaction)
    {
        if (transaction.IsFlagged || IsUncategorized(transaction.TransactionType))
        {
            return true;
        }

        var normalized = NormalizeOptionalText(transaction.AnomalyState)?.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(normalized) &&
            normalized is not "clear" and not "none" and not "normal" and not "resolved";
    }

    private static TransactionStatusPresentation ResolveStatusPresentation(
        bool isFlagged,
        string? anomalyState,
        string? category)
    {
        if (IsUncategorized(category))
        {
            return new("Uncategorized", "warning");
        }

        if (isFlagged)
        {
            return new("Flagged", "danger");
        }

        var normalized = NormalizeOptionalText(anomalyState)?.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            null or "" or "clear" or "none" or "normal" or "resolved" => new("Normal", "success"),
            "needs_review" or "review" or "open" or "active" => new("Needs review", "warning"),
            _ => new(FormatLabel(anomalyState), "warning")
        };
    }

    private static TransactionRowViewModel ToRowViewModel(FinanceTransactionResponse transaction, bool isSelected)
    {
        var status = ResolveStatusPresentation(transaction.IsFlagged, transaction.AnomalyState, transaction.TransactionType);
        return new TransactionRowViewModel(
            transaction.Id,
            FormatTableDate(transaction.TransactionUtc),
            FormatPlainText(transaction.Description, "Transaction"),
            FormatPlainText(transaction.CounterpartyName),
            FormatPlainText(transaction.AccountName),
            FormatCurrency(transaction.Amount, transaction.Currency),
            FormatLabel(transaction.TransactionType),
            FormatPlainText(transaction.ExternalReference),
            status.Label,
            status.Tone,
            transaction.Amount >= 0,
            isSelected);
    }

    private static TransactionDetailViewModel ToDetailViewModel(FinanceTransactionDetailResponse transaction)
    {
        var status = ResolveStatusPresentation(transaction.IsFlagged, transaction.AnomalyState, transaction.Category);
        var flags = transaction.Flags ?? [];
        return new TransactionDetailViewModel(
            FormatCurrency(transaction.Amount, transaction.Currency),
            FormatPlainText(transaction.Description, "Transaction"),
            FormatPlainText(transaction.CounterpartyName),
            FormatDate(transaction.TransactionUtc),
            FormatPlainText(transaction.AccountName),
            FormatLabel(transaction.Category),
            FormatPlainText(transaction.ExternalReference),
            ResolveSourceLabel(transaction),
            FormatLabel(transaction.AnomalyState),
            flags,
            status.Label,
            status.Tone,
            BuildAgentInsight(transaction, status.Label));
    }

    private static string ResolveSourceLabel(FinanceTransactionDetailResponse transaction)
    {
        if (transaction.InvoiceId is not null)
        {
            return "Invoice";
        }

        if (transaction.BillId is not null)
        {
            return "Supplier bill";
        }

        if (transaction.LinkedDocument.CanNavigate)
        {
            return "Linked document";
        }

        return "Bank or integration import";
    }

    private static string BuildAgentInsight(FinanceTransactionDetailResponse transaction, string statusLabel)
    {
        if (IsUncategorized(transaction.Category))
        {
            return "This transaction needs a category before reporting and reconciliation are reliable.";
        }

        if (transaction.IsFlagged || transaction.Flags.Count > 0)
        {
            return "This transaction has review flags. Check the reference, linked document, and category before closing the review.";
        }

        var normalized = NormalizeOptionalText(transaction.AnomalyState)?.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalized) &&
            normalized is not "clear" and not "none" and not "normal" and not "resolved")
        {
            return $"This transaction is marked {statusLabel.ToLowerInvariant()}. Review the account, amount, and source reference.";
        }

        return "No obvious issue detected.";
    }

    private static TransactionsSummaryViewModel BuildSummary(IReadOnlyList<FinanceTransactionResponse> transactions)
    {
        var currency = transactions.Select(x => x.Currency).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "USD";
        var inflow = transactions.Where(x => x.Amount > 0).Sum(x => x.Amount);
        var outflow = Math.Abs(transactions.Where(x => x.Amount < 0).Sum(x => x.Amount));
        return new TransactionsSummaryViewModel(
            FormatCurrency(inflow, currency),
            FormatCurrency(outflow, currency),
            transactions.Count(x => IsUncategorized(x.TransactionType)),
            transactions.Count(IsNeedsReview));
    }

    private sealed record TransactionStatusPresentation(string Label, string Tone);

    private sealed record TransactionsSummaryViewModel(
        string DisplayInflow,
        string DisplayOutflow,
        int UncategorizedCount,
        int NeedsReviewCount);

    private sealed record TransactionRowViewModel(
        Guid Id,
        string DisplayDate,
        string Description,
        string CounterpartyName,
        string AccountName,
        string DisplayAmount,
        string CategoryLabel,
        string ExternalReference,
        string StatusLabel,
        string StatusTone,
        bool IsPositive,
        bool IsSelected);

    private sealed record TransactionDetailViewModel(
        string DisplayAmount,
        string Description,
        string CounterpartyName,
        string DisplayDate,
        string AccountName,
        string CategoryLabel,
        string ExternalReference,
        string SourceLabel,
        string AnomalyLabel,
        IReadOnlyList<string> Flags,
        string StatusLabel,
        string StatusTone,
        string AgentInsight);
}
