using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class KnowledgeRetrievalIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public KnowledgeRetrievalIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Chunker_produces_stable_ordered_chunks_with_overlap()
    {
        var chunker = new DefaultKnowledgeChunker(
            Microsoft.Extensions.Options.Options.Create(
                new KnowledgeChunkingOptions
                {
                    TargetChunkLength = 80,
                    OverlapLength = 16,
                    MaxChunkCountPerDocument = 16,
                    StrategyVersion = "test-v1"
                }));

        var companyId = Guid.NewGuid();
        var document = new CompanyKnowledgeDocument(
            Guid.NewGuid(),
            companyId,
            "Handbook",
            CompanyKnowledgeDocumentType.Reference,
            $"companies/{companyId:N}/knowledge/handbook.txt",
            null,
            "handbook.txt",
            "text/plain",
            ".txt",
            100,
            accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));

        var chunks = chunker.ChunkDocument(
            document,
            "First paragraph has important onboarding guidance.\n\nSecond paragraph repeats context for overlap and retrieval.\n\nThird paragraph closes with escalation procedures.");

        Assert.True(chunks.Count >= 2);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.ChunkIndex));
        Assert.Contains("overlap", chunks[1].Content, StringComparison.OrdinalIgnoreCase);
        Assert.All(chunks, chunk => Assert.Equal("test-v1", chunk.Metadata["strategy"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Semantic_search_is_company_scoped_and_returns_source_references()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            ["payroll policy for remote employees", "payroll policy for remote employees"],
            CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "searcher@example.com", "Searcher", "dev-header", "searcher"));
            dbContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyAId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyBId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var documentA = CreateIndexedDocument(companyAId, "A Payroll Policy", sourceRef: "kb://company-a/payroll-policy");
            var documentB = CreateIndexedDocument(companyBId, "B Payroll Policy");

            dbContext.CompanyKnowledgeDocuments.AddRange(documentA, documentB);
            dbContext.CompanyKnowledgeChunks.AddRange(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyAId,
                    documentA.Id,
                    1,
                    0,
                    "payroll policy for remote employees",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["section"] = System.Text.Json.Nodes.JsonValue.Create("remote-payroll") },
                    "a-policy#chunk-1"),
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyBId,
                    documentB.Id,
                    1,
                    0,
                    "payroll policy for remote employees",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["section"] = System.Text.Json.Nodes.JsonValue.Create("remote-payroll") },
                    "b-policy#chunk-1"));

            await Task.CompletedTask;
        });

        var searchService = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeSearchService>();
        var results = await searchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(companyAId, "payroll policy for remote employees", 5),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(companyAId, await GetCompanyIdForDocumentAsync(result.DocumentId));
        Assert.Equal("A Payroll Policy", result.DocumentTitle);
        Assert.Equal(result.DocumentId, result.SourceReferenceInfo.DocumentId);
        Assert.Equal("A Payroll Policy", result.SourceReferenceInfo.DocumentTitle);
        Assert.Equal("policy", result.SourceReferenceInfo.DocumentType);
        Assert.Equal("upload", result.SourceReferenceInfo.SourceType);
        Assert.Equal("kb://company-a/payroll-policy", result.SourceReferenceInfo.SourceRef);
        Assert.Equal(result.ChunkId, result.SourceReferenceInfo.ChunkId);
        Assert.Equal(result.ChunkIndex, result.SourceReferenceInfo.ChunkIndex);
        Assert.Equal(result.SourceReference, result.SourceReferenceInfo.ChunkSourceReference);
        Assert.Equal("a-policy#chunk-1", result.SourceReference);
        Assert.Equal(result.DocumentId, result.SourceDocument.DocumentId);
        Assert.Equal("A Payroll Policy", result.SourceDocument.Title);
        Assert.Equal("policy", result.SourceDocument.DocumentType);
        Assert.Equal("upload", result.SourceDocument.SourceType);
        Assert.Equal("kb://company-a/payroll-policy", result.SourceDocument.SourceRef);
        Assert.Equal("remote-payroll", result.SourceMetadata["section"]!.GetValue<string>());
    }

    [Fact]
    public async Task Semantic_search_selects_top_k_within_company_filtered_candidates()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            ["payroll policy for remote employees", "travel reimbursement handbook for field staff"],
            CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "company-filter@example.com", "Company Filter", "dev-header", "company-filter"));
            dbContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyAId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var accessibleDocument = CreateIndexedDocument(companyAId, "Company A Payroll Summary");
            var otherCompanyDocument = CreateIndexedDocument(companyBId, "Company B Payroll Policy");

            dbContext.CompanyKnowledgeDocuments.AddRange(accessibleDocument, otherCompanyDocument);
            dbContext.CompanyKnowledgeChunks.AddRange(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyAId,
                    accessibleDocument.Id,
                    1,
                    0,
                    "travel reimbursement handbook for field staff",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(),
                    "company-a#chunk-1"),
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyBId,
                    otherCompanyDocument.Id,
                    1,
                    0,
                    "payroll policy for remote employees",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(),
                    "company-b#chunk-1"));

            await Task.CompletedTask;
        });

        var searchService = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeSearchService>();
        var results = await searchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(companyAId, "payroll policy for remote employees", 1),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Company A Payroll Summary", result.DocumentTitle);
        Assert.Equal(companyAId, await GetCompanyIdForDocumentAsync(result.DocumentId));
    }

    [Fact]
    public async Task Semantic_search_excludes_restricted_documents_before_similarity_ranking()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            ["payroll policy for remote employees", "remote staff payroll handbook"],
            CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "ranking@example.com", "Ranking", "dev-header", "ranking"));
            dbContext.Companies.Add(new Company(companyId, "Scoped Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var accessibleDocument = CreateIndexedDocument(companyId, "Accessible Payroll Handbook");
            var restrictedDocument = CreateIndexedDocument(
                companyId,
                "Restricted Payroll Handbook",
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["restricted"] = System.Text.Json.Nodes.JsonValue.Create(true),
                    ["roles"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonValue.Create("owner"))
                });

            dbContext.CompanyKnowledgeDocuments.AddRange(accessibleDocument, restrictedDocument);
            dbContext.CompanyKnowledgeChunks.AddRange(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    accessibleDocument.Id,
                    1,
                    0,
                    "remote staff payroll handbook",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(),
                    "accessible#chunk-1"),
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    restrictedDocument.Id,
                    1,
                    0,
                    "payroll policy for remote employees",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(),
                    "restricted#chunk-1"));

            await Task.CompletedTask;
        });

        var searchService = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeSearchService>();
        var results = await searchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(companyId, "payroll policy for remote employees", 5),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Accessible Payroll Handbook", result.DocumentTitle);
        Assert.DoesNotContain(results, item => item.DocumentTitle == "Restricted Payroll Handbook");
    }

    [Fact]
    public async Task Semantic_search_selects_top_k_within_scope_filtered_candidates()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            ["payroll policy for remote employees", "expense guidance for field teams"],
            CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "scope-filter@example.com", "Scope Filter", "dev-header", "scope-filter"));
            dbContext.Companies.Add(new Company(companyId, "Scope Filter Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var accessibleDocument = CreateIndexedDocument(companyId, "Accessible Expense Guide");
            var restrictedDocument = CreateIndexedDocument(
                companyId,
                "Restricted Payroll Handbook",
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["restricted"] = System.Text.Json.Nodes.JsonValue.Create(true),
                    ["scopes"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonValue.Create("finance"))
                });

            dbContext.CompanyKnowledgeDocuments.AddRange(accessibleDocument, restrictedDocument);
            dbContext.CompanyKnowledgeChunks.AddRange(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    accessibleDocument.Id,
                    1,
                    0,
                    "expense guidance for field teams",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(),
                    "accessible#chunk-1"),
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    restrictedDocument.Id,
                    1,
                    0,
                    "payroll policy for remote employees",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(),
                    "restricted#chunk-1"));

            await Task.CompletedTask;
        });

        var searchService = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeSearchService>();
        var results = await searchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(companyId, "payroll policy for remote employees", 1),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Accessible Expense Guide", result.DocumentTitle);
        Assert.DoesNotContain(results, item => item.DocumentTitle == "Restricted Payroll Handbook");
    }

    [Fact]
    public async Task Semantic_search_honors_caller_scope_context_for_scoped_documents()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(["finance controls for payroll approvals"], CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "scope@example.com", "Scope", "dev-header", "scope"));
            dbContext.Companies.Add(new Company(companyId, "Scope Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var scopedDocument = CreateIndexedDocument(
                companyId,
                "Finance Approvals",
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["restricted"] = System.Text.Json.Nodes.JsonValue.Create(true),
                    ["scopes"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonValue.Create("finance"))
                });

            dbContext.CompanyKnowledgeDocuments.Add(scopedDocument);
            dbContext.CompanyKnowledgeChunks.Add(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    scopedDocument.Id,
                    1,
                    0,
                    "finance controls for payroll approvals",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(),
                    "finance#chunk-1"));

            await Task.CompletedTask;
        });

        var searchService = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeSearchService>();
        var results = await searchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(
                companyId,
                "finance controls for payroll approvals",
                TopN: 5,
                AccessContext: new CompanyKnowledgeAccessContext(companyId, DataScopes: ["finance"])),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Finance Approvals", result.DocumentTitle);
        Assert.Equal("finance#chunk-1", result.SourceReference);
    }

    [Fact]
    public async Task Semantic_search_rejects_invalid_top_n_before_retrieval()
    {
        using var scope = _factory.Services.CreateScope();
        var searchService = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeSearchService>();

        var exception = await Assert.ThrowsAsync<CompanyKnowledgeSearchValidationException>(() =>
            searchService.SearchAsync(
                new CompanyKnowledgeSemanticSearchQuery(Guid.NewGuid(), "payroll policy", 0),
                CancellationToken.None));

        Assert.Equal("TopN must be between 1 and 20.", exception.Message);
    }

    [Fact]
    public async Task Process_pending_async_claims_and_indexes_a_queued_document()
    {
        _factory.DocumentStorage.Reset();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var storageKey = $"companies/{companyId:N}/knowledge/{documentId:N}/playbook.txt";

        _factory.DocumentStorage.Seed(storageKey, "Escalation playbooks must document the owner, timeline, and rollback plan.");

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "worker@example.com", "Worker", "dev-header", "worker"));
            dbContext.Companies.Add(new Company(companyId, "Worker Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var document = new CompanyKnowledgeDocument(
                documentId,
                companyId,
                "Escalation Playbook",
                CompanyKnowledgeDocumentType.Reference,
                storageKey,
                null,
                "playbook.txt",
                "text/plain",
                ".txt",
                128,
                accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));

            document.MarkScanClean();
            dbContext.CompanyKnowledgeDocuments.Add(document);
            await Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeIndexingProcessor>();

        var processedCount = await processor.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, processedCount);

        using var verificationScope = _factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var document = await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == documentId);
        var chunks = await dbContext.CompanyKnowledgeChunks.IgnoreQueryFilters().Where(x => x.DocumentId == documentId && x.IsActive).ToListAsync();

        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Processed, document.IngestionStatus);
        Assert.Equal(CompanyKnowledgeDocumentIndexingStatus.Indexed, document.IndexingStatus);
        Assert.False(string.IsNullOrWhiteSpace(document.EmbeddingProvider));
        Assert.NotEmpty(chunks);
        Assert.All(
            chunks,
            chunk =>
            {
                Assert.Equal(document.EmbeddingProvider, chunk.EmbeddingProvider);
                Assert.False(string.IsNullOrWhiteSpace(chunk.EmbeddingProvider));
            });
    }

    [Fact]
    public async Task Reindexing_replaces_active_chunk_set_without_leaking_old_results()
    {
        _factory.DocumentStorage.Reset();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var storageKey = $"companies/{companyId:N}/knowledge/{documentId:N}/manual.txt";

        _factory.DocumentStorage.Seed(storageKey, "The original procedure says to fax the form.");

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "indexer@example.com", "Indexer", "dev-header", "indexer"));
            dbContext.Companies.Add(new Company(companyId, "Indexed Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var document = new CompanyKnowledgeDocument(
                documentId,
                companyId,
                "Operations Manual",
                CompanyKnowledgeDocumentType.Procedure,
                storageKey,
                null,
                "manual.txt",
                "text/plain",
                ".txt",
                128,
                accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
            document.MarkScanClean();
            dbContext.CompanyKnowledgeDocuments.Add(document);
            await Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeIndexingProcessor>();
        var searchService = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeSearchService>();

        await processor.IndexDocumentAsync(companyId, documentId, CancellationToken.None);

        _factory.DocumentStorage.Seed(storageKey, "The updated procedure requires a secure portal upload.");

        using (var requeueScope = _factory.Services.CreateScope())
        {
            var dbContext = requeueScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var document = await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == documentId);
            document.QueueIndexing();
            await dbContext.SaveChangesAsync();
        }

        await processor.IndexDocumentAsync(companyId, documentId, CancellationToken.None);

        using (var verificationScope = _factory.Services.CreateScope())
        {
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var chunks = await dbContext.CompanyKnowledgeChunks.IgnoreQueryFilters()
                .Where(chunk => chunk.DocumentId == documentId)
                .OrderBy(chunk => chunk.ChunkSetVersion)
                .ToListAsync();

            Assert.Contains(chunks, chunk => chunk.ChunkSetVersion == 1 && !chunk.IsActive);
            Assert.Contains(chunks, chunk => chunk.ChunkSetVersion == 2 && chunk.IsActive);
        }

        var results = await searchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(companyId, "secure portal upload", 5),
            CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.DoesNotContain(results, result => result.Content.Contains("fax the form", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, result => result.Content.Contains("secure portal upload", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reindexing_failure_keeps_previous_chunk_set_retrievable()
    {
        _factory.DocumentStorage.Reset();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var storageKey = $"companies/{companyId:N}/knowledge/{documentId:N}/manual.txt";

        _factory.DocumentStorage.Seed(storageKey, "The current handbook requires badge checks at every entry point.");

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "resilience@example.com", "Resilience", "dev-header", "resilience"));
            dbContext.Companies.Add(new Company(companyId, "Resilience Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var document = new CompanyKnowledgeDocument(
                documentId,
                companyId,
                "Security Handbook",
                CompanyKnowledgeDocumentType.Reference,
                storageKey,
                null,
                "manual.txt",
                "text/plain",
                ".txt",
                128,
                accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
            document.MarkScanClean();
            dbContext.CompanyKnowledgeDocuments.Add(document);
            await Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeIndexingProcessor>();
        var searchService = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeSearchService>();

        await processor.IndexDocumentAsync(companyId, documentId, CancellationToken.None);

        _factory.DocumentStorage.Reset();

        using (var requeueScope = _factory.Services.CreateScope())
        {
            var dbContext = requeueScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var document = await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == documentId);
            document.QueueIndexing();
            await dbContext.SaveChangesAsync();
        }

        await processor.IndexDocumentAsync(companyId, documentId, CancellationToken.None);

        using (var verificationScope = _factory.Services.CreateScope())
        {
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var document = await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == documentId);
            var chunks = await dbContext.CompanyKnowledgeChunks.IgnoreQueryFilters()
                .Where(chunk => chunk.DocumentId == documentId)
                .OrderBy(chunk => chunk.ChunkSetVersion)
                .ToListAsync();

            Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Processed, document.IngestionStatus);
            Assert.Equal(CompanyKnowledgeDocumentIndexingStatus.Indexed, document.IndexingStatus);
            Assert.Equal(1, document.CurrentChunkSetVersion);
            Assert.All(chunks, chunk => Assert.True(chunk.IsActive));
            Assert.All(chunks, chunk => Assert.Equal(1, chunk.ChunkSetVersion));
        }

        var results = await searchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(companyId, "badge checks at every entry point", 5),
            CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Contains(results, result => result.Content.Contains("badge checks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reindexing_same_content_is_idempotent_and_reuses_current_chunk_set()
    {
        _factory.DocumentStorage.Reset();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var storageKey = $"companies/{companyId:N}/knowledge/{documentId:N}/manual.txt";

        _factory.DocumentStorage.Seed(storageKey, "The travel policy requires manager approval for international trips.");

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "dedupe@example.com", "Dedupe", "dev-header", "dedupe"));
            dbContext.Companies.Add(new Company(companyId, "Dedupe Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var document = new CompanyKnowledgeDocument(
                documentId,
                companyId,
                "Travel Policy",
                CompanyKnowledgeDocumentType.Policy,
                storageKey,
                null,
                "manual.txt",
                "text/plain",
                ".txt",
                128,
                accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
            document.MarkScanClean();
            dbContext.CompanyKnowledgeDocuments.Add(document);
            await Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeIndexingProcessor>();

        await processor.IndexDocumentAsync(companyId, documentId, CancellationToken.None);

        using (var requeueScope = _factory.Services.CreateScope())
        {
            var dbContext = requeueScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var document = await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == documentId);
            document.QueueIndexing();
            await dbContext.SaveChangesAsync();
        }

        await processor.IndexDocumentAsync(companyId, documentId, CancellationToken.None);

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var reindexedDocument = await verificationDbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == documentId);
        var chunks = await verificationDbContext.CompanyKnowledgeChunks.IgnoreQueryFilters()
            .Where(chunk => chunk.DocumentId == documentId)
            .OrderBy(chunk => chunk.ChunkSetVersion)
            .ToListAsync();

        Assert.Equal(1, reindexedDocument.CurrentChunkSetVersion);
        Assert.All(chunks, chunk => Assert.Equal(1, chunk.ChunkSetVersion));
        Assert.All(chunks, chunk => Assert.True(chunk.IsActive));
    }

    [Fact]
    public async Task Indexing_failure_marks_document_failed_when_no_text_is_available()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "failure@example.com", "Failure", "dev-header", "failure"));
            dbContext.Companies.Add(new Company(companyId, "Failure Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            var document = new CompanyKnowledgeDocument(
                documentId,
                companyId,
                "Unsupported PDF",
                CompanyKnowledgeDocumentType.Reference,
                $"companies/{companyId:N}/knowledge/{documentId:N}/unsupported.pdf",
                null,
                "unsupported.pdf",
                "application/pdf",
                ".pdf",
                512,
                accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
            document.MarkScanClean();
            dbContext.CompanyKnowledgeDocuments.Add(document);
            await Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeIndexingProcessor>();
        await processor.IndexDocumentAsync(companyId, documentId, CancellationToken.None);

        using var verificationScope = _factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var document = await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == documentId);

        Assert.Equal(CompanyKnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Equal(CompanyKnowledgeDocumentIndexingStatus.Failed, document.IndexingStatus);
        Assert.Equal("knowledge_indexing_failed", document.IndexingFailureCode);
        Assert.NotNull(document.FailureAction);
    }

    private static CompanyKnowledgeDocument CreateIndexedDocument(
        Guid companyId,
        string title,
        Dictionary<string, System.Text.Json.Nodes.JsonNode?>? accessScopeProperties = null,
        string? sourceRef = null)
    {
        var document = new CompanyKnowledgeDocument(
            Guid.NewGuid(),
            companyId,
            title,
            CompanyKnowledgeDocumentType.Policy,
            $"companies/{companyId:N}/knowledge/{Guid.NewGuid():N}/policy.txt",
            null,
            "policy.txt",
            "text/plain",
            ".txt",
            64,
            accessScope: new CompanyKnowledgeDocumentAccessScope(
                companyId,
                CompanyKnowledgeDocumentAccessScope.CompanyVisibility,
                accessScopeProperties),
            sourceRef: sourceRef);

        document.MarkScanClean();
        document.MarkProcessing();
        document.MarkProcessed();
        document.MarkIndexed("payroll policy for remote employees", 1, 1, "test-provider", "test", "v1", 256, "seed-fingerprint-v1");
        return document;
    }

    private async Task<Guid> GetCompanyIdForDocumentAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        return await dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters()
            .Where(document => document.Id == documentId)
            .Select(document => document.CompanyId)
            .SingleAsync();
    }
}