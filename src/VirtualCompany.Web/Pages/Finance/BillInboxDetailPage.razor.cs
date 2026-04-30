using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BillInboxDetailPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [Parameter] public Guid BillId { get; set; }

    private FinanceBillInboxDetailResponse? Detail { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsSubmittingAction { get; set; }
    private string? DetailErrorMessage { get; set; }
    private string? ActionStatusMessage { get; set; }
    private string Rationale { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Detail = null;
        DetailErrorMessage = null;
        ActionStatusMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadAsync(companyId);
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
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try
        {
            Detail = await FinanceApiClient.GetBillInboxDetailAsync(companyId, BillId);
        }
        catch (FinanceApiException ex)
        {
            Detail = null;
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private Task ApproveAsync() => SubmitActionAsync((companyId, rationale) => FinanceApiClient.ApproveBillInboxItemAsync(companyId, BillId, rationale), "Approval recorded. No payment was initiated.");
    private Task RejectAsync() => SubmitActionAsync((companyId, rationale) => FinanceApiClient.RejectBillInboxItemAsync(companyId, BillId, rationale), "Rejection recorded.");
    private Task RequestClarificationAsync() => SubmitActionAsync((companyId, rationale) => FinanceApiClient.RequestBillInboxClarificationAsync(companyId, BillId, rationale), "Clarification request recorded.");

    private async Task SubmitActionAsync(Func<Guid, string, Task<FinanceBillReviewActionResultResponse>> action, string successMessage)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Rationale))
        {
            ActionStatusMessage = "Enter a rationale before recording the review action.";
            return;
        }

        IsSubmittingAction = true;
        ActionStatusMessage = null;

        try
        {
            await action(companyId, Rationale.Trim());
            Rationale = string.Empty;
            ActionStatusMessage = successMessage;
            await LoadAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            ActionStatusMessage = ex.Message;
        }
        finally
        {
            IsSubmittingAction = false;
        }
    }

    private RenderFragment RenderWarnings(string title, IReadOnlyList<FinanceBillWarningResponse> warnings) => builder =>
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", "mb-3");
        builder.OpenElement(2, "h3");
        builder.AddAttribute(3, "class", "h6");
        builder.AddContent(4, title);
        builder.CloseElement();

        if (warnings.Count == 0)
        {
            builder.OpenElement(5, "p");
            builder.AddAttribute(6, "class", "text-body-secondary small mb-0");
            builder.AddContent(7, "No warnings.");
            builder.CloseElement();
        }
        else
        {
            builder.OpenElement(8, "ul");
            builder.AddAttribute(9, "class", "list-unstyled mb-0");
            foreach (var warning in warnings)
            {
                builder.OpenElement(10, "li");
                builder.AddAttribute(11, "class", "mb-2");
                builder.OpenElement(12, "span");
                builder.AddAttribute(13, "class", "badge text-bg-warning me-2");
                builder.AddContent(14, warning.Severity);
                builder.CloseElement();
                builder.AddContent(15, warning.Message);
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    };

    private static string FormatAmount(decimal? amount, string? currency) =>
        amount.HasValue ? $"{currency ?? string.Empty} {amount.Value.ToString("N2", CultureInfo.InvariantCulture)}".Trim() : "n/a";

    private static string FormatDate(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "n/a";
    private static string FormatDateTime(DateTime value) => value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private static string FormatConfidence(decimal? value) => value.HasValue ? $"({value.Value:P0})" : string.Empty;

    private static string FormatEvidenceLocation(FinanceBillEvidenceReferenceResponse evidence) =>
        string.Join(" / ", new[] { evidence.PageReference, evidence.SectionReference, evidence.TextSpan }.Where(x => !string.IsNullOrWhiteSpace(x)));
}