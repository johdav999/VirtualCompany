namespace VirtualCompany.Application.Documents;

public static class CompanyDocumentFileRules
{
    private const string UnsupportedFileAction =
        "Upload a TXT, MD, PDF, DOC, or DOCX file, or export the document to PDF or DOCX and try again.";

    private static readonly (string Extension, string[] ContentTypes)[] SupportedFormats =
    [
        (".txt", ["text/plain"]),
        (".md", ["text/markdown", "text/plain"]),
        (".pdf", ["application/pdf"]),
        (".doc", ["application/msword", "application/octet-stream"]),
        (".docx",
        [
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/octet-stream",
            "application/zip"
        ])
    ];

    private static readonly IReadOnlyDictionary<string, string[]> SupportedFileTypes =
        SupportedFormats.ToDictionary(x => x.Extension, x => x.ContentTypes, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> SupportedExtensions { get; } = SupportedFormats.Select(x => x.Extension).ToArray();
    public static string SupportedFormatsDisplay { get; } = string.Join(", ", SupportedExtensions);
    public static string FileInputAcceptValue { get; } = string.Join(",", SupportedExtensions);

    public static bool TryValidate(string? fileName, string? contentType, out CompanyDocumentIngestionFailure? failure)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            failure = new CompanyDocumentIngestionFailure(
                "missing_file_name",
                $"A file name with a supported extension is required. Supported formats: {SupportedFormatsDisplay}.",
                UnsupportedFileAction);
            return false;
        }

        var normalizedFileName = Path.GetFileName(fileName.Trim());
        var extension = Path.GetExtension(normalizedFileName);
        if (string.IsNullOrWhiteSpace(extension) || !SupportedFileTypes.TryGetValue(extension, out var allowedContentTypes))
        {
            failure = new CompanyDocumentIngestionFailure(
                "unsupported_file_format",
                $"Unsupported file format. Supported formats: {SupportedFormatsDisplay}.",
                UnsupportedFileAction);
            return false;
        }

        var normalizedContentType = NormalizeContentType(contentType);
        if (normalizedContentType is null || string.Equals(normalizedContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            failure = null;
            return true;
        }

        if (allowedContentTypes.Contains(normalizedContentType, StringComparer.OrdinalIgnoreCase))
        {
            failure = null;
            return true;
        }

        failure = new CompanyDocumentIngestionFailure(
            "unsupported_content_type",
            $"Content type '{normalizedContentType}' does not match the supported types for '{extension.ToLowerInvariant()}'.",
            $"Verify the file is a valid document, then re-save it as {SupportedFormatsDisplay} before uploading again.");
        return false;
    }

    public static string FormatValidationMessage(CompanyDocumentIngestionFailure failure) =>
        $"{failure.Message} {failure.Action}";

    public static CompanyDocumentIngestionFailure CreateProcessingFailure(Exception exception) =>
        new(
            "parser_failed",
            "We could not read the document during ingestion.",
            "Re-save or export the file to PDF, DOCX, TXT, or MD and upload it again.",
            NormalizeTechnicalDetail(exception),
            CanRetry: false);

    private static string? NormalizeContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim();

    private static string NormalizeTechnicalDetail(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}".Trim();
}