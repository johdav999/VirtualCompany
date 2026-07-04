using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
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
    public async Task Falls_back_to_next_pdf_text_extractor_when_first_finds_no_text()
    {
        var service = CreateService(
            new FakeDuplicateRepository(false),
            [new EmptyPdfTextExtractor(), new StaticPdfTextExtractor("pdf_ocr_openai", SampleInvoiceText())]);
        await using var pdf = TextStream("%PDF-1.4\n/scanned-image-only\n%%EOF");

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.Pdf, pdf, "scanned-invoice.pdf"),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("INV-1001", candidate.InvoiceNumber);
        Assert.Contains(candidate.Evidence, x => x.SourceDocumentType == "pdf_ocr_openai");
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
    public async Task Extracts_supplier_invoice_email_with_total_amount_due()
    {
        var service = CreateService(new FakeDuplicateRepository(false));
        await using var content = TextStream(NordicSupplierInvoiceEmailText());

        var result = await service.ExtractAsync(
            new ExtractBillDocumentCommand(Guid.NewGuid(), BillDocumentInputType.EmailBodyText, content, "email-body", "email-1", null),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Nordic IT Solutions AB", candidate.SupplierName);
        Assert.Equal("556123-4567", candidate.SupplierOrgNumber);
        Assert.Equal("INV-2026-0412", candidate.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 4, 28), candidate.InvoiceDate);
        Assert.Equal(new DateOnly(2026, 5, 12), candidate.DueDate);
        Assert.Equal(10000m, candidate.TotalAmount);
        Assert.Equal(2000m, candidate.VatAmount);
        Assert.Equal("SEK", candidate.Currency);
        Assert.Equal(BillValidationStatus.Valid, candidate.ValidationStatus);
    }

    [Fact]
    public async Task Persistence_refreshes_existing_detected_bill_after_rescan()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options;

        var companyId = Guid.NewGuid();
        var detectedBillId = Guid.NewGuid();
        await using (var setup = new VirtualCompanyDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Companies.Add(new Company(companyId, "Test Co"));
            setup.DetectedBills.Add(new DetectedBill(
                detectedBillId,
                companyId,
                "Nordic IT Solutions AB Org.nr: 556123-4567 VAT No: SE556123456701 Address: Sveavagen 12, 111 57 Stockholm, Sweden Email:",
                "SE5561234567",
                "Supplier",
                null,
                null,
                "SEK",
                8000m,
                2000m,
                null,
                null,
                null,
                null,
                null,
                0.4m,
                "low",
                "rejected",
                "required",
                true,
                false,
                true,
                "[]",
                "email-1",
                null,
                createdUtc: new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc),
                updatedUtc: new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc)));
            await setup.SaveChangesAsync();
        }

        var candidate = new NormalizedBillCandidateDto(
            "Nordic IT Solutions AB",
            "556123-4567",
            "INV-2026-0412",
            new DateOnly(2026, 4, 28),
            new DateOnly(2026, 5, 12),
            "SEK",
            10000m,
            2000m,
            "INV-2026-0412",
            null,
            null,
            "SE4550000000000012345678",
            "ESSESESS",
            BillExtractionConfidence.High,
            "email-1",
            null,
            [
                new BillFieldEvidenceDto("totalAmount", "10,000 SEK", "email-body", "email_body", "body", "289:28", "Regex", BillFieldConfidence.High, "Total Amount Due: 10,000 SEK"),
                new BillFieldEvidenceDto("dueDate", "2026-05-12", "email-body", "email_body", "body", "170:20", "Regex", BillFieldConfidence.High, "Due Date: 2026-05-12")
            ],
            [],
            BillValidationStatus.Valid,
            new BillDuplicateCheckDto(Guid.Empty, BillDuplicateCheckStatus.NotDuplicate, [], "not duplicate", DateTime.UtcNow),
            false,
            false,
            true);

        await using (var db = new VirtualCompanyDbContext(options))
        {
            var repository = new BillExtractionPersistenceRepository(db, TimeProvider.System);
            await repository.PersistAsync(new PersistNormalizedBillExtractionCommand(companyId, candidate), CancellationToken.None);
        }

        await using (var verify = new VirtualCompanyDbContext(options))
        {
            var bill = await verify.DetectedBills.IgnoreQueryFilters().SingleAsync(x => x.Id == detectedBillId);
            Assert.Equal("Nordic IT Solutions AB", bill.SupplierName);
            Assert.Equal("556123-4567", bill.SupplierOrgNumber);
            Assert.Equal("INV-2026-0412", bill.InvoiceNumber);
            Assert.Equal(new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc), bill.InvoiceDateUtc);
            Assert.Equal(new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc), bill.DueDateUtc);
            Assert.Equal(10000m, bill.TotalAmount);
            Assert.Equal(2000m, bill.VatAmount);
            Assert.Equal("valid", bill.ValidationStatus);
            Assert.False(bill.RequiresReview);

            var fields = await verify.DetectedBillFields
                .IgnoreQueryFilters()
                .Where(x => x.DetectedBillId == detectedBillId)
                .ToListAsync();
            Assert.Contains(fields, x => x.FieldName == "totalAmount" && x.NormalizedValue == "10,000 SEK");
            Assert.Contains(fields, x => x.FieldName == "dueDate" && x.NormalizedValue == "2026-05-12");
        }
    }

    [Fact]
    public void Fortnox_supplier_invoice_payload_uses_gross_total_and_omits_invoice_reference_ocr()
    {
        var bill = CreateDetectedBillForFortnoxPayload("INV-2026-0412");

        var payload = BuildSupplierInvoicePayloadForTest(bill, "2");
        var supplierInvoice = payload["SupplierInvoice"]!.AsObject();

        Assert.Equal(10000m, supplierInvoice["Total"]!.GetValue<decimal>());
        Assert.Equal(2000m, supplierInvoice["VAT"]!.GetValue<decimal>());
        Assert.False(supplierInvoice.ContainsKey("OCR"));
    }

    [Fact]
    public void Fortnox_supplier_invoice_payload_includes_valid_numeric_ocr()
    {
        var bill = CreateDetectedBillForFortnoxPayload("12345678903");

        var payload = BuildSupplierInvoicePayloadForTest(bill, "2");
        var supplierInvoice = payload["SupplierInvoice"]!.AsObject();

        Assert.Equal("12345678903", supplierInvoice["OCR"]!.GetValue<string>());
    }

    [Fact]
    public void Fortnox_supplier_creation_payload_uses_invoice_supplier_details()
    {
        var bill = CreateDetectedBillForFortnoxPayload("INV-2026-0412");
        bill.AddField(CreateDetectedBillField(bill, "supplierEmail", "billing@nordicit.se"));
        bill.AddField(CreateDetectedBillField(bill, "supplierPhone", "+46 8 123 456 78"));
        bill.AddField(CreateDetectedBillField(bill, "supplierAddress", "Sveavagen 12, 111 57 Stockholm, Sweden"));

        var payload = BuildSupplierPayloadForTest(bill);
        var supplier = payload["Supplier"]!.AsObject();

        Assert.Equal("Nordic IT Solutions AB", supplier["Name"]!.GetValue<string>());
        Assert.Equal("556123-4567", supplier["OrganisationNumber"]!.GetValue<string>());
        Assert.Equal("billing@nordicit.se", supplier["Email"]!.GetValue<string>());
        Assert.Equal("+46 8 123 456 78", supplier["Phone1"]!.GetValue<string>());
        Assert.Equal("Sveavagen 12, 111 57 Stockholm, Sweden", supplier["Address1"]!.GetValue<string>());
        Assert.Equal("SE4550000000000012345678", supplier["IBAN"]!.GetValue<string>());
        Assert.Equal("ESSESESS", supplier["BIC"]!.GetValue<string>());
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

    private static DocumentExtractionService CreateService(
        IBillDuplicateCheckRepository duplicateRepository,
        IReadOnlyList<IDocumentTextExtractor> textExtractors) =>
        new(textExtractors, duplicateRepository, TimeProvider.System);

    private static JsonObject BuildSupplierInvoicePayloadForTest(DetectedBill bill, string supplierNumber)
    {
        var method = typeof(CompanyFinanceBillInboxService).GetMethod(
            "BuildSupplierInvoicePayload",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<JsonObject>(method.Invoke(null, [bill, supplierNumber]));
    }

    private static JsonObject BuildSupplierPayloadForTest(DetectedBill bill)
    {
        var method = typeof(CompanyFinanceBillInboxService).GetMethod(
            "BuildSupplierPayload",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<JsonObject>(method.Invoke(null, [bill]));
    }

    private static DetectedBillField CreateDetectedBillField(DetectedBill bill, string fieldName, string value) =>
        new(
            Guid.NewGuid(),
            bill.CompanyId,
            bill.Id,
            fieldName,
            value,
            value,
            "email-body",
            "email",
            null,
            null,
            null,
            null,
            "test",
            0.95m);

    private static DetectedBill CreateDetectedBillForFortnoxPayload(string? paymentReference) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Nordic IT Solutions AB",
            "556123-4567",
            "INV-2026-0412",
            new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc),
            "SEK",
            10000m,
            2000m,
            paymentReference,
            null,
            null,
            "SE4550000000000012345678",
            "ESSESESS",
            0.95m,
            "high",
            "valid",
            "not_required",
            false,
            true,
            true,
            "[]",
            "email-1",
            null,
            validationStatusPersistedAtUtc: new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc),
            createdUtc: new DateTime(2026, 4, 29, 10, 0, 0, 0, DateTimeKind.Utc),
            updatedUtc: new DateTime(2026, 4, 29, 10, 0, 0, 0, DateTimeKind.Utc));

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

    private static string NordicSupplierInvoiceEmailText() =>
        """
        INVOICE

        Supplier:
        Nordic IT Solutions AB
        Org.nr: 556123-4567
        VAT No: SE556123456701
        Address: Sveavägen 12, 111 57 Stockholm, Sweden
        Email: billing@nordicit.se
        Phone: +46 8 123 456 78

        Customer:
        Virtual Company AB
        Org.nr: 559999-1234

        Invoice Number: INV-2026-0412
        Invoice Date: 2026-04-28
        Due Date: 2026-05-12
        Payment Terms: 14 days

        Quantity: 1
        Unit Price: 8,000 SEK
        Amount: 8,000 SEK

        Subtotal: 8,000 SEK
        VAT (25%): 2,000 SEK

        Total Amount Due: 10,000 SEK
        Currency: SEK

        Payment Reference:
        INV-2026-0412
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

    private sealed class EmptyPdfTextExtractor : IDocumentTextExtractor
    {
        public bool Supports(BillDocumentInputType inputType) => inputType == BillDocumentInputType.Pdf;

        public Task<ExtractedDocumentText> ExtractAsync(
            Stream content,
            string sourceDocumentName,
            BillDocumentInputType inputType,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ExtractedDocumentText("pdf", []));
    }

    private sealed class StaticPdfTextExtractor : IDocumentTextExtractor
    {
        private readonly string _sourceDocumentType;
        private readonly string _text;

        public StaticPdfTextExtractor(string sourceDocumentType, string text)
        {
            _sourceDocumentType = sourceDocumentType;
            _text = text;
        }

        public bool Supports(BillDocumentInputType inputType) => inputType == BillDocumentInputType.Pdf;

        public Task<ExtractedDocumentText> ExtractAsync(
            Stream content,
            string sourceDocumentName,
            BillDocumentInputType inputType,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ExtractedDocumentText(
                _sourceDocumentType,
                [new ExtractedDocumentSection("document", _text, 0)]));
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
