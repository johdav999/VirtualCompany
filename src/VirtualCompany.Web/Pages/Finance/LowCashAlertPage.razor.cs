using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class LowCashAlertPage : FinancePageBase
{
    [Inject] private ExecutiveCockpitApiClient ExecutiveCockpitApiClient { get; set; } = default!;
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [Parameter] public Guid AlertId { get; set; }

    private ExecutiveCockpitFinanceAlertDetailViewModel? detail;
    private bool isDetailLoading;
    private string? detailErrorMessage;
    private string? financeActionStatusMessage;
    private string? financeActionErrorMessage;
    private string? activeFinanceActionKey;

    private bool IsFinanceActionRunning(string actionKey) =>
        string.Equals(activeFinanceActionKey, actionKey, StringComparison.OrdinalIgnoreCase);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        detail = null;
        detailErrorMessage = null;
        financeActionStatusMessage = null;
        financeActionErrorMessage = null;
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid)
        {
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        isDetailLoading = true;
        detailErrorMessage = null;

        try
        {
            detail = await ExecutiveCockpitApiClient.GetFinanceAlertDetailAsync(companyId, AlertId);
        }
        catch (OnboardingApiException ex)
        {
            detail = null;
            detailErrorMessage = ex.Message;
        }
        finally
        {
            isDetailLoading = false;
        }
    }

    private async Task ExecuteFinanceActionAsync(ExecutiveCockpitFinanceActionViewModel action)
    {
        if (AccessState.CompanyId is not Guid companyId || !action.IsEnabled)
        {
            return;
        }

        activeFinanceActionKey = action.Key;
        financeActionStatusMessage = null;
        financeActionErrorMessage = null;

        try
        {
            switch (action.Key)
            {
                case "review_invoice" when action.TargetId is Guid invoiceId:
                    await FinanceApiClient.StartInvoiceReviewWorkflowAsync(companyId, invoiceId);
                    financeActionStatusMessage = "Invoice review started.";
                    break;
                case "inspect_anomaly" when action.TargetId is Guid transactionId:
                    await FinanceApiClient.EvaluateTransactionAnomalyAsync(companyId, transactionId);
                    financeActionStatusMessage = "Issue review submitted.";
                    break;
                case "view_cash_position":
                    await FinanceApiClient.EvaluateCashPositionAsync(companyId);
                    financeActionStatusMessage = "Cash position refreshed.";
                    break;
            }

            await ReloadAsync();
            if (!string.IsNullOrWhiteSpace(action.Route))
            {
                Navigation.NavigateTo(action.Route);
            }
        }
        catch (FinanceApiException ex)
        {
            financeActionErrorMessage = ex.Message;
        }
        finally
        {
            activeFinanceActionKey = null;
        }
    }
}
