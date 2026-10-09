namespace VirtualCompany.Application.CustomerMemory;

public interface ICustomerMemoryService
{
    Task<CustomerMemoryContext?> GetContextAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken);
    Task<CustomerMemoryContext?> RefreshProfileAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken);
    Task<OfferEligibilityResult> EvaluateOfferEligibilityAsync(Guid companyId, Guid contactId, string offerKey, TimeSpan lookbackWindow, CancellationToken cancellationToken);
}

public sealed record OfferEligibilityResult(
    bool CanSend,
    string OfferKey,
    DateTime LookbackStartUtc,
    string? BlockReason,
    IReadOnlyList<OfferExposureMemory> MatchingExposures)
{
    public static OfferEligibilityResult Allowed(string offerKey, DateTime lookbackStartUtc) =>
        new(true, offerKey, lookbackStartUtc, null, []);

    public static OfferEligibilityResult Blocked(string offerKey, DateTime lookbackStartUtc, IReadOnlyList<OfferExposureMemory> exposures)
    {
        var reason = exposures.Count == 1
            ? $"This contact already received or discussed this offer on {exposures[0].OccurredUtc:yyyy-MM-dd}."
            : $"This contact already received or discussed this offer {exposures.Count} times in the lookback window.";

        return new(false, offerKey, lookbackStartUtc, reason, exposures);
    }
}

public sealed class CustomerMemoryOptions
{
    public const string SectionName = "CustomerMemory";
    public int DuplicateOfferLookbackDays { get; set; } = 90;
}