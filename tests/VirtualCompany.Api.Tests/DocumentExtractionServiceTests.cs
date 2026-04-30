using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DocumentExtractionServiceTests
{
    [Fact]
    public async Task Extracts_normalized_bill_from_email_body_with_evidence()
    {
        var service = CreateService(new FakeDuplicateRepository(false));
        await using var content = TextStream(SampleInvoiceText());

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.EmailBodyText, content, "email-body", "email-1", null),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Acme Supplies AB", candidate.SupplierName);
        Assert.Equal("556677-8899", candidate.SupplierOrgNumber);
        Assert.Equal("INV-1001", candidate.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 4, 10), candidate.InvoiceDate);
        Assert.Equal(new DateOnly(2026, 5, 10), candidate.DueDate);
        Assert.Equal("SEK", candidate.Currency);
        Assert.Equal(1250m, candidate.TotalAmount);
        Assert.Equal(250m, candidate.VatAmount);
        Assert.Equal("1234567890", candidate.PaymentReference);
        Assert.Equal("123-4567", candidate.Bankgiro);
        Assert.Equal("DE89370400440532013000", candidate.Iban);
        Assert.Equal("DEUTDEFF", candidate.Bic);
        Assert.Equal(BillExtractionConfidence.High, candidate.Confidence);
        Assert.False(candidate.RequiresReview);
        Assert.True(candidate.IsEligibleForApprovalProposal);
        Assert.Contains(candidate.Evidence, x => x.FieldName == "totalAmount" && x.PageOrSectionReference == "body");
        Assert.Equal(BillValidationStatus.Valid, candidate.ValidationStatus);
    }

    [Fact]
    public async Task Extracts_from_docx_text_sections()
    {
        var service = CreateService(new FakeDuplicateRepository(false));
        await using var docx = CreateDocx(SampleInvoiceText());

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.Docx, docx, "invoice.docx"),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("INV-1001", candidate.InvoiceNumber);
        Assert.Contains(candidate.Evidence, x => x.PageOrSectionReference.StartsWith("paragraph:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Extracts_from_text_based_pdf()
    {
        var service = CreateService(new FakeDuplicateRepository(false));
        await using var pdf = TextStream($"%PDF-1.4\n({SampleInvoiceText().Replace("\n", ") Tj\n(", StringComparison.Ordinal)}) Tj\n%%EOF");

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.Pdf, pdf, "invoice.pdf"),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("INV-1001", candidate.InvoiceNumber);
        Assert.Contains(candidate.Evidence, x => x.SourceDocumentType == "pdf");
    }

    [Fact]
    public async Task Missing_amount_is_rejected_and_low_confidence()
    {
        var service = CreateService(new FakeDuplicateRepository(false));
        await using var content = TextStream(SampleInvoiceText().Replace("Total: 1250 SEK", string.Empty, StringComparison.Ordinal));

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.EmailBodyText, content, "email-body"),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(BillExtractionConfidence.Low, candidate.Confidence);
        Assert.True(candidate.RequiresReview);
        Assert.False(candidate.IsEligibleForApprovalProposal);
        Assert.Contains(candidate.ValidationFindings, x => x.Code == "missing_amount" && x.Severity == BillValidationSeverity.Rejection);
    }

    [Fact]
    public async Task Invalid_due_date_is_rejected()
    {
        var service = CreateService(new FakeDuplicateRepository(false));
        await using var content = TextStream(SampleInvoiceText().Replace("Due date: 2026-05-10", "Due date: 2099-01-01", StringComparison.Ordinal));

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.EmailBodyText, content, "email-body"),
            CancellationToken.None);

        Assert.Contains(Assert.Single(result.Candidates).ValidationFindings, x => x.Code == "invalid_due_date");
    }

    [Fact]
    public async Task Invalid_bankgiro_and_iban_are_rejected()
    {
        var service = CreateService(new FakeDuplicateRepository(false));
        var text = SampleInvoiceText()
            .Replace("Bankgiro: 123-4567", "Bankgiro: 12-34", StringComparison.Ordinal)
            .Replace("IBAN: DE89370400440532013000", "IBAN: DE89370400440532013001", StringComparison.Ordinal);
        await using var content = TextStream(text);

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.EmailBodyText, content, "email-body"),
            CancellationToken.None);

        var findings = Assert.Single(result.Candidates).ValidationFindings;
        Assert.Contains(findings, x => x.Code == "invalid_bankgiro");
        Assert.Contains(findings, x => x.Code == "invalid_iban");
    }

    [Fact]
    public async Task Implausible_vat_requires_review()
    {
        var service = CreateService(new FakeDuplicateRepository(false));
        await using var content = TextStream(SampleInvoiceText().Replace("VAT: 250", "VAT: 1000", StringComparison.Ordinal));

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.EmailBodyText, content, "email-body"),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(BillExtractionConfidence.Medium, candidate.Confidence);
        Assert.True(candidate.RequiresReview);
        Assert.Contains(candidate.ValidationFindings, x => x.Code == "implausible_vat_ratio");
        Assert.Equal(BillValidationStatus.Flagged, candidate.ValidationStatus);
    }

    [Fact]
    public async Task Duplicate_detection_blocks_approval()
    {
        var service = CreateService(new FakeDuplicateRepository(true));
        await using var content = TextStream(SampleInvoiceText());

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.EmailBodyText, content, "email-body"),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(BillDuplicateCheckStatus.Duplicate, candidate.DuplicateCheck.Status);
        Assert.Equal(BillExtractionConfidence.Low, candidate.Confidence);
        Assert.False(candidate.IsEligibleForApprovalProposal);
        Assert.Contains(candidate.ValidationFindings, x => x.Code == "duplicate_invoice_number");
    }

    [Fact]
    public async Task Bill_information_extractor_handles_detected_candidate_without_document_text_pipeline()
    {
        var extractor = new BillInformationExtractor(new FakeDuplicateRepository(false), TimeProvider.System);
        var document = new ExtractedDocumentText("email_body", [new ExtractedDocumentSection("body", SampleInvoiceText(), 0)]);

        var candidate = await extractor.ExtractAsync(
            new DetectedBillCandidateCommand(Guid.NewGuid(), document, "email-body", "email-1", "att-1"),
            CancellationToken.None);

        Assert.Equal("INV-1001", candidate.InvoiceNumber);
        Assert.Equal("email-1", candidate.SourceEmailId);
        Assert.Equal("att-1", candidate.SourceAttachmentId);
        Assert.All(candidate.Evidence, evidence => Assert.False(string.IsNullOrWhiteSpace(evidence.TextSpanOrLocator)));
    }

    [Fact]
    public async Task Duplicate_check_repository_persists_tenant_scoped_result()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options;

        var companyId = Guid.NewGuid();
        await using (var setup = new VirtualCompanyDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var supplier = new FinanceCounterparty(Guid.NewGuid(), companyId, "Acme Supplies AB", "supplier", taxId: "556677-8899");
            setup.Companies.Add(new Company(companyId, "Test Co"));
            setup.FinanceCounterparties.Add(supplier);
            setup.FinanceBills.Add(new FinanceBill(
                Guid.NewGuid(),
                companyId,
                supplier.Id,
                "INV-1001",
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(14),
                1250m,
                "SEK",
                "open"));
            await setup.SaveChangesAsync();
        }

        await using (var db = new VirtualCompanyDbContext(options))
        {
            var repository = new BillDuplicateCheckRepository(db, TimeProvider.System);
            var result = await repository.CheckAndPersistAsync(
                new BillDuplicateCheckRequest(companyId, "Acme Supplies AB", "556677-8899", "INV-1001", 1250m, "SEK", "email-1", "att-1"),
                CancellationToken.None);

            Assert.True(result.IsDuplicate);
            Assert.NotEmpty(result.MatchedBillIds);
            Assert.Equal(1, await db.BillDuplicateChecks.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId));
        }
    }

    private static DocumentExtractionService CreateService(IBillDuplicateCheckRepository duplicateRepository) =>
        new(
            [new EmailBodyTextExtractor(), new DocxDocumentTextExtractor(), new PdfDocumentTextExtractor()],
            duplicateRepository,
            TimeProvider.System);

    private static MemoryStream TextStream(string text) => new(Encoding.UTF8.GetBytes(text));

    private static string SampleInvoiceText() =>
        """
        Supplier: Acme Supplies AB
        Org number: 556677-8899
        Invoice number: INV-1001
        Invoice date: 2026-04-10
        Due date: 2026-05-10
        Total: 1250 SEK
        VAT: 250
        OCR: 1234567890
        Bankgiro: 123-4567
        IBAN: DE89370400440532013000
        BIC: DEUTDEFF
        """;

    private static MemoryStream CreateDocx(string text)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                writer.Write($"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(line.Trim())}</w:t></w:r></w:p>");
            }
            writer.Write("</w:body></w:document>");
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class FakeDuplicateRepository : IBillDuplicateCheckRepository
    {
        private readonly bool _isDuplicate;

        public FakeDuplicateRepository(bool isDuplicate)
        {
            _isDuplicate = isDuplicate;
        }

        public Task<BillDuplicateCheckResult> CheckAndPersistAsync(BillDuplicateCheckRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new BillDuplicateCheckResult(
                Guid.NewGuid(),
                _isDuplicate,
                _isDuplicate ? [Guid.NewGuid()] : [],
                "test criteria",
                DateTime.UtcNow));
    }
}
