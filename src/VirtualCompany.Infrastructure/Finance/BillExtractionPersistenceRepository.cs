using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BillExtractionPersistenceRepository : IBillExtractionPersistenceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public BillExtractionPersistenceRepository(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> PersistAsync(
        PersistNormalizedBillExtractionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(command));
        }

        var candidate = command.Candidate;
        var persistedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var extraction = new NormalizedBillExtraction(
            Guid.NewGuid(),
            command.CompanyId,
            candidate.SupplierName,
            candidate.SupplierOrgNumber,
            candidate.InvoiceNumber,
            ToUtcDate(candidate.InvoiceDate),
            ToUtcDate(candidate.DueDate),
            candidate.Currency,
            candidate.TotalAmount,
            candidate.VatAmount,
            candidate.PaymentReference,
            candidate.Bankgiro,
            candidate.Plusgiro,
            candidate.Iban,
            candidate.Bic,
            ToStorageValue(candidate.Confidence),
            candidate.SourceEmailId,
            candidate.SourceAttachmentId,
            JsonSerializer.Serialize(candidate.Evidence, JsonOptions),
            ToStorageValue(candidate.ValidationStatus),
            JsonSerializer.Serialize(candidate.ValidationFindings, JsonOptions),
            candidate.DuplicateCheck.Id,
            candidate.RequiresReview,
            candidate.ValidationStatusPersisted && candidate.IsEligibleForApprovalProposal,
            persistedAt,
            persistedAt);

        _dbContext.NormalizedBillExtractions.Add(extraction);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return extraction.Id;
    }

    private static DateTime? ToUtcDate(DateOnly? value) =>
        value?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static string ToStorageValue(BillExtractionConfidence confidence) =>
        confidence switch
        {
            BillExtractionConfidence.High => "high",
            BillExtractionConfidence.Medium => "medium",
            _ => "low"
        };

    private static string ToStorageValue(BillValidationStatus status) =>
        status switch
        {
            BillValidationStatus.Valid => "valid",
            BillValidationStatus.Flagged => "flagged",
            BillValidationStatus.Rejected => "rejected",
            _ => "pending"
        };
}
