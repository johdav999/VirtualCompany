namespace VirtualCompany.Application.BackgroundExecution;

public sealed record BackgroundExecutionIdentity
{
    public BackgroundExecutionIdentity(Guid companyId, string correlationId, string idempotencyKey)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("CorrelationId is required.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(idempotencyKey));
        }

        CompanyId = companyId;
        CorrelationId = correlationId.Trim();
        IdempotencyKey = idempotencyKey.Trim();
    }

    public Guid CompanyId { get; }
    public string CorrelationId { get; }
    public string IdempotencyKey { get; }
}

public interface IBackgroundExecutionIdentityFactory
{
    string CreateCorrelationId();

    string EnsureCorrelationId(string? correlationId);

    string CreateIdempotencyKey(string operationName, params object?[] stableSegments);

    BackgroundExecutionIdentity Create(
        Guid companyId,
        string operationName,
        string? correlationId,
        params object?[] stableSegments);

    BackgroundExecutionIdentity FromExisting(
        Guid companyId,
        string correlationId,
        string idempotencyKey);
}