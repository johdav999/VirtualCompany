using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Mailbox;

public sealed class BillDetectionService : IBillDetectionService
{
    private static readonly string[] BillingKeywords =
    [
        "invoice",
        "bill",
        "statement",
        "payment due",
        "amount due",
        "remit",
        "remittance",
        "due date",
        "account statement",
        "receipt",
        "invoice number",
        "inv #",
        "supplier",
        "faktura",
        "ocr",
        "org.nr",
        "org nr",
        "vat",
        "vat no",
        "address",
        "iban",
        "bankgiro",
        "plusgiro"
    ];

    private static readonly string[] FolderIndicators =
    [
        "invoice",
        "invoices",
        "bill",
        "bills",
        "finance",
        "accounting",
        "ap",
        "accounts payable",
        "payables"
    ];

    private static readonly string[] SenderIndicators =
    [
        "billing",
        "invoice",
        "invoices",
        "accounts",
        "accounting",
        "payments",
        "payable",
        "finance",
        "receipts",
        "no-reply"
    ];

    public BillDetectionResult Detect(MailboxMessageSummary message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var supportedAttachments = message.AttachmentSummaries
            .Select(ClassifyAttachment)
            .Where(attachment => attachment is not null)
            .Cast<BillCandidateAttachment>()
            .ToArray();
        var matchedRules = new HashSet<BillDetectionRuleMatch>();

        var senderMatch = IsSenderMatch(message.FromAddress);
        var folderMatch = IsFolderMatch(message.FolderId) || IsFolderMatch(message.FolderDisplayName);
        var keywordScore = CountKeywordMatches(message);
        var hasKeywordMatch = keywordScore > 0;

        if (senderMatch)
        {
            matchedRules.Add(BillDetectionRuleMatch.SenderMatch);
        }

        if (folderMatch)
        {
            matchedRules.Add(BillDetectionRuleMatch.FolderMatch);
        }

        if (hasKeywordMatch)
        {
            matchedRules.Add(BillDetectionRuleMatch.KeywordMatch);
        }

        if (supportedAttachments.Length > 0)
        {
            matchedRules.Add(BillDetectionRuleMatch.AttachmentPresent);
        }

        var qualifiesWithAttachment =
            supportedAttachments.Length > 0 && 
            hasKeywordMatch && (senderMatch || folderMatch);
        var qualifiesBodyOnly =
            supportedAttachments.Length == 0 &&
            IsBodyOnlyCandidate(message, keywordScore, senderMatch, folderMatch);

        if (!qualifiesWithAttachment && !qualifiesBodyOnly)
        {
            return new BillDetectionResult(
                false,
                matchedRules.ToArray(),
                [],
                [],
                "Message did not satisfy deterministic bill-candidate rules.");
        }

        var sourceTypes = qualifiesBodyOnly
            ? [BillSourceType.EmailBodyOnly]
            : supportedAttachments
                .Select(x => x.SourceType)
                .Distinct()
                .OrderBy(GetSourceTypePrecedence).ToArray();

        return new BillDetectionResult(
            true,
            matchedRules.ToArray(),
            sourceTypes,
            supportedAttachments,
            BuildReasonSummary(senderMatch, folderMatch, hasKeywordMatch, supportedAttachments.Length, qualifiesBodyOnly));
    }

    private static BillCandidateAttachment? ClassifyAttachment(MailboxAttachmentSummary attachment)
    {
        var sourceType = TryClassifySupportedSourceType(attachment);
        if (!sourceType.HasValue)
        {
            return null;
        }

        return new BillCandidateAttachment(
            string.IsNullOrWhiteSpace(attachment.ExternalAttachmentId)
                ? attachment.FileName ?? Guid.NewGuid().ToString("N")
                : attachment.ExternalAttachmentId,
            attachment.FileName,
            attachment.MimeType,
            attachment.SizeBytes,
            MailboxAttachmentHash.ComputeDeterministicHash(attachment),
            attachment.StorageReference,
            sourceType.Value,
            attachment.UntrustedExtractedText);
    }

    private static BillSourceType? TryClassifySupportedSourceType(MailboxAttachmentSummary attachment)
    {
        var fileName = attachment.FileName ?? string.Empty;
        var mimeType = attachment.MimeType ?? string.Empty;
        var isPdf = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
        var isDocx = fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase);
        var hasExtractableText = attachment.IsTextExtractable == true ||
            !string.IsNullOrWhiteSpace(attachment.UntrustedExtractedText);

        if (isPdf && hasExtractableText)
        {
            return BillSourceType.PdfAttachment;
        }

        if (isDocx && hasExtractableText)
        {
            return BillSourceType.DocxAttachment;
        }

        return null;
    }

    private static int CountKeywordMatches(MailboxMessageSummary message)
    {
        var text = string.Join(
            " ",
            message.Subject,
            message.Snippet,
            message.BodyPreview,
            string.Join(" ", message.AttachmentFileNames),
            string.Join(" ", message.AttachmentSummaries.Select(x => x.FileName)));

        return BillingKeywords.Count(keyword => Contains(text, keyword));
    }

    private static bool IsBodyOnlyCandidate(
        MailboxMessageSummary message,
        int keywordScore,
        bool senderMatch,
        bool folderMatch)
    {
        var text = string.Join(" ", message.Subject, message.Snippet, message.BodyPreview);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hasStrongPhrase =
            Contains(text, "amount due") ||
            Contains(text, "payment due") ||
            Contains(text, "invoice number") ||
            Contains(text, "due date") ||
            Contains(text, "remittance");

        if (senderMatch || folderMatch)
        {
            return keywordScore >= 2 || hasStrongPhrase;
        }

        return HasInvoiceKeyword(text) &&
            keywordScore >= 3 &&
            HasBusinessIdentitySignal(text);
    }

    private static bool HasInvoiceKeyword(string text) =>
        Contains(text, "invoice") ||
        Contains(text, "faktura") ||
        Contains(text, "bill");

    private static bool HasBusinessIdentitySignal(string text) =>
        Contains(text, "supplier") ||
        Contains(text, "org.nr") ||
        Contains(text, "org nr") ||
        Contains(text, "vat") ||
        Contains(text, "vat no") ||
        Contains(text, "address");

    private static bool IsSenderMatch(string? fromAddress)
    {
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            return false;
        }

        var at = fromAddress.IndexOf('@', StringComparison.Ordinal);
        var local = at > 0 ? fromAddress[..at] : fromAddress;
        var domain = at > 0 && at < fromAddress.Length - 1 ? fromAddress[(at + 1)..] : string.Empty;
        return SenderIndicators.Any(indicator => Contains(local, indicator) || Contains(domain, indicator));
    }

    private static bool IsFolderMatch(string? folder) =>
        FolderIndicators.Any(indicator => Contains(folder, indicator));

    private static bool Contains(string? value, string keyword) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string BuildReasonSummary(
        bool senderMatch,
        bool folderMatch,
        bool keywordMatch,
        int supportedAttachmentCount,
        bool bodyOnly)
    {
        var parts = new List<string>();
        if (senderMatch) parts.Add("sender");
        if (folderMatch) parts.Add("folder");
        if (keywordMatch) parts.Add("keyword");
        if (supportedAttachmentCount > 0) parts.Add("supported_attachment");
        if (bodyOnly) parts.Add("body_only");
        return $"Matched deterministic bill detection rules: {string.Join(", ", parts)}.";
    }

    private static int GetSourceTypePrecedence(BillSourceType sourceType) =>
        sourceType switch
        {
            BillSourceType.PdfAttachment => 0,
            BillSourceType.DocxAttachment => 1,
            _ => 2
        };
}
