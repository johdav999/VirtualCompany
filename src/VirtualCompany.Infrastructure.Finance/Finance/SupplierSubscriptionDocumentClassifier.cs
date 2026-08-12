using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class SupplierSubscriptionDocumentClassifier : ISupplierSubscriptionDocumentClassifier
{
    private static readonly string[] AgreementKeywords = ["subscription", "agreement", "contract", "renewal", "notice period", "recurring service", "abonnemang", "avtal", "fornyelse", "uppsagningstid", "prenumeration"];
    private static readonly string[] ReceiptKeywords = ["receipt", "payment received", "paid", "kvitto", "betalning", "debitering"];
    private static readonly string[] InvoiceKeywords = ["invoice", "faktura", "due date", "forfallodatum", "bankgiro", "ocr"];
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISupplierSubscriptionIntakeProposalService _proposalService;
    private readonly ILogger<SupplierSubscriptionDocumentClassifier> _logger;

    public SupplierSubscriptionDocumentClassifier(
        VirtualCompanyDbContext dbContext,
        ISupplierSubscriptionIntakeProposalService proposalService,
        ILogger<SupplierSubscriptionDocumentClassifier> logger)
    {
        _dbContext = dbContext;
        _proposalService = proposalService;
        _logger = logger;
    }

    public async Task<SupplierSubscriptionSourceClassificationResultDto> ClassifyAsync(ClassifySupplierSubscriptionSourceCommand command, CancellationToken cancellationToken)
    {
        var snapshot = await _dbContext.EmailMessageSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Attachments)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.SourceEmailMessageSnapshotId, cancellationToken);
        if (snapshot is null)
        {
            throw new InvalidOperationException("The source email was not found for this company.");
        }

        var suppliers = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.CounterpartyType == "supplier")
            .ToListAsync(cancellationToken);

        var proposals = new List<SupplierSubscriptionIntakeProposalSummaryDto>();
        var receiptEvidenceCount = 0;
        var bodyCandidate = BuildCandidate(snapshot, null, snapshot.UntrustedBodyText);
        if (bodyCandidate is not null)
        {
            var outcome = await ClassifyCandidateAsync(command, bodyCandidate, suppliers, cancellationToken);
            if (outcome.Proposal is not null) proposals.Add(outcome.Proposal);
            receiptEvidenceCount += outcome.IsReceiptEvidence ? 1 : 0;
        }

        foreach (var attachment in snapshot.Attachments.Where(x => !x.IsDuplicateByHash && !string.IsNullOrWhiteSpace(x.UntrustedExtractedText)))
        {
            var candidate = BuildCandidate(snapshot, attachment, attachment.UntrustedExtractedText);
            if (candidate is null)
            {
                continue;
            }

            var outcome = await ClassifyCandidateAsync(command, candidate, suppliers, cancellationToken);
            if (outcome.Proposal is not null) proposals.Add(outcome.Proposal);
            receiptEvidenceCount += outcome.IsReceiptEvidence ? 1 : 0;
        }

        _logger.LogInformation(
            "Supplier subscription document classification completed. CompanyId: {CompanyId}. MessageId: {MessageId}. Proposals: {ProposalCount}. ReceiptEvidence: {ReceiptEvidenceCount}.",
            command.CompanyId,
            command.SourceEmailMessageSnapshotId,
            proposals.Count,
            receiptEvidenceCount);

        return new SupplierSubscriptionSourceClassificationResultDto(command.CompanyId, command.SourceEmailMessageSnapshotId, proposals.Count, receiptEvidenceCount, proposals);
    }

    private async Task<(SupplierSubscriptionIntakeProposalSummaryDto? Proposal, bool IsReceiptEvidence)> ClassifyCandidateAsync(
        ClassifySupplierSubscriptionSourceCommand command,
        SourceCandidate candidate,
        IReadOnlyList<FinanceCounterparty> suppliers,
        CancellationToken cancellationToken)
    {
        var text = candidate.Text;
        var agreementScore = CountKeywordHits(text, AgreementKeywords) * 25;
        var receiptScore = CountKeywordHits(text, ReceiptKeywords) * 20;
        var invoiceScore = CountKeywordHits(text, InvoiceKeywords) * 15;
        var recurringScore = ResolveCadence(text) is not null ? 25 : 0;
        var amountScore = ExtractAmount(text).Amount.HasValue ? 15 : 0;

        if (receiptScore >= 20 && agreementScore == 0)
        {
            return (null, true);
        }

        if (agreementScore == 0 || recurringScore + amountScore == 0 || agreementScore + recurringScore + amountScore < 45 || invoiceScore > agreementScore + recurringScore)
        {
            return (null, false);
        }

        var supplierMatch = MatchSupplier(candidate, suppliers);
        var amount = ExtractAmount(text);
        var cadence = ResolveCadence(text) ?? SupplierSubscriptionCadences.Monthly;
        var startDate = ExtractDate(text, ["start", "starts", "effective", "startdatum", "galler fran"]);
        var nextExpected = ExtractDate(text, ["next", "first billing", "next invoice", "nasta faktura", "forsta debitering"]) ?? startDate ?? DateTime.UtcNow.Date;
        var noticeDays = ExtractNoticePeriodDays(text) ?? 30;
        var confidence = Math.Clamp(agreementScore + recurringScore + amountScore + (supplierMatch.CounterpartyId.HasValue ? 15 : 0) - Math.Min(invoiceScore, 30), 35, 95);
        var agreementName = ExtractContractReference(text) ?? candidate.AttachmentName ?? candidate.Subject ?? supplierMatch.SupplierName ?? "Supplier subscription";
        var evidence = BuildEvidence(candidate, supplierMatch.SupplierName, amount.Amount, amount.Currency, cadence, confidence);
        var status = MissingRequiredTerms(supplierMatch.CounterpartyId, amount.Amount, amount.Currency, cadence)
            ? SupplierSubscriptionIntakeProposalStatuses.NeedsReview
            : SupplierSubscriptionIntakeProposalStatuses.NeedsReview;

        var detail = await _proposalService.RecordAsync(
            new RecordSupplierSubscriptionIntakeProposalCommand(
                command.CompanyId,
                command.SourceEmailMessageSnapshotId,
                candidate.AttachmentId,
                null,
                BuildFingerprint(command.CompanyId, command.SourceEmailMessageSnapshotId, candidate.AttachmentId, candidate.AttachmentHash, agreementName),
                SupplierSubscriptionIntakeProposalClassifications.Agreement,
                status,
                confidence,
                evidence,
                supplierMatch.SupplierName,
                supplierMatch.SupplierOrgNumber,
                new SupplierSubscriptionProposalTermsDto(
                    supplierMatch.CounterpartyId,
                    agreementName,
                    amount.Currency ?? "SEK",
                    amount.Amount,
                    cadence,
                    ExtractBillingDay(text) ?? nextExpected.Day,
                    startDate ?? nextExpected,
                    nextExpected,
                    0m,
                    5,
                    null,
                    ExtractContractReference(text),
                    "Detected from finance inbox agreement evidence.",
                    noticeDays,
                    DetectAutoRenewal(text),
                    null),
                null,
                command.ActorUserId,
                command.ActorDisplayName),
            cancellationToken);

        return (MapSummary(detail), false);
    }

    private static SourceCandidate? BuildCandidate(EmailMessageSnapshot snapshot, EmailAttachmentSnapshot? attachment, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var bounded = string.Join("\n", new[] { snapshot.Subject, attachment?.FileName, text }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (bounded.Length > 12000)
        {
            bounded = bounded[..12000];
        }

        return new SourceCandidate(snapshot.Subject, attachment?.Id, attachment?.FileName, attachment?.ContentHash, NormalizeForScanning(bounded));
    }

    private static (Guid? CounterpartyId, string? SupplierName, string? SupplierOrgNumber) MatchSupplier(SourceCandidate candidate, IReadOnlyList<FinanceCounterparty> suppliers)
    {
        var normalizedText = NormalizeForScanning(candidate.Text);
        var matches = suppliers
            .Select(supplier => new
            {
                Supplier = supplier,
                Score = (ContainsNormalized(normalizedText, supplier.Name) ? 2 : 0) + (!string.IsNullOrWhiteSpace(supplier.TaxId) && ContainsNormalized(normalizedText, supplier.TaxId) ? 3 : 0) + (!string.IsNullOrWhiteSpace(supplier.Email) && ContainsNormalized(normalizedText, supplier.Email) ? 1 : 0)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Supplier.Name)
            .ToList();

        if (matches.Count == 0)
        {
            return (null, ExtractSupplierName(candidate.Text), ExtractOrgNumber(candidate.Text));
        }

        if (matches.Count > 1 && matches[0].Score == matches[1].Score)
        {
            return (null, matches[0].Supplier.Name, matches[0].Supplier.TaxId);
        }

        return (matches[0].Supplier.Id, matches[0].Supplier.Name, matches[0].Supplier.TaxId);
    }

    private static int CountKeywordHits(string text, IEnumerable<string> keywords) =>
        keywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsNormalized(string text, string value) =>
        text.Contains(NormalizeForScanning(value), StringComparison.OrdinalIgnoreCase);

    private static string? ResolveCadence(string text)
    {
        if (ContainsAny(text, ["quarterly", "per quarter", "kvartal"])) return SupplierSubscriptionCadences.Quarterly;
        if (ContainsAny(text, ["yearly", "annual", "annually", "per year", "arlig", "arsvis"])) return SupplierSubscriptionCadences.Yearly;
        if (ContainsAny(text, ["monthly", "per month", "manad", "manadsvis", "abonnemang"])) return SupplierSubscriptionCadences.Monthly;
        return null;
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static (decimal? Amount, string? Currency) ExtractAmount(string text)
    {
        var matches = AmountRegex()
            .Matches(text)
            .Cast<Match>()
            .Where(x => decimal.TryParse(NormalizeDecimal(x.Groups["amount"].Value), NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            .ToList();
        var match = matches.FirstOrDefault(x => x.Groups["currency"].Success || x.Groups["currency2"].Success) ?? matches.LastOrDefault();
        if (match is null)
        {
            return (null, null);
        }

        var currency = match.Groups["currency"].Success
            ? match.Groups["currency"].Value.ToUpperInvariant()
            : match.Groups["currency2"].Success ? match.Groups["currency2"].Value.ToUpperInvariant() : "SEK";
        return decimal.TryParse(NormalizeDecimal(match.Groups["amount"].Value), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? (decimal.Round(amount, 2, MidpointRounding.AwayFromZero), currency)
            : (null, currency);
    }

    private static string NormalizeDecimal(string value) => value.Replace(" ", string.Empty).Replace(",", ".");

    private static int? ExtractBillingDay(string text)
    {
        var match = BillingDayRegex().Match(text);
        return match.Success && int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) && day is >= 1 and <= 31 ? day : null;
    }

    private static int? ExtractNoticePeriodDays(string text)
    {
        var match = NoticeRegex().Match(text);
        return match.Success && int.TryParse(match.Groups["days"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days is >= 0 and <= 730 ? days : null;
    }

    private static DateTime? ExtractDate(string text, IReadOnlyList<string> labels)
    {
        foreach (var label in labels)
        {
            var match = Regex.Match(text, $"{Regex.Escape(label)}[^0-9]{{0,24}}(?<date>20[0-9]{{2}}[- /.][0-9]{{1,2}}[- /.][0-9]{{1,2}})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success && DateTime.TryParse(match.Groups["date"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
            }
        }

        return null;
    }

    private static string? ExtractContractReference(string text)
    {
        var match = ContractReferenceRegex().Match(text);
        return match.Success ? match.Groups["ref"].Value.Trim() : null;
    }

    private static string? ExtractSupplierName(string text)
    {
        var match = SupplierRegex().Match(text);
        return match.Success ? match.Groups["supplier"].Value.Trim() : null;
    }

    private static string? ExtractOrgNumber(string text)
    {
        var match = OrgNumberRegex().Match(text);
        return match.Success ? match.Groups["org"].Value.Trim() : null;
    }

    private static bool DetectAutoRenewal(string text) =>
        ContainsAny(text, ["auto renew", "automatically renew", "renews automatically", "fornyas automatiskt", "auto-renews"]);

    private static bool MissingRequiredTerms(Guid? counterpartyId, decimal? amount, string? currency, string? cadence) =>
        !counterpartyId.HasValue || !amount.HasValue || string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(cadence);

    private static string BuildEvidence(SourceCandidate candidate, string? supplierName, decimal? amount, string? currency, string cadence, int confidence)
    {
        var source = candidate.AttachmentName ?? candidate.Subject ?? "mailbox source";
        var supplier = supplierName ?? "an unknown supplier";
        var amountText = amount.HasValue ? $" Expected amount {amount:0.00} {currency ?? "SEK"}." : " Expected amount needs review.";
        return $"Detected recurring agreement evidence in {source} for {supplier}.{amountText} Cadence appears to be {cadence}. Confidence {confidence}%.";
    }

    private static SupplierSubscriptionIntakeProposalSummaryDto MapSummary(SupplierSubscriptionIntakeProposalDetailDto detail) =>
        new(detail.Id, detail.Status, detail.Classification, detail.SupplierName, detail.Terms.Name ?? "Subscription proposal", detail.Terms.Currency, detail.Terms.ExpectedAmount, detail.Terms.Cadence, detail.ConfidenceScore, detail.EvidenceSummary, detail.AcceptedSubscriptionId, detail.CreatedUtc, detail.UpdatedUtc);

    private static string BuildFingerprint(Guid companyId, Guid messageId, Guid? attachmentId, string? attachmentHash, string agreementName) =>
        $"subscription-intake:{companyId:N}:{messageId:N}:{attachmentId?.ToString("N") ?? "body"}:{attachmentHash ?? NormalizeForScanning(agreementName)}";

    private static string NormalizeForScanning(string value) =>
        value.ToLowerInvariant()
            .Replace('ö', 'o')
            .Replace('ä', 'a')
            .Replace('å', 'a')
            .Replace('é', 'e');

    private sealed record SourceCandidate(string? Subject, Guid? AttachmentId, string? AttachmentName, string? AttachmentHash, string Text);

    [GeneratedRegex(@"(?:(?<currency>sek|eur|usd)\s*)?(?<amount>[0-9]{1,3}(?:[ .][0-9]{3})*(?:[,.][0-9]{1,2})?|[0-9]+(?:[,.][0-9]{1,2})?)\s*(?<currency2>sek|eur|usd)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"(?:billing day|invoice day|debiteras den|faktureras den)[^0-9]{0,12}(?<day>[0-9]{1,2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BillingDayRegex();

    [GeneratedRegex(@"(?:notice period|uppsagningstid)[^0-9]{0,12}(?<days>[0-9]{1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoticeRegex();

    [GeneratedRegex(@"(?:contract reference|agreement reference|avtalsnummer|referens)[: #\-]*(?<ref>[A-Za-z0-9][A-Za-z0-9_.\-/]{1,60})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContractReferenceRegex();

    [GeneratedRegex(@"(?:supplier|leverantor)[: #\-]*(?<supplier>[^\r\n]{2,120})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SupplierRegex();

    [GeneratedRegex(@"(?:org(?:anisation)?(?: number| no)?|organisationsnummer|tax id)[: #\-]*(?<org>(?:se)?[0-9]{6}[- ]?[0-9]{4}|[0-9]{10,12})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrgNumberRegex();
}


