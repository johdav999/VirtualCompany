using VirtualCompany.Application.Context;
using VirtualCompany.Infrastructure.Context;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class GroundedPromptContextServiceTests
{
    [Fact]
    public async Task PrepareAsync_returns_structured_prompt_context_sections_from_retrieval_result()
    {
        var generatedAtUtc = new DateTime(2026, 4, 2, 9, 30, 0, DateTimeKind.Utc);
        var retrievalResult = new GroundedContextRetrievalResult(
            Guid.NewGuid(),
            new CompanyContextSectionDto(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Finance Agent",
                "Controller",
                "Keeps payroll approval routing accurate.",
                ["finance"],
                "finance payroll approvals"),
            new RetrievalSectionDto(
                "knowledge",
                "Knowledge",
                [
                    new RetrievalItemDto(
                        GroundedContextSourceTypes.KnowledgeChunk,
                        "chunk-a",
                        "Payroll Controls",
                        "Finance payroll approvals require controller signoff.",
                        0.91d,
                        null,
                        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["documentId"] = "document-a",
                            ["documentType"] = "policy",
                            ["sourceType"] = "file",
                            ["sourceRef"] = "companies/1/controls.txt",
                            ["chunkIndex"] = "0"
                        })
                ]),
            new RetrievalSectionDto(
                "memory",
                "Memory",
                [
                    new RetrievalItemDto(
                        GroundedContextSourceTypes.MemoryItem,
                        "memory-a",
                        "Fact memory",
                        "Shared payroll calendar is current.",
                        0.61d,
                        generatedAtUtc.AddMinutes(-1),
                        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["memoryType"] = "fact",
                            ["scope"] = "company_wide",
                            ["salience"] = "0.65",
                            ["validFromUtc"] = generatedAtUtc.AddDays(-1).ToString("O")
                        })
                ]),
            new RetrievalSectionDto(
                "recent_tasks",
                "Recent Task History",
                [
                    new RetrievalItemDto(
                        GroundedContextSourceTypes.RecentTask,
                        "attempt-a",
                        "payroll (read)",
                        "Status: executed. Summary: Reviewed approval routing.",
                        0.50d,
                        generatedAtUtc.AddMinutes(-1),
                        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["toolName"] = "payroll",
                            ["actionType"] = "read",
                            ["status"] = "executed",
                            ["scope"] = "finance",
                            ["taskId"] = "task-1"
                        })
                ]),
            new RetrievalSectionDto(
                "relevant_records",
                "Relevant Records",
                [
                    new RetrievalItemDto(
                        GroundedContextSourceTypes.ApprovalRequest,
                        "approval-a",
                        "Approval: payments",
                        "Status: pending. Action: write.",
                        0.80d,
                        generatedAtUtc.AddMinutes(-2),
                        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["sourceEntityType"] = "approval_request",
                            ["status"] = "pending"
                        })
                ]),
            [
                CreateSourceReference("knowledge", 1, 1, GroundedContextSourceTypes.KnowledgeChunk, "chunk-a", "Payroll Controls", "finance payroll approvals require controller signoff."),
                CreateSourceReference("memory", 2, 1, GroundedContextSourceTypes.MemoryItem, "memory-a", "Fact memory", "shared payroll calendar is current."),
                CreateSourceReference("recent_tasks", 3, 1, GroundedContextSourceTypes.RecentTask, "attempt-a", "payroll (read)", "reviewed approval routing."),
                CreateSourceReference("relevant_records", 4, 1, GroundedContextSourceTypes.ApprovalRequest, "approval-a", "Approval: payments", "status: pending.")
            ],
            new RetrievalAppliedFiltersDto(["finance"], MembershipResolved: true, RestrictedKnowledgeExcludedByDefault: true, RestrictedMemoryExcludedByDefault: true, ScopedTaskHistoryExcludedByDefault: true))
        {
            GeneratedAtUtc = generatedAtUtc
        };

        var service = new GroundedPromptContextService(
            new StubGroundedContextRetrievalService(retrievalResult),
            new GroundedContextPromptBuilder());

        var result = await service.PrepareAsync(
            new GroundedPromptContextRequest(
                retrievalResult.CompanyContextSection.CompanyId,
                retrievalResult.CompanyContextSection.AgentId,
                QueryText: "finance payroll approvals",
                ActorUserId: retrievalResult.CompanyContextSection.ActorUserId,
                TaskTitle: "Review payroll approvals"),
            CancellationToken.None);

        Assert.Equal(retrievalResult.RetrievalId, result.RetrievalId);
        Assert.Equal(retrievalResult.GeneratedAtUtc, result.GeneratedAtUtc);
        Assert.Equal(retrievalResult.CompanyContextSection, result.Company);
        Assert.Equal(["knowledge", "memory", "recent_tasks", "relevant_records"], result.Context.Sections.OrderBy(x => x.Order).Select(x => x.Id));
        Assert.Equal("chunk-a", Assert.Single(result.Context.Documents.Items).ChunkId);
        Assert.Equal("memory-a", Assert.Single(result.Context.Memory.Items).MemoryId);
        Assert.Equal("attempt-a", Assert.Single(result.Context.RecentTasks.Items).AttemptId);
        Assert.Equal("approval-a", Assert.Single(result.Context.RelevantRecords.Items).RecordId);
        Assert.Equal(retrievalResult.SourceReferences.Select(x => x.SourceId), result.Context.SourceReferences.Select(x => x.SourceId));
    }

    [Fact]
    public async Task PrepareAsync_passes_scope_and_tenant_inputs_to_retrieval_service_without_assembling_prompt_text()
    {
        var retrievalResult = new GroundedContextRetrievalResult(
            Guid.NewGuid(),
            new CompanyContextSectionDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Ops Agent", "Operator", null, ["finance"], "finance approvals"),
            new RetrievalSectionDto("knowledge", "Knowledge", Array.Empty<RetrievalItemDto>()),
            new RetrievalSectionDto("memory", "Memory", Array.Empty<RetrievalItemDto>()),
            new RetrievalSectionDto("recent_tasks", "Recent Task History", Array.Empty<RetrievalItemDto>()),
            new RetrievalSectionDto("relevant_records", "Relevant Records", Array.Empty<RetrievalItemDto>()),
            Array.Empty<RetrievalSourceReferenceDto>(),
            new RetrievalAppliedFiltersDto(["finance"], MembershipResolved: true, RestrictedKnowledgeExcludedByDefault: true, RestrictedMemoryExcludedByDefault: true, ScopedTaskHistoryExcludedByDefault: true))
        {
            GeneratedAtUtc = new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc)
        };

        var retrievalService = new CapturingGroundedContextRetrievalService(retrievalResult);
        var service = new GroundedPromptContextService(retrievalService, new GroundedContextPromptBuilder());
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var asOfUtc = new DateTime(2026, 4, 2, 8, 0, 0, DateTimeKind.Utc);

        await service.PrepareAsync(
            new GroundedPromptContextRequest(
                companyId,
                agentId,
                QueryText: "finance approvals",
                ActorUserId: actorUserId,
                TaskId: taskId,
                TaskTitle: "Review approval routing",
                TaskDescription: "Focus on tenant-safe payroll approval paths.",
                Limits: new RetrievalSourceLimitOptions(2, 3, 4, 5),
                CorrelationId: "corr-123",
                RetrievalPurpose: "orchestration_context",
                AsOfUtc: asOfUtc),
            CancellationToken.None);

        Assert.NotNull(retrievalService.LastRequest);
        Assert.Equal(companyId, retrievalService.LastRequest!.CompanyId);
        Assert.Equal(agentId, retrievalService.LastRequest.AgentId);
        Assert.Equal(actorUserId, retrievalService.LastRequest.ActorUserId);
        Assert.Equal(taskId, retrievalService.LastRequest.TaskId);
        Assert.Equal("finance approvals", retrievalService.LastRequest.QueryText);
        Assert.Equal("Review approval routing", retrievalService.LastRequest.TaskTitle);
        Assert.Equal("Focus on tenant-safe payroll approval paths.", retrievalService.LastRequest.TaskDescription);
        Assert.Equal("corr-123", retrievalService.LastRequest.CorrelationId);
        Assert.Equal("orchestration_context", retrievalService.LastRequest.RetrievalPurpose);
        Assert.Equal(asOfUtc, retrievalService.LastRequest.AsOfUtc);
        Assert.Equal(new RetrievalSourceLimitOptions(2, 3, 4, 5), retrievalService.LastRequest.Limits);
    }

    private static RetrievalSourceReferenceDto CreateSourceReference(string sectionId, int rank, int sectionRank, string sourceType, string sourceId, string title, string snippet) =>
        new(
            sourceType,
            sourceId,
            title,
            sourceType == GroundedContextSourceTypes.KnowledgeChunk ? "knowledge_document" : null,
            sourceType == GroundedContextSourceTypes.KnowledgeChunk ? $"document-{sourceId}" : null,
            sourceType == GroundedContextSourceTypes.KnowledgeChunk ? title : null,
            sectionId,
            sectionId switch
            {
                "knowledge" => "Knowledge",
                "memory" => "Memory",
                "recent_tasks" => "Recent Task History",
                "relevant_records" => "Relevant Records",
                _ => sectionId
            },
            sectionRank,
            $"{title} | {sectionId}",
            rank,
            0.5d,
            snippet,
            null,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["retrievalSection"] = sectionId,
                ["retrievalSectionTitle"] = sectionId,
                ["retrievalSectionRank"] = sectionRank.ToString()
            });

    private sealed class StubGroundedContextRetrievalService(GroundedContextRetrievalResult result) : IGroundedContextRetrievalService
    {
        public Task<GroundedContextRetrievalResult> RetrieveAsync(GroundedContextRetrievalRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class CapturingGroundedContextRetrievalService(GroundedContextRetrievalResult result) : IGroundedContextRetrievalService
    {
        public GroundedContextRetrievalRequest? LastRequest { get; private set; }

        public Task<GroundedContextRetrievalResult> RetrieveAsync(GroundedContextRetrievalRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
