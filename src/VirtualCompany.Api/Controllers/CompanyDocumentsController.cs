using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Documents;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/documents")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class CompanyDocumentsController : ControllerBase
{
    private readonly ICompanyDocumentService _documentService;
    private readonly ICompanyKnowledgeSearchService _knowledgeSearchService;
    private readonly IWebHostEnvironment _environment;

    public CompanyDocumentsController(
        ICompanyDocumentService documentService,
        ICompanyKnowledgeSearchService knowledgeSearchService,
        IWebHostEnvironment environment)
    {
        _documentService = documentService;
        _knowledgeSearchService = knowledgeSearchService;
        _environment = environment;
    }

    [HttpPost("import-default-support-knowledge")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<ImportDefaultSupportKnowledgeResponse>> ImportDefaultSupportKnowledgeAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var existing = await _documentService.ListAsync(companyId, cancellationToken);
        var imported = new List<CompanyKnowledgeDocumentDto>();
        var skipped = new List<string>();
        foreach (var definition in DefaultSupportKnowledgeDocument.All)
        {
            if (existing.Any(x =>
                x.Metadata.TryGetValue("catalogKey", out var key) &&
                string.Equals(key?.GetValue<string>(), definition.CatalogKey, StringComparison.OrdinalIgnoreCase)))
            {
                skipped.Add(definition.Title);
                continue;
            }

            var path = Path.Combine(_environment.ContentRootPath, "DefaultKnowledge", definition.FileName);
            if (!System.IO.File.Exists(path))
            {
                return Problem(
                    title: "Default support knowledge is unavailable",
                    detail: $"The bundled document '{definition.FileName}' is missing from this deployment.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            await using var stream = System.IO.File.OpenRead(path);
            imported.Add(await _documentService.UploadAsync(
                companyId,
                new UploadCompanyDocumentCommand(
                    definition.Title,
                    definition.DocumentType,
                    new Dictionary<string, JsonNode?>
                    {
                        ["visibility"] = JsonValue.Create("company"),
                        ["data_scopes"] = new JsonArray("support", "knowledge")
                    },
                    new Dictionary<string, JsonNode?>
                    {
                        ["catalogKey"] = JsonValue.Create(definition.CatalogKey),
                        ["purpose"] = JsonValue.Create("customer_support")
                    },
                    definition.FileName,
                    "text/markdown",
                    stream.Length,
                    stream),
                cancellationToken));
        }

        return Ok(new ImportDefaultSupportKnowledgeResponse(imported, skipped));
    }

    [HttpGet]
    public Task<IReadOnlyList<CompanyKnowledgeDocumentDto>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        _documentService.ListAsync(companyId, cancellationToken);

    [HttpGet("{documentId:guid}")]
    public async Task<ActionResult<CompanyKnowledgeDocumentDto>> GetAsync(
        Guid companyId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var document = await _documentService.GetAsync(companyId, documentId, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpGet("semantic-search")]
    public async Task<ActionResult<IReadOnlyList<CompanyKnowledgeSearchResultDto>>> SemanticSearchAsync(
        Guid companyId,
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "top")] int top,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw BuildValidationException("Query", "A semantic search query is required.");
        }

        return Ok(await _knowledgeSearchService.SearchAsync(new CompanyKnowledgeSemanticSearchQuery(companyId, query, top <= 0 ? 5 : top), cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<CompanyKnowledgeDocumentDto>> UploadAsync(
        Guid companyId,
        [FromForm] UploadCompanyDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            throw BuildValidationException("File", "A file upload is required.");
        }

        await using var stream = request.File.OpenReadStream();
        var command = new UploadCompanyDocumentCommand(
            request.Title ?? string.Empty,
            request.DocumentType ?? string.Empty,
            ParseJsonDictionary(request.AccessScope, "AccessScope"),
            ParseJsonDictionary(request.Metadata, "Metadata"),
            request.File.FileName,
            request.File.ContentType,
            request.File.Length,
            stream);

        var document = await _documentService.UploadAsync(companyId, command, cancellationToken);
        return CreatedAtAction(
            nameof(GetAsync),
            new { companyId, documentId = document.Id },
            document);
    }

    private static Dictionary<string, JsonNode?>? ParseJsonDictionary(string? rawValue, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        try
        {
            var parsed = JsonNode.Parse(rawValue);
            if (parsed is not JsonObject jsonObject)
            {
                throw BuildValidationException(fieldName, $"{fieldName} must be a JSON object.");
            }

            var result = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in jsonObject)
            {
                result[property.Key] = property.Value?.DeepClone();
            }

            return result;
        }
        catch (JsonException)
        {
            throw BuildValidationException(fieldName, $"{fieldName} must be a valid JSON object.");
        }
    }

    private static CompanyDocumentValidationException BuildValidationException(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = [message]
        });

    public sealed class UploadCompanyDocumentRequest
    {
        [FromForm(Name = "title")]
        public string? Title { get; init; }

        [FromForm(Name = "document_type")]
        public string? DocumentType { get; init; }

        [FromForm(Name = "access_scope")]
        public string? AccessScope { get; init; }

        [FromForm(Name = "metadata")]
        public string? Metadata { get; init; }

        [FromForm(Name = "file")]
        public IFormFile? File { get; init; }
    }

    public sealed record ImportDefaultSupportKnowledgeResponse(
        IReadOnlyList<CompanyKnowledgeDocumentDto> Imported,
        IReadOnlyList<string> Skipped);

    private sealed record DefaultSupportKnowledgeDocument(string CatalogKey, string Title, string FileName, string DocumentType)
    {
        public static IReadOnlyList<DefaultSupportKnowledgeDocument> All { get; } =
        [
            new("virtual-company-product-catalog", "Virtual Company product catalog", "product-catalog.md", "reference"),
            new("virtual-company-company-policies", "Virtual Company company policies", "company-policies.md", "policy"),
            new("virtual-company-faq", "Virtual Company frequently asked questions", "faq.md", "reference")
        ];
    }
}
