using VirtualCompany.Application.Documents;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyDocumentFileRulesTests
{
    [Theory]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("handbook.md", "text/markdown")]
    [InlineData("policy.pdf", "application/pdf")]
    [InlineData("legacy.doc", "application/msword")]
    [InlineData("playbook.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("POLICY.PDF", "application/pdf")]
    [InlineData("PLAYBOOK.DOCX", "application/octet-stream")]
    public void TryValidate_accepts_supported_formats_case_insensitively(string fileName, string contentType)
    {
        var valid = CompanyDocumentFileRules.TryValidate(fileName, contentType, out var failure);

        Assert.True(valid);
        Assert.Null(failure);
    }

    [Theory]
    [InlineData("spreadsheet.xls")]
    [InlineData("spreadsheet.xlsx")]
    [InlineData("deck.pptx")]
    [InlineData("image.png")]
    [InlineData("archive.zip")]
    public void TryValidate_rejects_formats_outside_the_initial_allowlist(string fileName)
    {
        var valid = CompanyDocumentFileRules.TryValidate(fileName, "application/octet-stream", out var failure);

        Assert.False(valid);
        Assert.NotNull(failure);
        Assert.Equal("unsupported_file_format", failure!.Code);
        Assert.Contains("Unsupported file format.", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(CompanyDocumentFileRules.SupportedFormatsDisplay, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("document")]
    public void TryValidate_rejects_missing_or_extensionless_file_names(string? fileName)
    {
        var valid = CompanyDocumentFileRules.TryValidate(fileName, "text/plain", out var failure);

        Assert.False(valid);
        Assert.NotNull(failure);
        Assert.Contains(CompanyDocumentFileRules.SupportedFormatsDisplay, failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Supported_extensions_match_the_v1_document_upload_allowlist()
    {
        Assert.Equal(
            [".txt", ".md", ".pdf", ".doc", ".docx"],
            CompanyDocumentFileRules.SupportedExtensions);
        Assert.Equal(".txt,.md,.pdf,.doc,.docx", CompanyDocumentFileRules.FileInputAcceptValue);
    }

    [Fact]
    public void TryValidate_rejects_supported_extension_with_mismatched_content_type()
    {
        var valid = CompanyDocumentFileRules.TryValidate("policy.pdf", "image/png", out var failure);

        Assert.False(valid);
        Assert.NotNull(failure);
        Assert.Equal("unsupported_content_type", failure!.Code);
    }
}