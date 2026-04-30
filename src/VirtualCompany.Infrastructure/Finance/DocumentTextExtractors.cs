using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class PdfDocumentTextExtractor : IDocumentTextExtractor
{
    public bool Supports(BillDocumentInputType inputType) => inputType == BillDocumentInputType.Pdf;

    public async Task<ExtractedDocumentText> ExtractAsync(
        Stream content,
        string sourceDocumentName,
        BillDocumentInputType inputType,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        // This intentionally supports only text-based PDFs. If no readable text is present,
        // downstream extraction receives an empty result instead of silently attempting OCR.
        var raw = Encoding.Latin1.GetString(bytes);
        var text = ExtractPdfTextOperators(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = ExtractReadableText(raw);
        }

        return new ExtractedDocumentText(
            "pdf",
            string.IsNullOrWhiteSpace(text)
                ? []
                : [new ExtractedDocumentSection("page:1", Normalize(text), 0)]);
    }

    private static string ExtractPdfTextOperators(string raw)
    {
        var builder = new StringBuilder();
        foreach (Match match in Regex.Matches(raw, @"\((?<text>(?:\\.|[^\\)])*)\)\s*Tj", RegexOptions.CultureInvariant))
        {
            builder.AppendLine(UnescapePdfText(match.Groups["text"].Value));
        }

        foreach (Match arrayMatch in Regex.Matches(raw, @"\[(?<items>.*?)\]\s*TJ", RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            foreach (Match item in Regex.Matches(arrayMatch.Groups["items"].Value, @"\((?<text>(?:\\.|[^\\)])*)\)", RegexOptions.CultureInvariant))
            {
                builder.Append(UnescapePdfText(item.Groups["text"].Value));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ExtractReadableText(string raw)
    {
        var text = Regex.Replace(raw, @"[^\u0020-\u007E\r\n\t]", " ");
        var lines = text
            .Split('\n')
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => x.Length >= 3 && !x.StartsWith('%') && !x.Contains(" obj", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return string.Join('\n', lines);
    }

    private static string UnescapePdfText(string value) =>
        value
            .Replace("\\(", "(", StringComparison.Ordinal)
            .Replace("\\)", ")", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal);

    private static string Normalize(string value) =>
        Regex.Replace(value.Replace("\r\n", "\n", StringComparison.Ordinal), @"[ \t]+", " ").Trim();
}

public sealed class DocxDocumentTextExtractor : IDocumentTextExtractor
{
    public bool Supports(BillDocumentInputType inputType) => inputType == BillDocumentInputType.Docx;

    public async Task<ExtractedDocumentText> ExtractAsync(
        Stream content,
        string sourceDocumentName,
        BillDocumentInputType inputType,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: true);
        var documentEntry = archive.GetEntry("word/document.xml");
        if (documentEntry is null)
        {
            return new ExtractedDocumentText("docx", []);
        }

        await using var entryStream = documentEntry.Open();
        var xml = await XDocument.LoadAsync(entryStream, LoadOptions.None, cancellationToken);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = xml
            .Descendants(w + "p")
            .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var sections = new List<ExtractedDocumentSection>();
        var offset = 0;
        for (var index = 0; index < paragraphs.Length; index++)
        {
            sections.Add(new ExtractedDocumentSection($"paragraph:{index + 1}", paragraphs[index], offset));
            offset += paragraphs[index].Length + 1;
        }

        return new ExtractedDocumentText("docx", sections);
    }
}

public sealed class EmailBodyTextExtractor : IDocumentTextExtractor
{
    public bool Supports(BillDocumentInputType inputType) =>
        inputType is BillDocumentInputType.EmailBodyText or BillDocumentInputType.EmailBodyHtml;

    public async Task<ExtractedDocumentText> ExtractAsync(
        Stream content,
        string sourceDocumentName,
        BillDocumentInputType inputType,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var text = await reader.ReadToEndAsync(cancellationToken);
        if (inputType == BillDocumentInputType.EmailBodyHtml)
        {
            text = HtmlToText(text);
        }

        text = Regex.Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal), @"[ \t]+", " ").Trim();
        return new ExtractedDocumentText(
            inputType == BillDocumentInputType.EmailBodyHtml ? "email_html" : "email_text",
            string.IsNullOrWhiteSpace(text) ? [] : [new ExtractedDocumentSection("body", text, 0)]);
    }

    private static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<(br|p|div|tr|li)\b[^>]*>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "<[^>]+>", " ", RegexOptions.CultureInvariant);
        return System.Net.WebUtility.HtmlDecode(text);
    }
}
