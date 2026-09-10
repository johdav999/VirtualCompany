namespace VirtualCompany.Domain.Entities;

public sealed class SalesNarrationAttempt : ICompanyOwnedEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
    public Guid ApprovalRevisionId { get; set; }
    public int Number { get; set; }
    public string Status { get; set; } = SalesNarrationAsset.Generating;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public int ReservedTokens { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int GeneratedMilliseconds { get; set; }
    public string? ProviderResponseId { get; set; }
    public string? UsageJson { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public string? RateVersion { get; set; }
}

