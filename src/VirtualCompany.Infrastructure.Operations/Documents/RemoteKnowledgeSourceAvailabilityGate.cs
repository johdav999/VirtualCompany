using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Documents;

internal sealed class RemoteKnowledgeSourceAvailabilityGate(
    VirtualCompanyDbContext db,
    IDocumentRepositoryGraphAdapter graph,
    IOptions<DocumentRepositoryOperationsOptions> operationsOptions,
    ILogger<RemoteKnowledgeSourceAvailabilityGate> logger) : IRemoteKnowledgeSourceAvailabilityGate
{
    public async Task<RemoteKnowledgeSourceAvailability> CheckAsync(Guid companyId, Guid documentId,
        CompanyKnowledgeAccessContext accessContext, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || documentId == Guid.Empty || accessContext.CompanyId != companyId)
            return new(false, "invalid_scope");
        if (!operationsOptions.Value.Enabled) return new(false, "feature_disabled");

        var source = await db.CompanyKnowledgeDocumentRemoteSources.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DocumentId == documentId)
            .Select(x => new
            {
                x.IsAvailable, x.RemoteVersion, x.ObservedRemoteVersion, x.ItemId,
                Connection = new { x.Connection.ProviderKind, x.Connection.DirectoryTenantId, x.Connection.ApplicationClientId,
                    x.Connection.CredentialReference, x.Connection.DriveId, x.Connection.RootItemId, x.Connection.LifecycleState, x.Connection.Audience, x.Connection.RetrievalPausedUtc },
                AgentGranted = !accessContext.AgentId.HasValue || x.Connection.AgentGrants.Any(g => g.AgentId == accessContext.AgentId.Value)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (source is null) return new(true, "local_source");
        if (!source.IsAvailable || !string.Equals(source.RemoteVersion, source.ObservedRemoteVersion, StringComparison.Ordinal)) return new(false, "source_stale", source.ObservedRemoteVersion);
        if (source.Connection.LifecycleState != DocumentRepositoryLifecycleStates.Active) return new(false, "connection_inactive");
        if (source.Connection.RetrievalPausedUtc.HasValue) return new(false, "retrieval_paused");
        if (source.Connection.Audience != DocumentRepositoryAudiences.Company || !source.AgentGranted) return new(false, "local_access_revoked");

        try
        {
            var context = new GraphRepositoryContext(source.Connection.ProviderKind, source.Connection.DirectoryTenantId,
                source.Connection.ApplicationClientId, source.Connection.CredentialReference, source.Connection.DriveId,
                source.Connection.RootItemId);
            var current = await graph.ValidateItemAsync(context, source.ItemId, cancellationToken);
            if (!current.IsAvailable) return new(false, "remote_access_denied");
            if (string.IsNullOrWhiteSpace(current.RemoteVersion) || !string.Equals(current.RemoteVersion, source.RemoteVersion, StringComparison.Ordinal))
                return new(false, "remote_version_changed", current.RemoteVersion);
            return new(true, "available", current.RemoteVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Remote knowledge availability validation failed for company {CompanyId}, document {DocumentId}.", companyId, documentId);
            return new(false, "remote_validation_unavailable");
        }
    }
}
