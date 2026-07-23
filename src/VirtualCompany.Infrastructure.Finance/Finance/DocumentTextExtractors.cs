using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

public sealed class OpenAiPdfOcrTextExtractor : IDocumentTextExtractor
{
    public const string ClientName = "finance-pdf-ocr";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FinanceDocumentOcrOptions _options;
    private readonly ILogger<OpenAiPdfOcrTextExtractor> _logger;

    public OpenAiPdfOcrTextExtractor(
        IHttpClientFactory httpClientFactory,
        IOptions<FinanceDocumentOcrOptions> options,
        ILogger<OpenAiPdfOcrTextExtractor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool Supports(BillDocumentInputType inputType) =>
        inputType is BillDocumentInputType.Pdf or BillDocumentInputType.Image;

    public async Task<ExtractedDocumentText> ExtractAsync(
        Stream content,
        string sourceDocumentName,
        BillDocumentInputType inputType,
        CancellationToken cancellationToken)
    {
        if (!CanUseOpenAi())
        {
            _logger.LogInformation(
                "OpenAI document OCR skipped because finance document OCR is not configured. SourceDocumentName: {SourceDocumentName}. InputType: {InputType}. Enabled: {Enabled}. HasApiKey: {HasApiKey}. HasModel: {HasModel}.",
                sourceDocumentName,
                inputType,
                _options.Enabled,
                !string.IsNullOrWhiteSpace(_options.ApiKey),
                !string.IsNullOrWhiteSpace(_options.Model));
            return Empty(inputType);
        }

        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var maxBytes = inputType == BillDocumentInputType.Image
            ? _options.MaxImageBytes
            : _options.MaxPdfBytes;
        if (bytes.Length == 0 || bytes.Length > maxBytes)
        {
            return Empty(inputType);
        }

        try
        {
            var text = await ExtractWithResponsesApiAsync(bytes, sourceDocumentName, inputType, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogInformation(
                    "OpenAI document OCR completed without readable text. SourceDocumentName: {SourceDocumentName}. InputType: {InputType}.",
                    sourceDocumentName,
                    inputType);
            }
            else
            {
                _logger.LogInformation(
                    "OpenAI document OCR extracted text. SourceDocumentName: {SourceDocumentName}. InputType: {InputType}. TextLength: {TextLength}.",
                    sourceDocumentName,
                    inputType,
                    text.Length);
            }

            var sourceDocumentType = inputType == BillDocumentInputType.Image ? "image_ocr_openai" : "pdf_ocr_openai";
            return string.IsNullOrWhiteSpace(text)
                ? Empty(inputType)
                : new ExtractedDocumentText(sourceDocumentType, [new ExtractedDocumentSection("document", NormalizeOcrText(text), 0)]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "OpenAI document OCR failed and bill extraction will continue without OCR. SourceDocumentName: {SourceDocumentName}. InputType: {InputType}.",
                sourceDocumentName,
                inputType);
            return Empty(inputType);
        }
    }

    private bool CanUseOpenAi() =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.Model) &&
        _options.MaxPdfBytes > 0 &&
        _options.MaxImageBytes > 0;

    private async Task<string?> ExtractWithResponsesApiAsync(
        byte[] documentBytes,
        string sourceDocumentName,
        BillDocumentInputType inputType,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        client.BaseAddress = new Uri(_options.BaseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 10, 180));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var request = new ResponsesRequest
        {
            Model = _options.Model,
            Store = false,
            MaxOutputTokens = Math.Clamp(_options.MaxOutputTokens, 256, 12000),
            Input =
            [
                new ResponsesInputMessage
                {
                    Role = "user",
                    Content = BuildOcrContent(documentBytes, sourceDocumentName, inputType)
                }
            ]
        };

        using var response = await client.PostAsJsonAsync("responses", request, SerializerOptions, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAI document OCR returned {(int)response.StatusCode}: {responseBody}",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(responseBody);
        return FindFirstOutputText(document.RootElement);
    }

    private static List<object> BuildOcrContent(
        byte[] documentBytes,
        string sourceDocumentName,
        BillDocumentInputType inputType)
    {
        var instruction = inputType == BillDocumentInputType.Image
            ? "Extract the visible text from this scanned supplier invoice image. Preserve invoice line breaks and labels as much as possible. Return only the extracted text. Do not summarize and do not invent missing values."
            : "Extract the visible text from this scanned supplier invoice PDF. Preserve invoice line breaks and labels as much as possible. Return only the extracted text. Do not summarize and do not invent missing values.";
        var content = new List<object>
        {
            new ResponsesInputText("input_text", instruction)
        };

        if (inputType == BillDocumentInputType.Image)
        {
            content.Add(new ResponsesInputImage(
                "input_image",
                $"data:{ResolveImageMimeType(sourceDocumentName)};base64,{Convert.ToBase64String(documentBytes)}"));
        }
        else
        {
            content.Add(new ResponsesInputFile(
                "input_file",
                SanitizeFileName(sourceDocumentName, "invoice.pdf"),
                $"data:application/pdf;base64,{Convert.ToBase64String(documentBytes)}"));
        }

        return content;
    }

    private static string ResolveImageMimeType(string sourceDocumentName)
    {
        var extension = Path.GetExtension(sourceDocumentName);
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }

    private static string? FindFirstOutputText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "output_text", StringComparison.OrdinalIgnoreCase) &&
                element.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }

            if (element.TryGetProperty("output_text", out var outputText) &&
                outputText.ValueKind == JsonValueKind.String)
            {
                return outputText.GetString();
            }

            foreach (var property in element.EnumerateObject())
            {
                var candidate = FindFirstOutputText(property.Value);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var candidate = FindFirstOutputText(item);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string SanitizeFileName(string value, string fallbackFileName)
    {
        var fileName = Path.GetFileName(value);
        return string.IsNullOrWhiteSpace(fileName) ? fallbackFileName : fileName;
    }

    private static string NormalizeOcrText(string value) =>
        Regex.Replace(value.Replace("\r\n", "\n", StringComparison.Ordinal), @"[ \t]+", " ").Trim();

    private static ExtractedDocumentText Empty(BillDocumentInputType inputType = BillDocumentInputType.Pdf) =>
        new(inputType == BillDocumentInputType.Image ? "image_ocr_openai" : "pdf_ocr_openai", []);

    public sealed class FinanceDocumentOcrOptions
    {
        public const string SectionName = "FinanceDocumentOcr";

        public bool Enabled { get; set; } = true;
        public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4.1-mini";
        public int TimeoutSeconds { get; set; } = 60;
        public int MaxOutputTokens { get; set; } = 4000;
        public int MaxPdfBytes { get; set; } = 10 * 1024 * 1024;
        public int MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    }

    private sealed class ResponsesRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public List<ResponsesInputMessage> Input { get; set; } = [];

        [JsonPropertyName("store")]
        public bool Store { get; set; }

        [JsonPropertyName("max_output_tokens")]
        public int MaxOutputTokens { get; set; }
    }

    private sealed class ResponsesInputMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public List<object> Content { get; set; } = [];
    }

    private sealed record ResponsesInputText(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record ResponsesInputFile(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("file_data")] string FileData);

    private sealed record ResponsesInputImage(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("image_url")] string ImageUrl);
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
