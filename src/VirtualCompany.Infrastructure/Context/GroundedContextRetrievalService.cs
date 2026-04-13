using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Context;

public sealed class GroundedContextRetrievalService : IGroundedContextRetrievalService
{
    private const int MaxQueryLength = 4000;
    private const int MaxSectionItems = 20;
    private const int MaxSnippetLength = 700;
    private const int TaskCandidateWindow = 24;
    private const int RelevantRecordCandidateWindow = 12;

    private static readonly IReadOnlyDictionary<string, int> SourceTypePriority =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [GroundedContextSourceTypes.KnowledgeChunk] = 0,
            [GroundedContextSourceTypes.MemoryItem] = 1,
            [GroundedContextSourceTypes.RecentTask] = 2,
            [GroundedContextSourceTypes.ApprovalRequest] = 3,
            [GroundedContextSourceTypes.AgentRecord] = 4,
            [GroundedContextSourceTypes.CompanyRecord] = 5
        };

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAgentRuntimeProfileResolver _agentRuntimeProfileResolver;
    private readonly ICompanyKnowledgeSearchService _companyKnowledgeSearchService;
    private readonly IRetrievalScopeEvaluator _retrievalScopeEvaluator;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly IGroundedContextRetrievalSectionCache _sectionCache;
    private readonly GroundedContextRetrievalCacheKeyBuilder _cacheKeyBuilder;
    private readonly GroundedContextRetrievalCacheOptions _cacheOptions;
    private readonly TimeProvider _timeProvider;

    public GroundedContextRetrievalService(
        VirtualCompanyDbContext dbContext,
        IAgentRuntimeProfileResolver agentRuntimeProfileResolver,
        ICompanyKnowledgeSearchService companyKnowledgeSearchService,
        IRetrievalScopeEvaluator retrievalScopeEvaluator,
        IGroundedContextRetrievalSectionCache sectionCache,
        GroundedContextRetrievalCacheKeyBuilder cacheKeyBuilder,
        IOptions<GroundedContextRetrievalCacheOptions> cacheOptions,
        ICorrelationContextAccessor correlationContextAccessor,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _agentRuntimeProfileResolver = agentRuntimeProfileResolver;
        _companyKnowledgeSearchService = companyKnowledgeSearchService;
        _sectionCache = sectionCache;
        _cacheKeyBuilder = cacheKeyBuilder;
        _cacheOptions = cacheOptions.Value;
        _retrievalScopeEvaluator = retrievalScopeEvaluator;
        _correlationContextAccessor = correlationContextAccessor;
        _timeProvider = timeProvider;
    }

    public async Task<GroundedContextRetrievalResult> RetrieveAsync(
        GroundedContextRetrievalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureRequest(request);

        var limits = NormalizeLimits(request.Limits);
        var retrievalIntent = BuildRetrievalIntent(request);
        var normalizedCorrelationId = NormalizeOptional(
            string.IsNullOrWhiteSpace(request.CorrelationId) ? _correlationContextAccessor.CorrelationId : request.CorrelationId,
            128);
        var asOfUtc = request.AsOfUtc.HasValue
            ? EnsureUtc(request.AsOfUtc.Value)
            : _timeProvider.GetUtcNow().UtcDateTime;

        var agent = await _agentRuntimeProfileResolver.GetCurrentProfileAsync(
            request.CompanyId,
            request.AgentId,
            cancellationToken);

        var actorMembership = await LoadActorMembershipAsync(request.CompanyId, request.ActorUserId, cancellationToken);
        var accessDecision = _retrievalScopeEvaluator.Evaluate(
            new RetrievalAccessContext(
                request.CompanyId,
                request.AgentId,
                request.ActorUserId,
                actorMembership?.MembershipId,
                actorMembership?.Role,
                agent.DataScopes));

        var knowledgeSection = await LoadKnowledgeSectionWithCacheAsync(
            request,
            retrievalIntent,
            accessDecision,
            limits.KnowledgeItems,
            cancellationToken);

        var memorySection = await LoadMemorySectionWithCacheAsync(
            request,
            retrievalIntent,
            accessDecision,
            limits.MemoryItems,
            asOfUtc,
            cancellationToken);

        // Task history and approval-sensitive records stay live-only because
        // staleness here is harder to reason about safely than read-mostly knowledge.
        var recentTaskSection = await LoadRecentTaskSectionAsync(
            request,
            retrievalIntent,
            accessDecision,
            limits.RecentTasks,
            asOfUtc,
            cancellationToken);

        var relevantRecordsSection = await LoadRelevantRecordsSectionAsync(
            request,
            agent,
            retrievalIntent,
            accessDecision,
            limits.RelevantRecords,
            asOfUtc,
            cancellationToken);

        var sourceReferences = BuildSourceReferences(
            knowledgeSection,
            memorySection,
            recentTaskSection,
            relevantRecordsSection);

        var normalizedContext = GroundedContextPromptReadyMapper.Normalize(
            asOfUtc,
            knowledgeSection,
            memorySection,
            recentTaskSection,
            relevantRecordsSection,
            sourceReferences);

        var retrievalId = Guid.NewGuid();
        var retrieval = new ContextRetrieval(
            retrievalId,
            request.CompanyId,
            request.AgentId,
            request.ActorUserId,
            request.TaskId,
            retrievalIntent,
            ComputeSha256(retrievalIntent),
            normalizedCorrelationId,
            request.RetrievalPurpose);

        _dbContext.ContextRetrievals.Add(retrieval);

        foreach (var sourceReference in sourceReferences)
        {
            _dbContext.ContextRetrievalSources.Add(new ContextRetrievalSource(
                Guid.NewGuid(),
                retrievalId,
                request.CompanyId,
                sourceReference.SourceType,
                sourceReference.SourceId,
                sourceReference.ParentSourceType,
                sourceReference.ParentSourceId,
                sourceReference.ParentTitle,
                sourceReference.Title,
                sourceReference.Snippet,
                sourceReference.SectionId,
                sourceReference.SectionTitle,
                sourceReference.SectionRank,
                sourceReference.Locator,
                sourceReference.Rank,
                sourceReference.Score,
                sourceReference.TimestampUtc,
                sourceReference.Metadata));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new GroundedContextRetrievalResult(
            retrievalId,
            new CompanyContextSectionDto(
                request.CompanyId,
                request.AgentId,
                request.ActorUserId,
                agent.DisplayName,
                agent.RoleName,
                agent.RoleBrief,
                accessDecision.EffectiveReadScopes,
                retrievalIntent),
            knowledgeSection,
            memorySection,
            recentTaskSection,
            relevantRecordsSection,
            sourceReferences,
            new RetrievalAppliedFiltersDto(
                accessDecision.EffectiveReadScopes,
                accessDecision.MembershipResolved,
                RestrictedKnowledgeExcludedByDefault: true,
                RestrictedMemoryExcludedByDefault: true,
                ScopedTaskHistoryExcludedByDefault: true))
        {
            GeneratedAtUtc = asOfUtc,
            NormalizedContext = normalizedContext
        };
    }

    private async Task<ResolvedActorMembership?> LoadActorMembershipAsync(
        Guid companyId,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId is not Guid userId || userId == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.CompanyMemberships
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.UserId == userId &&
                x.Status == CompanyMembershipStatus.Active)
            .Select(x => new ResolvedActorMembership(x.Id, x.UserId!.Value, x.Role))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<RetrievalSectionDto> LoadKnowledgeSectionWithCacheAsync(
        GroundedContextRetrievalRequest request,
        string retrievalIntent,
        RetrievalAccessDecision accessDecision,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!IsKnowledgeSectionCacheable(accessDecision, limit))
        {
            return await LoadKnowledgeSectionAsync(request, retrievalIntent, accessDecision, limit, cancellationToken);
        }

        var cacheKey = _cacheKeyBuilder.BuildKnowledgeSectionKey(
            _cacheOptions.KeyVersion,
            request,
            accessDecision,
            retrievalIntent,
            limit);

        var cached = await _sectionCache.TryGetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var liveSection = await LoadKnowledgeSectionAsync(request, retrievalIntent, accessDecision, limit, cancellationToken);
        await _sectionCache.TrySetAsync(cacheKey, liveSection, _cacheOptions.GetSectionTtl("knowledge"), cancellationToken);
        return liveSection;
    }

    private async Task<RetrievalSectionDto> LoadMemorySectionWithCacheAsync(
        GroundedContextRetrievalRequest request,
        string retrievalIntent,
        RetrievalAccessDecision accessDecision,
        int limit,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        if (!IsMemorySectionCacheable(request, accessDecision, limit))
        {
            return await LoadMemorySectionAsync(request, retrievalIntent, accessDecision, limit, asOfUtc, cancellationToken);
        }

        var cacheKey = _cacheKeyBuilder.BuildMemorySectionKey(
            _cacheOptions.KeyVersion,
            request,
            accessDecision,
            retrievalIntent,
            limit,
            asOfUtc);

        var cached = await _sectionCache.TryGetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var liveSection = await LoadMemorySectionAsync(request, retrievalIntent, accessDecision, limit, asOfUtc, cancellationToken);
        await _sectionCache.TrySetAsync(cacheKey, liveSection, _cacheOptions.GetSectionTtl("memory"), cancellationToken);
        return liveSection;
    }

    private async Task<RetrievalSectionDto> LoadKnowledgeSectionAsync(
        GroundedContextRetrievalRequest request,
        string retrievalIntent,
        RetrievalAccessDecision accessDecision,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0 || !accessDecision.CanRetrieve)
        {
            return EmptySection("knowledge", "Knowledge");
        }

        var accessContext = _retrievalScopeEvaluator.BuildKnowledgeAccessContext(accessDecision);

        var results = await _companyKnowledgeSearchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(
                request.CompanyId,
                retrievalIntent,
                limit,
                accessContext),
            cancellationToken);

        var ordered = results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DocumentId)
            .ThenBy(x => x.ChunkIndex)
            .ThenBy(x => x.ChunkId)
            .Take(limit)
            .Select(x => new RetrievalItemDto(
                GroundedContextSourceTypes.KnowledgeChunk,
                x.ChunkId.ToString("N"),
                x.DocumentTitle,
                Truncate(x.Content, MaxSnippetLength),
                x.Score,
                null,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["documentId"] = x.DocumentId.ToString("N"),
                    ["documentType"] = x.SourceDocument.DocumentType,
                    ["sourceType"] = x.SourceDocument.SourceType,
                    ["sourceRef"] = x.SourceDocument.SourceRef,
                    ["chunkIndex"] = x.ChunkIndex.ToString(),
                    ["chunkSourceReference"] = x.SourceReference
                }))
            .ToArray();

        return new RetrievalSectionDto("knowledge", "Knowledge", ordered);
    }

    private async Task<RetrievalSectionDto> LoadMemorySectionAsync(
        GroundedContextRetrievalRequest request,
        string retrievalIntent,
        RetrievalAccessDecision accessDecision,
        int limit,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        if (limit <= 0 || !accessDecision.CanRetrieve)
        {
            return EmptySection("memory", "Memory");
        }

        var candidates = await _dbContext.MemoryItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == request.CompanyId &&
                x.DeletedUtc == null &&
                x.ValidFromUtc <= asOfUtc &&
                (!x.ValidToUtc.HasValue || x.ValidToUtc.Value > asOfUtc) &&
                (x.AgentId == null || x.AgentId == request.AgentId))
            .ToListAsync(cancellationToken);

        var ordered = candidates
            .Where(x => _retrievalScopeEvaluator.CanAccessMemory(accessDecision, x))
            .Select(x => new RankedMemory(
                x,
                ComputeMemoryScore(retrievalIntent, x, asOfUtc)))
            // Deterministic memory ordering is explicit so equal scores stay stable across runs.
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.Salience)
            .ThenByDescending(x => x.Item.ValidFromUtc)
            .ThenByDescending(x => x.Item.CreatedUtc)
            .ThenBy(x => x.Item.Id)
            .Take(limit)
            .Select(x => new RetrievalItemDto(

                GroundedContextSourceTypes.MemoryItem,
                x.Item.Id.ToString("N"),
                BuildMemoryTitle(x.Item),
                Truncate(x.Item.Summary, MaxSnippetLength),
                x.Score,
                x.Item.CreatedUtc,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["memoryType"] = x.Item.MemoryType.ToStorageValue(),
                    ["scope"] = x.Item.AgentId.HasValue ? "agent_specific" : "company_wide",
                    ["agentId"] = x.Item.AgentId?.ToString("N"),
                    ["companyId"] = x.Item.CompanyId.ToString("N"),
                    ["sourceType"] = GroundedContextSourceTypes.MemoryItem,
                    ["sourceEntityType"] = x.Item.SourceEntityType,
                    ["sourceEntityId"] = x.Item.SourceEntityId?.ToString("N"),
                    ["salience"] = x.Item.Salience.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["validFromUtc"] = x.Item.ValidFromUtc.ToString("O"),
                    ["validToUtc"] = x.Item.ValidToUtc?.ToString("O")
                }))
            .ToArray();

        return new RetrievalSectionDto("memory", "Memory", ordered);
    }

    private async Task<RetrievalSectionDto> LoadRecentTaskSectionAsync(
        GroundedContextRetrievalRequest request,
        string retrievalIntent,
        RetrievalAccessDecision accessDecision,
        int limit,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        if (limit <= 0 || !accessDecision.CanRetrieve)
        {
            return EmptySection("recent_tasks", "Recent Task History");
        }

        var attempts = await _dbContext.ToolExecutionAttempts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId && x.AgentId == request.AgentId)
            .OrderByDescending(x => x.ExecutedUtc ?? x.UpdatedUtc)
            .ThenBy(x => x.Id)
            .Take(TaskCandidateWindow)
            .ToListAsync(cancellationToken);

        var ordered = attempts
            .Where(x => _retrievalScopeEvaluator.CanAccessTaskScope(accessDecision, x.Scope))
            .Select(x => new RankedAttempt(
                x,
                ComputeTaskScore(request, retrievalIntent, x, asOfUtc)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Attempt.ExecutedUtc ?? x.Attempt.UpdatedUtc)
            .ThenBy(x => x.Attempt.Id)
            .Take(limit)
            .Select(x => new RetrievalItemDto(
                GroundedContextSourceTypes.RecentTask,
                x.Attempt.Id.ToString("N"),
                BuildTaskTitle(x.Attempt),
                BuildTaskSnippet(x.Attempt),
                x.Score,
                x.Attempt.ExecutedUtc ?? x.Attempt.UpdatedUtc,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["toolName"] = x.Attempt.ToolName,
                    ["actionType"] = x.Attempt.ActionType.ToStorageValue(),
                    ["status"] = x.Attempt.Status.ToStorageValue(),
                    ["scope"] = x.Attempt.Scope,
                    ["approvalRequestId"] = x.Attempt.ApprovalRequestId?.ToString("N"),
                    ["taskId"] = TryResolveTaskId(x.Attempt, out var taskId) ? taskId.ToString("N") : null,
                    ["sourceEntityType"] = "tool_execution_attempt"
                }))
            .ToArray();

        return new RetrievalSectionDto("recent_tasks", "Recent Task History", ordered);
    }

    private async Task<RetrievalSectionDto> LoadRelevantRecordsSectionAsync(
        GroundedContextRetrievalRequest request,
        AgentRuntimeProfileDto agent,
        string retrievalIntent,
        RetrievalAccessDecision accessDecision,
        int limit,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        if (limit <= 0 || !accessDecision.CanRetrieve)
        {
            return EmptySection("relevant_records", "Relevant Records");
        }

        var items = new List<RetrievalItemDto>(RelevantRecordCandidateWindow + 2);
        var company = await _dbContext.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.CompanyId, cancellationToken);

        items.Add(new RetrievalItemDto(
            GroundedContextSourceTypes.AgentRecord,
            agent.Id.ToString("N"),
            $"Agent Profile: {agent.DisplayName}",
            BuildAgentRecordSnippet(agent, accessDecision.EffectiveReadScopes),
            ComputeStructuredRecordScore(
                retrievalIntent,
                $"{agent.DisplayName} {agent.RoleName} {agent.Department} {agent.RoleBrief}",
                0.45d),
            agent.UpdatedUtc,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sourceEntityType"] = "agent_profile",
                ["agentId"] = agent.Id.ToString("N"),
                ["companyId"] = agent.CompanyId.ToString("N"),
                ["department"] = agent.Department,
                ["roleName"] = agent.RoleName,
                ["autonomyLevel"] = agent.AutonomyLevel,
                ["readScopes"] = accessDecision.EffectiveReadScopes.Count == 0
                    ? null
                    : string.Join(",", accessDecision.EffectiveReadScopes.OrderBy(static scope => scope, StringComparer.OrdinalIgnoreCase))
            }));

        if (company is not null)
        {
            items.Add(new RetrievalItemDto(
                GroundedContextSourceTypes.CompanyRecord,
                company.Id.ToString("N"),
                $"Company Profile: {company.Name}",
                BuildCompanyRecordSnippet(company),
                ComputeStructuredRecordScore(
                    retrievalIntent,
                    $"{company.Name} {company.Industry} {company.BusinessType} {company.Timezone} {company.ComplianceRegion}",
                    0.35d),
                company.UpdatedUtc,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sourceEntityType"] = "company_profile",
                    ["companyId"] = company.Id.ToString("N")
                }));
        }

        var approvals = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId && x.AgentId == request.AgentId)
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenBy(x => x.Id)
            .Take(RelevantRecordCandidateWindow)
            .ToListAsync(cancellationToken);

        items.AddRange(approvals
            .Where(x => _retrievalScopeEvaluator.CanAccessTaskScope(accessDecision, x.ApprovalTarget))
            .Select(x => new RetrievalItemDto(
                GroundedContextSourceTypes.ApprovalRequest,
                x.Id.ToString("N"),
                $"Approval: {x.ToolName}",
                BuildApprovalSnippet(x),
                ComputeApprovalScore(retrievalIntent, x, asOfUtc),
                x.UpdatedUtc,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sourceEntityType"] = "approval_request",
                    ["companyId"] = x.CompanyId.ToString("N"),
                    ["agentId"] = x.AgentId.ToString("N"),
                    ["status"] = x.Status.ToStorageValue(),
                    ["toolName"] = x.ToolName,
                    ["approvalTarget"] = x.ApprovalTarget,
                    ["actionType"] = x.ActionType.ToStorageValue(),
                    ["toolExecutionAttemptId"] = x.ToolExecutionAttemptId?.ToString("N")
                })));

        var ordered = items
            .OrderByDescending(x => x.RelevanceScore ?? 0d)
            .ThenByDescending(x => x.TimestampUtc)
            .ThenBy(x => SourceTypePriority.TryGetValue(x.SourceType, out var priority) ? priority : int.MaxValue)
            .ThenBy(x => x.SourceId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        return new RetrievalSectionDto("relevant_records", "Relevant Records", ordered);
    }

    private static RetrievalSectionDto EmptySection(string id, string title) =>
        new(id, title, Array.Empty<RetrievalItemDto>());

    private bool IsKnowledgeSectionCacheable(RetrievalAccessDecision accessDecision, int limit) =>
        _cacheOptions.IsPayloadCachingAllowed &&
        accessDecision.CanRetrieve &&
        limit > 0;

    private bool IsMemorySectionCacheable(
        GroundedContextRetrievalRequest request,
        RetrievalAccessDecision accessDecision,
        int limit) =>
        _cacheOptions.IsPayloadCachingAllowed &&
        accessDecision.CanRetrieve &&
        limit > 0 &&
        request.AsOfUtc.HasValue;

    private static RetrievalSourceLimitOptions NormalizeLimits(RetrievalSourceLimitOptions? limits)
    {
        var normalized = limits ?? new RetrievalSourceLimitOptions();
        return new RetrievalSourceLimitOptions(
            Math.Clamp(normalized.KnowledgeItems, 0, MaxSectionItems),
            Math.Clamp(normalized.MemoryItems, 0, MaxSectionItems),
            Math.Clamp(normalized.RecentTasks, 0, MaxSectionItems),
            Math.Clamp(normalized.RelevantRecords, 0, MaxSectionItems));
    }

    private static void EnsureRequest(GroundedContextRetrievalRequest request)
    {
        if (request.CompanyId == Guid.Empty)
        {
            throw new GroundedContextRetrievalValidationException("CompanyId is required.");
        }

        if (request.AgentId == Guid.Empty)
        {
            throw new GroundedContextRetrievalValidationException("AgentId is required.");
        }

        if (request.ActorUserId.HasValue && request.ActorUserId.Value == Guid.Empty)
        {
            throw new GroundedContextRetrievalValidationException("ActorUserId cannot be empty.");
        }

        if (request.TaskId.HasValue && request.TaskId.Value == Guid.Empty)
        {
            throw new GroundedContextRetrievalValidationException("TaskId cannot be empty.");
        }
    }

    private static string BuildRetrievalIntent(GroundedContextRetrievalRequest request)
    {
        var parts = new[]
        {
            NormalizeQueryPart(request.QueryText),
            NormalizeQueryPart(request.TaskTitle),
            NormalizeQueryPart(request.TaskDescription)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

        if (parts.Length == 0)
        {
            throw new GroundedContextRetrievalValidationException(
                "A retrieval query, task title, or task description is required.");
        }

        var combined = string.Join("\n\n", parts);
        if (combined.Length > MaxQueryLength)
        {
            combined = combined[..MaxQueryLength].TrimEnd();
        }

        return combined;
    }

    private static string? NormalizeQueryPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = CollapseWhitespace(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            previousWasWhitespace = false;
            builder.Append(character);
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ResolveReadScopes(IReadOnlyDictionary<string, JsonNode?> dataScopes)
    {
        if (!TryReadConfiguredIdentifiers(dataScopes, "read", out var scopes))
        {
            return Array.Empty<string>();
        }

        return scopes;
    }

    private static bool TryReadConfiguredIdentifiers(
        IReadOnlyDictionary<string, JsonNode?> nodes,
        string key,
        out string[] values)
    {
        values = Array.Empty<string>();

        if (!nodes.TryGetValue(key, out var node) || node is null)
        {
            return true;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var singleValue))
        {
            values = string.IsNullOrWhiteSpace(singleValue)
                ? Array.Empty<string>()
                : [singleValue.Trim()];
            return true;
        }

        if (node is not JsonArray array)
        {
            return false;
        }

        var results = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonValue itemValue || !itemValue.TryGetValue<string>(out var textValue) || string.IsNullOrWhiteSpace(textValue))
            {
                values = Array.Empty<string>();
                return false;
            }

            results.Add(textValue.Trim());
        }

        values = results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return true;
    }

    private static bool CanAccessMemory(
        MemoryItem item,
        IReadOnlyList<string> readScopes,
        Guid agentId,
        CompanyMembershipRole? membershipRole)
    {
        var metadata = item.Metadata;
        var hasUnsupportedConstraint =
            HasConfiguredIdentifiers(metadata, ["roles", "allowed_roles", "membership_roles"]) ||
            HasConfiguredIdentifiers(metadata, ["user_ids", "users"]);

        if (hasUnsupportedConstraint && membershipRole is null)
        {
            return false;
        }

        if (membershipRole.HasValue &&
            !EvaluateIdentifierConstraint(metadata, ["roles", "allowed_roles", "membership_roles"], membershipRole.Value.ToStorageValue()))
        {
            return false;
        }

        if (!EvaluateIdentifierConstraint(metadata, ["agent_id", "agent_ids", "agents"], agentId.ToString("D")))
        {
            return false;
        }

        if (!EvaluateScopeConstraint(metadata, ["scope", "scopes", "data_scope", "data_scopes", "read_scope"], readScopes))
        {
            return false;
        }

        var hasExplicitConstraint =
            HasConfiguredIdentifiers(metadata, ["roles", "allowed_roles", "membership_roles"]) ||
            HasConfiguredIdentifiers(metadata, ["agent_id", "agent_ids", "agents"]) ||
            HasConfiguredIdentifiers(metadata, ["scope", "scopes", "data_scope", "data_scopes", "read_scope"]);

        if ((HasTrueBoolean(metadata, ["restricted", "is_restricted"]) ||
             HasTrueBoolean(metadata, ["private", "is_private"])) &&
            !hasExplicitConstraint)
        {
            return false;
        }

        return true;
    }

    private static bool EvaluateIdentifierConstraint(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys,
        string currentValue)
    {
        if (!TryGetConfiguredIdentifiers(metadata, keys, out var configuredValues, out var exists))
        {
            return false;
        }

        if (!exists)
        {
            return true;
        }

        return configuredValues.Contains(currentValue);
    }

    private static bool EvaluateScopeConstraint(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys,
        IReadOnlyList<string> readScopes)
    {
        if (!TryGetConfiguredIdentifiers(metadata, keys, out var configuredScopes, out var exists))
        {
            return false;
        }

        if (!exists)
        {
            return true;
        }

        if (readScopes.Count == 0)
        {
            return false;
        }

        return readScopes.Any(configuredScopes.Contains);
    }

    private static bool HasConfiguredIdentifiers(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys) =>
        TryGetConfiguredIdentifiers(metadata, keys, out var configuredValues, out var exists) &&
        exists &&
        configuredValues.Count > 0;

    private static bool TryGetConfiguredIdentifiers(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys,
        out HashSet<string> configuredValues,
        out bool exists)
    {
        configuredValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        exists = false;

        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var node) || node is null)
            {
                continue;
            }

            exists = true;
            if (!TryAppendIdentifiers(node, configuredValues))
            {
                configuredValues.Clear();
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendIdentifiers(JsonNode node, ISet<string> identifiers)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var singleValue))
        {
            if (string.IsNullOrWhiteSpace(singleValue))
            {
                return false;
            }

            identifiers.Add(singleValue.Trim());
            return true;
        }

        if (node is not JsonArray array)
        {
            return false;
        }

        foreach (var item in array)
        {
            if (item is not JsonValue itemValue || !itemValue.TryGetValue<string>(out var textValue) || string.IsNullOrWhiteSpace(textValue))
            {
                return false;
            }

            identifiers.Add(textValue.Trim());
        }

        return true;
    }

    private static bool HasTrueBoolean(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var node) || node is not JsonValue jsonValue)
            {
                continue;
            }

            if ((jsonValue.TryGetValue<bool>(out var boolValue) && boolValue) ||
                (jsonValue.TryGetValue<string>(out var textValue) &&
                 bool.TryParse(textValue, out var parsed) &&
                 parsed))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanAccessTaskScope(string? scope, IReadOnlyList<string> readScopes)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return true;
        }

        if (readScopes.Count == 0)
        {
            return false;
        }

        return readScopes.Contains(scope.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static double ComputeMemoryScore(string retrievalIntent, MemoryItem item, DateTime nowUtc)
    {
        var overlapScore = ComputeTokenOverlap(retrievalIntent, item.Summary);
        var salienceScore = (double)item.Salience;
        var recencyScore = ComputeRecencyScore(item.CreatedUtc, nowUtc, 30d);

        return Math.Round((overlapScore * 0.60d) + (salienceScore * 0.25d) + (recencyScore * 0.15d), 6);
    }

    private static double ComputeTaskScore(
        GroundedContextRetrievalRequest request,
        string retrievalIntent,
        ToolExecutionAttempt attempt,
        DateTime asOfUtc)
    {
        var taskText = $"{attempt.ToolName} {attempt.ActionType.ToStorageValue()} {attempt.Scope} {ReadString(attempt.ResultPayload, "summary")}";
        var overlapScore = ComputeTokenOverlap(retrievalIntent, taskText);
        var statusScore = attempt.Status switch
        {
            ToolExecutionStatus.AwaitingApproval => 0.30d,
            ToolExecutionStatus.Failed => 0.25d,
            ToolExecutionStatus.Executed => 0.20d,
            _ => 0.10d
        };

        var taskIdScore = request.TaskId.HasValue && TryResolveTaskId(attempt, out var attemptTaskId) && attemptTaskId == request.TaskId
            ? 0.40d
            : 0d;

        var timestamp = attempt.ExecutedUtc ?? attempt.UpdatedUtc;
        var recencyScore = ComputeRecencyScore(timestamp, asOfUtc, 14d);

        return Math.Round((overlapScore * 0.45d) + statusScore + taskIdScore + (recencyScore * 0.15d), 6);
    }

    private static double ComputeApprovalScore(string retrievalIntent, ApprovalRequest request, DateTime asOfUtc)
    {
        var overlapScore = ComputeTokenOverlap(
            retrievalIntent,
            $"{request.ToolName} {request.ActionType.ToStorageValue()} {request.ApprovalTarget}");
        var statusScore = request.Status == ApprovalRequestStatus.Pending ? 0.35d : 0.15d;
        var recencyScore = ComputeRecencyScore(request.UpdatedUtc, asOfUtc, 14d);
        return Math.Round((overlapScore * 0.45d) + statusScore + (recencyScore * 0.20d), 6);
    }

    private static double ComputeStructuredRecordScore(string retrievalIntent, string recordText, double baseScore)
        => Math.Round(baseScore + (ComputeTokenOverlap(retrievalIntent, recordText) * 0.55d), 6);

    private static double ComputeRecencyScore(DateTime timestampUtc, DateTime nowUtc, double windowDays)
    {
        var ageDays = Math.Max(0d, (nowUtc - EnsureUtc(timestampUtc)).TotalDays);
        var normalized = 1d - Math.Min(1d, ageDays / Math.Max(1d, windowDays));
        return Math.Round(normalized, 6);
    }

    private static double ComputeTokenOverlap(string left, string right)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0d;
        }

        var overlapCount = leftTokens.Count(rightTokens.Contains);
        return Math.Round((double)overlapCount / leftTokens.Count, 6);
    }

    private static HashSet<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return value
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '-', '_', '/', '\\', '(', ')', '[', ']', '{', '}', '?', '!'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => token.Length > 1)
            .Select(static token => token.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildMemoryTitle(MemoryItem item) =>
        item.AgentId.HasValue
            ? $"{item.MemoryType.ToStorageValue()} memory for agent"
            : $"{item.MemoryType.ToStorageValue()} company memory";

    private static string BuildTaskTitle(ToolExecutionAttempt attempt) =>
        $"{attempt.ToolName} ({attempt.ActionType.ToStorageValue()})";

    private static string BuildTaskSnippet(ToolExecutionAttempt attempt)
    {
        // Task-history snippets intentionally collapse stored payloads into bounded summaries
        // so retrieval output stays prompt-ready and never mirrors raw execution blobs.
        var pieces = new List<string>
        {
            $"Status: {attempt.Status.ToStorageValue()}."
        };

        if (!string.IsNullOrWhiteSpace(attempt.Scope))
        {
            pieces.Add($"Scope: {attempt.Scope.Trim()}.");
        }

        var summary = ReadString(attempt.ResultPayload, "summary");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            pieces.Add($"Summary: {Truncate(summary, 240)}");
        }

        return Truncate(string.Join(" ", pieces), MaxSnippetLength);
    }

    private static string BuildAgentRecordSnippet(AgentRuntimeProfileDto agent, IReadOnlyList<string> readScopes)
    {
        var pieces = new List<string>
        {
            $"Role: {agent.RoleName}.",
            $"Department: {agent.Department}.",
            $"Autonomy: {agent.AutonomyLevel}."
        };

        if (!string.IsNullOrWhiteSpace(agent.RoleBrief))
        {
            pieces.Add($"Brief: {Truncate(agent.RoleBrief, 240)}");
        }

        if (readScopes.Count > 0)
        {
            pieces.Add($"Read scopes: {string.Join(", ", readScopes.OrderBy(static scope => scope, StringComparer.OrdinalIgnoreCase))}.");
        }

        return Truncate(string.Join(" ", pieces), MaxSnippetLength);
    }

    private static string BuildCompanyRecordSnippet(Company company)
    {
        var pieces = new List<string> { $"Company: {company.Name}." };

        AppendDetail(pieces, "Industry", company.Industry);
        AppendDetail(pieces, "Business type", company.BusinessType);
        AppendDetail(pieces, "Timezone", company.Timezone);
        AppendDetail(pieces, "Compliance region", company.ComplianceRegion);

        return Truncate(string.Join(" ", pieces), MaxSnippetLength);
    }

    private static string BuildApprovalSnippet(ApprovalRequest request)
    {
        var pieces = new List<string>
        {
            $"Status: {request.Status.ToStorageValue()}.",
            $"Action: {request.ActionType.ToStorageValue()}."
        };

        if (!string.IsNullOrWhiteSpace(request.ApprovalTarget))
        {
            pieces.Add($"Target: {request.ApprovalTarget.Trim()}.");
        }

        return string.Join(" ", pieces);
    }

    private static void AppendDetail(ICollection<string> pieces, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            pieces.Add($"{label}: {CollapseWhitespace(value)}.");
        }
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?> values, string key)
    {
        if (!values.TryGetValue(key, out var node) || node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : CollapseWhitespace(value);
    }

    private static bool TryResolveTaskId(ToolExecutionAttempt attempt, out Guid taskId)
    {
        taskId = Guid.Empty;
        return TryReadGuid(attempt.RequestPayload, "taskId", out taskId) ||
               TryReadGuid(attempt.RequestPayload, "task_id", out taskId) ||
               TryReadGuid(attempt.ResultPayload, "taskId", out taskId) ||
               TryReadGuid(attempt.ResultPayload, "task_id", out taskId);
    }

    private static bool TryReadGuid(IReadOnlyDictionary<string, JsonNode?> values, string key, out Guid value)
    {
        value = Guid.Empty;

        if (!values.TryGetValue(key, out var node) || node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<Guid>(out value))
        {
            return value != Guid.Empty;
        }

        if (jsonValue.TryGetValue<string>(out var text) && Guid.TryParse(text, out value))
        {
            return value != Guid.Empty;
        }

        value = Guid.Empty;
        return false;
    }

    private static IReadOnlyList<RetrievalSourceReferenceDto> BuildSourceReferences(
        RetrievalSectionDto knowledgeSection,
        RetrievalSectionDto memorySection,
        RetrievalSectionDto recentTaskSection,
        RetrievalSectionDto relevantRecordsSection)
        => new[] { knowledgeSection, memorySection, recentTaskSection, relevantRecordsSection }
            // Persist source references in the same fixed section/item order used by the prompt-ready payload.
            .SelectMany(section => section.Items.Select((item, index) => new SectionedRetrievalItem(section.Id, section.Title, index + 1, item)))
            .Select((item, index) => CreateSourceReference(item, index + 1))
            .ToArray();

    

    private static RetrievalSourceReferenceDto CreateSourceReference(SectionedRetrievalItem item, int rank)
    {
        var parentSourceType = ResolveParentSourceType(item);
        var parentSourceId = ResolveParentSourceId(item);
        var parentTitle = ResolveParentTitle(item);
        var locator = BuildSourceLocator(item);

        return new RetrievalSourceReferenceDto(
            item.Item.SourceType,
            item.Item.SourceId,
            item.Item.Title,
            parentSourceType,
            parentSourceId,
            parentTitle,
            item.SectionId,
            item.SectionTitle,
            item.SectionItemRank,
            locator,
            rank,
            item.Item.RelevanceScore,
            item.Item.Content,
            item.Item.TimestampUtc,
            CreateSourceReferenceMetadata(item, parentSourceType, parentSourceId, parentTitle, locator));
    }

    private static IReadOnlyDictionary<string, string?> CreateSourceReferenceMetadata(
        SectionedRetrievalItem item,
        string? parentSourceType,
        string? parentSourceId,
        string? parentTitle,
        string? locator)
    {
        var metadata = new Dictionary<string, string?>(item.Item.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["retrievalSection"] = item.SectionId,
            ["retrievalSectionTitle"] = item.SectionTitle,
            ["retrievalSectionRank"] = item.SectionItemRank.ToString()
        };

        if (!string.IsNullOrWhiteSpace(parentSourceType))
        {
            metadata["parentSourceType"] = parentSourceType;
        }

        if (!string.IsNullOrWhiteSpace(parentSourceId))
        {
            metadata["parentSourceId"] = parentSourceId;
        }

        if (!string.IsNullOrWhiteSpace(parentTitle))
        {
            metadata["parentTitle"] = parentTitle;
        }

        if (!string.IsNullOrWhiteSpace(locator))
        {
            metadata["locator"] = locator;
        }

        return metadata;
    }

    private static string? ResolveParentSourceType(SectionedRetrievalItem item)
    {
        return item.Item.SourceType switch
        {
            GroundedContextSourceTypes.KnowledgeChunk => "knowledge_document",
            GroundedContextSourceTypes.MemoryItem => ReadMetadata(item.Item.Metadata, "sourceEntityType"),
            GroundedContextSourceTypes.RecentTask => ReadMetadata(item.Item.Metadata, "taskId") is not null
                ? "task"
                : ReadMetadata(item.Item.Metadata, "approvalRequestId") is not null
                    ? GroundedContextSourceTypes.ApprovalRequest
                    : null,
            GroundedContextSourceTypes.ApprovalRequest => "tool_execution_attempt",
            _ => null
        };
    }

    private static string? ResolveParentSourceId(SectionedRetrievalItem item)
    {
        return item.Item.SourceType switch
        {
            GroundedContextSourceTypes.KnowledgeChunk => ReadMetadata(item.Item.Metadata, "documentId"),
            GroundedContextSourceTypes.MemoryItem => ReadMetadata(item.Item.Metadata, "sourceEntityId"),
            GroundedContextSourceTypes.RecentTask => ReadMetadata(item.Item.Metadata, "taskId") ?? ReadMetadata(item.Item.Metadata, "approvalRequestId"),
            GroundedContextSourceTypes.ApprovalRequest => ReadMetadata(item.Item.Metadata, "toolExecutionAttemptId"),
            _ => null
        };
    }

    private static string? ResolveParentTitle(SectionedRetrievalItem item) =>
        item.Item.SourceType == GroundedContextSourceTypes.KnowledgeChunk
            ? NormalizeOptional(item.Item.Title, 256)
            : null;

    private static string? BuildSourceLocator(SectionedRetrievalItem item)
    {
        string? locator = item.Item.SourceType switch
        {
            GroundedContextSourceTypes.KnowledgeChunk => BuildKnowledgeLocator(item),
            GroundedContextSourceTypes.MemoryItem => JoinLocatorParts(
                ReadMetadata(item.Item.Metadata, "memoryType"),
                ReadMetadata(item.Item.Metadata, "scope"),
                ReadMetadata(item.Item.Metadata, "sourceEntityType")),
            GroundedContextSourceTypes.RecentTask => JoinLocatorParts(
                ReadMetadata(item.Item.Metadata, "toolName"),
                ReadMetadata(item.Item.Metadata, "actionType"),
                ReadMetadata(item.Item.Metadata, "scope"),
                ReadMetadata(item.Item.Metadata, "status")),
            GroundedContextSourceTypes.ApprovalRequest => JoinLocatorParts(
                ReadMetadata(item.Item.Metadata, "toolName"),
                ReadMetadata(item.Item.Metadata, "status"),
                ReadMetadata(item.Item.Metadata, "approvalTarget")),
            GroundedContextSourceTypes.AgentRecord or GroundedContextSourceTypes.CompanyRecord => JoinLocatorParts(
                ReadMetadata(item.Item.Metadata, "sourceEntityType"),
                item.Item.Title),
            _ => item.Item.Title
        };

        return NormalizeOptional(locator ?? item.Item.Title, 512);
    }

    private static string? BuildKnowledgeLocator(SectionedRetrievalItem item)
    {
        var chunkIndex = ParseNullableIntValue(ReadMetadata(item.Item.Metadata, "chunkIndex"));
        var chunkLabel = chunkIndex.HasValue ? $"chunk {chunkIndex.Value + 1}" : null;

        return JoinLocatorParts(
            item.Item.Title,
            chunkLabel,
            ReadMetadata(item.Item.Metadata, "chunkSourceReference") ?? ReadMetadata(item.Item.Metadata, "sourceRef"));
    }

    private static string? JoinLocatorParts(params string?[] values)
    {
        var parts = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => CollapseWhitespace(value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" | ", parts);
    }

    private static string? ReadMetadata(IReadOnlyDictionary<string, string?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? CollapseWhitespace(value)
            : null;

    private static int? ParseNullableIntValue(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private sealed record SectionedRetrievalItem(
        string SectionId,
        string SectionTitle,
        int SectionItemRank,
        RetrievalItemDto Item);

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Truncate(string value, int maxLength)
    {
        var normalized = CollapseWhitespace(value);
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = CollapseWhitespace(value);
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private sealed record ResolvedActorMembership(
        Guid MembershipId,
        Guid UserId,
        CompanyMembershipRole Role);

    private sealed record RankedMemory(
        MemoryItem Item,
        double Score);

    private sealed record RankedAttempt(
        ToolExecutionAttempt Attempt,
        double Score);
}
