using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyKnowledgeDocumentTests
{
    [Fact]
    public void New_document_defaults_to_uploaded()
    {
        var document = CreateDocument();

        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Uploaded, document.IngestionStatus);
        Assert.NotNull(document.UploadedUtc);
        Assert.Null(document.ProcessedUtc);
        Assert.Null(document.FailedUtc);
    }

    [Fact]
    public void Uploaded_document_can_transition_to_pending_scan()
    {
        var document = CreateDocument();

        var changed = document.MarkPendingScan();

        Assert.True(changed);
        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.PendingScan, document.IngestionStatus);
    }

    [Fact]
    public void Scan_clean_document_can_transition_to_processing()
    {
        var document = CreateDocument();
        document.MarkPendingScan();
        document.MarkScanClean();

        var changed = document.MarkProcessing();

        Assert.True(changed);
        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Processing, document.IngestionStatus);
        Assert.NotNull(document.ProcessingStartedUtc);
    }

    [Fact]
    public void Uploaded_document_can_transition_to_processed()
    {
        var document = CreateDocument();
        document.MarkPendingScan();
        document.MarkScanClean();
        document.MarkProcessing();

        var changed = document.MarkProcessed();

        Assert.True(changed);
        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Processed, document.IngestionStatus);
        Assert.NotNull(document.ProcessedUtc);
        Assert.Null(document.FailureCode);
        Assert.Null(document.FailureMessage);
    }

    [Fact]
    public void Uploaded_document_can_transition_to_failed_with_actionable_reason()
    {
        var document = CreateDocument();

        var changed = document.MarkFailed(
            "file_corrupted",
            "The uploaded file is corrupted and could not be read.",
            "Upload a new copy and retry ingestion.",
            "InvalidDataException: PDF header was missing.",
            canRetry: false);

        Assert.True(changed);
        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Equal("file_corrupted", document.FailureCode);
        Assert.Equal("The uploaded file is corrupted and could not be read.", document.FailureMessage);
        Assert.Equal("Upload a new copy and retry ingestion.", document.FailureAction);
        Assert.Equal("InvalidDataException: PDF header was missing.", document.FailureTechnicalDetail);
        Assert.False(document.CanRetry);
        Assert.NotNull(document.FailedUtc);
    }

    [Fact]
    public void Uploaded_document_cannot_transition_to_processing_before_scan_clearance()
    {
        var document = CreateDocument();

        var exception = Assert.Throws<InvalidOperationException>(() => document.MarkProcessing());

        Assert.Contains("cannot execute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pending_scan_document_can_transition_to_blocked()
    {
        var document = CreateDocument();
        document.MarkPendingScan();

        var changed = document.MarkBlocked("virus_scan_blocked", "The document was blocked by the scanner.", "Remove the unsafe content and upload a clean file.");

        Assert.True(changed);
        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Blocked, document.IngestionStatus);
    }

    [Fact]
    public void Failed_document_cannot_transition_to_processed()
    {
        var document = CreateDocument();
        document.MarkFailed(
            "extraction_failed",
            "The document contents could not be extracted.",
            "Upload a clean copy and retry ingestion.");

        var exception = Assert.Throws<InvalidOperationException>(() => document.MarkProcessed());

        Assert.Contains("cannot transition", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void New_document_normalizes_access_scope_to_document_company()
    {
        var document = CreateDocument();

        Assert.Equal(document.CompanyId, document.AccessScope.CompanyId);
        Assert.Equal(CompanyKnowledgeDocumentAccessScope.CompanyVisibility, document.AccessScope.Visibility);
    }

    [Fact]
    public void New_document_rejects_cross_tenant_access_scope()
    {
        var companyId = Guid.NewGuid();
        var foreignCompanyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var exception = Assert.Throws<ArgumentException>(() => new CompanyKnowledgeDocument(
            documentId,
            companyId,
            "Operations Handbook",
            CompanyKnowledgeDocumentType.Reference,
            $"companies/{companyId:N}/knowledge/{documentId:N}/operations-handbook.txt",
            null,
            "operations-handbook.txt",
            "text/plain",
            ".txt",
            128,
            accessScope: new CompanyKnowledgeDocumentAccessScope { Visibility = "company", CompanyId = foreignCompanyId }));

        Assert.Contains("current company context", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CompanyKnowledgeDocument CreateDocument()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        return new CompanyKnowledgeDocument(
            documentId,
            companyId,
            "Operations Handbook",
            CompanyKnowledgeDocumentType.Reference,
            $"companies/{companyId:N}/knowledge/{documentId:N}/operations-handbook.txt",
            null,
            "operations-handbook.txt",
            "text/plain",
            ".txt",
            128,
            metadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["checksum_sha256"] = JsonValue.Create("abc123")
            },
            accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
    }
}
