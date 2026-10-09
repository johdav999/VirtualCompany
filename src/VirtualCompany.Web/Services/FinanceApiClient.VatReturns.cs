namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public async Task<IReadOnlyList<VatFilingPeriodResponse>> GetVatFilingPeriodsAsync(Guid companyId,
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<VatFilingPeriodResponse>>(companyId,
            $"internal/companies/{companyId}/finance/accounting/vat/filing-periods", false, cancellationToken) ?? [];

    public Task<VatFilingPeriodResponse> CreateVatFilingPeriodAsync(Guid companyId, string periodCode,
        DateOnly startDate, DateOnly endDate, Guid? fiscalPeriodId, CancellationToken cancellationToken = default,
        DateOnly? dueDate = null)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, VatFilingPeriodResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/vat/filing-periods",
            new { periodCode, startDate, endDate, currency = "SEK", fiscalPeriodId, dueDate }, cancellationToken);
    }

    public async Task<IReadOnlyList<VatReturnResponse>> GetVatReturnsAsync(Guid companyId,
        Guid? filingPeriodId = null, CancellationToken cancellationToken = default) =>
        await GetAsync<List<VatReturnResponse>>(companyId,
            $"internal/companies/{companyId}/finance/accounting/vat/returns{(filingPeriodId.HasValue ? $"?filingPeriodId={filingPeriodId:D}" : string.Empty)}",
            false, cancellationToken) ?? [];

    public Task<VatFilingPeriodResponse> SetVatFilingPeriodDueDateAsync(Guid companyId,Guid filingPeriodId,DateOnly dueDate,CancellationToken cancellationToken=default){EnsureOnlineMutation();return SendCompanyScopedAsync<object,VatFilingPeriodResponse>(companyId,HttpMethod.Put,$"internal/companies/{companyId}/finance/accounting/vat/filing-periods/{filingPeriodId:D}/due-date",new{dueDate},cancellationToken);}

    public Task<VatReturnResponse?> GetVatReturnAsync(Guid companyId, Guid vatReturnId,
        CancellationToken cancellationToken = default) => GetAsync<VatReturnResponse>(companyId,
        $"internal/companies/{companyId}/finance/accounting/vat/returns/{vatReturnId:D}", false, cancellationToken);

    public Task<VatReturnResponse> CalculateVatReturnAsync(Guid companyId, Guid filingPeriodId,
        Guid? vatReturnId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, VatReturnResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/vat/returns/calculate",
            new { filingPeriodId, vatReturnId, idempotencyKey }, cancellationToken);
    }

    public Task<VatReturnResponse> RequestVatReturnApprovalAsync(Guid companyId, Guid vatReturnId,
        string expectedInputHash, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, VatReturnResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/vat/returns/{vatReturnId:D}/approval",
            new { expectedInputHash }, cancellationToken);
    }

    public Task<VatReturnResponse> FinalizeVatReturnAsync(Guid companyId, Guid vatReturnId,
        string expectedInputHash, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, VatReturnResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/vat/returns/{vatReturnId:D}/finalize",
            new { expectedInputHash }, cancellationToken);
    }

    public Task<VatReturnResponse> CreateVatReturnCorrectionAsync(Guid companyId, Guid vatReturnId,
        string reason, string evidenceReference, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, VatReturnResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/vat/returns/{vatReturnId:D}/corrections",
            new { reason, evidenceReference, idempotencyKey }, cancellationToken);
    }

    public static string GetVatReturnPackageDownloadUrl(Guid companyId, Guid vatReturnId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(companyId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(vatReturnId, Guid.Empty);
        return $"internal/companies/{companyId}/finance/accounting/vat/returns/{vatReturnId:D}/package";
    }
}
