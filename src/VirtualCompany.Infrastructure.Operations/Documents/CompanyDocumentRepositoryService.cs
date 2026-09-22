using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Documents;

internal sealed class CompanyDocumentRepositoryService(
    VirtualCompanyDbContext dbContext,
    IDocumentRepositoryGraphAdapter graphAdapter,
    ICompanyContextAccessor companyContext,
    IAuditEventWriter auditWriter,
    ICorrelationContextAccessor correlationContext,
    IOptions<DocumentRepositoryOperationsOptions> operationsOptions,
    TimeProvider timeProvider) : ICompanyDocumentRepositoryService
{
    public async Task<DocumentRepositoryConnectionDto> CreateAsync(Guid companyId, ConfigureDocumentRepositoryConnectionCommand command, CancellationToken cancellationToken)
    {
        EnsureCompanyContext(companyId);
        ValidateCommand(command, requireVersion: false);
        await EnsureAgentsAsync(companyId, command.AgentIds, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var connection = new CompanyDocumentRepositoryConnection(
            companyId, command.ProviderKind, command.DirectoryTenantId, command.ApplicationClientId,
            command.CredentialReference, command.DriveId, command.RootItemId, command.DisplayName,
            command.Audience, now);
        dbContext.CompanyDocumentRepositoryConnections.Add(connection);
        connection.ConfigureWrites(command.EnableWrites, command.WritableFolderItemId, now);
        ReplaceAgentGrants(connection, command.AgentIds, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync(connection, AuditEventActions.DocumentRepositoryConnectionCreated, AuditEventOutcomes.Succeeded, "Registered a read-only Microsoft 365 document repository connection.", cancellationToken);
        return Map(connection);
    }

    public async Task<DocumentRepositoryConnectionDto> UpdateAsync(Guid companyId, Guid connectionId, ConfigureDocumentRepositoryConnectionCommand command, CancellationToken cancellationToken)
    {
        EnsureCompanyContext(companyId);
        ValidateCommand(command, requireVersion: true);
        await EnsureAgentsAsync(companyId, command.AgentIds, cancellationToken);
        var connection = await FindAsync(companyId, connectionId, tracking: true, cancellationToken) ?? throw new DocumentRepositoryNotFoundException();
        EnsureVersion(connection, command.ExpectedConcurrencyVersion!.Value);
        connection.Reconfigure(command.ProviderKind, command.DirectoryTenantId, command.ApplicationClientId,
            command.CredentialReference, command.DriveId, command.RootItemId, command.DisplayName, command.Audience,
            timeProvider.GetUtcNow().UtcDateTime);
        dbContext.CompanyDocumentRepositoryAgentGrants.RemoveRange(connection.AgentGrants.ToList());
        connection.AgentGrants.Clear();
        connection.ConfigureWrites(command.EnableWrites, command.WritableFolderItemId, timeProvider.GetUtcNow().UtcDateTime);
        ReplaceAgentGrants(connection, command.AgentIds, timeProvider.GetUtcNow().UtcDateTime);
        await SaveWithConcurrencyAsync(cancellationToken);
        await AuditAsync(connection, AuditEventActions.DocumentRepositoryConnectionUpdated, AuditEventOutcomes.Succeeded, "Updated the repository connection and reset validation.", cancellationToken);
        return Map(connection);
    }

    public async Task<IReadOnlyList<DocumentRepositoryConnectionDto>> ListAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompanyContext(companyId);
        var connections = await dbContext.CompanyDocumentRepositoryConnections.AsNoTracking()
            .Where(x => x.CompanyId == companyId).Include(x => x.AgentGrants).OrderBy(x => x.DisplayName).ToListAsync(cancellationToken);
        var presentations = await LoadPresentationAsync(companyId, connections.Select(x => x.Id).ToArray(), cancellationToken);
        return connections.Select(x => Map(x, presentations.GetValueOrDefault(x.Id))).ToList();
    }

    public async Task<DocumentRepositoryConnectionDto?> GetAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        EnsureCompanyContext(companyId);
        var connection = await FindAsync(companyId, connectionId, tracking: false, cancellationToken);
        if (connection is null) return null;
        var presentations = await LoadPresentationAsync(companyId, [connection.Id], cancellationToken);
        return Map(connection, presentations.GetValueOrDefault(connection.Id));
    }

    public async Task<DocumentRepositoryValidationResult> ValidateAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        EnsureCompanyContext(companyId);
        EnsureFeatureEnabled();
        var connection = await FindAsync(companyId, connectionId, tracking: true, cancellationToken) ?? throw new DocumentRepositoryNotFoundException();
        EnsureUsable(connection, allowUnavailable: true);
        try
        {
            var result = await graphAdapter.ValidateAsync(ToGraphContext(connection), cancellationToken);
            connection.MarkValidated(result.RepositoryName, timeProvider.GetUtcNow().UtcDateTime);
            await SaveWithConcurrencyAsync(cancellationToken);
            await AuditAsync(connection, AuditEventActions.DocumentRepositoryConnectionValidated, AuditEventOutcomes.Succeeded, "Validated read-only access to the explicitly granted repository root.", cancellationToken);
            return new DocumentRepositoryValidationResult(true, DocumentRepositoryValidationCodes.Succeeded, result.RepositoryName, result.RootName, null);
        }
        catch (DocumentRepositoryUnavailableException exception)
        {
            connection.MarkValidationFailed(exception.Code, exception.SafeMessage, timeProvider.GetUtcNow().UtcDateTime);
            await SaveWithConcurrencyAsync(cancellationToken);
            await AuditAsync(connection, AuditEventActions.DocumentRepositoryConnectionValidated, AuditEventOutcomes.Failed, exception.SafeMessage, cancellationToken);
            return new DocumentRepositoryValidationResult(false, exception.Code, connection.DisplayName, string.Empty, exception.SafeMessage);
        }
    }

    public async Task<DocumentRepositoryBrowseResult> BrowseAsync(Guid companyId, Guid connectionId, string? parentItemId, int maxItems, CancellationToken cancellationToken)
    {
        EnsureCompanyContext(companyId);
        EnsureFeatureEnabled();
        var connection = await FindAsync(companyId, connectionId, tracking: false, cancellationToken) ?? throw new DocumentRepositoryNotFoundException();
        EnsureUsable(connection, allowUnavailable: false);
        var parent = string.IsNullOrWhiteSpace(parentItemId) ? connection.RootItemId : parentItemId.Trim();
        var page = await graphAdapter.BrowseAsync(ToGraphContext(connection), parent, maxItems <= 0 ? 100 : maxItems, cancellationToken);
        return new DocumentRepositoryBrowseResult(parent, page.Items, page.IsTruncated);
    }

    public async Task DisconnectAsync(Guid companyId, Guid connectionId, long expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        EnsureCompanyContext(companyId);
        var connection = await FindAsync(companyId, connectionId, tracking: true, cancellationToken) ?? throw new DocumentRepositoryNotFoundException();
        EnsureVersion(connection, expectedConcurrencyVersion);
        connection.Disconnect(timeProvider.GetUtcNow().UtcDateTime);
        await SaveWithConcurrencyAsync(cancellationToken);
        await AuditAsync(connection, AuditEventActions.DocumentRepositoryConnectionDisconnected, AuditEventOutcomes.Succeeded, "Disconnected the repository without changing remote content or shared credentials.", cancellationToken);
    }

    public async Task<DocumentRepositoryConnectionDto> SetPauseAsync(Guid companyId, Guid connectionId, SetDocumentRepositoryPauseCommand command, CancellationToken cancellationToken)
    {
        EnsureCompanyContext(companyId);
        ArgumentNullException.ThrowIfNull(command);
        var scope = DocumentRepositoryPauseScopes.Normalize(command.Scope);
        var connection = await FindAsync(companyId, connectionId, tracking: true, cancellationToken) ?? throw new DocumentRepositoryNotFoundException();
        EnsureVersion(connection, command.ExpectedConcurrencyVersion);
        connection.SetOperationalPause(scope, command.Paused, timeProvider.GetUtcNow().UtcDateTime);
        await SaveWithConcurrencyAsync(cancellationToken);
        await auditWriter.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, companyContext.UserId,
            AuditEventActions.DocumentRepositoryPauseChanged, AuditTargetTypes.DocumentRepositoryConnection,
            connection.Id.ToString("D"), AuditEventOutcomes.Succeeded,
            command.Paused ? $"Paused repository {scope}." : $"Resumed repository {scope}.",
            ["document_repository", "operator_recovery"],
            new Dictionary<string, string?> { ["scope"] = scope, ["paused"] = command.Paused.ToString() },
            correlationContext.CorrelationId), cancellationToken);
        var presentations = await LoadPresentationAsync(companyId, [connection.Id], cancellationToken);
        return Map(connection, presentations.GetValueOrDefault(connection.Id));
    }

    private Task<CompanyDocumentRepositoryConnection?> FindAsync(Guid companyId, Guid connectionId, bool tracking, CancellationToken cancellationToken)
    {
        var query = dbContext.CompanyDocumentRepositoryConnections.Where(x => x.CompanyId == companyId && x.Id == connectionId).Include(x => x.AgentGrants);
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    private async Task EnsureAgentsAsync(Guid companyId, IReadOnlyCollection<Guid>? agentIds, CancellationToken cancellationToken)
    {
        var requested = (agentIds ?? []).Distinct().ToArray();
        if (requested.Any(x => x == Guid.Empty)) throw new DocumentRepositoryValidationException("Agent grants must contain valid agent identifiers.");
        if (requested.Length == 0) return;
        var existing = await dbContext.Agents.CountAsync(x => x.CompanyId == companyId && requested.Contains(x.Id), cancellationToken);
        if (existing != requested.Length) throw new DocumentRepositoryValidationException("Every repository agent grant must reference an agent in the current company.");
    }

    private void ReplaceAgentGrants(CompanyDocumentRepositoryConnection connection, IReadOnlyCollection<Guid>? agentIds, DateTime now)
    {
        foreach (var agentId in (agentIds ?? []).Distinct())
            connection.AgentGrants.Add(new CompanyDocumentRepositoryAgentGrant(connection.CompanyId, connection.Id, agentId, now));
    }

    private async Task SaveWithConcurrencyAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new DocumentRepositoryConflictException("The repository connection changed. Reload it before trying again."); }
    }

    private void EnsureCompanyContext(Guid companyId)
    {
        if (companyId == Guid.Empty || companyContext.CompanyId != companyId)
            throw new UnauthorizedAccessException("Document repository access is scoped to the active company context.");
    }

    private static void EnsureVersion(CompanyDocumentRepositoryConnection connection, long expected)
    {
        if (expected <= 0 || connection.ConcurrencyVersion != expected)
            throw new DocumentRepositoryConflictException("The repository connection changed. Reload it before trying again.");
    }

    private void EnsureFeatureEnabled()
    {
        if (!operationsOptions.Value.Enabled)
            throw new DocumentRepositoryUnavailableException("feature_disabled", "Document repository operations are disabled for this deployment.");
    }

    private static void EnsureUsable(CompanyDocumentRepositoryConnection connection, bool allowUnavailable)
    {
        if (connection.LifecycleState == DocumentRepositoryLifecycleStates.Disconnected)
            throw new DocumentRepositoryConflictException("The repository connection is disconnected.");
        if (!allowUnavailable && connection.LifecycleState != DocumentRepositoryLifecycleStates.Active)
            throw new DocumentRepositoryUnavailableException(connection.LastValidationCode ?? DocumentRepositoryValidationCodes.Unavailable, "The repository connection must be successfully validated before browsing.");
    }

    private static void ValidateCommand(ConfigureDocumentRepositoryConnectionCommand command, bool requireVersion)
    {
        ArgumentNullException.ThrowIfNull(command);
        _ = DocumentRepositoryProviderKinds.Normalize(command.ProviderKind);
        _ = DocumentRepositoryAudiences.Normalize(command.Audience);
        if (command.DirectoryTenantId == Guid.Empty || command.ApplicationClientId == Guid.Empty) throw new DocumentRepositoryValidationException("DirectoryTenantId and ApplicationClientId are required.");
        if (string.IsNullOrWhiteSpace(command.CredentialReference) || string.IsNullOrWhiteSpace(command.DriveId) || string.IsNullOrWhiteSpace(command.RootItemId) || string.IsNullOrWhiteSpace(command.DisplayName))
            throw new DocumentRepositoryValidationException("CredentialReference, DriveId, RootItemId, and DisplayName are required.");
        if (command.EnableWrites && string.IsNullOrWhiteSpace(command.WritableFolderItemId))
            throw new DocumentRepositoryValidationException("WritableFolderItemId is required when approved agent writes are enabled.");
        if (requireVersion && command.ExpectedConcurrencyVersion is null or <= 0) throw new DocumentRepositoryValidationException("ExpectedConcurrencyVersion is required when updating a connection.");
    }

    private async Task AuditAsync(CompanyDocumentRepositoryConnection connection, string action, string outcome, string summary, CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditEventWriteRequest(
            connection.CompanyId, AuditActorTypes.User, companyContext.UserId, action,
            AuditTargetTypes.DocumentRepositoryConnection, connection.Id.ToString("D"), outcome, summary,
            ["microsoft_graph", "document_repository"],
            new Dictionary<string, string?>
            {
                ["providerKind"] = connection.ProviderKind,
                ["directoryTenantId"] = connection.DirectoryTenantId.ToString("D"),
                ["applicationClientId"] = connection.ApplicationClientId.ToString("D"),
                ["driveId"] = connection.DriveId,
                ["rootItemId"] = connection.RootItemId,
                ["lifecycleState"] = connection.LifecycleState,
                ["agentGrantCount"] = connection.AgentGrants.Count.ToString()
            }, correlationContext.CorrelationId), cancellationToken);
    }

    private static GraphRepositoryContext ToGraphContext(CompanyDocumentRepositoryConnection connection) =>
        new(connection.ProviderKind, connection.CredentialMode, connection.DirectoryTenantId, connection.ApplicationClientId, connection.CredentialReference, connection.DriveId, connection.RootItemId);

    private async Task<Dictionary<Guid, RepositoryPresentation>> LoadPresentationAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> connectionIds,
        CancellationToken cancellationToken)
    {
        if (connectionIds.Count == 0) return [];

        var documentStats = await dbContext.CompanyKnowledgeDocumentRemoteSources.AsNoTracking()
            .Where(x => x.CompanyId == companyId && connectionIds.Contains(x.ConnectionId))
            .GroupBy(x => x.ConnectionId)
            .Select(group => new
            {
                ConnectionId = group.Key,
                Indexed = group.Count(x => x.IsAvailable && x.Document.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexed),
                Processing = group.Count(x => x.IsAvailable &&
                    x.Document.IndexingStatus != CompanyKnowledgeDocumentIndexingStatus.Indexed &&
                    x.Document.IndexingStatus != CompanyKnowledgeDocumentIndexingStatus.Failed &&
                    x.Document.IngestionStatus != CompanyKnowledgeDocumentIngestionStatus.Failed &&
                    x.Document.IngestionStatus != CompanyKnowledgeDocumentIngestionStatus.Blocked),
                Failed = group.Count(x => x.Document.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Failed ||
                    x.Document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Failed ||
                    x.Document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Blocked)
            }).ToListAsync(cancellationToken);

        var importJobs = await dbContext.CompanyDocumentRepositoryImportJobs.AsNoTracking()
            .Where(x => x.CompanyId == companyId && connectionIds.Contains(x.ConnectionId))
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new { x.ConnectionId, x.Id, x.Status, x.CreatedUtc })
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var syncJobs = await dbContext.CompanyDocumentRepositorySynchronizationJobs.AsNoTracking()
            .Where(x => x.CompanyId == companyId && connectionIds.Contains(x.ConnectionId))
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new { x.ConnectionId, x.Id, x.Status, x.CreatedUtc, x.UpdatedUtc, x.LeaseExpiresUtc, x.FailureCode })
            .ToListAsync(cancellationToken);
        var publications = await dbContext.CompanyDocumentPublicationRequests.AsNoTracking()
            .Where(x => x.CompanyId == companyId && connectionIds.Contains(x.ConnectionId))
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new { x.ConnectionId, x.Id, x.OperationKind, x.Status, x.FailureMessage, x.CreatedUtc })
            .ToListAsync(cancellationToken);
        var retryableItems = await dbContext.CompanyDocumentRepositoryImportItems.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CanRetry && x.Status == DocumentRepositoryImportItemStates.Failed && connectionIds.Contains(x.Job.ConnectionId))
            .Select(x => new { x.Job.ConnectionId, x.FailureCode }).ToListAsync(cancellationToken);

        return connectionIds.ToDictionary(
            id => id,
            id =>
            {
                var stats = documentStats.FirstOrDefault(x => x.ConnectionId == id);
                var import = importJobs.FirstOrDefault(x => x.ConnectionId == id);
                var sync = syncJobs.FirstOrDefault(x => x.ConnectionId == id);
                var publication = publications.FirstOrDefault(x => x.ConnectionId == id);
                var queueTimes = importJobs.Where(x => x.ConnectionId == id && x.Status is DocumentRepositoryImportStates.Queued or DocumentRepositoryImportStates.Running).Select(x => x.CreatedUtc)
                    .Concat(syncJobs.Where(x => x.ConnectionId == id && x.Status is DocumentRepositorySynchronizationStates.Queued or DocumentRepositorySynchronizationStates.Running or DocumentRepositorySynchronizationStates.RetryWaiting).Select(x => x.CreatedUtc))
                    .Concat(publications.Where(x => x.ConnectionId == id && x.Status is DocumentPublicationStatuses.Queued or DocumentPublicationStatuses.Sending or DocumentPublicationStatuses.ReconciliationRequired).Select(x => x.CreatedUtc))
                    .ToArray();
                var failures = retryableItems.Where(x => x.ConnectionId == id).ToArray();
                var dependencyHealth = failures.Any(x => x.FailureCode is "virus_scanner_unavailable" or "embedding_unavailable" or "indexing_failed") ? "degraded" : "healthy";
                return new RepositoryPresentation(stats?.Indexed ?? 0, stats?.Processing ?? 0, stats?.Failed ?? 0,
                    import?.Id, import?.Status, sync?.Id, sync?.Status,
                    publication?.Id, publication?.OperationKind, publication?.Status, publication?.FailureMessage,
                    queueTimes.Length == 0 ? null : Math.Max(0, (now - queueTimes.Min()).TotalSeconds),
                    syncJobs.Count(x => x.ConnectionId == id && x.Status == DocumentRepositorySynchronizationStates.Running && x.LeaseExpiresUtc <= now),
                    failures.Length,
                    publications.Count(x => x.ConnectionId == id && x.Status is DocumentPublicationStatuses.Unresolved or DocumentPublicationStatuses.ReconciliationRequired),
                    dependencyHealth,
                    sync?.FailureCode == DocumentRepositoryValidationCodes.Throttled);
            });
    }

    private DocumentRepositoryConnectionDto Map(CompanyDocumentRepositoryConnection connection, RepositoryPresentation? presentation = null) => new(
        connection.Id, connection.CompanyId, connection.ProviderKind, connection.DirectoryTenantId, connection.ApplicationClientId,
        connection.DriveId, connection.RootItemId, connection.DisplayName, connection.IsReadOnly, connection.LifecycleState,
        connection.Audience, connection.AgentGrants.Select(x => x.AgentId).OrderBy(x => x).ToList(),
        connection.LastValidationCode, connection.LastValidationSummary, connection.LastValidatedUtc, connection.DisconnectedUtc,
        connection.ConcurrencyVersion, connection.CreatedUtc, connection.UpdatedUtc, connection.LastSynchronizedUtc,
        presentation?.Indexed ?? 0, presentation?.Processing ?? 0, presentation?.Failed ?? 0,
        presentation?.ImportJobId, presentation?.ImportStatus, presentation?.SynchronizationJobId, presentation?.SynchronizationStatus,
        connection.WritableFolderItemId, presentation?.PublicationRequestId, presentation?.PublicationOperationKind,
        presentation?.PublicationStatus, presentation?.PublicationFailureMessage,
        connection.RetrievalPausedUtc, connection.SynchronizationPausedUtc, connection.WritesPausedUtc,
        presentation?.OldestQueueAgeSeconds, presentation?.StaleLeaseCount ?? 0,
        presentation?.RetryableFailedItemCount ?? 0, presentation?.UnresolvedPublicationCount ?? 0,
        presentation?.DependencyHealth ?? "healthy", presentation?.IsThrottled ?? false,
        operationsOptions.Value.Enabled, connection.CredentialMode);

    private sealed record RepositoryPresentation(
        int Indexed,
        int Processing,
        int Failed,
        Guid? ImportJobId,
        string? ImportStatus,
        Guid? SynchronizationJobId,
        string? SynchronizationStatus,
        Guid? PublicationRequestId,
        string? PublicationOperationKind,
        string? PublicationStatus,
        string? PublicationFailureMessage,
        double? OldestQueueAgeSeconds,
        int StaleLeaseCount,
        int RetryableFailedItemCount,
        int UnresolvedPublicationCount,
        string DependencyHealth,
        bool IsThrottled);
}
