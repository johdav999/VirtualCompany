using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AnomaliesPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "type")]
    public string? Type { get; set; }

    [SupplyParameterFromQuery(Name = "status")]
    public string? Status { get; set; }

    [SupplyParameterFromQuery(Name = "confidenceMin")]
    public decimal? ConfidenceMin { get; set; }

    [SupplyParameterFromQuery(Name = "confidenceMax")]
    public decimal? ConfidenceMax { get; set; }

    [SupplyParameterFromQuery(Name = "supplier")]
    public string? Supplier { get; set; }

    [SupplyParameterFromQuery(Name = "dateFrom")]
    public DateTime? DateFrom { get; set; }

    [SupplyParameterFromQuery(Name = "dateTo")]
    public DateTime? DateTo { get; set; }

    [SupplyParameterFromQuery(Name = "page")]
    public int? Page { get; set; }

    [SupplyParameterFromQuery(Name = "pageSize")]
    public int? PageSize { get; set; }

    private FinanceAnomalyFilterState Filters { get; set; } = new();
    private FinanceAnomalyWorkbenchResponse? Workbench { get; set; }
    private bool IsListLoading { get; set; }
    private string? ListErrorMessage { get; set; }

    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && (Workbench?.Items.Count ?? 0) == 0;
    private string CurrentPageSummary => $"{Workbench?.Page ?? Filters.Page} / {Math.Max(1, TotalPages)}";
    private string VisibleRangeSummary => Workbench is not { TotalCount: > 0 }
        ? "No results in the current workbench view."
        : $"Showing {((Workbench.Page - 1) * Workbench.PageSize) + 1}-{Math.Min(Workbench.Page * Workbench.PageSize, Workbench.TotalCount)} of {Workbench.TotalCount}.";
    private int TotalPages => Workbench is { TotalCount: > 0, PageSize: > 0 }
        ? (int)Math.Ceiling(Workbench.TotalCount / (double)Workbench.PageSize)
        : 1;
    private bool CanGoToPreviousPage => (Workbench?.Page ?? Filters.Page) > 1;
    private bool CanGoToNextPage => Workbench is { } workbench && workbench.Page * workbench.PageSize < workbench.TotalCount;

    private static IReadOnlyList<string> AnomalyTypeOptions => ["threshold_breach", "historical_baseline_deviation", "missing_counterparty"];
    private static IReadOnlyList<string> StatusOptions => ["new", "in_progress", "blocked", "awaiting_approval", "completed", "failed", "open", "acknowledged", "resolved", "closed"];
    private static IReadOnlyList<int> PageSizeOptions => [25, 50, 100];

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Filters = CreateFilterState();
        Workbench = null;
        ListErrorMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadWorkbenchAsync(companyId, Filters);
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadWorkbenchAsync(companyId, Filters);
        }
    }

    private async Task LoadWorkbenchAsync(Guid companyId, FinanceAnomalyFilterState filter)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            Workbench = await FinanceApiClient.GetAnomalyWorkbenchAsync(
                companyId,
                filter.AnomalyType,
                filter.Status,
                filter.ConfidenceMin,
                filter.ConfidenceMax,
                filter.Supplier,
                NormalizeStartUtc(filter.DateFrom),
                NormalizeEndExclusiveUtc(filter.DateTo),
                filter.Page,
                filter.PageSize);
        }
        catch (FinanceApiException ex)
        {
            Workbench = null;
            ListErrorMessage = ex.Message;
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private Task HandleApplyFiltersAsync() =>
        NavigateToFiltersAsync(Filters, resetPage: true);

    private Task HandleFiltersChangedAsync() =>
        NavigateToFiltersAsync(Filters, resetPage: true);

    private Task HandlePageSizeChangedAsync() =>
        NavigateToFiltersAsync(Filters, resetPage: true);

    private Task ClearFiltersAsync() =>
        NavigateToFiltersAsync(new FinanceAnomalyFilterState(), resetPage: false);

    private Task PreviousPageAsync() =>
        ChangePageAsync((Workbench?.Page ?? Filters.Page) - 1);

    private Task NextPageAsync() =>
        ChangePageAsync((Workbench?.Page ?? Filters.Page) + 1);

    private Task ChangePageAsync(int page)
    {
        var next = Filters.Normalize();
        next.Page = Math.Max(1, page);
        return NavigateToFiltersAsync(next, resetPage: false);
    }

    private Task NavigateToFiltersAsync(FinanceAnomalyFilterState nextFilters, bool resetPage)
    {
        Filters = nextFilters.Normalize();
        if (resetPage)
        {
            Filters.Page = 1;
        }

        var nextUri = Navigation.GetUriWithQueryParameters(Filters.ToQueryParameters(AccessState.CompanyId));
        if (!string.Equals(Navigation.Uri, nextUri, StringComparison.Ordinal))
        {
            Navigation.NavigateTo(nextUri);
        }

        return Task.CompletedTask;
    }

    private string BuildDetailHref(FinanceAnomalyWorkbenchItemResponse item)
    {
        var query = Filters.Normalize().ToQueryString(AccessState.CompanyId);
        var path = FinanceRoutes.BuildAnomalyDetailPath(item.Id, null);
        return string.IsNullOrWhiteSpace(query) ? path : $"{path}?{query}";
    }

    private FinanceAnomalyFilterState CreateFilterState() =>
        new FinanceAnomalyFilterState
        {
            AnomalyType = Type,
            Status = Status,
            ConfidenceMin = ConfidenceMin,
            ConfidenceMax = ConfidenceMax,
            Supplier = Supplier,
            DateFrom = DateFrom,
            DateTo = DateTo,
            Page = Page ?? 1,
            PageSize = PageSize ?? 50
        }.Normalize();

    private static DateTime? NormalizeStartUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc) : null;

    private static DateTime? NormalizeEndExclusiveUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value.Date.AddDays(1), DateTimeKind.Utc) : null;
}
