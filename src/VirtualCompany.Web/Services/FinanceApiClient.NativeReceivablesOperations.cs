namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<NativeReceivablesReadinessResponse?> GetNativeReceivablesReadinessAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<NativeReceivablesReadinessResponse>(companyId,
            $"api/companies/{companyId:D}/finance/receivables/readiness",
            allowNotFound: false,
            cancellationToken);
}

public sealed class NativeReceivablesReadinessResponse
{
    public Guid CompanyId { get; set; }
    public string Status { get; set; } = "blocking";
    public bool IsReady { get; set; }
    public DateTime EvaluatedUtc { get; set; }
    public int BlockingCheckCount { get; set; }
    public int AttentionCheckCount { get; set; }
    public int HealthyCheckCount { get; set; }
    public List<NativeReceivablesReadinessSignalResponse> Signals { get; set; } = [];
}

public sealed class NativeReceivablesReadinessSignalResponse
{
    public string Key { get; set; } = "";
    public string Status { get; set; } = "healthy";
    public int Count { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string Explanation { get; set; } = "";
    public string OperatorAction { get; set; } = "";
    public List<Guid> SubjectIds { get; set; } = [];
}
