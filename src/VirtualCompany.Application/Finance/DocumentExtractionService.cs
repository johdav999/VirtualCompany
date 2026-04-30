using System.Globalization;
using System.Text.RegularExpressions;

namespace VirtualCompany.Application.Finance;

public sealed class DocumentExtractionService : IDocumentExtractionService
{
    private static readonly RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly FieldPattern[] FieldPatterns =
    [
        new("supplierName", @"(?:supplier|vendor|from|leverant(?:o|ö)r)\s*[:\-]\s*(?<value>[^\r\n]{2,120})", BillFieldConfidence.High),
        new("supplierOrgNumber", @"(?:org(?:anisation)?(?:\.| number| no)?|organisationsnummer|vat no|tax id)\s*[:\-]?\s*(?<value>(?:SE)?\d{6}[- ]?\d{4}|\d{10,12})", BillFieldConfidence.High),
        new("invoiceNumber", @"(?:invoice\s*(?:number|no|#)?|inv\s*#|faktura(?:nummer|nr)?|bill\s*(?:number|no)?)\s*[:#\-]?\s*(?<value>[A-Z0-9][A-Z0-9\-\/]{2,63})", BillFieldConfidence.High),
        new("invoiceDate", @"(?:invoice\s*date|issued|fakturadatum|datum)\s*[:\-]?\s*(?<value>\d{4}[-\/.]\d{1,2}[-\/.]\d{1,2}|\d{1,2}[-\/.]\d{1,2}[-\/.]\d{2,4})", BillFieldConfidence.High),
        new("dueDate", @"(?:due\s*date|payment\s*due|pay\s*by|förfallodatum|forfallodatum|senast\s*betala)\s*[:\-]?\s*(?<value>\d{4}[-\/.]\d{1,2}[-\/.]\d{1,2}|\d{1,2}[-\/.]\d{1,2}[-\/.]\d{2,4})", BillFieldConfidence.High),
        new("currency", @"\b(?<value>SEK|EUR|USD|GBP|NOK|DKK)\b|(?<value>kr)\b", BillFieldConfidence.Medium),
        new("totalAmount", @"(?:total\s*(?:amount)?|amount\s*(?:to\s*)?pay|amount\s*due|att\s*betala|summa(?:\s*att\s*betala)?|totalt)\s*[:\-]?\s*(?<value>(?:SEK|EUR|USD|GBP|NOK|DKK|kr)?\s*-?\d[\d\s.,']*)", BillFieldConfidence.High),
        new("vatAmount", @"(?:vat|moms)\s*[:\-]?\s*(?<value>-?\d[\d\s.,']*)", BillFieldConfidence.High),
        new("paymentReference", @"(?:ocr|payment\s*reference|reference|referens|meddelande)\s*[:#\-]?\s*(?<value>[A-Z0-9][A-Z0-9\- ]{2,64})", BillFieldConfidence.High),
        new("bankgiro", @"(?:bankgiro|bg)\s*[:\-]?\s*(?<value>\d{3,4}[- ]?\d{4})", BillFieldConfidence.High),
        new("plusgiro", @"(?:plusgiro|pg)\s*[:\-]?\s*(?<value>\d{2,8}[- ]?\d{1,4})", BillFieldConfidence.High),
        new("iban", @"(?:iban)\s*[:\-]?\s*(?<value>[A-Z]{2}\d{2}[A-Z0-9 ]{8,30})", BillFieldConfidence.High),
        new("bic", @"(?:bic|swift)\s*[:\-]?\s*(?<value>[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}(?:[A-Z0-9]{3})?)", BillFieldConfidence.High)
    ];

    private readonly IReadOnlyList<IDocumentTextExtractor> _textExtractors;
    private readonly IBillDuplicateCheckRepository _duplicateChecks;
    private readonly TimeProvider _timeProvider;
    private readonly IBillInformationExtractor _billInformationExtractor;
    private readonly IBillExtractionPersistenceRepository? _billExtractionPersistence;

    public DocumentExtractionService(
        IEnumerable<IDocumentTextExtractor> textExtractors,
        IBillDuplicateCheckRepository duplicateChecks,
        TimeProvider timeProvider)
    {
        _textExtractors = textExtractors.ToArray();
        _duplicateChecks = duplicateChecks;
        _timeProvider = timeProvider;
        _billInformationExtractor = new BillInformationExtractor(duplicateChecks, timeProvider);
        _billExtractionPersistence = null;
    }

    public DocumentExtractionService(
        IEnumerable<IDocumentTextExtractor> textExtractors,
        IBillDuplicateCheckRepository duplicateChecks,
        TimeProvider timeProvider,
        IBillInformationExtractor billInformationExtractor,
        IBillExtractionPersistenceRepository? billExtractionPersistence = null)
    {
        _textExtractors = textExtractors.ToArray();
        _duplicateChecks = duplicateChecks;
        _timeProvider = timeProvider;
        _billInformationExtractor = billInformationExtractor;
        _billExtractionPersistence = billExtractionPersistence;
    }

    public async Task<DocumentExtractionResult> ExtractAsync(
        ExtractBillDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(command));
        }

        var extractor = _textExtractors.FirstOrDefault(x => x.Supports(command.InputType))
            ?? throw new UnsupportedBillDocumentException($"No text extractor supports '{command.InputType}'.");

        var document = await extractor.ExtractAsync(
            command.Content,
            command.SourceDocumentName,
            command.InputType,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(document.FullText))
        {
            return new DocumentExtractionResult(command.CompanyId, []);
        }

        var candidate = await _billInformationExtractor.ExtractAsync(
            new DetectedBillCandidateCommand(
                command.CompanyId,
                document,
                command.SourceDocumentName,
                command.SourceEmailId,
                command.SourceAttachmentId,
                command.SourceDocumentId),
            cancellationToken);

        if (_billExtractionPersistence is not null)
        {
            await _billExtractionPersistence.PersistAsync(
                new PersistNormalizedBillExtractionCommand(command.CompanyId, candidate),
                cancellationToken);
        }

        return new DocumentExtractionResult(command.CompanyId, [candidate]);
    }

    private static RawBillCandidate ExtractCandidate(
        ExtractedDocumentText document,
        ExtractBillDocumentCommand command)
    {
        var evidence = new List<BillFieldEvidenceDto>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in FieldPatterns)
        {
            var match = Regex.Match(document.FullText, pattern.Pattern, MatchOptions);
            if (!match.Success)
            {
                continue;
            }

            var value = NormalizeValue(match.Groups["value"].Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            values.TryAdd(pattern.FieldName, value);
            var section = FindSection(document, match.Index);
            evidence.Add(new BillFieldEvidenceDto(
                pattern.FieldName,
                value,
                command.SourceDocumentId ?? command.SourceDocumentName,
                document.SourceDocumentType,
                section.Reference,
                $"{match.Index}:{match.Length}",
                "regex_label_match",
                pattern.Confidence,
                BuildSnippet(document.FullText, match.Index, match.Length)));
        }

        if (!values.ContainsKey("supplierName"))
        {
            var inferredSupplier = InferSupplierName(document.FullText);
            if (!string.IsNullOrWhiteSpace(inferredSupplier))
            {
                values["supplierName"] = inferredSupplier;
                evidence.Add(new BillFieldEvidenceDto(
                    "supplierName",
                    inferredSupplier,
                    command.SourceDocumentId ?? command.SourceDocumentName,
                    document.SourceDocumentType,
                    document.Sections.FirstOrDefault()?.Reference ?? "document",
                    "line:1",
                    "header_inference",
                    BillFieldConfidence.Medium,
                    inferredSupplier));
            }
        }

        if (!values.ContainsKey("currency"))
        {
            values["currency"] = InferCurrency(document.FullText) ?? "SEK";
        }

        return new RawBillCandidate(
            Get(values, "supplierName"),
            NormalizeOrgNumber(Get(values, "supplierOrgNumber")),
            Get(values, "invoiceNumber"),
            ParseDate(Get(values, "invoiceDate")),
            ParseDate(Get(values, "dueDate")),
            NormalizeCurrency(Get(values, "currency")),
            ParseAmount(Get(values, "totalAmount")),
            ParseAmount(Get(values, "vatAmount")),
            Get(values, "paymentReference"),
            NormalizeDigits(Get(values, "bankgiro"), keepHyphen: true),
            NormalizeDigits(Get(values, "plusgiro"), keepHyphen: true),
            NormalizeIban(Get(values, "iban")),
            NormalizeBic(Get(values, "bic")),
            evidence);
    }

    private List<BillValidationFindingDto> Validate(RawBillCandidate candidate)
    {
        var findings = new List<BillValidationFindingDto>();
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        if (candidate.TotalAmount is null)
        {
            findings.Add(new BillValidationFindingDto(
                "missing_amount",
                "Total amount is missing.",
                BillValidationSeverity.Rejection,
                "totalAmount"));
        }

        if (candidate.DueDate is null || candidate.DueDate.Value < today.AddYears(-2) || candidate.DueDate.Value > today.AddYears(5))
        {
            findings.Add(new BillValidationFindingDto(
                "invalid_due_date",
                "Due date is missing or outside the accepted range.",
                BillValidationSeverity.Rejection,
                "dueDate"));
        }

        if (!string.IsNullOrWhiteSpace(candidate.Bankgiro) && !IsValidBankgiro(candidate.Bankgiro))
        {
            findings.Add(new BillValidationFindingDto(
                "invalid_bankgiro",
                "Bankgiro format is invalid.",
                BillValidationSeverity.Rejection,
                "bankgiro"));
        }

        if (!string.IsNullOrWhiteSpace(candidate.Iban) && !IsValidIban(candidate.Iban))
        {
            findings.Add(new BillValidationFindingDto(
                "invalid_iban",
                "IBAN checksum or format is invalid.",
                BillValidationSeverity.Rejection,
                "iban"));
        }

        if (candidate.VatAmount is < 0)
        {
            findings.Add(new BillValidationFindingDto(
                "implausible_vat",
                "VAT amount cannot be negative for extracted bills.",
                BillValidationSeverity.Warning,
                "vatAmount"));
        }

        if (candidate.TotalAmount is not null && candidate.VatAmount is not null)
        {
            if (candidate.VatAmount > candidate.TotalAmount)
            {
                findings.Add(new BillValidationFindingDto(
                    "implausible_vat",
                    "VAT amount cannot exceed total amount.",
                    BillValidationSeverity.Rejection,
                    "vatAmount"));
            }
            else if (candidate.TotalAmount > 0 && candidate.VatAmount / candidate.TotalAmount > 0.35m)
            {
                findings.Add(new BillValidationFindingDto(
                    "implausible_vat_ratio",
                    "VAT amount is high relative to total amount.",
                    BillValidationSeverity.Warning,
                    "vatAmount"));
            }
        }

        if (string.IsNullOrWhiteSpace(candidate.InvoiceNumber))
        {
            findings.Add(new BillValidationFindingDto(
                "missing_invoice_number",
                "Invoice number is missing.",
                BillValidationSeverity.Warning,
                "invoiceNumber"));
        }

        return findings;
    }

    private static BillExtractionConfidence Score(
        RawBillCandidate candidate,
        IReadOnlyList<BillValidationFindingDto> findings,
        BillDuplicateCheckResult duplicateCheck)
    {
        if (findings.Any(x => x.Severity == BillValidationSeverity.Rejection) || duplicateCheck.IsDuplicate)
        {
            return BillExtractionConfidence.Low;
        }

        var keyFields = new[]
        {
            candidate.SupplierName,
            candidate.InvoiceNumber,
            candidate.Currency,
            candidate.DueDate?.ToString("O", CultureInfo.InvariantCulture),
            candidate.TotalAmount?.ToString(CultureInfo.InvariantCulture)
        };

        var strongSupplierSignal = !string.IsNullOrWhiteSpace(candidate.SupplierOrgNumber) ||
            candidate.Evidence.Any(x => x.FieldName == "supplierName" && x.ExtractionMethod == "regex_label_match");

        if (keyFields.All(x => !string.IsNullOrWhiteSpace(x)) &&
            strongSupplierSignal &&
            findings.Count == 0)
        {
            return BillExtractionConfidence.High;
        }

        return BillExtractionConfidence.Medium;
    }

    private static ExtractedDocumentSection FindSection(ExtractedDocumentText document, int absoluteOffset) =>
        document.Sections.LastOrDefault(x => x.StartOffset <= absoluteOffset)
        ?? document.Sections.FirstOrDefault()
        ?? new ExtractedDocumentSection("document", document.FullText, 0);

    private static string? InferSupplierName(string text)
    {
        foreach (var line in text.Split('\n').Select(x => x.Trim()))
        {
            if (line.Length is >= 2 and <= 120 &&
                !line.Contains("invoice", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("faktura", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return null;
    }

    private static string? InferCurrency(string text)
    {
        var match = Regex.Match(text, @"\b(SEK|EUR|USD|GBP|NOK|DKK)\b", MatchOptions);
        return match.Success ? NormalizeCurrency(match.Value) : null;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyy.MM.dd",
            "dd-MM-yyyy",
            "dd/MM/yyyy",
            "dd.MM.yyyy",
            "dd-MM-yy",
            "dd/MM/yy",
            "dd.MM.yy"
        };

        return DateOnly.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
               DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
            ? parsed
            : null;
    }

    private static decimal? ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value, @"[^\d,.\-]", string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Contains(',') && normalized.Contains('.'))
        {
            normalized = normalized.LastIndexOf(',') > normalized.LastIndexOf('.')
                ? normalized.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : normalized.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (normalized.Contains(','))
        {
            normalized = normalized.Replace(',', '.');
        }

        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string NormalizeValue(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    private static string? NormalizeOrgNumber(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static string? NormalizeCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized == "KR" ? "SEK" : normalized;
    }

    private static string? NormalizeDigits(string? value, bool keepHyphen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var allowed = keepHyphen ? @"[^\d\-]" : @"\D";
        return Regex.Replace(value, allowed, string.Empty);
    }

    private static string? NormalizeIban(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Regex.Replace(value, @"\s+", string.Empty).ToUpperInvariant();

    private static string? NormalizeBic(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();

    private static bool IsValidBankgiro(string value)
    {
        var digits = Regex.Replace(value, @"\D", string.Empty);
        return digits.Length is 7 or 8;
    }

    private static bool IsValidIban(string value)
    {
        var iban = NormalizeIban(value);
        if (iban is null || iban.Length < 15 || iban.Length > 34 || !Regex.IsMatch(iban, @"^[A-Z]{2}\d{2}[A-Z0-9]+$"))
        {
            return false;
        }

        var rearranged = iban[4..] + iban[..4];
        var checksum = 0;
        foreach (var ch in rearranged)
        {
            var token = char.IsLetter(ch)
                ? (char.ToUpperInvariant(ch) - 'A' + 10).ToString(CultureInfo.InvariantCulture)
                : ch.ToString();

            foreach (var digit in token)
            {
                checksum = (checksum * 10 + (digit - '0')) % 97;
            }
        }

        return checksum == 1;
    }

    private static string BuildSnippet(string text, int index, int length)
    {
        var start = Math.Max(0, index - 40);
        var end = Math.Min(text.Length, index + length + 40);
        return Regex.Replace(text[start..end], @"\s+", " ").Trim();
    }

    private sealed record FieldPattern(
        string FieldName,
        string Pattern,
        BillFieldConfidence Confidence);

    private sealed record RawBillCandidate(
        string? SupplierName,
        string? SupplierOrgNumber,
        string? InvoiceNumber,
        DateOnly? InvoiceDate,
        DateOnly? DueDate,
        string? Currency,
        decimal? TotalAmount,
        decimal? VatAmount,
        string? PaymentReference,
        string? Bankgiro,
        string? Plusgiro,
        string? Iban,
        string? Bic,
        IReadOnlyList<BillFieldEvidenceDto> Evidence);
}
