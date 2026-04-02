using System.Data.Common;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Memory;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Domain.Policies;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Memory;

public sealed class CompanyMemoryService : ICompanyMemoryService, IMemoryRetrievalService
{
    private const double SemanticWeight = 0.60d;
    private const double SemanticSalienceWeight = 0.25d;
    private const double SemanticRecencyWeight = 0.15d;
    private const double SalienceOnlyWeight = 0.65d;
    private const double RecencyOnlyWeight = 0.35d;
    private const int MaxSemanticCandidateCount = 200;
    private const int MaxSearchLimit = 100;
    private const double RecencyWindowDays = 14d;
    private const int AuditMetadataValueMaxLength = 512;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;

    public CompanyMemoryService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IEmbeddingGenerator embeddingGenerator,
        IAuditEventWriter auditEventWriter,
        ICorrelationContextAccessor correlationContextAccessor)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _embeddingGenerator = embeddingGenerator;
        _auditEventWriter = auditEventWriter;
        _correlationContextAccessor = correlationContextAccessor;
    }

    public async Task<MemoryItemDto?> GetAsync(Guid companyId, Guid memoryId, CancellationToken cancellationToken)
    {
        EnsureCompanyId(companyId);
        await RequireMembershipAsync(companyId, cancellationToken);
        var nowUtc = DateTime.UtcNow;

        var item = await _dbContext.MemoryItems
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Id == memoryId && x.DeletedUtc == null && x.ValidFromUtc <= nowUtc && (!x.ValidToUtc.HasValue || x.ValidToUtc.Value > nowUtc),
                cancellationToken);

        return item is null ? null : ToDto(item);
    }
    public async Task<MemoryItemDto> CreateAsync(Guid companyId, CreateMemoryItemCommand command, CancellationToken cancellationToken)
    {
        EnsureCompanyId(companyId);
        await EnsureManagerAccessAsync(companyId, cancellationToken);
        var memoryType = await ValidateCreateCommandAsync(companyId, command, cancellationToken);

        var validFromUtc = command.ValidFromUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var validToUtc = command.ValidToUtc?.ToUniversalTime();
        var item = new MemoryItem(
            Guid.NewGuid(),
            companyId,
            command.AgentId,
            memoryType,
            command.Summary,
            command.SourceEntityType,
            command.SourceEntityId,
            command.Salience,
            validFromUtc,
            validToUtc,
            command.Metadata);

        var embeddingBatch = await _embeddingGenerator.GenerateAsync([item.Summary], cancellationToken);
        if (embeddingBatch.Embeddings.Count > 0)
        {
            item.AttachEmbedding(
                KnowledgeEmbeddingSerializer.Serialize(embeddingBatch.Embeddings[0].Values),
                embeddingBatch.Provider,
                embeddingBatch.Model,
                embeddingBatch.ModelVersion,
                embeddingBatch.Dimensions);
        }

        _dbContext.MemoryItems.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(item);
    }

    public async Task<MemoryRetrievalResultDto> RetrieveAsync(MemoryRetrievalRequest request, CancellationToken cancellationToken)
    {
        var criteria = await ValidateAndNormalizeRetrievalRequestAsync(request, cancellationToken);
        var result = await ExecuteSearchAsync(criteria, includeTotalCount: false, cancellationToken);
        return new MemoryRetrievalResultDto(result.Items, result.SemanticSearchApplied);
    }

    public async Task<MemorySearchResultDto> SearchAsync(Guid companyId, MemorySearchFilters filters, CancellationToken cancellationToken)
    {
        EnsureCompanyId(companyId);
        var criteria = await ValidateAndNormalizeFiltersAsync(companyId, filters, cancellationToken);
        return await ExecuteSearchAsync(criteria, includeTotalCount: true, cancellationToken);
    }

    public async Task<MemoryItemDto> ExpireAsync(ExpireMemoryItemCommand command, CancellationToken cancellationToken)
    {
        EnsureCompanyId(command.CompanyId);
        EnsureMemoryItemId(command.MemoryItemId);
        ValidateLifecycleMetadataValue(nameof(command.Reason), command.Reason);
        ValidateLifecycleMetadataValue(nameof(command.PolicyContext), command.PolicyContext);

        var item = await RequireLifecycleTargetAsync(command.CompanyId, command.MemoryItemId, cancellationToken);

        var membership = await EnsureLifecycleAccessAsync(
            command.CompanyId,
            MemoryLifecycleAction.Expire,
            item,
            command.Reason,
            cancellationToken);

        var previousValidToUtc = item.ValidToUtc;
        var validToUtc = command.ValidToUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        try
        {
            item.Expire(validToUtc, AuditActorTypes.User, membership.UserId, command.Reason);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw CreateValidationException(nameof(command.ValidToUtc), ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw CreateValidationException(nameof(command.ValidToUtc), ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw CreateValidationException(nameof(command.ValidToUtc), ex.Message);
        }

        await WriteLifecycleAuditEventAsync(
            command.CompanyId,
            membership,
            item,
            MemoryLifecycleAction.Expire,
            AuditEventOutcomes.Succeeded,
            command.Reason,
            policyContext: command.PolicyContext,
            previousValidToUtc: previousValidToUtc,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(item);
    }

    public async Task DeleteAsync(DeleteMemoryItemCommand command, CancellationToken cancellationToken)
    {
        EnsureCompanyId(command.CompanyId);
        EnsureMemoryItemId(command.MemoryItemId);
        ValidateLifecycleMetadataValue(nameof(command.Reason), command.Reason);
        ValidateLifecycleMetadataValue(nameof(command.PolicyContext), command.PolicyContext);
        ValidateDeletionMode(nameof(command.DeletionMode), command.DeletionMode);

        var item = await RequireLifecycleTargetAsync(command.CompanyId, command.MemoryItemId, cancellationToken);
        var membership = await EnsureLifecycleAccessAsync(
            command.CompanyId,
            MemoryLifecycleAction.Delete,
            item,
            command.Reason,
            cancellationToken);

        item.Delete(AuditActorTypes.User, membership.UserId, command.Reason);

        await WriteLifecycleAuditEventAsync(
            command.CompanyId,
            membership,
            item,
            MemoryLifecycleAction.Delete,
            AuditEventOutcomes.Succeeded,
            command.Reason,
            policyContext: command.PolicyContext,
            cancellationToken: cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<MemorySearchResultDto> ExecuteSearchAsync(
        MemoryRetrievalCriteria criteria,
        bool includeTotalCount,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(
            _dbContext.MemoryItems
                .AsNoTracking()
                .Where(x => x.CompanyId == criteria.CompanyId),
            criteria);

        var semanticRequested = criteria.QueryEmbedding is not null || !string.IsNullOrWhiteSpace(criteria.QueryText);
        var totalCount = includeTotalCount ? await query.CountAsync(cancellationToken) : 0;
        IReadOnlyList<MemoryCandidate> candidates;

        if (criteria.QueryEmbedding is not null && IsPostgreSql())
        {
            candidates = await LoadSemanticCandidatesAsync(criteria, cancellationToken);
        }
        else
        {
            candidates = (await query.ToListAsync(cancellationToken)).Select(ToCandidate).ToArray();
        }

        var ranked = RankCandidates(candidates, criteria, semanticRequested)
            .Skip(criteria.Offset)
            .Take(criteria.Limit)
            .Select(ToDto)
            .ToArray();

        return new MemorySearchResultDto(ranked, includeTotalCount ? totalCount : ranked.Length, semanticRequested);
    }

    private async Task<MemoryType> ValidateCreateCommandAsync(
        Guid companyId,
        CreateMemoryItemCommand command,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!MemoryTypeValues.TryParse(command.MemoryType, out var memoryType))
        {
            errors[nameof(command.MemoryType)] = [MemoryTypeValues.BuildValidationMessage(command.MemoryType)];
        }

        if (string.IsNullOrWhiteSpace(command.Summary))
        {
            errors[nameof(command.Summary)] = ["Summary is required."];
        }
        else if (command.Summary.Trim().Length > 4000)
        {
            errors[nameof(command.Summary)] = ["Summary must be 4000 characters or fewer."];
        }
        else if (!MemoryContentSafetyPolicy.TryValidateSummary(command.Summary, out var summarySafetyError))
        {
            errors[nameof(command.Summary)] = [summarySafetyError];
        }

        if (!MemoryContentSafetyPolicy.TryValidateMetadata(command.Metadata, out var metadataSafetyError))
        {
            errors[nameof(command.Metadata)] = [metadataSafetyError];
        }

        foreach (var propertyName in MemoryContentSafetyPolicy.FindUnsafeAdditionalProperties(command.AdditionalProperties))
        {
            errors[propertyName] =
            [
                "This field is not allowed on memory writes. Persist only sanitized summary content."
            ];
        }

        if (command.SourceEntityId.HasValue && command.SourceEntityId.Value == Guid.Empty)
        {
            errors[nameof(command.SourceEntityId)] = ["SourceEntityId cannot be empty."];
        }

        if (command.Salience < 0m || command.Salience > 1m)
        {
            errors[nameof(command.Salience)] = ["Salience must be between 0 and 1."];
        }

        if (!string.IsNullOrWhiteSpace(command.SourceEntityType) && command.SourceEntityType.Trim().Length > 100)
        {
            errors[nameof(command.SourceEntityType)] = ["SourceEntityType must be 100 characters or fewer."];
        }

        if (command.ValidFromUtc.HasValue &&
            command.ValidToUtc.HasValue &&
            command.ValidToUtc.Value.ToUniversalTime() < command.ValidFromUtc.Value.ToUniversalTime())
        {
            errors[nameof(command.ValidToUtc)] = ["ValidToUtc must be greater than or equal to ValidFromUtc."];
        }

        if (command.AgentId.HasValue)
        {
            if (command.AgentId.Value == Guid.Empty)
            {
                errors[nameof(command.AgentId)] = ["AgentId cannot be empty."];
            }

            var agentExists = await _dbContext.Agents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == command.AgentId.Value, cancellationToken);

            if (!agentExists)
            {
                errors[nameof(command.AgentId)] = ["AgentId must reference an agent that belongs to the requested company."];
            }
        }

        if (errors.Count > 0)
        {
            throw new MemoryValidationException(errors);
        }

        return memoryType;
    }

    private async Task<MemoryRetrievalCriteria> ValidateAndNormalizeRetrievalRequestAsync(
        MemoryRetrievalRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCompanyId(request.CompanyId);
        await RequireMembershipAsync(request.CompanyId, cancellationToken);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var memoryTypes = ParseMemoryTypes(null, request.MemoryTypes, errors);
        MemoryScope? explicitScope = null;

        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            if (MemoryScopeValues.TryParse(request.Scope, out var parsedScope))
            {
                explicitScope = parsedScope;
            }
            else
            {
                errors[nameof(request.Scope)] = [MemoryScopeValues.BuildValidationMessage(request.Scope)];
            }
        }

        if (request.AgentId.HasValue && request.AgentId.Value == Guid.Empty)
        {
            errors[nameof(request.AgentId)] = ["AgentId cannot be empty."];
        }

        if (request.Top is < 1 or > MaxSearchLimit)
        {
            errors[nameof(request.Top)] = [$"Top must be between 1 and {MaxSearchLimit}."];
        }

        var resolvedScope = ResolveSearchScope(request.AgentId, request.IncludeCompanyWide, explicitScope);
        if (resolvedScope is MemoryScope.AgentSpecific or MemoryScope.CombinedForAgent && !request.AgentId.HasValue)
        {
            errors[nameof(request.AgentId)] = ["AgentId is required when scope targets agent-specific memory."];
        }

        if (request.AgentId.HasValue && !errors.ContainsKey(nameof(request.AgentId)))
        {
            var agentExists = await _dbContext.Agents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == request.CompanyId && x.Id == request.AgentId.Value, cancellationToken);

            if (!agentExists)
            {
                errors[nameof(request.AgentId)] = ["AgentId must reference an agent that belongs to the requested company."];
            }
        }

        if (errors.Count > 0)
        {
            throw new MemoryValidationException(errors);
        }

        var queryText = string.IsNullOrWhiteSpace(request.QueryText) ? null : request.QueryText.Trim();
        IReadOnlyList<float>? queryEmbedding = request.QueryEmbedding is { Count: > 0 } ? request.QueryEmbedding.ToArray() : null;

        if (queryEmbedding is null && queryText is not null)
        {
            var embeddingBatch = await _embeddingGenerator.GenerateAsync([queryText], cancellationToken);
            queryEmbedding = embeddingBatch.Embeddings.Count > 0 ? embeddingBatch.Embeddings[0].Values : null;
        }

        return new MemoryRetrievalCriteria(
            CompanyId: request.CompanyId,
            AgentId: request.AgentId,
            Scope: resolvedScope,
            MemoryTypes: memoryTypes,
            OnlyActive: true,
            AsOfUtc: request.AsOfUtc?.ToUniversalTime() ?? DateTime.UtcNow,
            QueryText: queryText,
            QueryEmbedding: queryEmbedding,
            Offset: 0,
            Limit: request.Top,
            CreatedAfterUtc: null,
            CreatedBeforeUtc: null);
    }

    private async Task<MemoryRetrievalCriteria> ValidateAndNormalizeFiltersAsync(
        Guid companyId,
        MemorySearchFilters filters,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        if ((!filters.OnlyActive || filters.IncludeDeleted) &&
            membership.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin))
        {
            throw new UnauthorizedAccessException("Only owner and admin memberships can query inactive memory.");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var memoryTypes = ParseMemoryTypes(filters.MemoryType, filters.MemoryTypes, errors);
        MemoryScope? explicitScope = null;

        if (!string.IsNullOrWhiteSpace(filters.Scope))
        {
            if (MemoryScopeValues.TryParse(filters.Scope, out var parsedScope))
            {
                explicitScope = parsedScope;
            }
            else
            {
                errors[nameof(filters.Scope)] = [MemoryScopeValues.BuildValidationMessage(filters.Scope)];
            }
        }

        if (filters.CreatedAfterUtc.HasValue &&
            filters.CreatedBeforeUtc.HasValue &&
            filters.CreatedBeforeUtc.Value.ToUniversalTime() < filters.CreatedAfterUtc.Value.ToUniversalTime())
        {
            errors[nameof(filters.CreatedBeforeUtc)] = ["CreatedBeforeUtc must be greater than or equal to CreatedAfterUtc."];
        }

        if (filters.MinSalience.HasValue && (filters.MinSalience.Value < 0m || filters.MinSalience.Value > 1m))
        {
            errors[nameof(filters.MinSalience)] = ["MinSalience must be between 0 and 1."];
        }

        if (filters.Offset < 0)
        {
            errors[nameof(filters.Offset)] = ["Offset must be zero or greater."];
        }

        if (filters.Limit is < 1 or > MaxSearchLimit)
        {
            errors[nameof(filters.Limit)] = [$"Limit must be between 1 and {MaxSearchLimit}."];
        }

        var resolvedScope = ResolveSearchScope(filters.AgentId, filters.IncludeCompanyWide, explicitScope);

        if (filters.AgentId.HasValue && filters.AgentId.Value == Guid.Empty)
        {
            errors[nameof(filters.AgentId)] = ["AgentId cannot be empty."];
        }
        else if (resolvedScope is MemoryScope.AgentSpecific or MemoryScope.CombinedForAgent &&
                 !filters.AgentId.HasValue)
        {
            errors[nameof(filters.AgentId)] = ["AgentId is required when scope targets agent-specific memory."];
        }

        if (filters.AgentId.HasValue && !errors.ContainsKey(nameof(filters.AgentId)))
        {
            var agentExists = await _dbContext.Agents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == filters.AgentId.Value, cancellationToken);

            if (!agentExists)
            {
                errors[nameof(filters.AgentId)] = ["AgentId must reference an agent that belongs to the requested company."];
            }
        }

        if (errors.Count > 0)
        {
            throw new MemoryValidationException(errors);
        }

        var queryText = string.IsNullOrWhiteSpace(filters.QueryText) ? null : filters.QueryText.Trim();
        IReadOnlyList<float>? queryEmbedding = null;
        if (queryText is not null)
        {
            var embeddingBatch = await _embeddingGenerator.GenerateAsync([queryText], cancellationToken);
            queryEmbedding = embeddingBatch.Embeddings.Count > 0 ? embeddingBatch.Embeddings[0].Values : null;
        }

        return new MemoryRetrievalCriteria(
            CompanyId: companyId,
            AgentId: filters.AgentId,
            Scope: resolvedScope,
            MemoryTypes: memoryTypes,
            OnlyActive: filters.OnlyActive,
            AsOfUtc: filters.AsOfUtc?.ToUniversalTime() ?? DateTime.UtcNow,
            QueryText: queryText,
            QueryEmbedding: queryEmbedding,
            Offset: filters.Offset,
            Limit: filters.Limit,
            CreatedAfterUtc: filters.CreatedAfterUtc?.ToUniversalTime(),
            CreatedBeforeUtc: filters.CreatedBeforeUtc?.ToUniversalTime(),
            MinSalience: filters.MinSalience,
            IncludeDeleted: filters.IncludeDeleted);
    }

    private static IReadOnlyList<MemoryType> ParseMemoryTypes(
        string? memoryType,
        IReadOnlyList<string>? memoryTypes,
        IDictionary<string, string[]> errors)
    {
        var parsed = new List<MemoryType>();

        if (!string.IsNullOrWhiteSpace(memoryType))
        {
            if (MemoryTypeValues.TryParse(memoryType, out var parsedType))
            {
                parsed.Add(parsedType);
            }
            else
            {
                errors[nameof(MemorySearchFilters.MemoryType)] = [MemoryTypeValues.BuildValidationMessage(memoryType)];
            }
        }

        if (memoryTypes is null)
        {
            return parsed.Distinct().ToArray();
        }

        var invalidTypes = memoryTypes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(value => !MemoryTypeValues.TryParse(value, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalidTypes.Length > 0)
        {
            errors[nameof(MemorySearchFilters.MemoryTypes)] =
            [
                string.Join(
                    " ",
                    invalidTypes.Select(value => MemoryTypeValues.BuildValidationMessage(value)))
            ];
            return parsed.Distinct().ToArray();
        }

        foreach (var value in memoryTypes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            parsed.Add(MemoryTypeValues.Parse(value));
        }

        return parsed.Distinct().ToArray();
    }

    private static MemoryScope ResolveSearchScope(Guid? agentId, bool includeCompanyWide, MemoryScope? explicitScope)
    {
        if (explicitScope.HasValue)
        {
            return explicitScope.Value;
        }

        if (agentId.HasValue)
        {
            return includeCompanyWide ? MemoryScope.CombinedForAgent : MemoryScope.AgentSpecific;
        }

        return MemoryScope.CompanyWide;
    }

    private IQueryable<MemoryItem> ApplyFilters(
        IQueryable<MemoryItem> query,
        MemoryRetrievalCriteria criteria)
    {
        if (!criteria.IncludeDeleted)
        {
            query = query.Where(x => x.DeletedUtc == null);
        }

        query = criteria.Scope switch
        {
            MemoryScope.CompanyWide => query.Where(x => x.AgentId == null),
            MemoryScope.AgentSpecific => query.Where(x => x.AgentId == criteria.AgentId!.Value),
            MemoryScope.CombinedForAgent => query.Where(x => x.AgentId == criteria.AgentId!.Value || x.AgentId == null),
            _ => query
        };

        if (criteria.MemoryTypes.Count > 0)
        {
            query = query.Where(x => criteria.MemoryTypes.Contains(x.MemoryType));
        }

        if (criteria.CreatedAfterUtc.HasValue)
        {
            query = query.Where(x => x.CreatedUtc >= criteria.CreatedAfterUtc.Value);
        }

        if (criteria.CreatedBeforeUtc.HasValue)
        {
            query = query.Where(x => x.CreatedUtc <= criteria.CreatedBeforeUtc.Value);
        }

        if (criteria.MinSalience.HasValue)
        {
            query = query.Where(x => x.Salience >= criteria.MinSalience.Value);
        }

        if (criteria.OnlyActive)
        {
            query = query.Where(x => x.ValidFromUtc <= criteria.AsOfUtc && (!x.ValidToUtc.HasValue || x.ValidToUtc.Value > criteria.AsOfUtc));
        }

        return query;
    }

    private async Task<IReadOnlyList<MemoryCandidate>> LoadSemanticCandidatesAsync(
        MemoryRetrievalCriteria criteria,
        CancellationToken cancellationToken)
    {
        var semanticCandidates = await LoadSemanticCandidatesPostgreSqlAsync(criteria, cancellationToken);
        var nonEmbeddedCandidates = await ApplyFilters(
                _dbContext.MemoryItems
                    .AsNoTracking()
                    .Where(x => x.CompanyId == criteria.CompanyId && x.Embedding == null),
                criteria)
            .ToListAsync(cancellationToken);

        return semanticCandidates
            .Concat(nonEmbeddedCandidates.Select(ToCandidate))
            .ToArray();
    }

    private async Task<IReadOnlyList<MemoryCandidate>> LoadSemanticCandidatesPostgreSqlAsync(
        MemoryRetrievalCriteria criteria,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                m."Id",
                m."CompanyId",
                m."AgentId",
                m."MemoryType",
                m."Summary",
                m."SourceEntityType",
                m."SourceEntityId",
                m."Salience",
                m.metadata_json,
                m."ValidFromUtc",
                m."ValidToUtc",
                m."CreatedUtc",
                m."Embedding"
            FROM memory_items m
            WHERE
                m."CompanyId" = @companyId
                AND m."Embedding" IS NOT NULL
            """);

        if (!criteria.IncludeDeleted)
        {
            sql.Append(" AND m.\"DeletedUtc\" IS NULL");
        }

        if (criteria.MemoryTypes.Count > 0)
        {
            sql.Append(" AND m.\"MemoryType\" IN (");
            for (var index = 0; index < criteria.MemoryTypes.Count; index++)
            {
                if (index > 0)
                {
                    sql.Append(", ");
                }

                var parameterName = $"@memoryType{index}";
                sql.Append(parameterName);
                AddParameter(command, parameterName, criteria.MemoryTypes[index].ToStorageValue());
            }

            sql.Append(')');
        }

        if (criteria.CreatedAfterUtc.HasValue)
        {
            sql.Append(" AND m.\"CreatedUtc\" >= @createdAfterUtc");
            AddParameter(command, "@createdAfterUtc", criteria.CreatedAfterUtc.Value);
        }

        if (criteria.CreatedBeforeUtc.HasValue)
        {
            sql.Append(" AND m.\"CreatedUtc\" <= @createdBeforeUtc");
            AddParameter(command, "@createdBeforeUtc", criteria.CreatedBeforeUtc.Value);
        }

        if (criteria.MinSalience.HasValue)
        {
            sql.Append(" AND m.\"Salience\" >= @minSalience");
            AddParameter(command, "@minSalience", criteria.MinSalience.Value);
        }

        sql.Append(criteria.Scope switch
        {
            MemoryScope.CompanyWide => " AND m.\"AgentId\" IS NULL",
            MemoryScope.AgentSpecific => " AND m.\"AgentId\" = @agentId",
            MemoryScope.CombinedForAgent => " AND (m.\"AgentId\" = @agentId OR m.\"AgentId\" IS NULL)",
            _ => string.Empty
        });

        if (criteria.Scope is MemoryScope.AgentSpecific or MemoryScope.CombinedForAgent)
        {
            AddParameter(command, "@agentId", criteria.AgentId);
        }

        if (criteria.OnlyActive)
        {
            sql.Append(" AND m.\"ValidFromUtc\" <= @asOfUtc AND (m.\"ValidToUtc\" IS NULL OR m.\"ValidToUtc\" > @asOfUtc)");
            AddParameter(command, "@asOfUtc", criteria.AsOfUtc);
        }

        sql.Append(
            """
             ORDER BY m."Embedding" <=> CAST(@queryEmbedding AS vector), m."CreatedUtc" DESC, m."Id" ASC
             LIMIT @candidateLimit;
            """);

        AddParameter(command, "@companyId", criteria.CompanyId);
        AddParameter(command, "@queryEmbedding", KnowledgeEmbeddingSerializer.Serialize(criteria.QueryEmbedding!));
        AddParameter(
            command,
            "@candidateLimit",
            Math.Min(MaxSemanticCandidateCount, Math.Max(criteria.Offset + (criteria.Limit * 5), criteria.Limit)));

        command.CommandText = sql.ToString();

        var candidates = new List<MemoryCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new MemoryCandidate(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                MemoryTypeValues.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetDecimal(7),
                DeserializeDictionary(reader.IsDBNull(8) ? "{}" : reader.GetString(8)),
                reader.GetDateTime(9),
                reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                reader.GetDateTime(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return candidates;
    }

    private static IEnumerable<ScoredMemoryCandidate> RankCandidates(
        IReadOnlyList<MemoryCandidate> candidates,
        MemoryRetrievalCriteria criteria,
        bool semanticRequested)
    {
        return candidates
            .Select(item =>
            {
                var vectorScore = criteria.QueryEmbedding is not null && !string.IsNullOrWhiteSpace(item.Embedding)
                    ? NormalizeCosineSimilarity(CosineSimilarity(criteria.QueryEmbedding, KnowledgeEmbeddingSerializer.Deserialize(item.Embedding!)))
                    : 0d;
                var lexicalScore = string.IsNullOrWhiteSpace(criteria.QueryText) ? 0d : CalculateLexicalScore(item.Summary, criteria.QueryText);
                var semanticScore = Math.Max(vectorScore, lexicalScore);
                var recencyScore = CalculateRecencyScore(criteria.AsOfUtc, item.CreatedUtc);
                var combinedScore = semanticRequested
                    ? (SemanticWeight * semanticScore) + (SemanticSalienceWeight * (double)item.Salience) + (SemanticRecencyWeight * recencyScore)
                    : (SalienceOnlyWeight * (double)item.Salience) + (RecencyOnlyWeight * recencyScore);

                return new ScoredMemoryCandidate(item, semanticScore, recencyScore, combinedScore);
            })
            .OrderByDescending(x => x.CombinedScore)
            .ThenByDescending(x => x.SemanticScore)
            .ThenByDescending(x => x.Item.Salience)
            .ThenByDescending(x => x.Item.CreatedUtc)
            .ThenBy(x => x.Item.Id);
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user cannot access memory for this company.");
        }

        return membership;
    }

    private async Task<ResolvedCompanyMembershipContext> EnsureManagerAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        if (membership.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))
        {
            throw new UnauthorizedAccessException("Only owner, admin, and manager memberships can modify company memory.");
        }

        return membership;
    }

    private async Task<ResolvedCompanyMembershipContext> EnsureLifecycleAccessAsync(
        Guid companyId,
        MemoryLifecycleAction action,
        MemoryItem item,
        string? rationale,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var allowed = action switch
        {
            MemoryLifecycleAction.Delete => membership.MembershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin,
            MemoryLifecycleAction.Expire => membership.MembershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin ||
                                            (membership.MembershipRole == CompanyMembershipRole.Manager &&
                                             item.IsAgentSpecific &&
                                             item.MemoryType is not MemoryType.CompanyMemory),
            _ => false
        };

        if (allowed)
        {
            return membership;
        }

        await WriteLifecycleAuditEventAsync(
            companyId,
            membership,
            item,
            action,
            AuditEventOutcomes.Denied,
            rationale,
            cancellationToken: cancellationToken);

        throw new UnauthorizedAccessException("The current user is not allowed to manage the requested memory item.");
    }

    private async Task WriteLifecycleAuditEventAsync(
        Guid companyId,
        ResolvedCompanyMembershipContext membership,
        MemoryItem item,
        MemoryLifecycleAction action,
        string outcome,
        string? rationale,
        string? policyContext = null,
        DateTime? previousValidToUtc = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["memoryType"] = item.MemoryType.ToStorageValue(),
            ["scope"] = item.IsCompanyWide ? MemoryScopeValues.CompanyWide : MemoryScopeValues.AgentSpecific,
            ["agentId"] = item.AgentId?.ToString("D"),
            ["previousValidToUtc"] = previousValidToUtc?.ToString("O"),
            ["validToUtc"] = item.ValidToUtc?.ToString("O"),
            ["deletedUtc"] = item.DeletedUtc?.ToString("O"),
            ["policyContext"] = PrepareMetadataValue(policyContext),
            ["policyRole"] = membership.MembershipRole.ToStorageValue()
        };

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                membership.UserId,
                action == MemoryLifecycleAction.Delete ? AuditEventActions.MemoryItemDeleted : AuditEventActions.MemoryItemExpired,
                AuditTargetTypes.MemoryItem,
                item.Id.ToString("N"),
                outcome,
                PrepareMetadataValue(rationale),
                Metadata: metadata.ToDictionary(
                    pair => pair.Key,
                    pair => PrepareMetadataValue(pair.Value),
                    StringComparer.OrdinalIgnoreCase),
                CorrelationId: _correlationContextAccessor.CorrelationId),
            cancellationToken);
    }

    private static void ValidateLifecycleMetadataValue(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > AuditMetadataValueMaxLength)
        {
            throw CreateValidationException(key, $"{key} must be {AuditMetadataValueMaxLength} characters or fewer.");
        }
    }

    private static void ValidateDeletionMode(string key, string? deletionMode)
    {
        var normalizedMode = string.IsNullOrWhiteSpace(deletionMode)
            ? MemoryDeletionModes.SoftDelete
            : deletionMode.Trim();

        if (!string.Equals(normalizedMode, MemoryDeletionModes.SoftDelete, StringComparison.OrdinalIgnoreCase))
        {
            throw CreateValidationException(key, $"Only '{MemoryDeletionModes.SoftDelete}' is currently supported.");
        }
    }

    // Keep tenant-scoped lifecycle resolution in one place so future retention, legal hold,
    // and policy-based deletion checks can plug into the mutation path without redesign.
    private async Task<MemoryItem> RequireLifecycleTargetAsync(Guid companyId, Guid memoryId, CancellationToken cancellationToken)
    {
        var item = await _dbContext.MemoryItems
            .Where(x => x.CompanyId == companyId)
            .SingleOrDefaultAsync(x => x.Id == memoryId, cancellationToken);

        if (item is null)
        {
            throw new KeyNotFoundException("Memory item not found.");
        }

        return item;
    }

    private static MemoryValidationException CreateValidationException(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = [message]
        });

    private static string? PrepareMetadataValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= AuditMetadataValueMaxLength
            ? trimmed
            : $"{trimmed[..(AuditMetadataValueMaxLength - 3)]}...";
    }

    private static double NormalizeCosineSimilarity(double similarity) =>
        Clamp01((similarity + 1d) / 2d);

    private static double CalculateRecencyScore(DateTime asOfUtc, DateTime createdUtc)
    {
        if (createdUtc >= asOfUtc)
        {
            return 1d;
        }

        var ageDays = (asOfUtc - createdUtc).TotalDays;
        if (ageDays <= 0d)
        {
            return 1d;
        }

        return 1d / (1d + (ageDays / RecencyWindowDays));
    }

    private static double Clamp01(double value)
    {
        if (value < 0d) return 0d;
        if (value > 1d) return 1d;
        return value;
    }

    private static double CalculateLexicalScore(string summary, string queryText)
    {
        if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(queryText))
        {
            return 0d;
        }

        var normalizedSummary = summary.Trim();
        var normalizedQuery = queryText.Trim();
        if (normalizedSummary.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 1d;
        }

        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return 0d;
        }

        var matches = tokens.Count(token => normalizedSummary.Contains(token, StringComparison.OrdinalIgnoreCase));
        return matches / (double)tokens.Length;
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || right.Count == 0 || left.Count != right.Count)
        {
            return 0d;
        }

        double dot = 0d;
        double leftMagnitude = 0d;
        double rightMagnitude = 0d;
        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude <= 0d || rightMagnitude <= 0d)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static MemoryCandidate ToCandidate(MemoryItem item) =>
        new(
            item.Id,
            item.CompanyId,
            item.AgentId,
            item.MemoryType,
            item.Summary,
            item.SourceEntityType,
            item.SourceEntityId,
            item.Salience,
            item.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
            item.ValidFromUtc,
            item.ValidToUtc,
            item.CreatedUtc,
            item.Embedding);

    private static MemoryItemDto ToDto(ScoredMemoryCandidate candidate) =>
        new(
            candidate.Item.Id,
            candidate.Item.CompanyId,
            candidate.Item.AgentId,
            candidate.Item.AgentId is null ? MemoryScopeValues.CompanyWide : MemoryScopeValues.AgentSpecific,
            candidate.Item.MemoryType.ToStorageValue(),
            candidate.Item.Summary,
            candidate.Item.SourceEntityType,
            candidate.Item.SourceEntityId,
            candidate.Item.Salience,
            candidate.Item.Metadata,
            candidate.Item.ValidFromUtc,
            candidate.Item.ValidToUtc,
            candidate.Item.CreatedUtc,
            candidate.SemanticScore,
            candidate.RecencyScore,
            candidate.CombinedScore);

    private static MemoryItemDto ToDto(MemoryItem item) =>
        new(
            item.Id,
            item.CompanyId,
            item.AgentId,
            item.IsCompanyWide ? MemoryScopeValues.CompanyWide : MemoryScopeValues.AgentSpecific,
            item.MemoryType.ToStorageValue(),
            item.Summary,
            item.SourceEntityType,
            item.SourceEntityId,
            item.Salience,
            item.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
            item.ValidFromUtc,
            item.ValidToUtc,
            item.CreatedUtc,
            null,
            null,
            null);

    private static Dictionary<string, JsonNode?> DeserializeDictionary(string json)
    {
        var parsed = JsonNode.Parse(json) as JsonObject;
        if (parsed is null)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return parsed.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }

    private bool IsPostgreSql() =>
        string.Equals(_dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);

    private static void EnsureCompanyId(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    private static void EnsureMemoryItemId(Guid memoryId)
    {
        if (memoryId == Guid.Empty)
        {
            throw new ArgumentException("MemoryItemId is required.", nameof(memoryId));
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record MemoryCandidate(
        Guid Id,
        Guid CompanyId,
        Guid? AgentId,
        MemoryType MemoryType,
        string Summary,
        string? SourceEntityType,
        Guid? SourceEntityId,
        decimal Salience,
        Dictionary<string, JsonNode?> Metadata,
        DateTime ValidFromUtc,
        DateTime? ValidToUtc,
        DateTime CreatedUtc,
        string? Embedding);

    private sealed record ScoredMemoryCandidate(
        MemoryCandidate Item,
        double SemanticScore,
        double RecencyScore,
        double CombinedScore);

    private sealed record MemoryRetrievalCriteria(
        Guid CompanyId,
        Guid? AgentId,
        MemoryScope Scope,
        IReadOnlyList<MemoryType> MemoryTypes,
        bool OnlyActive,
        DateTime AsOfUtc,
        string? QueryText,
        IReadOnlyList<float>? QueryEmbedding,
        int Offset,
        int Limit,
        DateTime? CreatedAfterUtc = null,
        DateTime? CreatedBeforeUtc = null,
        decimal? MinSalience = null,
        bool IncludeDeleted = false);

    private enum MemoryLifecycleAction
    {
        Expire = 1,
        Delete = 2
    }
}
