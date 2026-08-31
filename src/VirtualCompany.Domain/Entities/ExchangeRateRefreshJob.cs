using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ExchangeRateRefreshJob : ICompanyOwnedEntity
{
    private ExchangeRateRefreshJob() { }

    public ExchangeRateRefreshJob(Guid id, Guid companyId, Guid sourceId, string idempotencyKey,
        DateOnly requestedDate, string requestedCurrencies, Guid? requestedByUserId,
        string? correlationId, DateTime createdUtc)
    {
        Id = ExchangeRateText.Id(id, nameof(id));
        CompanyId = ExchangeRateText.Id(companyId, nameof(companyId));
        SourceId = ExchangeRateText.Id(sourceId, nameof(sourceId));
        if (requestedByUserId == Guid.Empty) throw new ArgumentException("Requested user cannot be empty.", nameof(requestedByUserId));
        IdempotencyKey = ExchangeRateText.Required(idempotencyKey, 200, nameof(idempotencyKey));
        RequestedDate = requestedDate;
        RequestedCurrencies = ExchangeRateText.Required(requestedCurrencies, 1000, nameof(requestedCurrencies));
        RequestedByUserId = requestedByUserId;
        CorrelationId = ExchangeRateText.Optional(correlationId, 128);
        Status = ExchangeRateRefreshJobStatuses.Queued;
        CreatedUtc = UpdatedUtc = ExchangeRateText.Utc(createdUtc);
        NextAttemptUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SourceId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public DateOnly RequestedDate { get; private set; }
    public string RequestedCurrencies { get; private set; } = null!;
    public Guid? RequestedByUserId { get; private set; }
    public string? CorrelationId { get; private set; }
    public string Status { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public string? FailureReasonCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public Guid? RateSetId { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public ExchangeRateSource Source { get; private set; } = null!;

    public bool TryClaim(string owner, DateTime nowUtc, TimeSpan leaseDuration)
    {
        var now = ExchangeRateText.Utc(nowUtc);
        if (Status is ExchangeRateRefreshJobStatuses.Completed or ExchangeRateRefreshJobStatuses.Failed ||
            NextAttemptUtc > now || LeaseExpiresUtc > now && !string.Equals(LeaseOwner, owner, StringComparison.Ordinal))
            return false;
        LeaseOwner = ExchangeRateText.Required(owner, 128, nameof(owner));
        LeaseExpiresUtc = now.Add(leaseDuration);
        NextAttemptUtc = null;
        Status = ExchangeRateRefreshJobStatuses.Running;
        AttemptCount++;
        Touch(now);
        return true;
    }

    public bool IsClaimedBy(string owner, DateTime nowUtc) =>
        Status == ExchangeRateRefreshJobStatuses.Running &&
        string.Equals(LeaseOwner, owner, StringComparison.Ordinal) &&
        LeaseExpiresUtc >= ExchangeRateText.Utc(nowUtc);

    public void Complete(string owner, Guid rateSetId, DateTime nowUtc)
    {
        RequireLease(owner, nowUtc);
        RateSetId = ExchangeRateText.Id(rateSetId, nameof(rateSetId));
        Status = ExchangeRateRefreshJobStatuses.Completed;
        LeaseOwner = null;
        LeaseExpiresUtc = NextAttemptUtc = null;
        FailureReasonCode = FailureSummary = null;
        Touch(nowUtc);
    }

    public void Retry(string owner, string reasonCode, string summary, DateTime nowUtc, TimeSpan delay)
    {
        RequireLease(owner, nowUtc);
        Status = ExchangeRateRefreshJobStatuses.RetryScheduled;
        FailureReasonCode = ExchangeRateText.Token(reasonCode, 96, nameof(reasonCode));
        FailureSummary = ExchangeRateText.Required(summary, 1000, nameof(summary));
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        NextAttemptUtc = ExchangeRateText.Utc(nowUtc).Add(delay);
        Touch(nowUtc);
    }

    public void Fail(string owner, string reasonCode, string summary, DateTime nowUtc)
    {
        RequireLease(owner, nowUtc);
        Status = ExchangeRateRefreshJobStatuses.Failed;
        FailureReasonCode = ExchangeRateText.Token(reasonCode, 96, nameof(reasonCode));
        FailureSummary = ExchangeRateText.Required(summary, 1000, nameof(summary));
        LeaseOwner = null;
        LeaseExpiresUtc = NextAttemptUtc = null;
        Touch(nowUtc);
    }

    private void RequireLease(string owner, DateTime nowUtc)
    {
        if (!IsClaimedBy(owner, nowUtc)) throw new InvalidOperationException("The exchange-rate refresh lease is no longer current.");
    }

    private void Touch(DateTime nowUtc) { UpdatedUtc = ExchangeRateText.Utc(nowUtc); Version++; }
}
