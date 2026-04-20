using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class InvoiceReviewDetailPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [Parameter] public Guid InvoiceId { get; set; }

    [SupplyParameterFromQuery(Name = "status")]
    public string? Status { get; set; }

    [SupplyParameterFromQuery(Name = "supplier")]
    public string? Supplier { get; set; }

    [SupplyParameterFromQuery(Name = "riskLevel")]
    public string? RiskLevel { get; set; }

    [SupplyParameterFromQuery(Name = "outcome")]
    public string? Outcome { get; set; }

    private FinanceInvoiceReviewDetailResponse? ReviewDetail { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsSubmittingAction { get; set; }
    private string? DetailErrorMessage { get; set; }
    private string? ActionStatusMessage { get; set; }
    private string BackToListHref => BuildBackToListHref();
    private string? SourceInvoiceHref =>
        ReviewDetail?.SourceInvoiceId is Guid sourceInvoiceId && sourceInvoiceId != Guid.Empty
            ? FinanceRoutes.BuildInvoiceDetailPath(sourceInvoiceId, AccessState.CompanyId)
            : null;
    private string? RelatedApprovalHref =>
        ReviewDetail?.RelatedApprovalId is Guid approvalId && AccessState.CompanyId is Guid companyId
            ? $"/approvals?companyId={companyId:D}&approvalId={approvalId:D}"
            : null;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        ReviewDetail = null;
        DetailErrorMessage = null;
        ActionStatusMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadDetailAsync(companyId);
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadDetailAsync(companyId);
        }
    }

    private async Task LoadDetailAsync(Guid companyId)
    {
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try
        {
            ReviewDetail = NormalizeDetail(await FinanceApiClient.GetInvoiceReviewDetailAsync(companyId, InvoiceId));
        }
        catch (FinanceApiException ex)
        {
            ReviewDetail = null;
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private Task ApproveAsync() =>
        ExecuteActionAsync("approve", "Invoice approved.");

    private Task RejectAsync() =>
        ExecuteActionAsync("reject", "Invoice rejected.");

    private Task SendForFollowUpAsync() =>
        ExecuteActionAsync("follow-up", "Invoice sent for follow-up.");

    private async Task ExecuteActionAsync(string action, string successMessage)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsSubmittingAction = true;
        DetailErrorMessage = null;
        ActionStatusMessage = null;

        try
        {
            ReviewDetail = NormalizeDetail(await FinanceApiClient.SubmitInvoiceReviewActionAsync(companyId, InvoiceId, action));
            ActionStatusMessage = successMessage;
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

    private string BuildBackToListHref()
    {
        var query = new InvoiceReviewFilterState
        {
            Status = Status,
            Supplier = Supplier,
            RiskLevel = RiskLevel,
            RecommendationOutcome = Outcome
        }.Normalize().ToQueryString(AccessState.CompanyId);

        return string.IsNullOrWhiteSpace(query)
            ? FinanceRoutes.WithCompanyContext(FinanceRoutes.Reviews, AccessState.CompanyId)
            : $"{FinanceRoutes.Reviews}?{query}";
    }

    private FinanceInvoiceReviewDetailResponse? NormalizeDetail(FinanceInvoiceReviewDetailResponse? detail)
    {
        if (detail is null)
        {
            return null;
        }

        detail.RecommendationDetails = InvoiceWorkflowPresentation.ResolveRecommendationDetails(detail);
        detail.WorkflowHistory = InvoiceWorkflowPresentation
            .NormalizeWorkflowHistory(detail.WorkflowHistory)
            .ToList();
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
}
