using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public sealed class SupportMemoryObservation : ICompanyOwnedEntity
{
    private SupportMemoryObservation() { }

    public SupportMemoryObservation(
        Guid id,
        Guid companyId,
        Guid supportCaseId,
        Guid supportCaseResolutionId,
        Guid contactId,
        string status,
        string? value,
        string evidenceSummary,
        decimal confidence,
        DateTime observedUtc,
        DateTime? validUntilUtc,
        string policyVersion,
        string sourceEventKey)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        SupportCaseResolutionId = supportCaseResolutionId == Guid.Empty ? throw new ArgumentException("SupportCaseResolutionId is required.", nameof(supportCaseResolutionId)) : supportCaseResolutionId;
        ContactId = contactId == Guid.Empty ? throw new ArgumentException("ContactId is required.", nameof(contactId)) : contactId;
        Status = SupportMemoryObservationStatuses.Normalize(status);
        Value = value is null ? null : SupportEntityText.NormalizeRequired(value, nameof(value), 1000);
        EvidenceSummary = SupportEntityText.NormalizeRequired(evidenceSummary, nameof(evidenceSummary), 500);
        Confidence = confidence is < 0 or > 1 ? throw new ArgumentOutOfRangeException(nameof(confidence)) : decimal.Round(confidence, 3, MidpointRounding.AwayFromZero);
        ObservedUtc = SupportEntityText.NormalizeUtc(observedUtc, nameof(observedUtc));
        ValidUntilUtc = validUntilUtc is null ? null : SupportEntityText.NormalizeUtc(validUntilUtc.Value, nameof(validUntilUtc));
        PolicyVersion = SupportEntityText.NormalizeRequired(policyVersion, nameof(policyVersion), 40);
        SourceEventKey = SupportEntityText.NormalizeRequired(sourceEventKey, nameof(sourceEventKey), 200);
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public Guid SupportCaseResolutionId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid? CustomerMemoryProfilePreferenceId { get; private set; }
    public string Status { get; private set; } = null!;
    public string? Value { get; private set; }
    public string EvidenceSummary { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public DateTime? ValidUntilUtc { get; private set; }
    public string PolicyVersion { get; private set; } = null!;
    public string SourceEventKey { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Approve(Guid preferenceId)
    {
        CustomerMemoryProfilePreferenceId = preferenceId == Guid.Empty ? throw new ArgumentException("PreferenceId is required.", nameof(preferenceId)) : preferenceId;
        Status = SupportMemoryObservationStatuses.Approved;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkReviewRequired() { Status = SupportMemoryObservationStatuses.Review; UpdatedUtc = DateTime.UtcNow; }
    public void Reject() { Status = SupportMemoryObservationStatuses.Rejected; Value = null; UpdatedUtc = DateTime.UtcNow; }
    public void Expire() { Status = SupportMemoryObservationStatuses.Expired; UpdatedUtc = DateTime.UtcNow; }
    public void Delete() { Status = SupportMemoryObservationStatuses.Deleted; Value = null; UpdatedUtc = DateTime.UtcNow; }
}

