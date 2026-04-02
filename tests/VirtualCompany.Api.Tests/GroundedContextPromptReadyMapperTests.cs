using VirtualCompany.Application.Context;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class GroundedContextPromptReadyMapperTests
{
    [Fact]
    public void Normalize_splits_retrieved_context_into_typed_sections_and_preserves_source_references()
    {
        var generatedAtUtc = new DateTime(2026, 4, 2, 9, 30, 0, DateTimeKind.Utc);

        var knowledgeSection = new RetrievalSectionDto(
            "knowledge",
            "Knowledge",
            new[]
            {
                new RetrievalItemDto(
                    GroundedContextSourceTypes.KnowledgeChunk,
                    "chunk-b",
                    "  Payroll Controls  ",
                    " Finance  payroll approvals   require dual review. ",
                    0.72d,
                    null,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["documentId"] = "document-b",
                        ["documentType"] = "policy",
                        ["sourceType"] = "file",
                        ["sourceRef"] = "companies/1/policy.txt",
                        ["chunkIndex"] = "1"
                    }),
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
            });

        var memorySection = new RetrievalSectionDto(
            "memory",
            "Memory",
            new[]
            {
                new RetrievalItemDto(
                    GroundedContextSourceTypes.MemoryItem,
                    "memory-blank",
                    "Ignore",
                    "   ",
                    0.90d,
                    generatedAtUtc.AddMinutes(-5),
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["memoryType"] = "fact",
                        ["scope"] = "company_wide"
                    }),
                new RetrievalItemDto(
                    GroundedContextSourceTypes.MemoryItem,
                    "memory-a",
                    " Fact memory ",
                    " Shared   payroll calendar is current. ",
                    0.61d,
                    generatedAtUtc.AddMinutes(-1),
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["memoryType"] = "fact",
                        ["scope"] = "company_wide",
                        ["salience"] = "0.65",
                        ["validFromUtc"] = generatedAtUtc.AddDays(-1).ToString("O")
                    })
            });

        var recentTaskSection = new RetrievalSectionDto(
            "recent_tasks",
            "Recent Task History",
            new[]
            {
                new RetrievalItemDto(
                    GroundedContextSourceTypes.RecentTask,
                    "attempt-b",
                    " payroll (read) ",
                    " Status: executed.   Summary: Reviewed payroll controls. ",
                    0.50d,
                    generatedAtUtc.AddMinutes(-10),
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["toolName"] = "payroll",
                        ["actionType"] = "read",
                        ["status"] = "executed",
                        ["scope"] = "finance",
                        ["taskId"] = "task-2"
                    }),
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
                        ["taskId"] = "task-1",
                        ["approvalRequestId"] = "approval-1"
                    })
            });

        var relevantRecordsSection = new RetrievalSectionDto(
            "relevant_records",
            "Relevant Records",
            new[]
            {
                new RetrievalItemDto(
                    GroundedContextSourceTypes.CompanyRecord,
                    "company-a",
                    " Company Profile: VC ",
                    " Industry: Finance. ",
                    0.20d,
                    generatedAtUtc.AddHours(-1),
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sourceEntityType"] = "company_profile",
                        ["companyId"] = "company-a"
                    }),
                new RetrievalItemDto(
                    GroundedContextSourceTypes.ApprovalRequest,
                    "approval-a",
                    "Approval: payments",
                    " Status: pending. Action: write. ",
                    0.80d,
                    generatedAtUtc.AddMinutes(-2),
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sourceEntityType"] = "approval_request",
                        ["status"] = "pending"
                    })
            });

        var sourceReferences = new[]
        {
            CreateSourceReference("knowledge", 2, 2, GroundedContextSourceTypes.KnowledgeChunk, "chunk-b", "Payroll Controls", "finance payroll approvals require dual review."),
            CreateSourceReference("knowledge", 1, 1, GroundedContextSourceTypes.KnowledgeChunk, "chunk-a", "Payroll Controls", "finance payroll approvals require controller signoff."),
            CreateSourceReference("memory", 3, 1, GroundedContextSourceTypes.MemoryItem, "memory-a", "Fact memory", "shared payroll calendar is current."),
            CreateSourceReference("recent_tasks", 4, 1, GroundedContextSourceTypes.RecentTask, "attempt-a", "payroll (read)", "reviewed approval routing."),
            CreateSourceReference("recent_tasks", 5, 2, GroundedContextSourceTypes.RecentTask, "attempt-b", "payroll (read)", "reviewed payroll controls."),
            CreateSourceReference("relevant_records", 6, 1, GroundedContextSourceTypes.ApprovalRequest, "approval-a", "Approval: payments", "status: pending."),
            CreateSourceReference("relevant_records", 7, 2, GroundedContextSourceTypes.CompanyRecord, "company-a", "Company Profile: VC", "industry: finance.")
        };

        var normalized = GroundedContextPromptReadyMapper.Normalize(
            generatedAtUtc,
            knowledgeSection,
            memorySection,
            recentTaskSection,
            relevantRecordsSection,
            sourceReferences);

        Assert.Equal(["knowledge", "memory", "recent_tasks", "relevant_records"], normalized.Sections.OrderBy(x => x.Order).Select(x => x.Id));
        Assert.Equal(2, normalized.Documents.Items.Count);
        Assert.Equal("chunk-a", normalized.Documents.Items[0].ChunkId);
        Assert.Equal("Finance payroll approvals require controller signoff.", normalized.Documents.Items[0].Excerpt);
        Assert.Equal(1, normalized.Documents.Items[0].Source.GlobalRank);

        var memoryItem = Assert.Single(normalized.Memory.Items);
        Assert.Equal("Shared payroll calendar is current.", memoryItem.Summary);
        Assert.Equal("fact", memoryItem.MemoryType);
        Assert.Equal(1, memoryItem.Source.SectionRank);

        Assert.Equal(["attempt-a", "attempt-b"], normalized.RecentTasks.Items.Select(x => x.AttemptId));
        Assert.Equal("approval_request", normalized.RelevantRecords.Items[0].RecordType);
        Assert.Equal(["chunk-a", "chunk-b", "memory-a", "attempt-a", "attempt-b", "approval-a", "company-a"], normalized.SourceReferences.Select(x => x.SourceId));
        Assert.Equal(sourceReferences.Length, normalized.SourceReferences.Count);
        Assert.Equal(sourceReferences.Length, normalized.Counts.SourceReferences);
    }

    [Fact]
    public void Normalize_orders_equal_memory_candidates_by_salience_then_valid_from_then_identifier()
    {
        var generatedAtUtc = new DateTime(2026, 4, 2, 9, 30, 0, DateTimeKind.Utc);
        var normalized = GroundedContextPromptReadyMapper.Normalize(
            generatedAtUtc,
            new RetrievalSectionDto("knowledge", "Knowledge", Array.Empty<RetrievalItemDto>()),
            new RetrievalSectionDto(
                "memory",
                "Memory",
                new[]
                {
                    new RetrievalItemDto(GroundedContextSourceTypes.MemoryItem, "memory-c", "Memory C", "Shared payroll notes.", 0.80d, generatedAtUtc.AddMinutes(-4), new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["salience"] = "0.40", ["validFromUtc"] = generatedAtUtc.AddHours(-1).ToString("O") }),
                    new RetrievalItemDto(GroundedContextSourceTypes.MemoryItem, "memory-a", "Memory A", "Shared payroll notes.", 0.80d, generatedAtUtc.AddMinutes(-3), new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["salience"] = "0.90", ["validFromUtc"] = generatedAtUtc.AddHours(-4).ToString("O") }),
                    new RetrievalItemDto(GroundedContextSourceTypes.MemoryItem, "memory-b", "Memory B", "Shared payroll notes.", 0.80d, generatedAtUtc.AddMinutes(-2), new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["salience"] = "0.90", ["validFromUtc"] = generatedAtUtc.AddHours(-2).ToString("O") })
                }),
            new RetrievalSectionDto("recent_tasks", "Recent Task History", Array.Empty<RetrievalItemDto>()),
            new RetrievalSectionDto("relevant_records", "Relevant Records", Array.Empty<RetrievalItemDto>()),
            Array.Empty<RetrievalSourceReferenceDto>());

        Assert.Equal(["memory-b", "memory-a", "memory-c"], normalized.Memory.Items.Select(x => x.MemoryId));
    }

    [Fact]
    public void Normalize_returns_empty_sections_when_sources_are_empty()
    {
        var normalized = GroundedContextPromptReadyMapper.Normalize(
            new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc),
            new RetrievalSectionDto("knowledge", "Knowledge", Array.Empty<RetrievalItemDto>()),
            new RetrievalSectionDto("memory", "Memory", Array.Empty<RetrievalItemDto>()),
            new RetrievalSectionDto("recent_tasks", "Recent Task History", Array.Empty<RetrievalItemDto>()),
            new RetrievalSectionDto("relevant_records", "Relevant Records", Array.Empty<RetrievalItemDto>()),
            Array.Empty<RetrievalSourceReferenceDto>());

        Assert.Empty(normalized.Documents.Items);
        Assert.Empty(normalized.Memory.Items);
        Assert.Empty(normalized.RecentTasks.Items);
        Assert.Empty(normalized.RelevantRecords.Items);
        Assert.Empty(normalized.SourceReferences);
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
                ["retrievalSectionTitle"] = sectionId switch
                {
                    "knowledge" => "Knowledge",
                    "memory" => "Memory",
                    "recent_tasks" => "Recent Task History",
                    "relevant_records" => "Relevant Records",
                    _ => sectionId
                },
                ["retrievalSectionRank"] = sectionRank.ToString()
            });
}
