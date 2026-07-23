namespace VirtualCompany.Infrastructure.Observability;

public interface ICorrelationContextAccessor
{
    string? CorrelationId { get; set; }
}

public sealed class RequestCorrelationContextAccessor : ICorrelationContextAccessor
{
    public string? CorrelationId { get; set; }
}