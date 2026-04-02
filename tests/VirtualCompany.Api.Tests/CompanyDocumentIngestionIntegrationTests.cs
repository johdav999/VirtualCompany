using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Documents;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyDocumentIngestionIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CompanyDocumentIngestionIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Upload_persists_metadata_stores_file_and_marks_document_scan_clean_via_placeholder_scanner()
    {
        _factory.DocumentStorage.Reset();
        _factory.DocumentVirusScanner.Reset();
        var seed = await SeedSingleMembershipAsync("manager", "manager@example.com", CompanyMembershipRole.Manager);

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        using var content = CreateUploadContent(
            "Operations Handbook",
            "reference",
            "ops-handbook.txt",
            "text/plain",
            "Important internal operating guidance.",
            """{"visibility":"company"}""",
            """{"category":"operations"}""");

        var response = await client.PostAsync($"/api/companies/{seed.CompanyId}/documents", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CompanyDocumentResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.CompanyId);
        Assert.Equal("Operations Handbook", payload.Title);
        Assert.Equal("reference", payload.DocumentType);
        Assert.NotNull(payload.AccessScope);
        Assert.Equal("company", payload.AccessScope!.Visibility);
        Assert.Equal(seed.CompanyId, payload.AccessScope.CompanyId);
        Assert.Equal("scan_clean", payload.IngestionStatus);
        Assert.Null(payload.FailureCode);
        Assert.StartsWith($"companies/{seed.CompanyId:N}/knowledge/", payload.StorageKey, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var document = await dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == payload.Id);

        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.ScanClean, document.IngestionStatus);
        Assert.Equal("company", document.AccessScope.Visibility);
        Assert.Equal(seed.CompanyId, document.AccessScope.CompanyId);
        Assert.Equal("ops-handbook.txt", document.OriginalFileName);
        Assert.Equal(".txt", document.FileExtension);
        Assert.Equal("text/plain", document.ContentType);
        Assert.Equal("ops-handbook.txt", document.Metadata["original_file_name"]!.GetValue<string>());
        Assert.Equal(".txt", document.Metadata["file_extension"]!.GetValue<string>());
        Assert.Equal(document.FileSizeBytes, document.Metadata["file_size_bytes"]!.GetValue<long>());
        Assert.Equal("text/plain", document.Metadata["content_type"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(document.Metadata["checksum_sha256"]!.GetValue<string>()));
        Assert.Equal("clean", document.Metadata["virus_scan"]!["outcome"]!.GetValue<string>());
        Assert.Equal("no_op_placeholder", document.Metadata["virus_scan"]!["scanner_name"]!.GetValue<string>());
        Assert.Null(document.ProcessedUtc);
        Assert.Null(document.ProcessingStartedUtc);
        Assert.True(_factory.DocumentStorage.StoredObjects.TryGetValue(document.StorageKey, out var storedBytes));
        Assert.Equal(1, _factory.DocumentVirusScanner.ScanCount);
        Assert.Equal("Important internal operating guidance.", Encoding.UTF8.GetString(storedBytes));

        var auditEvent = await dbContext.AuditEvents
            .SingleAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.TargetType == AuditTargetTypes.CompanyDocument &&
                x.TargetId == payload.Id.ToString("D") &&
                x.Action == AuditEventActions.CompanyDocumentUploaded);

        Assert.Equal(AuditEventOutcomes.Succeeded, auditEvent.Outcome);
   }

    [Fact]
    public async Task Upload_accepts_supported_docx_files_with_case_insensitive_extension_checks()
    {
        _factory.DocumentStorage.Reset();
        var seed = await SeedSingleMembershipAsync("manager", "manager@example.com", CompanyMembershipRole.Manager);

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        using var content = CreateUploadContent(
            "Policy Playbook",
            "policy",
            "playbook.DOCX",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "Supported document upload.",
            """{"visibility":"company"}""",
            """{"category":"policy"}""");

        var response = await client.PostAsync($"/api/companies/{seed.CompanyId}/documents", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CompanyDocumentResponse>();
        Assert.NotNull(payload);
        Assert.Equal("scan_clean", payload!.IngestionStatus);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var document = await dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == payload.Id);

        Assert.Equal(".docx", document.FileExtension);
        Assert.Equal("playbook.DOCX", document.OriginalFileName);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            document.ContentType);
        Assert.True(_factory.DocumentStorage.StoredObjects.ContainsKey(document.StorageKey));
    }

    [Fact]
    public async Task Upload_rejects_unsupported_file_types_with_actionable_validation_errors()
    {
        _factory.DocumentStorage.Reset();
        var seed = await SeedSingleMembershipAsync("manager", "manager@example.com", CompanyMembershipRole.Manager);

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        using var content = CreateUploadContent(
            "Malware",
            "report",
            "spreadsheet.xlsx",
            "application/octet-stream",
            "not allowed",
            """{"visibility":"company"}""",
            null);

        var response = await client.PostAsync($"/api/companies/{seed.CompanyId}/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(payload);
        Assert.Contains("File", payload!.Errors.Keys);
        Assert.Contains("Unsupported file format", payload.Errors["File"][0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(CompanyDocumentFileRules.SupportedFormatsDisplay, payload.Errors["File"][0], StringComparison.Ordinal);
        Assert.Contains("Upload a TXT, MD, PDF, DOC, or DOCX file", payload.Errors["File"][0], StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.False(await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().AnyAsync());
        Assert.Empty(_factory.DocumentStorage.StoredObjects);
    }

    [Fact]
    public async Task Upload_requires_access_scope_metadata_with_visibility()
    {
        _factory.DocumentStorage.Reset();
        var seed = await SeedSingleMembershipAsync("manager", "manager@example.com", CompanyMembershipRole.Manager);

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        using var content = new MultipartFormDataContent
        {
            { new StringContent("Operations Handbook"), "title" },
            { new StringContent("reference"), "document_type" }
        };

        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Important internal operating guidance."));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(fileContent, "file", "ops-handbook.txt");

        var response = await client.PostAsync($"/api/companies/{seed.CompanyId}/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(payload);
        Assert.Contains("AccessScope", payload!.Errors.Keys);
        Assert.Contains("visibility", payload.Errors["AccessScope"][0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_rejects_access_scope_company_id_that_crosses_tenants()
    {
        _factory.DocumentStorage.Reset();
        var seed = await SeedSingleMembershipAsync("manager", "manager@example.com", CompanyMembershipRole.Manager);
        var foreignCompanyId = Guid.NewGuid();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        using var content = CreateUploadContent(
            "Operations Handbook",
            "reference",
            "ops-handbook.txt",
            "text/plain",
            "Important internal operating guidance.",
            $"{{\"visibility\":\"company\",\"company_id\":\"{foreignCompanyId:D}\"}}",
            null);

        var response = await client.PostAsync($"/api/companies/{seed.CompanyId}/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(payload);
        Assert.Contains("AccessScope", payload!.Errors.Keys);
        Assert.Contains("must match", payload.Errors["AccessScope"][0], StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.False(await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().AnyAsync());
    }

    [Fact]
    public async Task Upload_rejects_nested_cross_tenant_scope_references()
    {
        _factory.DocumentStorage.Reset();
        var seed = await SeedSingleMembershipAsync("manager", "manager@example.com", CompanyMembershipRole.Manager);
        var foreignCompanyId = Guid.NewGuid();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        using var content = CreateUploadContent(
            "Operations Handbook",
            "reference",
            "ops-handbook.txt",
            "text/plain",
            "Important internal operating guidance.",
            $"{{\"visibility\":\"company\",\"rules\":{{\"company_id\":\"{foreignCompanyId:D}\"}}}}",
            null);

        var response = await client.PostAsync($"/api/companies/{seed.CompanyId}/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(payload);
        Assert.Contains("cross-tenant", payload!.Errors["AccessScope"][0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_and_get_are_company_scoped()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var documentAId = Guid.NewGuid();
        var documentBId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            var user = new User(Guid.NewGuid(), "alice@example.com", "Alice", "dev-header", "alice");
            dbContext.Users.Add(user);
            dbContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyAId, user.Id, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyBId, user.Id, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var companyADocument = new CompanyKnowledgeDocument(
                documentAId,
                companyAId,
                "A document",
                CompanyKnowledgeDocumentType.Policy,
                $"companies/{companyAId:N}/knowledge/{documentAId:N}/a.txt",
                null,
                "a.txt",
                "text/plain",
                ".txt",
                5,
                accessScope: new CompanyKnowledgeDocumentAccessScope(companyAId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
            companyADocument.MarkUploaded(companyADocument.StorageKey);
            companyADocument.MarkProcessed();

            var companyBDocument = new CompanyKnowledgeDocument(
                documentBId,
                companyBId,
                "B document",
                CompanyKnowledgeDocumentType.Reference,
                $"companies/{companyBId:N}/knowledge/{documentBId:N}/b.txt",
                null,
                "b.txt",
                "text/plain",
                ".txt",
                5,
                accessScope: new CompanyKnowledgeDocumentAccessScope(companyBId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
            companyBDocument.MarkUploaded(companyBDocument.StorageKey);
            companyBDocument.MarkProcessed();

            dbContext.CompanyKnowledgeDocuments.AddRange(companyADocument, companyBDocument);
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");

        var listResponse = await client.GetFromJsonAsync<List<CompanyDocumentResponse>>($"/api/companies/{companyAId}/documents");
        Assert.NotNull(listResponse);
        var listedDocument = Assert.Single(listResponse!);
        Assert.Equal(documentAId, listedDocument.Id);

        var getResponse = await client.GetAsync($"/api/companies/{companyBId}/documents/{documentAId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_marks_document_failed_when_storage_write_fails()
    {
        _factory.DocumentStorage.Reset();
        _factory.DocumentStorage.FailNext();
        var seed = await SeedSingleMembershipAsync("manager", "manager@example.com", CompanyMembershipRole.Manager);

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        using var content = CreateUploadContent(
            "Storage Failure Example",
            "report",
            "incident.txt",
            "text/plain",
            "storage should fail",
            """{"visibility":"company"}""",
            null);

        var response = await client.PostAsync($"/api/companies/{seed.CompanyId}/documents", content);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(payload);
        Assert.Contains("could not be stored", payload!.Detail!, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.False(await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().AnyAsync());

        var auditEvent = await dbContext.AuditEvents
            .SingleAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.TargetType == AuditTargetTypes.CompanyDocument &&
                x.Action == AuditEventActions.CompanyDocumentUploadFailed);

        Assert.Equal(AuditEventOutcomes.Failed, auditEvent.Outcome);
    }

    [Fact]
    public async Task Ingestion_status_service_marks_document_processing()
    {
        var seed = await SeedUploadedDocumentAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var ingestionStatusService = scope.ServiceProvider.GetRequiredService<ICompanyDocumentIngestionStatusService>();
            await ingestionStatusService.MarkScanCleanAsync(seed.CompanyId, seed.DocumentId, CompanyDocumentVirusScanResult.CleanPlaceholder(), CancellationToken.None);
            await ingestionStatusService.MarkScanCleanAsync(seed.CompanyId, seed.DocumentId, CompanyDocumentVirusScanResult.CleanPlaceholder(), CancellationToken.None);
            await ingestionStatusService.MarkProcessingAsync(seed.CompanyId, seed.DocumentId, CancellationToken.None);
        }

        using var verificationScope = _factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var document = await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.DocumentId);

        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Processing, document.IngestionStatus);
        Assert.NotNull(document.ProcessingStartedUtc);
    }

    [Fact]
    public async Task Ingestion_status_service_marks_document_processed()
    {
        var seed = await SeedUploadedDocumentAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var ingestionStatusService = scope.ServiceProvider.GetRequiredService<ICompanyDocumentIngestionStatusService>();
            await ingestionStatusService.MarkProcessedAsync(seed.CompanyId, seed.DocumentId, CancellationToken.None);
        }

        using var verificationScope = _factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var document = await dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == seed.DocumentId);

        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Processed, document.IngestionStatus);
        Assert.NotNull(document.ProcessedUtc);
        Assert.Null(document.FailureCode);
        Assert.Null(document.FailureMessage);

        var auditEvent = await dbContext.AuditEvents.SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.TargetType == AuditTargetTypes.CompanyDocument &&
            x.TargetId == seed.DocumentId.ToString("D") &&
            x.Action == AuditEventActions.CompanyDocumentProcessed);

        Assert.Equal(AuditActorTypes.System, auditEvent.ActorType);
        Assert.Equal(AuditEventOutcomes.Succeeded, auditEvent.Outcome);
    }

    [Fact]
    public async Task Ingestion_status_service_marks_document_failed_with_reason()
    {
        var seed = await SeedUploadedDocumentAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var ingestionStatusService = scope.ServiceProvider.GetRequiredService<ICompanyDocumentIngestionStatusService>();
            await ingestionStatusService.MarkFailedAsync(
                seed.CompanyId,
                seed.DocumentId,
                new CompanyDocumentIngestionFailure(
                    "extraction_failed",
                    "The document contents could not be extracted.",
                    "Upload a clean copy and retry ingestion.",
                    "InvalidDataException: Unexpected EOF while reading the document.",
                    CanRetry: false),
                CancellationToken.None);
        }

        using var client = CreateAuthenticatedClient("ingestion-manager", "ingestion@example.com", "Ingestion Manager");
        var response = await client.GetFromJsonAsync<CompanyDocumentResponse>($"/api/companies/{seed.CompanyId}/documents/{seed.DocumentId}");
        Assert.NotNull(response);
        Assert.Equal("Upload a clean copy and retry ingestion.", response!.FailureAction);
        Assert.False(response.CanRetry);

        using var verificationScope = _factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var document = await dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == seed.DocumentId);

        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Equal("extraction_failed", document.FailureCode);
        Assert.Equal("The document contents could not be extracted.", document.FailureMessage);
        Assert.Equal("Upload a clean copy and retry ingestion.", document.FailureAction);
        Assert.NotNull(document.FailedUtc);
        Assert.Equal("InvalidDataException: Unexpected EOF while reading the document.", document.FailureTechnicalDetail);
    }

    [Fact]
    public async Task Upload_marks_document_blocked_when_scan_result_is_blocked()
    {
        _factory.DocumentStorage.Reset();
        _factory.DocumentVirusScanner.Reset();
        _factory.DocumentVirusScanner.EnqueueResult(
            CompanyDocumentVirusScanResult.Blocked(
                "test_scanner",
                "2026.04",
                "virus_scan_blocked",
                "The uploaded document matched the blocking policy."));

        var seed = await SeedSingleMembershipAsync("manager", "manager@example.com", CompanyMembershipRole.Manager);

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        using var content = CreateUploadContent(
            "Blocked Example",
            "reference",
            "blocked.txt",
            "text/plain",
            "blocked content",
            """{"visibility":"company"}""",
            null);

        var response = await client.PostAsync($"/api/companies/{seed.CompanyId}/documents", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CompanyDocumentResponse>();
        Assert.NotNull(payload);
        Assert.Equal("blocked", payload!.IngestionStatus);
        Assert.Equal("virus_scan_blocked", payload.FailureCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var document = await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == payload.Id);
        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Blocked, document.IngestionStatus);
        Assert.Null(document.ProcessingStartedUtc);
        Assert.Equal("blocked", document.Metadata["virus_scan"]!["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task Semantic_search_endpoint_returns_chunk_content_and_source_reference_for_company_scope()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var otherDocumentId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
            var embeddings = await embeddingGenerator.GenerateAsync(
                ["secure remote payroll submission policy", "expense reimbursement handbook"],
                CancellationToken.None);

            await _factory.SeedAsync(dbContext =>
            {
                dbContext.Users.Add(new User(userId, "searcher@example.com", "Searcher", "dev-header", "semantic-searcher"));
                dbContext.Companies.AddRange(new Company(companyId, "Search Company"), new Company(otherCompanyId, "Other Company"));
                dbContext.CompanyMemberships.Add(
                    new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

                var document = CreateIndexedDocument(companyId, documentId, "Payroll Guide");
                var otherDocument = CreateIndexedDocument(otherCompanyId, otherDocumentId, "Expense Guide");

                dbContext.CompanyKnowledgeDocuments.AddRange(document, otherDocument);
                dbContext.CompanyKnowledgeChunks.AddRange(
                    new CompanyKnowledgeChunk(
                        Guid.NewGuid(),
                        companyId,
                        documentId,
                        1,
                        0,
                        "secure remote payroll submission policy",
                        KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                        embeddings.Provider,
                        embeddings.Model,
                        embeddings.ModelVersion,
                        embeddings.Dimensions,
                        new Dictionary<string, JsonNode?> { ["section"] = JsonValue.Create("payroll") },
                        "payroll-guide#chunk-1"),
                    new CompanyKnowledgeChunk(
                        Guid.NewGuid(),
                        otherCompanyId,
                        otherDocumentId,
                        1,
                        0,
                        "expense reimbursement handbook",
                        KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                        embeddings.Provider,
                        embeddings.Model,
                        embeddings.ModelVersion,
                        embeddings.Dimensions,
                        new Dictionary<string, JsonNode?> { ["section"] = JsonValue.Create("expenses") },
                        "expense-guide#chunk-1"));

                return Task.CompletedTask;
            });
        }

        using var client = CreateAuthenticatedClient("semantic-searcher", "searcher@example.com", "Searcher");
        var response = await client.GetFromJsonAsync<List<SemanticSearchResponse>>($"/api/companies/{companyId}/documents/semantic-search?q=secure%20remote%20payroll%20submission%20policy&top=3");
        var result = Assert.Single(response!);
        Assert.Equal(documentId, result.DocumentId);
        Assert.Equal("Payroll Guide", result.DocumentTitle);
        Assert.NotNull(result.SourceReferenceInfo);
        Assert.Equal(documentId, result.SourceReferenceInfo!.DocumentId);
        Assert.Equal("Payroll Guide", result.SourceReferenceInfo.DocumentTitle);
        Assert.Equal("policy", result.SourceReferenceInfo.DocumentType);
        Assert.Equal("upload", result.SourceReferenceInfo.SourceType);
        Assert.Equal(result.ChunkIndex, result.SourceReferenceInfo.ChunkIndex);
        Assert.Equal(result.SourceReference, result.SourceReferenceInfo.ChunkSourceReference);
        Assert.Equal("payroll-guide#chunk-1", result.SourceReference);
        Assert.Equal("secure remote payroll submission policy", result.Content);
    }

    private MultipartFormDataContent CreateUploadContent(
        string title,
        string documentType,
        string fileName,
        string contentType,
        string fileContents,
        string accessScopeJson,
        string? metadataJson)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(title), "title" },
            { new StringContent(documentType), "document_type" },
            { new StringContent(accessScopeJson), "access_scope" }
        };

        if (metadataJson is not null)
        {
            content.Add(new StringContent(metadataJson), "metadata");
        }

        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContents));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private async Task<SeededMembership> SeedSingleMembershipAsync(string subject, string email, CompanyMembershipRole role)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, subject, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Document Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, role, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new SeededMembership(userId, companyId);
    }

    private sealed record SeededMembership(Guid UserId, Guid CompanyId);
    private sealed record SeededDocument(Guid CompanyId, Guid DocumentId);

    private static CompanyKnowledgeDocument CreateIndexedDocument(Guid companyId, Guid documentId, string title)
    {
        var document = new CompanyKnowledgeDocument(
            documentId,
            companyId,
            title,
            CompanyKnowledgeDocumentType.Policy,
            $"companies/{companyId:N}/knowledge/{documentId:N}/policy.txt",
            null,
            "policy.txt",
            "text/plain",
            ".txt",
            64,
            accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));

        document.MarkScanClean();
        document.MarkProcessing();
        document.MarkProcessed();
        document.MarkIndexed(title, 1, 1, "test-provider", "test", "v1", 256, "seed-fingerprint-v1");
        return document;
    }

    private async Task<SeededDocument> SeedUploadedDocumentAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "ingestion@example.com", "Ingestion Manager", "dev-header", "ingestion-manager"));
            dbContext.Companies.Add(new Company(companyId, "Ingestion Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            dbContext.CompanyKnowledgeDocuments.Add(
                new CompanyKnowledgeDocument(
                    documentId,
                    companyId,
                    "Uploaded document",
                    CompanyKnowledgeDocumentType.Reference,
                    $"companies/{companyId:N}/knowledge/{documentId:N}/document.txt",
                    null,
                    "document.txt",
                    "text/plain",
                    ".txt",
                    42,
                    accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility)));

            return Task.CompletedTask;
        });

        return new SeededDocument(companyId, documentId);
    }

    private sealed class CompanyDocumentResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string IngestionStatus { get; set; } = string.Empty;
        public AccessScopeResponse? AccessScope { get; set; }
        public string StorageKey { get; set; } = string.Empty;
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
        public string? FailureAction { get; set; }
        public bool CanRetry { get; set; }
    }

    private sealed class SemanticSearchResponse
    {
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string SourceReference { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public double Score { get; set; }
        public SourceReferenceInfoResponse? SourceReferenceInfo { get; set; }
    }

    private sealed class SourceReferenceInfoResponse
    {
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string? SourceRef { get; set; }
        public Guid ChunkId { get; set; }
        public int ChunkIndex { get; set; }
        public string ChunkSourceReference { get; set; } = string.Empty;
    }

    private sealed class AccessScopeResponse
    {
        [JsonPropertyName("visibility")]
        public string Visibility { get; set; } = string.Empty;

        [JsonPropertyName("company_id")]
        public Guid CompanyId { get; set; }
    }
}
