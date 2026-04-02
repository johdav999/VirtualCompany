using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Distributed;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class GroundedContextRetrievalIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public GroundedContextRetrievalIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RetrieveAsync_composes_structured_sections_and_persists_source_references()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var otherAgentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var asOfUtc = new DateTime(2026, 4, 2, 8, 0, 0, DateTimeKind.Utc);
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            ["finance payroll approvals", "finance payroll approvals", "finance payroll approvals"],
            CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "grounded@example.com", "Grounded User", "dev-header", "grounded-user"));
            dbContext.Companies.AddRange(new Company(companyId, "Context Company"), new Company(otherCompanyId, "Other Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            dbContext.Agents.AddRange(
                CreateAgent(companyId, agentId, "Finance Agent", ["finance"]),
                CreateAgent(companyId, otherAgentId, "Other Agent", ["hr"]));

            var generalDocument = CreateIndexedDocument(companyId, "Payroll Playbook");
            var scopedDocument = CreateIndexedDocument(
                companyId,
                "Finance Controls",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["restricted"] = JsonValue.Create(true),
                    ["scopes"] = new JsonArray(JsonValue.Create("finance"))
                });

            var otherCompanyDocument = CreateIndexedDocument(otherCompanyId, "Other Company Controls");

            dbContext.CompanyKnowledgeDocuments.AddRange(generalDocument, scopedDocument, otherCompanyDocument);
            dbContext.CompanyKnowledgeChunks.AddRange(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    generalDocument.Id,
                    1,
                    0,
                    "payroll guidance for approvals and reconciliation",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "payroll#chunk-1"),
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    scopedDocument.Id,
                    1,
                    0,
                    "finance payroll approvals require dual review",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "finance#chunk-1"),
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    otherCompanyId,
                    otherCompanyDocument.Id,
                    1,
                    0,
                    "finance payroll approvals require cross-company review",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[2].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "other#chunk-1"));

            dbContext.MemoryItems.AddRange(
                CreateMemory(companyId, agentId, "Finance approvals require controller signoff.", 0.950m,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["scope"] = JsonValue.Create("finance")
                    }),
                CreateMemory(companyId, null, "Shared payroll calendar is current.", 0.650m),
                CreateExpiredMemory(companyId, agentId, "This expired memory should not be returned.", 0.990m),
                CreateMemory(companyId, agentId, "HR-only staffing guidance.", 0.990m,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["scope"] = JsonValue.Create("hr")
                    }));

            dbContext.ToolExecutionAttempts.AddRange(
                CreateAttempt(companyId, agentId, "payroll", ToolActionType.Read, ToolExecutionStatus.Executed, "finance", taskId, "Validated the finance approval workflow."),
                CreateAttempt(companyId, otherAgentId, "staffing", ToolActionType.Read, ToolExecutionStatus.Executed, "hr", Guid.NewGuid(), "Other agent task should be excluded."));

            dbContext.ApprovalRequests.Add(
                new ApprovalRequest(
                    Guid.NewGuid(),
                    companyId,
                    agentId,
                    Guid.NewGuid(),
                    actorUserId,
                    "payments",
                    ToolActionType.Write,
                    "finance",
                    policyDecision: new Dictionary<string, JsonNode?>()));

            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var result = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                agentId,
                QueryText: "finance payroll approvals",
                ActorUserId: actorUserId,
                TaskId: taskId,
                TaskTitle: "Review payroll approvals",
                Limits: new RetrievalSourceLimitOptions(3, 4, 3, 4),
                CorrelationId: "ctx-test-correlation",
                RetrievalPurpose: "orchestration_context",
                AsOfUtc: asOfUtc),
            CancellationToken.None);

        Assert.Equal(companyId, result.CompanyContextSection.CompanyId);
        Assert.Equal(agentId, result.CompanyContextSection.AgentId);
        Assert.Contains("finance", result.CompanyContextSection.ReadScopes);

        Assert.Contains(result.KnowledgeSection.Items, x => x.Title == "Finance Controls");
        Assert.DoesNotContain(result.KnowledgeSection.Items, x => x.Title == "Other Company Controls");

        Assert.Contains(result.MemorySection.Items, x => x.Content.Contains("controller signoff", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MemorySection.Items, x => x.Content.Contains("Shared payroll calendar", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MemorySection.Items, x => x.Content.Contains("expired", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MemorySection.Items, x => x.Content.Contains("HR-only", StringComparison.OrdinalIgnoreCase));

        var recentTask = Assert.Single(result.RecentTaskSection.Items);
        Assert.Equal(GroundedContextSourceTypes.RecentTask, recentTask.SourceType);
        Assert.Contains("finance", recentTask.Content, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(result.RelevantRecordsSection.Items, x => x.SourceType == GroundedContextSourceTypes.ApprovalRequest);
        Assert.Contains(result.RelevantRecordsSection.Items, x => x.SourceType == GroundedContextSourceTypes.AgentRecord);
        Assert.Contains(result.RelevantRecordsSection.Items, x => x.SourceType == GroundedContextSourceTypes.CompanyRecord);
        Assert.All(result.SourceReferences, x => Assert.True(x.Metadata.ContainsKey("retrievalSection")));
        Assert.All(result.SourceReferences, x => Assert.False(string.IsNullOrWhiteSpace(x.SectionId)));
        Assert.All(result.SourceReferences, x => Assert.False(string.IsNullOrWhiteSpace(x.SectionTitle)));
        Assert.All(result.SourceReferences, x => Assert.True(x.SectionRank > 0));
        Assert.All(result.SourceReferences, x => Assert.False(string.IsNullOrWhiteSpace(x.Locator)));
        Assert.All(result.SourceReferences, x => Assert.True(x.Metadata.ContainsKey("retrievalSectionRank")));

        var returnedItemCount =
            result.KnowledgeSection.Items.Count +
            result.MemorySection.Items.Count +
            result.RecentTaskSection.Items.Count +
            result.RelevantRecordsSection.Items.Count;

        var expectedSourceOrder = result.KnowledgeSection.Items.Select(x => $"{x.SourceType}:{x.SourceId}")
            .Concat(result.MemorySection.Items.Select(x => $"{x.SourceType}:{x.SourceId}"))
            .Concat(result.RecentTaskSection.Items.Select(x => $"{x.SourceType}:{x.SourceId}"))
            .Concat(result.RelevantRecordsSection.Items.Select(x => $"{x.SourceType}:{x.SourceId}"))
            .ToArray();

        Assert.Equal(expectedSourceOrder, result.SourceReferences.Select(x => $"{x.SourceType}:{x.SourceId}"));

        Assert.Equal(returnedItemCount, result.SourceReferences.Count);
        Assert.Equal(Enumerable.Range(1, result.SourceReferences.Count), result.SourceReferences.Select(x => x.Rank));

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var persistedRetrieval = await verificationDbContext.ContextRetrievals
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == result.RetrievalId);
        var persistedSources = await verificationDbContext.ContextRetrievalSources
            .IgnoreQueryFilters()
            .Where(x => x.RetrievalId == result.RetrievalId)
            .OrderBy(x => x.Rank)
            .ToListAsync();

        Assert.Equal(companyId, persistedRetrieval.CompanyId);
        Assert.Equal(agentId, persistedRetrieval.AgentId);
        Assert.Equal(actorUserId, persistedRetrieval.ActorUserId);
        Assert.Equal(taskId, persistedRetrieval.TaskId);
        Assert.Equal("ctx-test-correlation", persistedRetrieval.CorrelationId);
        Assert.Equal(result.SourceReferences.Count, persistedSources.Count);
        Assert.Equal(result.SourceReferences.Select(x => x.SectionId), persistedSources.Select(x => x.SectionId));
        Assert.Equal(result.SourceReferences.Select(x => x.SectionTitle), persistedSources.Select(x => x.SectionTitle));
        Assert.Equal(result.SourceReferences.Select(x => x.SectionRank), persistedSources.Select(x => x.SectionRank));
        Assert.Equal(result.SourceReferences.Select(x => x.Locator), persistedSources.Select(x => x.Locator));
        Assert.Equal(result.SourceReferences.Select(x => x.SourceId), persistedSources.Select(x => x.SourceEntityId));

        var knowledgeSource = persistedSources.First(x =>
            x.SourceType == GroundedContextSourceTypes.KnowledgeChunk &&
            x.Title == "Finance Controls");

        Assert.Equal("knowledge_document", knowledgeSource.ParentSourceType);
        Assert.False(string.IsNullOrWhiteSpace(knowledgeSource.ParentSourceEntityId));
        Assert.Equal("Finance Controls", knowledgeSource.ParentTitle);
        Assert.Equal("knowledge", knowledgeSource.SectionId);
        Assert.Contains("chunk 1", knowledgeSource.Locator!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetrieveAsync_default_denies_scoped_sources_when_agent_scope_configuration_is_missing()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(["finance approvals", "finance approvals"], CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "noscope@example.com", "No Scope", "dev-header", "no-scope"));
            dbContext.Companies.Add(new Company(companyId, "No Scope Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "No Scope Agent", []));

            var openDocument = CreateIndexedDocument(companyId, "General Payroll Guide");
            var restrictedDocument = CreateIndexedDocument(
                companyId,
                "Restricted Finance Guide",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["restricted"] = JsonValue.Create(true),
                    ["scopes"] = new JsonArray(JsonValue.Create("finance"))
                });

            dbContext.CompanyKnowledgeDocuments.AddRange(openDocument, restrictedDocument);
            dbContext.CompanyKnowledgeChunks.AddRange(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    openDocument.Id,
                    1,
                    0,
                    "general payroll guide",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "general#chunk-1"),
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    restrictedDocument.Id,
                    1,
                    0,
                    "finance approvals",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "restricted#chunk-1"));

            dbContext.MemoryItems.AddRange(
                CreateMemory(companyId, null, "Finance-only guidance.", 0.900m,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["scope"] = JsonValue.Create("finance")
                    }),
                CreateMemory(companyId, null, "General guidance.", 0.500m));

            dbContext.ToolExecutionAttempts.Add(
                CreateAttempt(companyId, agentId, "payroll", ToolActionType.Read, ToolExecutionStatus.Executed, "finance", Guid.NewGuid(), "Scoped task should be excluded."));

            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var result = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                agentId,
                QueryText: "finance approvals",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(5, 5, 5, 5)),
            CancellationToken.None);

        Assert.Contains(result.KnowledgeSection.Items, x => x.Title == "General Payroll Guide");
        Assert.DoesNotContain(result.KnowledgeSection.Items, x => x.Title == "Restricted Finance Guide");
        Assert.Contains(result.MemorySection.Items, x => x.Content.Contains("General guidance", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MemorySection.Items, x => x.Content.Contains("Finance-only", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.RecentTaskSection.Items);
        Assert.Empty(result.CompanyContextSection.ReadScopes);
    }

    [Fact]
    public async Task RetrieveAsync_denies_all_context_when_actor_lacks_active_membership_in_target_company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(["finance approvals"], CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "outsider@example.com", "Outside User", "dev-header", "outside-user"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Target Company"),
                new Company(otherCompanyId, "Other Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "Finance Agent", ["finance"]));

            var document = CreateIndexedDocument(companyId, "Finance Controls");
            dbContext.CompanyKnowledgeDocuments.Add(document);
            dbContext.CompanyKnowledgeChunks.Add(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    document.Id,
                    1,
                    0,
                    "finance approvals",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "finance#chunk-1"));

            dbContext.MemoryItems.Add(CreateMemory(companyId, null, "General finance memory.", 0.800m));
            dbContext.ToolExecutionAttempts.Add(
                CreateAttempt(companyId, agentId, "payroll", ToolActionType.Read, ToolExecutionStatus.Executed, "finance", Guid.NewGuid(), "Should never be returned."));
            dbContext.ApprovalRequests.Add(
                new ApprovalRequest(
                    Guid.NewGuid(),
                    companyId,
                    agentId,
                    Guid.NewGuid(),
                    actorUserId,
                    "payments",
                    ToolActionType.Write,
                    "finance",
                    policyDecision: new Dictionary<string, JsonNode?>()));

            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var result = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                agentId,
                QueryText: "finance approvals",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(5, 5, 5, 5)),
            CancellationToken.None);

        Assert.Empty(result.KnowledgeSection.Items);
        Assert.Empty(result.MemorySection.Items);
        Assert.Empty(result.RecentTaskSection.Items);
        Assert.Empty(result.RelevantRecordsSection.Items);
        Assert.Empty(result.SourceReferences);
        Assert.Empty(result.CompanyContextSection.ReadScopes);
        Assert.False(result.AppliedFilters.MembershipResolved);
    }

    [Fact]
    public async Task RetrieveAsync_applies_human_role_restrictions_in_addition_to_agent_scopes()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            ["finance approvals", "finance approvals"],
            CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "employee@example.com", "Employee User", "dev-header", "employee-user"));
            dbContext.Companies.Add(new Company(companyId, "Role Scoped Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "Finance Agent", ["finance"]));

            var openDocument = CreateIndexedDocument(companyId, "General Finance Guide");
            var managerOnlyDocument = CreateIndexedDocument(
                companyId,
                "Manager Finance Guide",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["restricted"] = JsonValue.Create(true),
                    ["roles"] = new JsonArray(JsonValue.Create("manager")),
                    ["scopes"] = new JsonArray(JsonValue.Create("finance"))
                });

            dbContext.CompanyKnowledgeDocuments.AddRange(openDocument, managerOnlyDocument);
            dbContext.CompanyKnowledgeChunks.AddRange(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    openDocument.Id,
                    1,
                    0,
                    "finance approvals general guidance",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "general#chunk-1"),
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    managerOnlyDocument.Id,
                    1,
                    0,
                    "finance approvals manager-only guidance",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "manager#chunk-1"));

            dbContext.MemoryItems.AddRange(
                CreateMemory(companyId, null, "General finance memory.", 0.700m),
                CreateMemory(companyId, null, "Manager-only finance memory.", 0.950m,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["roles"] = new JsonArray(JsonValue.Create("manager")),
                        ["scope"] = JsonValue.Create("finance")
                    }));

            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var result = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                agentId,
                QueryText: "finance approvals",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(5, 5, 0, 0)),
            CancellationToken.None);

        Assert.Contains(result.KnowledgeSection.Items, x => x.Title == "General Finance Guide");
        Assert.DoesNotContain(result.KnowledgeSection.Items, x => x.Title == "Manager Finance Guide");
        Assert.Contains(result.MemorySection.Items, x => x.Content.Contains("General finance memory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MemorySection.Items, x => x.Content.Contains("Manager-only finance memory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.SourceReferences, x => x.Title == "Manager Finance Guide");
        Assert.DoesNotContain(result.SourceReferences, x => x.Snippet.Contains("Manager-only finance memory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetrieveAsync_persists_source_references_without_task_context_when_only_company_memory_is_used()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "memory-only@example.com", "Memory Only User", "dev-header", "memory-only-user"));
            dbContext.Companies.Add(new Company(companyId, "Memory Only Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "Memory Agent", ["finance"]));
            dbContext.MemoryItems.Add(
                CreateMemory(companyId, null, "Finance policy memory for downstream audit checks.", 0.880m));

            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var result = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                agentId,
                QueryText: "finance policy memory",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(0, 1, 0, 0)),
            CancellationToken.None);

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var persistedRetrieval = await verificationDbContext.ContextRetrievals
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == result.RetrievalId);
        var persistedSource = await verificationDbContext.ContextRetrievalSources
            .IgnoreQueryFilters()
            .SingleAsync(x => x.RetrievalId == result.RetrievalId);

        Assert.Null(persistedRetrieval.TaskId);
        Assert.Equal(companyId, persistedSource.CompanyId);
        Assert.Equal("memory", persistedSource.SectionId);
        Assert.Equal("Memory", persistedSource.SectionTitle);
        Assert.Equal(1, persistedSource.SectionRank);
        Assert.Equal(GroundedContextSourceTypes.MemoryItem, persistedSource.SourceType);
        Assert.Null(persistedSource.ParentSourceType);
        Assert.Null(persistedSource.ParentSourceEntityId);
        Assert.Contains("fact", persistedSource.Locator!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("company_wide", persistedSource.Locator!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetrieveAsync_returns_fewer_scoped_items_for_a_narrower_agent_scope()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var broadAgentId = Guid.NewGuid();
        var narrowAgentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            ["finance payroll approvals", "finance payroll approvals"],
            CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "scopes@example.com", "Scope User", "dev-header", "scope-user"));
            dbContext.Companies.Add(new Company(companyId, "Scope Comparison Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.AddRange(
                CreateAgent(companyId, broadAgentId, "Broad Agent", ["finance"]),
                CreateAgent(companyId, narrowAgentId, "Narrow Agent", ["hr"]));

            var openDocument = CreateIndexedDocument(companyId, "General Payroll Guide");
            var restrictedDocument = CreateIndexedDocument(
                companyId,
                "Finance Payroll Guide",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["restricted"] = JsonValue.Create(true),
                    ["scopes"] = new JsonArray(JsonValue.Create("finance"))
                });

            dbContext.CompanyKnowledgeDocuments.AddRange(openDocument, restrictedDocument);
            dbContext.CompanyKnowledgeChunks.AddRange(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    openDocument.Id,
                    1,
                    0,
                    "general payroll guidance",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "general#chunk-1"),
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    restrictedDocument.Id,
                    1,
                    0,
                    "finance payroll approvals",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "finance#chunk-1"));

            dbContext.MemoryItems.AddRange(
                CreateMemory(companyId, null, "General payroll process memory.", 0.500m),
                CreateMemory(companyId, broadAgentId, "Finance approval routing memory.", 0.950m,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["scope"] = JsonValue.Create("finance")
                    }));

            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var broadResult = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                broadAgentId,
                QueryText: "finance payroll approvals",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(5, 5, 0, 0)),
            CancellationToken.None);
        var narrowResult = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                narrowAgentId,
                QueryText: "finance payroll approvals",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(5, 5, 0, 0)),
            CancellationToken.None);

        Assert.Contains(broadResult.KnowledgeSection.Items, x => x.Title == "Finance Payroll Guide");
        Assert.DoesNotContain(narrowResult.KnowledgeSection.Items, x => x.Title == "Finance Payroll Guide");
        Assert.Contains(broadResult.MemorySection.Items, x => x.Content.Contains("Finance approval routing", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(narrowResult.MemorySection.Items, x => x.Content.Contains("Finance approval routing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetrieveAsync_orders_equal_memory_candidates_deterministically_by_created_time_then_id()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var firstMemoryId = Guid.NewGuid();
        var secondMemoryId = Guid.NewGuid();
        var createdUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

        using var scope = _factory.Services.CreateScope();

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "order@example.com", "Ordering User", "dev-header", "ordering-user"));
            dbContext.Companies.Add(new Company(companyId, "Ordering Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "Ordering Agent", ["finance"]));

            dbContext.MemoryItems.AddRange(
                new MemoryItem(firstMemoryId, companyId, null, MemoryType.Fact, "Finance guidance alpha.", null, null, 0.800m, createdUtc, null),
                new MemoryItem(secondMemoryId, companyId, null, MemoryType.Fact, "Finance guidance beta.", null, null, 0.800m, createdUtc, null));

            await Task.CompletedTask;
        });

        using (var normalizationScope = _factory.Services.CreateScope())
        {
            var dbContext = normalizationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE memory_items
                SET "CreatedUtc" = {0}
                WHERE "Id" IN ({1}, {2});
                """,
                createdUtc,
                firstMemoryId,
                secondMemoryId);
        }

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var result = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                agentId,
                QueryText: "finance guidance",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(0, 5, 0, 0)),
            CancellationToken.None);

        Assert.Equal(
            result.MemorySection.Items.OrderBy(x => x.SourceId, StringComparer.Ordinal).Select(x => x.SourceId),
            result.MemorySection.Items.Select(x => x.SourceId));
    }

    [Fact]
    public async Task RetrieveAsync_uses_injected_clock_when_request_as_of_utc_is_not_supplied()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var fixedUtc = new DateTime(2026, 4, 2, 9, 30, 0, DateTimeKind.Utc);

        using var factory = new TestWebApplicationFactory(new FixedTimeProvider(fixedUtc));
        using var scope = factory.Services.CreateScope();

        await factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "clock@example.com", "Clock User", "dev-header", "clock-user"));
            dbContext.Companies.Add(new Company(companyId, "Clock Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "Clock Agent", ["finance"]));
            dbContext.MemoryItems.AddRange(
                new MemoryItem(Guid.NewGuid(), companyId, null, MemoryType.Fact, "Active payroll memory.", null, null, 0.80m, fixedUtc.AddHours(-2), null),
                new MemoryItem(Guid.NewGuid(), companyId, null, MemoryType.Fact, "Future payroll memory.", null, null, 0.95m, fixedUtc.AddMinutes(10), null),
                new MemoryItem(Guid.NewGuid(), companyId, null, MemoryType.Fact, "Expired payroll memory.", null, null, 0.90m, fixedUtc.AddHours(-3), fixedUtc.AddMinutes(-1)));
            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var result = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                agentId,
                QueryText: "payroll memory",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(0, 5, 0, 0)),
            CancellationToken.None);

        Assert.Equal(fixedUtc, result.GeneratedAtUtc);
        Assert.Contains(result.MemorySection.Items, x => x.Content.Contains("Active payroll memory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MemorySection.Items, x => x.Content.Contains("Future payroll memory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MemorySection.Items, x => x.Content.Contains("Expired payroll memory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetrieveAsync_uses_as_of_utc_to_keep_memory_ranking_and_source_references_stable()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var asOfUtc = new DateTime(2026, 4, 2, 9, 30, 0, DateTimeKind.Utc);

        using var scope = _factory.Services.CreateScope();

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "stable@example.com", "Stable User", "dev-header", "stable-user"));
            dbContext.Companies.Add(new Company(companyId, "Stable Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "Stable Agent", ["finance"]));
            dbContext.MemoryItems.AddRange(
                CreateMemory(companyId, null, "Finance approval guidance alpha.", 0.800m),
                CreateMemory(companyId, null, "Finance approval guidance beta.", 0.700m));
            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var first = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                agentId,
                QueryText: "finance approval guidance",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(0, 5, 0, 0),
                AsOfUtc: asOfUtc),
            CancellationToken.None);
        var second = await retrievalService.RetrieveAsync(
            new GroundedContextRetrievalRequest(
                companyId,
                agentId,
                QueryText: "finance approval guidance",
                ActorUserId: actorUserId,
                Limits: new RetrievalSourceLimitOptions(0, 5, 0, 0),
                AsOfUtc: asOfUtc),
            CancellationToken.None);

        Assert.Equal(first.MemorySection.Items.Select(x => $"{x.SourceId}:{x.RelevanceScore}"), second.MemorySection.Items.Select(x => $"{x.SourceId}:{x.RelevanceScore}"));
        Assert.Equal(first.SourceReferences.Select(x => $"{x.SourceType}:{x.SourceId}:{x.Score}"), second.SourceReferences.Select(x => $"{x.SourceType}:{x.SourceId}:{x.Score}"));
    }

    [Fact]
    public async Task RetrieveAsync_reuses_cached_knowledge_section_for_identical_scoped_requests()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(
            ["finance payroll approvals", "finance payroll approvals"],
            CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "cache-hit@example.com", "Cache Hit User", "dev-header", "cache-hit-user"));
            dbContext.Companies.Add(new Company(companyId, "Cache Hit Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "Cache Hit Agent", ["finance"]));

            var firstDocument = CreateIndexedDocument(companyId, "Finance Controls A");
            dbContext.CompanyKnowledgeDocuments.Add(firstDocument);
            dbContext.CompanyKnowledgeChunks.Add(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    firstDocument.Id,
                    1,
                    0,
                    "finance payroll approvals require controller review",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "finance-a#chunk-1"));

            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var request = new GroundedContextRetrievalRequest(
            companyId,
            agentId,
            QueryText: "finance payroll approvals",
            ActorUserId: actorUserId,
            Limits: new RetrievalSourceLimitOptions(5, 0, 0, 0));

        var first = await retrievalService.RetrieveAsync(request, CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            var secondDocument = CreateIndexedDocument(companyId, "Finance Controls B");
            dbContext.CompanyKnowledgeDocuments.Add(secondDocument);
            dbContext.CompanyKnowledgeChunks.Add(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    secondDocument.Id,
                    1,
                    0,
                    "finance payroll approvals require secondary approval",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[1].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "finance-b#chunk-1"));

            await Task.CompletedTask;
        });

        var second = await retrievalService.RetrieveAsync(request, CancellationToken.None);

        Assert.Contains(first.KnowledgeSection.Items, item => item.Title == "Finance Controls A");
        Assert.Contains(second.KnowledgeSection.Items, item => item.Title == "Finance Controls A");
        Assert.DoesNotContain(second.KnowledgeSection.Items, item => item.Title == "Finance Controls B");
    }

    [Fact]
    public async Task RetrieveAsync_bypasses_cache_for_memory_requests_without_explicit_as_of_utc()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var validFromUtc = new DateTime(2026, 4, 2, 8, 0, 0, DateTimeKind.Utc);

        using var scope = _factory.Services.CreateScope();

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "memory-bypass@example.com", "Memory Bypass User", "dev-header", "memory-bypass-user"));
            dbContext.Companies.Add(new Company(companyId, "Memory Bypass Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "Memory Bypass Agent", ["finance"]));
            dbContext.MemoryItems.Add(
                new MemoryItem(Guid.NewGuid(), companyId, null, MemoryType.Fact, "General payroll reminder.", null, null, 0.200m, validFromUtc, null));
            await Task.CompletedTask;
        });

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var request = new GroundedContextRetrievalRequest(
            companyId,
            agentId,
            QueryText: "finance payroll approvals",
            ActorUserId: actorUserId,
            Limits: new RetrievalSourceLimitOptions(0, 5, 0, 0));

        var first = await retrievalService.RetrieveAsync(request, CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.MemoryItems.Add(
                new MemoryItem(Guid.NewGuid(), companyId, null, MemoryType.Fact, "Finance payroll approvals require controller signoff.", null, null, 1.000m, validFromUtc, null));
            await Task.CompletedTask;
        });

        var second = await retrievalService.RetrieveAsync(request, CancellationToken.None);

        Assert.DoesNotContain(first.MemorySection.Items, item => item.Content.Contains("controller signoff", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(second.MemorySection.Items, item => item.Content.Contains("controller signoff", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetrieveAsync_falls_back_to_live_knowledge_retrieval_when_cached_payload_is_invalid()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var actorMembershipId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var embeddingGenerator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();
        var embeddings = await embeddingGenerator.GenerateAsync(["finance payroll approvals"], CancellationToken.None);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(actorUserId, "invalid-cache@example.com", "Invalid Cache User", "dev-header", "invalid-cache-user"));
            dbContext.Companies.Add(new Company(companyId, "Invalid Cache Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(actorMembershipId, companyId, actorUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(companyId, agentId, "Invalid Cache Agent", ["finance"]));

            var document = CreateIndexedDocument(companyId, "Finance Controls");
            dbContext.CompanyKnowledgeDocuments.Add(document);
            dbContext.CompanyKnowledgeChunks.Add(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    companyId,
                    document.Id,
                    1,
                    0,
                    "finance payroll approvals require controller review",
                    KnowledgeEmbeddingSerializer.Serialize(embeddings.Embeddings[0].Values),
                    embeddings.Provider,
                    embeddings.Model,
                    embeddings.ModelVersion,
                    embeddings.Dimensions,
                    new Dictionary<string, JsonNode?>(),
                    "finance-invalid#chunk-1"));

            await Task.CompletedTask;
        });

        var distributedCache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var cacheKeyBuilder = scope.ServiceProvider.GetRequiredService<GroundedContextRetrievalCacheKeyBuilder>();
        var accessDecision = new RetrievalAccessDecision(
            companyId,
            agentId,
            actorUserId,
            actorMembershipId,
            CompanyMembershipRole.Manager,
            ["finance"],
            ["finance"],
            MembershipResolved: true,
            CanRetrieve: true);

        var request = new GroundedContextRetrievalRequest(
            companyId,
            agentId,
            QueryText: "finance payroll approvals",
            ActorUserId: actorUserId,
            Limits: new RetrievalSourceLimitOptions(5, 0, 0, 0));

        var cacheKey = cacheKeyBuilder.BuildKnowledgeSectionKey("tests-v1", request, accessDecision, "finance payroll approvals", 5);
        await distributedCache.SetAsync(cacheKey, [0x01, 0x02, 0x03], CancellationToken.None);

        var retrievalService = scope.ServiceProvider.GetRequiredService<IGroundedContextRetrievalService>();
        var result = await retrievalService.RetrieveAsync(request, CancellationToken.None);

        Assert.Contains(result.KnowledgeSection.Items, item => item.Title == "Finance Controls");
    }

    private static Agent CreateAgent(Guid companyId, Guid agentId, string displayName, IReadOnlyList<string> readScopes)
    {
        return new Agent(
            agentId,
            companyId,
            "operations",
            displayName,
            "Operations",
            "Finance",
            null,
            AgentSeniority.Mid,
            AgentStatus.Active,
            AgentAutonomyLevel.Guided,
            scopes: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["read"] = new JsonArray(readScopes.Select(JsonValue.Create).ToArray())
            });
    }

    private static CompanyKnowledgeDocument CreateIndexedDocument(
        Guid companyId,
        string title,
        Dictionary<string, JsonNode?>? accessScopeProperties = null)
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
                accessScopeProperties));

        document.MarkScanClean();
        document.MarkProcessing();
        document.MarkProcessed();
        document.MarkIndexed("finance payroll approvals", 1, 1, "test-provider", "test", "v1", 256, "seed-fingerprint-v1");
        return document;
    }

    private static MemoryItem CreateMemory(
        Guid companyId,
        Guid? agentId,
        string summary,
        decimal salience,
        Dictionary<string, JsonNode?>? metadata = null)
    {
        return new MemoryItem(
            Guid.NewGuid(),
            companyId,
            agentId,
            MemoryType.Fact,
            summary,
            null,
            null,
            salience,
            DateTime.UtcNow.AddHours(-1),
            null,
            metadata);
    }

    private static MemoryItem CreateExpiredMemory(Guid companyId, Guid? agentId, string summary, decimal salience)
    {
        return new MemoryItem(
            Guid.NewGuid(),
            companyId,
            agentId,
            MemoryType.Fact,
            summary,
            null,
            null,
            salience,
            DateTime.UtcNow.AddDays(-5),
            DateTime.UtcNow.AddDays(-1));
    }

    private static ToolExecutionAttempt CreateAttempt(
        Guid companyId,
        Guid agentId,
        string toolName,
        ToolActionType actionType,
        ToolExecutionStatus status,
        string? scope,
        Guid taskId,
        string summary)
    {
        var attempt = new ToolExecutionAttempt(
            Guid.NewGuid(),
            companyId,
            agentId,
            toolName,
            actionType,
            scope,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["taskId"] = JsonValue.Create(taskId)
            });

        if (status == ToolExecutionStatus.Executed)
        {
            attempt.MarkExecuted(
                policyDecision: new Dictionary<string, JsonNode?>(),
                resultPayload: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["summary"] = JsonValue.Create(summary),
                    ["taskId"] = JsonValue.Create(taskId)
                });
        }
        else
        {
            attempt.MarkDenied(new Dictionary<string, JsonNode?>());
        }

        return attempt;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTime utcNow)
        {
            _utcNow = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}