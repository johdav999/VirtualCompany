namespace VirtualCompany.Application.Finance;
public sealed record NativeReceivablesReadinessDto(Guid CompanyId, string Status, bool IsReady, DateTime EvaluatedUtc, int BlockingCheckCount, int AttentionCheckCount, int HealthyCheckCount, IReadOnlyList<NativeReceivablesReadinessSignalDto> Signals)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string Status { get; set; } = Status;
    public bool IsReady { get; set; } = IsReady;
    public DateTime EvaluatedUtc { get; set; } = EvaluatedUtc;
    public int BlockingCheckCount { get; set; } = BlockingCheckCount;
    public int AttentionCheckCount { get; set; } = AttentionCheckCount;
    public int HealthyCheckCount { get; set; } = HealthyCheckCount;
    public IReadOnlyList<NativeReceivablesReadinessSignalDto> Signals { get; set; } = Signals;

    public NativeReceivablesReadinessDto() : this(default !, "blocking", default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record NativeReceivablesReadinessSignalDto(string Key, string Status, int Count, decimal? Amount, string? Currency, string Explanation, string OperatorAction, IReadOnlyList<Guid> SubjectIds)
{
    public string Key { get; set; } = Key;
    public string Status { get; set; } = Status;
    public int Count { get; set; } = Count;
    public decimal? Amount { get; set; } = Amount;
    public string? Currency { get; set; } = Currency;
    public string Explanation { get; set; } = Explanation;
    public string OperatorAction { get; set; } = OperatorAction;
    public IReadOnlyList<Guid> SubjectIds { get; set; } = SubjectIds;

    public NativeReceivablesReadinessSignalDto() : this("", "healthy", default !, default !, default !, "", "", [])
    {
    }
}
