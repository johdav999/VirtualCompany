using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingPeriodsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private IReadOnlyList<AccountingFiscalYearResponse> Years { get; set; } = [];
    private AccountingPeriodResponse? Selected { get; set; }
    private AccountingFiscalYearPreviewResponse? FiscalYearPreview { get; set; }
    private DateOnly NewFiscalYearStart { get; set; } = new(DateTime.Today.Year + 1, 1, 1);
    private bool IsPeriodsLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsPreviewLoading { get; set; }
    private bool IsSaving { get; set; }
    private bool ShowCreate { get; set; }
    private string? ListError { get; set; }
    private string? ActionError { get; set; }
    private string? ActionMessage { get; set; }
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private AccountingFiscalYearResponse? CurrentFiscalYear => Years.FirstOrDefault(year => year.StartDate <= DateOnly.FromDateTime(DateTime.Today) && year.EndDate >= DateOnly.FromDateTime(DateTime.Today)) ?? Years.FirstOrDefault();

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId)
        {
            await LoadYearsAsync(companyId, selectFirst: true);
        }
    }

    private async Task LoadYearsAsync(Guid companyId, bool selectFirst)
    {
        IsPeriodsLoading = true;
        ListError = null;
        try
        {
            Years = await FinanceApiClient.GetAccountingFiscalYearsAsync(companyId);
            var latestEnd = Years.Select(year => (DateOnly?)year.EndDate).Max();
            if (latestEnd.HasValue)
            {
                NewFiscalYearStart = latestEnd.Value.AddDays(1);
            }

            if (selectFirst && Selected is null)
            {
                var first = Years.SelectMany(year => year.Periods).FirstOrDefault(period => !period.IsClosed && !period.IsReportingLocked)
                    ?? Years.SelectMany(year => year.Periods).FirstOrDefault();
                if (first is not null)
                {
                    await SelectPeriodAsync(first.Id);
                }
            }
        }
        catch (FinanceApiException exception)
        {
            Years = [];
            ListError = exception.Message;
        }
        finally
        {
            IsPeriodsLoading = false;
        }
    }

    private async Task SelectPeriodAsync(Guid periodId)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        ShowCreate = false;
        IsDetailLoading = true;
        ActionError = null;
        try
        {
            Selected = await FinanceApiClient.GetAccountingPeriodAsync(companyId, periodId);
        }
        catch (FinanceApiException exception)
        {
            ActionError = exception.Message;
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private Task HandleRowKeyAsync(KeyboardEventArgs args, Guid periodId) =>
        args.Key is "Enter" or " " ? SelectPeriodAsync(periodId) : Task.CompletedTask;

    private void ToggleCreate()
    {
        ShowCreate = !ShowCreate;
        FiscalYearPreview = null;
        ActionError = null;
    }

    private async Task PreviewFiscalYearAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsPreviewLoading = true;
        ActionError = null;
        try
        {
            FiscalYearPreview = await FinanceApiClient.PreviewAccountingFiscalYearAsync(companyId, new PreviewAccountingFiscalYearApiRequest { FiscalYearStart = NewFiscalYearStart });
        }
        catch (FinanceApiException exception)
        {
            FiscalYearPreview = null;
            ActionError = exception.Message;
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    private async Task CreateFiscalYearAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId || FiscalYearPreview?.IsValid != true)
        {
            return;
        }

        IsSaving = true;
        ActionError = null;
        ActionMessage = null;
        try
        {
            var result = await FinanceApiClient.CreateAccountingFiscalYearAsync(companyId, new CreateAccountingFiscalYearApiRequest
            {
                FiscalYearStart = NewFiscalYearStart,
                IdempotencyKey = $"fiscal-year:{companyId:N}:{NewFiscalYearStart:yyyyMMdd}"
            });
            ShowCreate = false;
            FiscalYearPreview = null;
            ActionMessage = result.WasAlreadyPresent ? FinanceText["FiscalYearAlreadyExists"] : FinanceText["FiscalYearCreated"];
            Selected = result.FiscalYear.Periods.FirstOrDefault();
            await LoadYearsAsync(companyId, selectFirst: Selected is null);
        }
        catch (FinanceApiException exception)
        {
            ActionError = exception.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            ActionError = null;
            await LoadYearsAsync(companyId, selectFirst: Selected is null);
        }
    }

    private string FormatTimestamp(DateTime? value) => value.HasValue ? LocalDateTime.DateTime(value.Value) : FinanceText["NotRecorded"];
    private string FormatFiscalYearDates(AccountingFiscalYearResponse? fiscalYear) => fiscalYear is null ? FinanceText["NoFiscalYearAvailable"] : $"{fiscalYear.StartDate:d} – {fiscalYear.EndDate:d}";
    private static string PeriodStatusKey(AccountingPeriodResponse period) => period.IsReportingLocked ? "ReportingLocked" : period.IsClosed ? "Closed" : "Open";
}
