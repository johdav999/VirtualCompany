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

    public CompanyDocumentsController(ICompanyDocumentService documentService)
    {
        _documentService = documentService;
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
}