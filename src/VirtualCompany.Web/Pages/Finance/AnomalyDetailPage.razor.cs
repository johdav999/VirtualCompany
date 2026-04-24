using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AnomalyDetailPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [Parameter] public Guid AnomalyId { get; set; }

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

    private FinanceAnomalyDetailResponse? Detail { get; set; }
    private FinanceAnomalyFilterState ReturnState { get; set; } = new();
    private bool IsDetailLoading { get; set; }
    private string? DetailErrorMessage { get; set; }

    private string BackToListHref
    {
        get
        {
            var query = ReturnState.Normalize().ToQueryString(AccessState.CompanyId);
            return string.IsNullOrWhiteSpace(query)
                ? FinanceRoutes.WithCompanyContext(FinanceRoutes.Anomalies, AccessState.CompanyId)
                : $"{FinanceRoutes.Anomalies}?{query}";
        }
    }

    private string TransactionHref => Detail?.AffectedRecord is not null
        ? FinanceRoutes.BuildTransactionDetailPath(Detail.AffectedRecord.Id, AccessState.CompanyId)
        : FinanceRoutes.WithCompanyContext(FinanceRoutes.Anomalies, AccessState.CompanyId);
    private string? InvoiceHref => Detail?.RelatedInvoiceId is Guid invoiceId ? FinanceRoutes.BuildInvoiceDetailPath(invoiceId, AccessState.CompanyId) : null;
    private string? BillHref => Detail?.RelatedBillId is Guid billId ? FinanceRoutes.BuildBillDetailPath(billId, AccessState.CompanyId) : null;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        ReturnState = new FinanceAnomalyFilterState
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

        Detail = null;
        DetailErrorMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
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

        IsDetailLoading = true;
        DetailErrorMessage = null;

        try
        {
            Detail = await FinanceApiClient.GetAnomalyDetailAsync(companyId, AnomalyId);
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

    private string BuildTaskHref(Guid taskId)
    {
        var path = $"/tasks?companyId={AccessState.CompanyId:D}&taskId={taskId:D}";
        return ReturnUrlNavigation.AppendReturnUrl(path, BackToListHref);
    }

    private string? BuildRecordHref(FinanceAnomalyRecordLinkResponse record) =>
        record.RecordId is not Guid recordId || recordId == Guid.Empty
            ? null
            : record.RecordType.Trim().ToLowerInvariant() switch
            {
                "transaction" => FinanceRoutes.BuildTransactionDetailPath(recordId, AccessState.CompanyId),
                "invoice" => FinanceRoutes.BuildInvoiceDetailPath(recordId, AccessState.CompanyId),
                "bill" => FinanceRoutes.BuildBillDetailPath(recordId, AccessState.CompanyId),
                _ => null
            };
}