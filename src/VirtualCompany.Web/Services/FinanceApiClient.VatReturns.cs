namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public async Task<IReadOnlyList<VatFilingPeriodResponse>> GetVatFilingPeriodsAsync(Guid companyId,
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<VatFilingPeriodResponse>>(companyId,
            $"internal/companies/{companyId}/finance/accounting/vat/filing-periods", false, cancellationToken) ?? [];

    public Task<VatFilingPeriodResponse> CreateVatFilingPeriodAsync(Guid companyId, string periodCode,
        DateOnly startDate, DateOnly endDate, Guid? fiscalPeriodId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, VatFilingPeriodResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/vat/filing-periods",
            new { periodCode, startDate, endDate, currency = "SEK", fiscalPeriodId }, cancellationToken);
    }

    public async Task<IReadOnlyList<VatReturnResponse>> GetVatReturnsAsync(Guid companyId,
        Guid? filingPeriodId = null, CancellationToken cancellationToken = default) =>
        await GetAsync<List<VatReturnResponse>>(companyId,
            $"internal/companies/{companyId}/finance/accounting/vat/returns{(filingPeriodId.HasValue ? $"?filingPeriodId={filingPeriodId:D}" : string.Empty)}",
            false, cancellationToken) ?? [];

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

public sealed class VatFilingPeriodResponse
{
    public Guid Id { get; set; }
    public string PeriodCode { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid? FiscalPeriodId { get; set; }
}

public sealed class VatReturnResponse
{
    public Guid Id { get; set; }
    public Guid FilingPeriodId { get; set; }
    public string PeriodCode { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsStale { get; set; }
    public bool IsSuperseded { get; set; }
    public Guid? CorrectionOfVatReturnId { get; set; }
    public string? CorrectionReason { get; set; }
    public string? CorrectionEvidenceReference { get; set; }
    public DateTime? CutoffUtc { get; set; }
    public string? InputHash { get; set; }
    public string? CalculationChecksum { get; set; }
    public int IncludedSourceCount { get; set; }
    public int ExcludedSourceCount { get; set; }
    public decimal OutputVatExact { get; set; }
    public decimal InputVatExact { get; set; }
    public decimal SettlementExact { get; set; }
    public long SettlementFilingAmount { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public string? ApprovalStatus { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public DateTime? FinalizedUtc { get; set; }
    public string? PackageChecksum { get; set; }
    public string? PackageFileName { get; set; }
    public string? PackageMediaType { get; set; }
    public long? PackageContentLength { get; set; }
    public bool CanDownloadPackage { get; set; }
    public List<VatReturnBoxResultResponse> Boxes { get; set; } = [];
    public List<VatReturnSourceContributionResponse> Contributions { get; set; } = [];
    public List<VatReturnValidationIssueResponse> Issues { get; set; } = [];
    public List<VatReturnReviewResponse> Reviews { get; set; } = [];
    public List<string> AllowedActions { get; set; } = [];
}

public sealed class VatReturnBoxResultResponse
{
    public string BoxCode { get; set; } = string.Empty;
    public string FactType { get; set; } = string.Empty;
    public decimal ExactAmount { get; set; }
    public long FilingAmount { get; set; }
    public int SourceCount { get; set; }
}

public sealed class VatReturnSourceContributionResponse
{
    public Guid Id { get; set; }
    public Guid LedgerEntryId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string BoxCode { get; set; } = string.Empty;
    public string FactType { get; set; } = string.Empty;
    public decimal ExactAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public sealed class VatReturnValidationIssueResponse
{
    public string Code { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public Guid? LedgerEntryId { get; set; }
    public string? SourceReference { get; set; }
    public decimal? Difference { get; set; }
}

public sealed class VatReturnReviewResponse
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public string EvidenceHash { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; }
}
