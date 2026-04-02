using System.Text.Json.Nodes;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Context;

public sealed record GroundedPromptContextRequest(
    Guid CompanyId,
    Guid AgentId,
    string? QueryText = null,
    Guid? ActorUserId = null,
    Guid? TaskId = null,
    string? TaskTitle = null,
    string? TaskDescription = null,
    RetrievalSourceLimitOptions? Limits = null,
    string? CorrelationId = null,
    string? RetrievalPurpose = null,
    DateTime? AsOfUtc = null)
{
    public GroundedContextRetrievalRequest ToRetrievalRequest() =>
        new(CompanyId, AgentId, QueryText, ActorUserId, TaskId, TaskTitle, TaskDescription, Limits, CorrelationId, RetrievalPurpose, AsOfUtc);
}

public sealed record GroundedContextRetrievalRequest(
    Guid CompanyId,
    Guid AgentId,
    string? QueryText = null,
    Guid? ActorUserId = null,
    Guid? TaskId = null,
    string? TaskTitle = null,
    string? TaskDescription = null,
    RetrievalSourceLimitOptions? Limits = null,
    string? CorrelationId = null,
    string? RetrievalPurpose = null,
    DateTime? AsOfUtc = null);

public sealed record RetrievalSourceLimitOptions(
    int KnowledgeItems = 5,
    int MemoryItems = 5,
    int RecentTasks = 5,
    int RelevantRecords = 3);

public sealed record GroundedContextRetrievalResult(
    Guid RetrievalId,
    CompanyContextSectionDto CompanyContextSection,
    RetrievalSectionDto KnowledgeSection,
    RetrievalSectionDto MemorySection,
    RetrievalSectionDto RecentTaskSection,
    RetrievalSectionDto RelevantRecordsSection,
    IReadOnlyList<RetrievalSourceReferenceDto> SourceReferences,
    RetrievalAppliedFiltersDto AppliedFilters)
{
    public DateTime GeneratedAtUtc { get; init; }
    public NormalizedGroundedContextDto NormalizedContext { get; init; } = NormalizedGroundedContextDto.Empty;
}

public sealed record CompanyContextSectionDto(
    Guid CompanyId,
    Guid AgentId,
    Guid? ActorUserId,
    string AgentDisplayName,
    string AgentRoleName,
    string? AgentRoleBrief,
    IReadOnlyList<string> ReadScopes,
    string RetrievalIntent);

public sealed record RetrievalSectionDto(
    string Id,
    string Title,
    IReadOnlyList<RetrievalItemDto> Items);

public sealed record RetrievalItemDto(
    string SourceType,
    string SourceId,
    string Title,
    string Content,
    double? RelevanceScore,
    DateTime? TimestampUtc,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record RetrievalSourceReferenceDto(
    string SourceType,
    string SourceId,
    string Title,
    string? ParentSourceType,
    string? ParentSourceId,
    string? ParentTitle,
    string SectionId,
    string SectionTitle,
    int SectionRank,
    string? Locator,
    int Rank,
    double? Score,
    string Snippet,
    DateTime? TimestampUtc,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record RetrievalAppliedFiltersDto(
    IReadOnlyList<string> ReadScopes,
    bool MembershipResolved,
    bool RestrictedKnowledgeExcludedByDefault,
    bool RestrictedMemoryExcludedByDefault,
    bool ScopedTaskHistoryExcludedByDefault);

public sealed record NormalizedGroundedContextDto(
    IReadOnlyList<GroundedContextSectionDescriptorDto> Sections,
    DocumentContextSectionDto Documents,
    MemoryContextSectionDto Memory,
    RecentTaskContextSectionDto RecentTasks,
    RelevantRecordContextSectionDto RelevantRecords,
    IReadOnlyList<RetrievalSourceReferenceDto> SourceReferences,
    GroundedContextSectionCountsDto Counts)
{
    public static NormalizedGroundedContextDto Empty { get; } = new(
        Array.Empty<GroundedContextSectionDescriptorDto>(),
        new DocumentContextSectionDto("knowledge", "Knowledge", Array.Empty<DocumentContextItemDto>()),
        new MemoryContextSectionDto("memory", "Memory", Array.Empty<MemoryContextItemDto>()),
        new RecentTaskContextSectionDto("recent_tasks", "Recent Task History", Array.Empty<RecentTaskContextItemDto>()),
        new RelevantRecordContextSectionDto("relevant_records", "Relevant Records", Array.Empty<RelevantRecordContextItemDto>()),
        Array.Empty<RetrievalSourceReferenceDto>(),
        new GroundedContextSectionCountsDto(0, 0, 0, 0, 0));
}

public sealed record GroundedPromptContextDto(
    Guid RetrievalId,
    DateTime GeneratedAtUtc,
    CompanyContextSectionDto Company,
    NormalizedGroundedContextDto Context,
    RetrievalAppliedFiltersDto AppliedFilters);

public sealed record GroundedContextSectionDescriptorDto(
    string Id,
    string Title,
    int Order,
    int ItemCount);

public sealed record GroundedContextSectionCountsDto(
    int DocumentItems,
    int MemoryItems,
    int RecentTaskItems,
    int RelevantRecordItems,
    int SourceReferences);

public sealed record GroundedContextItemSourceDto(
    string SourceType,
    string SourceId,
    string Title,
    int? GlobalRank,
    int? SectionRank,
    double? Score,
    DateTime? TimestampUtc,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record DocumentContextSectionDto(
    string Id,
    string Title,
    IReadOnlyList<DocumentContextItemDto> Items);

public sealed record DocumentContextItemDto(
    string ChunkId,
    string DocumentId,
    string Title,
    string Excerpt,
    string? DocumentType,
    string? DocumentSourceType,
    string? DocumentSourceReference,
    int? ChunkIndex,
    double? RelevanceScore,
    GroundedContextItemSourceDto Source);

public sealed record MemoryContextSectionDto(
    string Id,
    string Title,
    IReadOnlyList<MemoryContextItemDto> Items);

public sealed record MemoryContextItemDto(
    string MemoryId,
    string Title,
    string Summary,
    string? MemoryType,
    string? Scope,
    double? Salience,
    DateTime? CreatedUtc,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc,
    double? RelevanceScore,
    GroundedContextItemSourceDto Source);

public sealed record RecentTaskContextSectionDto(
    string Id,
    string Title,
    IReadOnlyList<RecentTaskContextItemDto> Items);

public sealed record RecentTaskContextItemDto(
    string AttemptId,
    string Title,
    string Summary,
    string? ToolName,
    string? ActionType,
    string? Status,
    string? Scope,
    string? TaskId,
    string? ApprovalRequestId,
    DateTime? TimestampUtc,
    double? RelevanceScore,
    GroundedContextItemSourceDto Source);

public sealed record RelevantRecordContextSectionDto(
    string Id,
    string Title,
    IReadOnlyList<RelevantRecordContextItemDto> Items);

public sealed record RelevantRecordContextItemDto(
    string RecordId,
    string RecordType,
    string Label,
    string Summary,
    IReadOnlyDictionary<string, string?> Fields,
    DateTime? TimestampUtc,
    double? RelevanceScore,
    GroundedContextItemSourceDto Source);

public sealed record RetrievalAccessContext(
    Guid CompanyId,
    Guid AgentId,
    Guid? ActorUserId,
    Guid? ActorMembershipId,
    CompanyMembershipRole? ActorMembershipRole,
    IReadOnlyDictionary<string, JsonNode?> AgentDataScopes);

public sealed record RetrievalAccessDecision(
    Guid CompanyId,
    Guid AgentId,
    Guid? ActorUserId,
    Guid? ActorMembershipId,
    CompanyMembershipRole? ActorMembershipRole,
    IReadOnlyList<string> AgentReadScopes,
    IReadOnlyList<string> EffectiveReadScopes,
    bool MembershipResolved,
    bool CanRetrieve);

public interface IRetrievalScopeEvaluator
{
    RetrievalAccessDecision Evaluate(RetrievalAccessContext context);
    CompanyKnowledgeAccessContext BuildKnowledgeAccessContext(RetrievalAccessDecision decision);
    bool CanAccessMemory(RetrievalAccessDecision decision, MemoryItem item);
    bool CanAccessTaskScope(RetrievalAccessDecision decision, string? scope);
}
public interface IGroundedContextRetrievalService
{
    Task<GroundedContextRetrievalResult> RetrieveAsync(
        GroundedContextRetrievalRequest request,
        CancellationToken cancellationToken);
}

public interface IGroundedContextPromptBuilder
{
    GroundedPromptContextDto Build(GroundedContextRetrievalResult result);
}

public interface IGroundedPromptContextService
{
    Task<GroundedPromptContextDto> PrepareAsync(
        GroundedPromptContextRequest request,
        CancellationToken cancellationToken);
}

public static class GroundedContextSourceTypes
{
    public const string KnowledgeChunk = "knowledge_chunk";
    public const string MemoryItem = "memory_item";
    public const string RecentTask = "recent_task";
    public const string AgentRecord = "agent_record";
    public const string CompanyRecord = "company_record";
    public const string ApprovalRequest = "approval_request";
}

public sealed class GroundedContextRetrievalValidationException : Exception
{
    public GroundedContextRetrievalValidationException(string message) : base(message) { }
}