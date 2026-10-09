namespace VirtualCompany.Application.Finance;
public sealed record FinancePaymentDto(Guid Id, Guid CompanyId, string PaymentType, decimal Amount, string Currency, DateTime PaymentDate, string Method, string Status, string CounterpartyReference, DateTime CreatedUtc, DateTime UpdatedUtc, IReadOnlyList<NormalizedFinanceInsightDto> AgentInsights, string Source = "simulation")
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public string PaymentType { get; set; } = PaymentType;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public DateTime PaymentDate { get; set; } = PaymentDate;
    public string Method { get; set; } = Method;
    public string Status { get; set; } = Status;
    public string CounterpartyReference { get; set; } = CounterpartyReference;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public IReadOnlyList<NormalizedFinanceInsightDto> AgentInsights { get; set; } = AgentInsights;
    public string Source { get; set; } = Source;

    public FinancePaymentDto() : this(default !, default !, string.Empty, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, default !, default !, [], string.Empty)
    {
    }
}
