using System.Text;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Documents;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyDocumentTextExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ReadsCsvAsSearchableText()
    {
        var storage = new MemoryDocumentStorage(Encoding.UTF8.GetBytes("Segment,Price sensitivity\nSME,Moderate to high"));
        var extractor = new CompanyDocumentTextExtractor(storage);

        var result = await extractor.ExtractAsync(CreateDocument("research.csv", ".csv", "text/csv"), CancellationToken.None);

        Assert.Contains("SME,Moderate to high", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_ReadsParagraphsFromDocx()
    {
        using var source = new MemoryStream();
        using (var word = WordprocessingDocument.Create(source, WordprocessingDocumentType.Document, true))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("SME customers compare total cost."))),
                new Paragraph(new Run(new Text("Support and scalability reduce price sensitivity.")))));
            main.Document.Save();
        }

        var extractor = new CompanyDocumentTextExtractor(new MemoryDocumentStorage(source.ToArray()));
        var result = await extractor.ExtractAsync(
            CreateDocument("research.docx", ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            CancellationToken.None);

        Assert.Contains("SME customers compare total cost.", result, StringComparison.Ordinal);
        Assert.Contains("Support and scalability reduce price sensitivity.", result, StringComparison.Ordinal);
    }

    private static CompanyKnowledgeDocument CreateDocument(string fileName, string extension, string contentType)
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        return new CompanyKnowledgeDocument(
            documentId,
            companyId,
            "Workshop research",
            CompanyKnowledgeDocumentType.Reference,
            $"companies/{companyId:N}/knowledge/{documentId:N}/{fileName}",
            null,
            fileName,
            contentType,
            extension,
            256,
            metadata: new Dictionary<string, JsonNode?>(),
            accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
    }

    private sealed class MemoryDocumentStorage(byte[] content) : ICompanyDocumentStorage
    {
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(content, writable: false));

        public Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
