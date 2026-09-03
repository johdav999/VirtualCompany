using System.Text.RegularExpressions;

namespace VirtualCompany.Application.Finance;

public static partial class DocumentTextQuality
{
    public static bool IsUsableForBillExtraction(ExtractedDocumentText document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var text = document.FullText;
        if (string.IsNullOrWhiteSpace(text) || text.Count(char.IsLetterOrDigit) < 3)
        {
            return false;
        }

        return !IsPdfText(document.SourceDocumentType) || !PdfContainerSyntax().IsMatch(text);
    }

    private static bool IsPdfText(string sourceDocumentType) =>
        sourceDocumentType.StartsWith("pdf", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(
        @"(?:^|\n)\s*(?:%PDF-[^\r\n]*|endobj|xref|trailer|startxref|stream|endstream)\s*(?:\r?$|\n)|/(?:BaseFont|FontDescriptor|ProcSet|Resources)\b|/F\d+\s+\d+\s+\d+\s+R\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex PdfContainerSyntax();
}
