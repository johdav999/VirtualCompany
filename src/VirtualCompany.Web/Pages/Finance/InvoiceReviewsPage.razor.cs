using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class InvoiceReviewsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "status")]
    public string? Status { get; set; }

    [SupplyParameterFromQuery(Name = "supplier")]
    public string? Supplier { get; set; }

    [SupplyParameterFromQuery(Name = "riskLevel")]
    public string? RiskLevel { get; set; }

    [SupplyParameterFromQuery(Name = "outcome")]
    public string? Outcome { get; set; }

    private InvoiceReviewFilterState Filters { get; set; } = new();
    private IReadOnlyList<FinanceInvoiceReviewListItemResponse> Reviews { get; set; } = [];
    private FinanceInvoiceReviewDetailResponse? SelectedReviewDetail { get; set; }
    private Guid? SelectedInvoiceId { get; set; }
    private bool HighRiskOnly { get; set; }
    private bool NeedsApprovalOnly { get; set; }
    private bool LowConfidenceOnly { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsSubmittingAction { get; set; }
    private bool IsListLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? DetailErrorMessage { get; set; }
    private string? ActionStatusMessage { get; set; }
    private string SortColumn { get; set; } = nameof(FinanceInvoiceReviewListItemResponse.RiskLevel);
    private bool SortDescending { get; set; } = true;
    private static IReadOnlyList<string> StatusOptions => ["open", "pending_approval"];
    private static IReadOnlyList<string> RiskLevelOptions => ["low", "medium", "high", "critical"];
    private static IReadOnlyList<string> OutcomeOptions => ["approve", "reject", "request_human_approval", "no_action", "pending_review"];
    private IReadOnlyList<FinanceInvoiceReviewListItemResponse> VisibleReviews => ApplySort(ApplyQuickFilters(Reviews)).ToList();
    private FinanceInvoiceReviewListItemResponse? SelectedReview => VisibleReviews.FirstOrDefault(x => x.Id == SelectedInvoiceId);
    private int PendingReviewCount => VisibleReviews.Count;
    private int HighRiskReviewCount => VisibleReviews.Count(IsHighRisk);
    private int NeedsAttentionCount => VisibleReviews.Count(NeedsAttention);
    private decimal PendingReviewAmount => VisibleReviews.Sum(x => x.Amount);
    private string PendingAmountCurrencyLabel => ResolveSummaryCurrencyLabel(VisibleReviews);
    private bool CanRunReview => SelectedInvoiceId.HasValue && AccessState.CompanyId.HasValue;
    private bool CanReviewNextHighRisk => VisibleReviews.Any(IsHighRisk);
    private bool IsWorkbenchBusy => IsListLoading || IsDetailLoading || IsSubmittingAction;
    private string SandboxAdminHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.SandboxAdmin, AccessState.CompanyId);
    private string SelectedDetailHref =>
        SelectedReview is null
            ? "#"
            : BuildDetailHref(SelectedReview);
    private string? SourceInvoiceHref =>
        SelectedReviewDetail?.SourceInvoiceId is Guid sourceInvoiceId && sourceInvoiceId != Guid.Empty
            ? FinanceRoutes.BuildInvoiceDetailPath(sourceInvoiceId, AccessState.CompanyId)
            : null;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Filters = CreateFilterState();
        Reviews = [];
        ListErrorMessage = null;
        DetailErrorMessage = null;
        ActionStatusMessage = null;
        SelectedReviewDetail = null;
        SelectedInvoiceId = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadReviewsAsync(companyId, Filters);
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadReviewsAsync(companyId, Filters);
            await ReloadSelectedDetailAsync();
        }
    }

    private async Task LoadReviewsAsync(Guid companyId, InvoiceReviewFilterState filter)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            Reviews = await FinanceApiClient.GetInvoiceReviewsAsync(companyId, filter.Status, filter.Supplier, filter.RiskLevel, filter.RecommendationOutcome, 200);
            if (SelectedInvoiceId.HasValue && Reviews.All(x => x.Id != SelectedInvoiceId.Value))
            {
                SelectedInvoiceId = null;
                SelectedReviewDetail = null;
                DetailErrorMessage = null;
            }
        }
        catch (FinanceApiException ex)
        {
            Reviews = [];
            ListErrorMessage = ex.Message;
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private Task HandleApplyFiltersAsync() =>
        NavigateToFiltersAsync(Filters);

    private Task ClearFiltersAsync()
    {
        HighRiskOnly = false;
        NeedsApprovalOnly = false;
        LowConfidenceOnly = false;
        return NavigateToFiltersAsync(new InvoiceReviewFilterState());
    }

    private Task ToggleHighRiskOnlyAsync()
    {
        HighRiskOnly = !HighRiskOnly;
        return Task.CompletedTask;
    }

    private Task ToggleNeedsApprovalOnlyAsync()
    {
        NeedsApprovalOnly = !NeedsApprovalOnly;
        return Task.CompletedTask;
    }

    private Task ToggleLowConfidenceOnlyAsync()
    {
        LowConfidenceOnly = !LowConfidenceOnly;
        return Task.CompletedTask;
    }

    private string BuildDetailHref(FinanceInvoiceReviewListItemResponse item)
    {
        var query = Filters.Normalize().ToQueryString(AccessState.CompanyId);
        var path = FinanceRoutes.BuildInvoiceReviewDetailPath(item.Id, null);
        return string.IsNullOrWhiteSpace(query) ? path : $"{path}?{query}";
    }

    private async Task SelectReviewAsync(FinanceInvoiceReviewListItemResponse item)
    {
        if (SelectedInvoiceId == item.Id && SelectedReviewDetail is not null)
        {
            return;
        }

        SelectedInvoiceId = item.Id;
        ActionStatusMessage = null;
        await ReloadSelectedDetailAsync();
    }

    private async Task ReloadSelectedDetailAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || !SelectedInvoiceId.HasValue)
        {
            return;
        }

        var invoiceId = SelectedInvoiceId.Value;
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try
        {
            var detail = NormalizeDetail(await FinanceApiClient.GetInvoiceReviewDetailAsync(companyId, invoiceId));
            if (SelectedInvoiceId == invoiceId)
            {
                SelectedReviewDetail = detail;
            }
        }
        catch (FinanceApiException ex)
        {
            if (SelectedInvoiceId == invoiceId)
            {
                SelectedReviewDetail = null;
                DetailErrorMessage = ex.Message;
            }
        }
        finally
        {
            if (SelectedInvoiceId == invoiceId)
            {
                IsDetailLoading = false;
            }
        }
    }

    private async Task ReviewNextHighRiskAsync()
    {
        var highRiskItems = VisibleReviews.Where(IsHighRisk).ToList();
        if (highRiskItems.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedInvoiceId.HasValue
            ? highRiskItems.FindIndex(x => x.Id == SelectedInvoiceId.Value)
            : -1;
        var nextIndex = currentIndex >= 0 && currentIndex + 1 < highRiskItems.Count ? currentIndex + 1 : 0;
        await SelectReviewAsync(highRiskItems[nextIndex]);
    }

    private async Task RunSelectedReviewAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || !SelectedInvoiceId.HasValue)
        {
            return;
        }

        IsSubmittingAction = true;
        DetailErrorMessage = null;
        ActionStatusMessage = null;

        try
        {
            await FinanceApiClient.StartInvoiceReviewWorkflowAsync(companyId, SelectedInvoiceId.Value);
            ActionStatusMessage = "Invoice review started.";
            await LoadReviewsAsync(companyId, Filters);
            await ReloadSelectedDetailAsync();
        }
        catch (FinanceApiException ex)
        {
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            IsSubmittingAction = false;
        }
    }

    private async Task RefreshAnalysisAsync()
    {
        ActionStatusMessage = null;
        await ReloadAsync();
    }

    private Task ApproveAsync() =>
        ExecuteActionAsync("approve", "Invoice approved.");

    private Task RejectAsync() =>
        ExecuteActionAsync("reject", "Invoice rejected.");

    private Task SendForFollowUpAsync() =>
        ExecuteActionAsync("follow-up", "Invoice sent for follow-up.");

    private async Task ExecuteActionAsync(string action, string successMessage)
    {
        if (AccessState.CompanyId is not Guid companyId || !SelectedInvoiceId.HasValue)
        {
            return;
        }

        IsSubmittingAction = true;
        DetailErrorMessage = null;
        ActionStatusMessage = null;

        try
        {
            SelectedReviewDetail = NormalizeDetail(await FinanceApiClient.SubmitInvoiceReviewActionAsync(companyId, SelectedInvoiceId.Value, action));
            ActionStatusMessage = successMessage;
            await LoadReviewsAsync(companyId, Filters);
        }
        catch (FinanceApiValidationException ex)
        {
            DetailErrorMessage = ResolveValidationMessage(ex, "Status");
        }
        catch (FinanceApiException ex)
        {
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            IsSubmittingAction = false;
        }
    }

    private Task HandleSortChangedAsync(string column)
    {
        if (string.Equals(SortColumn, column, StringComparison.Ordinal))
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = string.Equals(column, nameof(FinanceInvoiceReviewListItemResponse.Amount), StringComparison.Ordinal) ||
                string.Equals(column, nameof(FinanceInvoiceReviewListItemResponse.Confidence), StringComparison.Ordinal) ||
                string.Equals(column, nameof(FinanceInvoiceReviewListItemResponse.LastUpdatedUtc), StringComparison.Ordinal) ||
                string.Equals(column, nameof(FinanceInvoiceReviewListItemResponse.RiskLevel), StringComparison.Ordinal);
        }

        return Task.CompletedTask;
    }

    private Task NavigateToFiltersAsync(InvoiceReviewFilterState nextFilters)
    {
        Filters = nextFilters.Normalize();
        var nextUri = Navigation.GetUriWithQueryParameters(Filters.ToQueryParameters(AccessState.CompanyId));
        if (!string.Equals(Navigation.Uri, nextUri, StringComparison.Ordinal))
        {
            Navigation.NavigateTo(nextUri);
        }

        return Task.CompletedTask;
    }

    private InvoiceReviewFilterState CreateFilterState() =>
        new InvoiceReviewFilterState
        {
            Status = Status,
            Supplier = Supplier,
            RiskLevel = RiskLevel,
            RecommendationOutcome = Outcome
        }.Normalize();

    private FinanceInvoiceReviewDetailResponse? NormalizeDetail(FinanceInvoiceReviewDetailResponse? detail)
    {
        if (detail is null)
        {
            return null;
        }

        detail.RecommendationDetails = InvoiceWorkflowPresentation.ResolveRecommendationDetails(detail);
        detail.WorkflowHistory = InvoiceWorkflowPresentation.NormalizeWorkflowHistory(detail.WorkflowHistory).ToList();
        var serverActions = detail.Actions ?? new FinanceInvoiceReviewActionAvailabilityResponse();
        var hasApprovalPermission = VirtualCompany.Shared.FinanceAccess.CanApproveInvoices(AccessState.MembershipRole);
        var isActionable = hasApprovalPermission && serverActions.IsActionable;
        detail.Actions = new FinanceInvoiceReviewActionAvailabilityResponse
        {
            IsActionable = isActionable,
            CanApprove = isActionable && serverActions.CanApprove,
            CanReject = isActionable && serverActions.CanReject,
            CanSendForFollowUp = isActionable && serverActions.CanSendForFollowUp
        };

        return detail;
    }

    private static string ResolveValidationMessage(FinanceApiValidationException exception, string key) =>
        exception.Errors.TryGetValue(key, out var directErrors) && directErrors.Length > 0 ? directErrors[0] : exception.Message;

    private IEnumerable<FinanceInvoiceReviewListItemResponse> ApplyQuickFilters(IEnumerable<FinanceInvoiceReviewListItemResponse> items)
    {
        IEnumerable<FinanceInvoiceReviewListItemResponse> query = items;

        if (HighRiskOnly)
        {
            query = query.Where(IsHighRisk);
        }

        if (NeedsApprovalOnly)
        {
            query = query.Where(NeedsAttention);
        }

        if (LowConfidenceOnly)
        {
            query = query.Where(x => x.Confidence < 0.65m);
        }

        return query;
    }

    private IEnumerable<FinanceInvoiceReviewListItemResponse> ApplySort(IEnumerable<FinanceInvoiceReviewListItemResponse> items)
    {
        Func<FinanceInvoiceReviewListItemResponse, object> selector = SortColumn switch
        {
            nameof(FinanceInvoiceReviewListItemResponse.InvoiceNumber) => item => item.InvoiceNumber,
            nameof(FinanceInvoiceReviewListItemResponse.SupplierName) => item => item.SupplierName,
            nameof(FinanceInvoiceReviewListItemResponse.Amount) => item => item.Amount,
            nameof(FinanceInvoiceReviewListItemResponse.RiskLevel) => item => GetRiskRank(item.RiskLevel),
            nameof(FinanceInvoiceReviewListItemResponse.Confidence) => item => item.Confidence,
            nameof(FinanceInvoiceReviewListItemResponse.LastUpdatedUtc) => item => item.LastUpdatedUtc,
            _ => item => item.Status
        };

        return SortDescending
            ? items.OrderByDescending(selector).ThenBy(x => x.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(selector).ThenBy(x => x.InvoiceNumber, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsHighRisk(FinanceInvoiceReviewListItemResponse item) =>
        GetRiskRank(item.RiskLevel) >= 3;

    private static bool NeedsAttention(FinanceInvoiceReviewListItemResponse item) =>
        IsHighRisk(item) ||
        item.Confidence < 0.65m ||
        string.Equals(item.Status, "pending_approval", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.RecommendationOutcome, "request_human_approval", StringComparison.OrdinalIgnoreCase);

    private static int GetRiskRank(string? riskLevel) =>
        riskLevel?.Trim().ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };

    private static string ResolveSummaryCurrencyLabel(IReadOnlyList<FinanceInvoiceReviewListItemResponse> items)
    {
        var currencies = items
            .Select(x => x.Currency)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return currencies.Count switch
        {
            0 => "USD",
            1 => currencies[0],
            _ => "Mixed"
        };
    }
}
