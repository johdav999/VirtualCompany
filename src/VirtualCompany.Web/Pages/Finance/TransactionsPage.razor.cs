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
    private string FlaggedFilterValue => NormalizeFlaggedState(Flagged);
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
    private string ClearFiltersHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.Activity, AccessState.CompanyId);

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
                DetailErrorMessage = "The selected activity item could not be found for this company.";
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

        return $"{path}?{string.Join("&", query)}";
    }

    private string BuildDocumentHref(Guid documentId) =>
        $"/api/companies/{AccessState.CompanyId}/documents/{documentId}";

    private string GetTransactionListItemClass(Guid transactionId) =>
        TransactionId == transactionId
            ? "list-group-item list-group-item-action active"
            : "list-group-item list-group-item-action";

    private bool IsCategorySelected(string option) =>
        string.Equals(CategoryFilterValue, option, StringComparison.OrdinalIgnoreCase);

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
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatLabel(string? value) =>
        FinanceAnomalyPresentation.FormatLabel(value);
}
