using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BillDetectionServiceTests
{
    private readonly BillDetectionService _service = new();

    [Fact]
    public void Detects_text_pdf_bill_attachment()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-1",
            "Invoice 1001",
            null,
            null,
            ["invoice.pdf"],
            "billing@supplier.example",
            "Supplier Billing",
            DateTime.UtcNow,
            "invoices",
            "Invoices",
            null,
            [new MailboxAttachmentSummary("att-1", "invoice.pdf", "application/pdf", 1000, UntrustedExtractedText: "Invoice 1001 amount due 42.00")]));

        Assert.True(result.IsCandidate);
        Assert.Contains(BillSourceType.PdfAttachment, result.DetectedSourceTypes);
        Assert.Contains(BillDetectionRuleMatch.AttachmentPresent, result.MatchedRules);
        Assert.Single(result.CandidateAttachments);
    }

    [Fact]
    public void Detects_docx_bill_attachment()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-2",
            "Statement",
            null,
            null,
            ["statement.docx"],
            "accounts@supplier.example",
            null,
            DateTime.UtcNow,
            "ap",
            "Accounts Payable",
            null,
            [new MailboxAttachmentSummary("att-2", "statement.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 2048, UntrustedExtractedText: "Account statement payment due")]));

        Assert.True(result.IsCandidate);
        Assert.Contains(BillSourceType.DocxAttachment, result.DetectedSourceTypes);
    }

    [Fact]
    public void Detects_body_only_invoice_when_no_supported_attachment_exists()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-3",
            "Invoice INV-77",
            null,
            "Invoice number INV-77 amount due 120.00 due date 2026-05-01",
            [],
            "billing@supplier.example"));

        Assert.True(result.IsCandidate);
        Assert.Contains(BillSourceType.EmailBodyOnly, result.DetectedSourceTypes);
        Assert.Empty(result.CandidateAttachments);
    }

    [Fact]
    public void Detects_forwarded_body_only_invoice_with_business_identity_signals()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-3b",
            "INVOICE Nordic IT Solutions AB",
            "INVOICE Supplier: Nordic IT Solutions AB Org.nr: 556123-4567 VAT No: SE556123456701 Address: Sveavägen 12",
            null,
            [],
            "Johan Davidsson <johandavidsson@hotmail.se>",
            "Johan Davidsson",
            DateTime.UtcNow,
            "INBOX",
            "Inbox"));

        Assert.True(result.IsCandidate);
        Assert.Contains(BillSourceType.EmailBodyOnly, result.DetectedSourceTypes);
        Assert.Contains(BillDetectionRuleMatch.KeywordMatch, result.MatchedRules);
        Assert.Empty(result.CandidateAttachments);
    }

    [Fact]
    public void Rejects_unrelated_message()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-4",
            "Lunch plans",
            null,
            "Are you available tomorrow?",
            [],
            "person@example.com"));

        Assert.False(result.IsCandidate);
        Assert.Empty(result.DetectedSourceTypes);
    }

    [Fact]
    public void Rejects_keyword_only_without_sender_or_folder_signal()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-4b",
            "Invoice",
            null,
            "Please mention invoice in the meeting notes.",
            [],
            "person@example.com"));

        Assert.False(result.IsCandidate);
    }

    [Fact]
    public void Rejects_sender_only_without_keyword_or_attachment_signal()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-4c",
            "Hello",
            null,
            "General supplier update.",
            [],
            "billing@supplier.example"));

        Assert.False(result.IsCandidate);
        Assert.Contains(BillDetectionRuleMatch.SenderMatch, result.MatchedRules);
    }

    [Fact]
    public void Rejects_attachment_only_without_keyword_and_trusted_context_signal()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-4d",
            "Document attached",
            null,
            null,
            ["document.pdf"],
            "person@example.com",
            Attachments: [new MailboxAttachmentSummary("att-4d", "document.pdf", "application/pdf", 1000, UntrustedExtractedText: "Document text")]));

        Assert.False(result.IsCandidate);
        Assert.Contains(BillDetectionRuleMatch.AttachmentPresent, result.MatchedRules);
    }

    [Fact]
    public void Detects_folder_keyword_and_supported_attachment_combination()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-4e",
            "Amount due",
            null,
            null,
            ["statement.pdf"],
            "supplier@example.com",
            FolderId: "ap",
            FolderDisplayName: "Accounts Payable",
            Attachments: [new MailboxAttachmentSummary("att-4e", "statement.pdf", "application/pdf", 1000, UntrustedExtractedText: "Statement amount due")]));

        Assert.True(result.IsCandidate);
        Assert.Contains(BillSourceType.PdfAttachment, result.DetectedSourceTypes);
        Assert.Contains(BillDetectionRuleMatch.FolderMatch, result.MatchedRules);
        Assert.Contains(BillDetectionRuleMatch.KeywordMatch, result.MatchedRules);
        Assert.Contains(BillDetectionRuleMatch.AttachmentPresent, result.MatchedRules);
    }

    [Fact]
    public void Rejects_image_only_pdf_as_supported_source()
    {
        var result = _service.Detect(new MailboxMessageSummary(
            "msg-5",
            "Invoice attached",
            null,
            null,
            ["invoice.pdf"],
            "billing@supplier.example",
            Attachments: [new MailboxAttachmentSummary("att-5", "invoice.pdf", "application/pdf", 5000, IsTextExtractable: false)]));

        Assert.False(result.IsCandidate);
        Assert.Empty(result.CandidateAttachments);
    }
}
