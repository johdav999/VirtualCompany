using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BillExtractionPersistenceRepository : IBillExtractionPersistenceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

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
        for (var attempt = 0; attempt < 2; attempt++)
        {
            _dbContext.ChangeTracker.Clear();
            var persistedAt = _timeProvider.GetUtcNow().UtcDateTime;
            var extraction = CreateExtraction(command.CompanyId, candidate, persistedAt);

            _dbContext.NormalizedBillExtractions.Add(extraction);
            await PersistDetectedBillAsync(command.CompanyId, candidate, persistedAt, cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return extraction.Id;
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        return await PersistExtractionOnlyAsync(command.CompanyId, candidate, cancellationToken);
    }

    private async Task<Guid> PersistExtractionOnlyAsync(
        Guid companyId,
        NormalizedBillCandidateDto candidate,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        var persistedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var extraction = CreateExtraction(companyId, candidate, persistedAt);
        _dbContext.NormalizedBillExtractions.Add(extraction);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return extraction.Id;
    }

    private static NormalizedBillExtraction CreateExtraction(
        Guid companyId,
        NormalizedBillCandidateDto candidate,
        DateTime persistedAt) =>
        new(
            Guid.NewGuid(),
            companyId,
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

    private async Task PersistDetectedBillAsync(
        Guid companyId,
        NormalizedBillCandidateDto candidate,
        DateTime persistedAt,
        CancellationToken cancellationToken)
    {
        var existingDetectedBill = await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId &&
                    x.SourceEmailId == candidate.SourceEmailId &&
                    x.SourceAttachmentId == candidate.SourceAttachmentId)
            .OrderBy(x => x.CreatedUtc)
            .Select(x => new ExistingDetectedBillSnapshot(
                x.Id,
                x.SupplierName,
                x.SupplierOrgNumber,
                x.InvoiceNumber,
                x.InvoiceDateUtc,
                x.DueDateUtc,
                x.Currency,
                x.TotalAmount,
                x.VatAmount,
                x.PaymentReference,
                x.Bankgiro,
                x.Plusgiro,
                x.Iban,
                x.Bic))
            .FirstOrDefaultAsync(cancellationToken);
        if (existingDetectedBill is not null)
        {
            await RefreshDetectedBillAsync(companyId, existingDetectedBill, candidate, persistedAt, cancellationToken);
            return;
        }

        var bill = new DetectedBill(
            Guid.NewGuid(),
            companyId,
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
            ToConfidenceScore(candidate.Confidence),
            ToStorageValue(candidate.Confidence),
            ToStorageValue(candidate.ValidationStatus),
            candidate.RequiresReview ? "required" : "not_required",
            candidate.RequiresReview,
            candidate.IsEligibleForApprovalProposal,
            candidate.ValidationStatusPersisted,
            JsonSerializer.Serialize(candidate.ValidationFindings, JsonOptions),
            candidate.SourceEmailId,
            candidate.SourceAttachmentId,
            candidate.DuplicateCheck.Id == Guid.Empty ? null : candidate.DuplicateCheck.Id,
            candidate.ValidationStatusPersisted ? persistedAt : null,
            persistedAt,
            persistedAt);

        AddFields(bill, companyId, candidate, persistedAt);

        _dbContext.DetectedBills.Add(bill);
    }

    private async Task RefreshDetectedBillAsync(
        Guid companyId,
        ExistingDetectedBillSnapshot existingBill,
        NormalizedBillCandidateDto candidate,
        DateTime persistedAt,
        CancellationToken cancellationToken)
    {
        var supplierName = PreferNewValue(existingBill.SupplierName, candidate.SupplierName);
        var supplierOrgNumber = PreferNewValue(existingBill.SupplierOrgNumber, candidate.SupplierOrgNumber);
        var invoiceNumber = PreferInvoiceNumber(existingBill.InvoiceNumber, candidate.InvoiceNumber);
        var invoiceDateUtc = existingBill.InvoiceDateUtc ?? ToUtcDate(candidate.InvoiceDate);
        var dueDateUtc = existingBill.DueDateUtc ?? ToUtcDate(candidate.DueDate);
        var currency = PreferNewValue(existingBill.Currency, candidate.Currency);
        var totalAmount = PreferCandidateAmount(existingBill.TotalAmount, candidate.TotalAmount);
        var vatAmount = PreferCandidateAmount(existingBill.VatAmount, candidate.VatAmount);
        var paymentReference = PreferNewValue(existingBill.PaymentReference, candidate.PaymentReference);
        var bankgiro = PreferNewValue(existingBill.Bankgiro, candidate.Bankgiro);
        var plusgiro = PreferNewValue(existingBill.Plusgiro, candidate.Plusgiro);
        var iban = PreferNewValue(existingBill.Iban, candidate.Iban);
        var bic = PreferNewValue(existingBill.Bic, candidate.Bic);
        var confidence = ToConfidenceScore(candidate.Confidence);
        var confidenceLevel = ToStorageValue(candidate.Confidence);
        var validationStatus = ToStorageValue(candidate.ValidationStatus);
        var reviewStatus = candidate.RequiresReview ? "required" : "not_required";
        var validationIssuesJson = JsonSerializer.Serialize(candidate.ValidationFindings, JsonOptions);
        Guid? duplicateCheckId = candidate.DuplicateCheck.Id == Guid.Empty ? null : candidate.DuplicateCheck.Id;
        var validationStatusPersistedAtUtc = candidate.ValidationStatusPersisted ? persistedAt : (DateTime?)null;

        await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Id == existingBill.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.SupplierName, supplierName)
                    .SetProperty(x => x.SupplierOrgNumber, supplierOrgNumber)
                    .SetProperty(x => x.InvoiceNumber, invoiceNumber)
                    .SetProperty(x => x.InvoiceDateUtc, invoiceDateUtc)
                    .SetProperty(x => x.DueDateUtc, dueDateUtc)
                    .SetProperty(x => x.Currency, currency)
                    .SetProperty(x => x.TotalAmount, totalAmount)
                    .SetProperty(x => x.VatAmount, vatAmount)
                    .SetProperty(x => x.PaymentReference, paymentReference)
                    .SetProperty(x => x.Bankgiro, bankgiro)
                    .SetProperty(x => x.Plusgiro, plusgiro)
                    .SetProperty(x => x.Iban, iban)
                    .SetProperty(x => x.Bic, bic)
                    .SetProperty(x => x.Confidence, confidence)
                    .SetProperty(x => x.ConfidenceLevel, confidenceLevel)
                    .SetProperty(x => x.ValidationStatus, validationStatus)
                    .SetProperty(x => x.ReviewStatus, reviewStatus)
                    .SetProperty(x => x.RequiresReview, candidate.RequiresReview)
                    .SetProperty(x => x.IsEligibleForApprovalProposal, candidate.ValidationStatusPersisted && candidate.IsEligibleForApprovalProposal)
                    .SetProperty(x => x.ValidationStatusPersisted, candidate.ValidationStatusPersisted)
                    .SetProperty(x => x.ValidationIssuesJson, validationIssuesJson)
                    .SetProperty(x => x.DuplicateCheckId, duplicateCheckId)
                    .SetProperty(x => x.ValidationStatusPersistedAtUtc, validationStatusPersistedAtUtc)
                    .SetProperty(x => x.UpdatedUtc, persistedAt),
                cancellationToken);

        await RefreshFieldsAsync(companyId, existingBill.Id, candidate, persistedAt, cancellationToken);
    }

    private async Task RefreshFieldsAsync(
        Guid companyId,
        Guid detectedBillId,
        NormalizedBillCandidateDto candidate,
        DateTime persistedAt,
        CancellationToken cancellationToken)
    {
        var existingFieldNames = (await _dbContext.DetectedBillFields
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.DetectedBillId == detectedBillId)
            .Select(x => x.FieldName)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var evidence in candidate.Evidence
            .GroupBy(x => x.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First()))
        {
            var field = CreateField(companyId, detectedBillId, evidence, persistedAt);
            if (existingFieldNames.Contains(evidence.FieldName))
            {
                await UpdateFieldAsync(companyId, detectedBillId, field, cancellationToken);
                continue;
            }

            _dbContext.DetectedBillFields.Add(field);
            existingFieldNames.Add(evidence.FieldName);
        }
    }

    private async Task UpdateFieldAsync(
        Guid companyId,
        Guid detectedBillId,
        DetectedBillField field,
        CancellationToken cancellationToken)
    {
        await _dbContext.DetectedBillFields
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId &&
                x.DetectedBillId == detectedBillId &&
                x.FieldName == field.FieldName)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.RawValue, field.RawValue)
                    .SetProperty(x => x.NormalizedValue, field.NormalizedValue)
                    .SetProperty(x => x.SourceDocument, field.SourceDocument)
                    .SetProperty(x => x.SourceDocumentType, field.SourceDocumentType)
                    .SetProperty(x => x.PageReference, field.PageReference)
                    .SetProperty(x => x.SectionReference, field.SectionReference)
                    .SetProperty(x => x.TextSpan, field.TextSpan)
                    .SetProperty(x => x.Locator, field.Locator)
                    .SetProperty(x => x.ExtractionMethod, field.ExtractionMethod)
                    .SetProperty(x => x.FieldConfidence, field.FieldConfidence)
                    .SetProperty(x => x.Snippet, field.Snippet),
                cancellationToken);
    }

    private static void AddFields(
        DetectedBill bill,
        Guid companyId,
        NormalizedBillCandidateDto candidate,
        DateTime persistedAt)
    {
        foreach (var evidence in candidate.Evidence
            .GroupBy(x => x.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First()))
        {
            var field = CreateField(companyId, bill.Id, evidence, persistedAt);
            var existingField = bill.Fields.FirstOrDefault(field =>
                string.Equals(field.FieldName, evidence.FieldName, StringComparison.OrdinalIgnoreCase));

            if (existingField is not null)
            {
                existingField.ReplaceExtraction(
                    field.RawValue,
                    field.NormalizedValue,
                    field.SourceDocument,
                    field.SourceDocumentType,
                    field.PageReference,
                    field.SectionReference,
                    field.TextSpan,
                    field.Locator,
                    field.ExtractionMethod,
                    field.FieldConfidence,
                    field.Snippet);
                continue;
            }

            bill.AddField(field);
        }
    }

    private static DetectedBillField CreateField(
        Guid companyId,
        Guid detectedBillId,
        BillFieldEvidenceDto evidence,
        DateTime persistedAt)
    {
        var pageReference = evidence.PageOrSectionReference.StartsWith("page:", StringComparison.OrdinalIgnoreCase)
            ? evidence.PageOrSectionReference
            : null;
        var sectionReference = evidence.PageOrSectionReference.StartsWith("page:", StringComparison.OrdinalIgnoreCase)
            ? null
            : evidence.PageOrSectionReference;

        return new DetectedBillField(
            Guid.NewGuid(),
            companyId,
            detectedBillId,
            evidence.FieldName,
            evidence.ExtractedValue,
            evidence.ExtractedValue,
            evidence.SourceDocument,
            evidence.SourceDocumentType,
            pageReference,
            sectionReference,
            evidence.TextSpanOrLocator,
            null,
            evidence.ExtractionMethod,
            ToConfidenceScore(evidence.Confidence),
            evidence.Snippet,
            persistedAt);
    }

    private static bool ShouldRefreshDetectedBill(
        DetectedBill existingBill,
        NormalizedBillCandidateDto candidate) =>
        HasNewValue(existingBill.SupplierName, candidate.SupplierName) ||
        HasNewValue(existingBill.SupplierOrgNumber, candidate.SupplierOrgNumber) ||
        HasImprovedInvoiceNumber(existingBill.InvoiceNumber, candidate.InvoiceNumber) ||
        (!existingBill.InvoiceDateUtc.HasValue && candidate.InvoiceDate.HasValue) ||
        (!existingBill.DueDateUtc.HasValue && candidate.DueDate.HasValue) ||
        HasNewValue(existingBill.Currency, candidate.Currency) ||
        HasDifferentAmount(existingBill.TotalAmount, candidate.TotalAmount) ||
        HasDifferentAmount(existingBill.VatAmount, candidate.VatAmount) ||
        HasNewValue(existingBill.PaymentReference, candidate.PaymentReference) ||
        HasNewValue(existingBill.Bankgiro, candidate.Bankgiro) ||
        HasNewValue(existingBill.Plusgiro, candidate.Plusgiro) ||
        HasNewValue(existingBill.Iban, candidate.Iban) ||
        HasNewValue(existingBill.Bic, candidate.Bic);

    private static bool HasNewValue(string? existingValue, string? candidateValue)
    {
        if (string.IsNullOrWhiteSpace(candidateValue))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(existingValue))
        {
            return true;
        }

        return candidateValue.Length < existingValue.Length &&
            !string.Equals(existingValue, candidateValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasImprovedInvoiceNumber(string? existingValue, string? candidateValue)
    {
        if (HasNewValue(existingValue, candidateValue))
        {
            return true;
        }

        return IsLikelyMisreadInvoiceLabel(existingValue) &&
            !IsLikelyMisreadInvoiceLabel(candidateValue) &&
            !string.IsNullOrWhiteSpace(candidateValue);
    }

    private static string? PreferNewValue(string? existingValue, string? candidateValue) =>
        HasNewValue(existingValue, candidateValue) ? candidateValue : existingValue;

    private static string? PreferInvoiceNumber(string? existingValue, string? candidateValue) =>
        HasImprovedInvoiceNumber(existingValue, candidateValue) ? candidateValue : existingValue;

    private static decimal? PreferCandidateAmount(decimal? existingValue, decimal? candidateValue) =>
        candidateValue ?? existingValue;

    private static bool HasDifferentAmount(decimal? existingValue, decimal? candidateValue) =>
        candidateValue.HasValue &&
        (!existingValue.HasValue || decimal.Round(existingValue.Value, 2) != decimal.Round(candidateValue.Value, 2));

    private static bool IsLikelyMisreadInvoiceLabel(string? value) =>
        string.Equals(value?.Trim(), "Supplier", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), "Customer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), "Invoice", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), "Vendor", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), "Leverantor", StringComparison.OrdinalIgnoreCase);

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

    private static decimal ToConfidenceScore(BillExtractionConfidence confidence) =>
        confidence switch
        {
            BillExtractionConfidence.High => 0.95m,
            BillExtractionConfidence.Medium => 0.75m,
            _ => 0.4m
        };

    private static decimal ToConfidenceScore(BillFieldConfidence confidence) =>
        confidence switch
        {
            BillFieldConfidence.High => 0.95m,
            BillFieldConfidence.Medium => 0.75m,
            _ => 0.4m
        };

    private sealed record ExistingDetectedBillSnapshot(
        Guid Id,
        string? SupplierName,
        string? SupplierOrgNumber,
        string? InvoiceNumber,
        DateTime? InvoiceDateUtc,
        DateTime? DueDateUtc,
        string? Currency,
        decimal? TotalAmount,
        decimal? VatAmount,
        string? PaymentReference,
        string? Bankgiro,
        string? Plusgiro,
        string? Iban,
        string? Bic);
}
