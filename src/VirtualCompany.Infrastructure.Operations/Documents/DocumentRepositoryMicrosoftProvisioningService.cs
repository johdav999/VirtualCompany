using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Documents;

internal sealed partial class DocumentRepositoryMicrosoftOnboardingService : IDocumentRepositoryProvisioningProcessor
{
    private static readonly Meter ProvisioningMeter = new("VirtualCompany.DocumentRepository.Provisioning", "1.0");
    private static readonly Counter<long> ProvisioningOutcomes = ProvisioningMeter.CreateCounter<long>("document_repository.provisioning.outcomes");
    private static readonly Histogram<double> ProvisioningAge = ProvisioningMeter.CreateHistogram<double>("document_repository.provisioning.age.seconds");

    public async Task<Microsoft365RepositoryAccessDraftDto> ConfigureAccessAsync(Guid companyId, string sessionHandle, ConfigureMicrosoft365RepositoryAccessCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (session, state) = await RequireDiscoverySessionAsync(companyId, sessionHandle, true, ct);
        if (state.Selection is null) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "Choose and verify a repository root before configuring access.");
        if (command.ExpectedConcurrencyVersion <= 0 || session.ConcurrencyVersion != command.ExpectedConcurrencyVersion)
            throw Failure(DocumentRepositoryOnboardingFailureCodes.ExpiredOrReplayedState, "Microsoft 365 setup changed. Reload before configuring access.");

        var agentIds = (command.AgentIds ?? []).Distinct().ToArray();
        if (agentIds.Any(x => x == Guid.Empty)) throw Failure("invalid_agent_selection", "Agent access contains an invalid selection.");
        var validAgents = agentIds.Length == 0 ? 0 : await db.Agents.CountAsync(x => x.CompanyId == companyId && agentIds.Contains(x.Id), ct);
        if (validAgents != agentIds.Length) throw Failure("invalid_agent_selection", "Every selected agent must belong to the current company.");

        string? outputId = null; string? outputName = null;
        if (command.EnableWrites)
        {
            RequireScope(state, "Files.ReadWrite");
            var handle = ReadHandle(session, command.OutputFolderHandle ?? string.Empty, "folder");
            var selected = state.Selection;
            if (!string.Equals(handle.ProviderKind, selected.ProviderKind, StringComparison.Ordinal) ||
                !string.Equals(handle.DriveId, selected.DriveId, StringComparison.Ordinal))
                throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "The writable folder must be in the selected repository drive.");
            var boundedSource = new GraphSetupSource(selected.ProviderKind, selected.SiteId, selected.DriveId, selected.RootItemId, selected.SourceDisplayName, selected.SourceContext, selected.WebUrl);
            var output = await setupAdapter.ValidateFolderAsync(state.Credential.AccessToken, boundedSource, handle.ItemId!, ct);
            if (string.Equals(output.ItemId, selected.RootItemId, StringComparison.Ordinal))
                throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "Choose a dedicated output folder below the approved root.");
            outputId = output.ItemId; outputName = output.DisplayName;
        }

        var version = session.ConcurrencyVersion + 1;
        var draft = new Microsoft365RepositoryAccessDraft(command.EnableWrites, outputId, outputName, agentIds, version);
        session.UpdateProtectedMaterial(protector.ProtectState(state with { Draft = draft }), clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await WriteProvisioningAudit(session, AuditEventActions.DocumentRepositoryOnboardingAccessConfigured, AuditEventOutcomes.Succeeded,
            "Configured reviewed repository access without changing Microsoft permissions.", ct);
        return new(draft.EnableWrites, draft.WritableFolderDisplayName, draft.AgentIds, session.ConcurrencyVersion);
    }

    public async Task<Microsoft365RepositoryReviewDto> GetReviewAsync(Guid companyId, string sessionHandle, CancellationToken ct)
    {
        var (_, session, state) = await RequireDraftAsync(companyId, sessionHandle, ct);
        var selection = state.Selection!; var draft = state.Draft!;
        var agents = await db.Agents.AsNoTracking().Where(x => x.CompanyId == companyId && draft.AgentIds.Contains(x.Id))
            .OrderBy(x => x.DisplayName).Select(x => new Microsoft365RepositoryReviewAgentDto(x.Id, x.DisplayName)).ToListAsync(ct);
        var changes = new List<string> { $"Grant this Virtual Company application read access to ‘{selection.RootDisplayName}’ only." };
        if (draft.EnableWrites) changes.Add($"Grant write access to the dedicated output folder ‘{draft.WritableFolderDisplayName}’ only.");
        return new(selection.TenantId.ToString("D"), selection.ProviderKind, selection.SourceDisplayName, selection.RootDisplayName,
            draft.EnableWrites ? "Read knowledge and write approved whole files" : "Read-only", draft.WritableFolderDisplayName,
            agents, DocumentRepositoryAudiences.Company, "Queue one bounded initial import after application-only validation succeeds.", changes, session.ConcurrencyVersion);
    }

    public async Task<Microsoft365RepositoryProvisioningDto> FinalizeAsync(Guid companyId, string sessionHandle, FinalizeMicrosoft365RepositoryCommand command, CancellationToken ct)
    {
        EnsureCompany(companyId); var userId = RequireUser();
        var session = await Find(companyId, userId, sessionHandle, true, ct) ?? throw new KeyNotFoundException();
        var existing = await db.CompanyDocumentRepositoryProvisionings.Include(x => x.Agents).SingleOrDefaultAsync(x => x.OnboardingSessionId == session.Id, ct);
        if (existing is not null) return MapProvisioning(existing);
        if (!command.Confirmed) throw Failure("review_confirmation_required", "Confirm the server-derived Microsoft permission review before connecting.");
        if (command.ExpectedConcurrencyVersion <= 0 || session.ConcurrencyVersion != command.ExpectedConcurrencyVersion)
            throw Failure(DocumentRepositoryOnboardingFailureCodes.ExpiredOrReplayedState, "The reviewed setup changed. Reload the review before connecting.");
        await EnsureInitiatorIsStillAdmin(session, ct);
        var state = protector.UnprotectState(session.ProtectedSetupMaterial ?? string.Empty);
        if (state.Selection is null || state.Draft is null) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "Complete repository, access, and agent selections before connecting.");
        var validAgentCount = state.Draft.AgentIds.Count == 0 ? 0 : await db.Agents.CountAsync(x => x.CompanyId == companyId && state.Draft.AgentIds.Contains(x.Id), ct);
        if (validAgentCount != state.Draft.AgentIds.Count) throw Failure("invalid_agent_selection", "One or more reviewed agents no longer belong to this company.");
        var source = new GraphSetupSource(state.Selection.ProviderKind, state.Selection.SiteId, state.Selection.DriveId, state.Selection.RootItemId, state.Selection.SourceDisplayName, state.Selection.SourceContext, state.Selection.WebUrl);
        _ = await setupAdapter.ValidateFolderAsync(state.Credential.AccessToken, source, state.Selection.RootItemId, ct);
        if (state.Draft.EnableWrites)
        {
            RequireScope(state, "Files.ReadWrite");
            _ = await setupAdapter.ValidateFolderAsync(state.Credential.AccessToken, source, state.Draft.WritableFolderItemId!, ct);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var operation = new CompanyDocumentRepositoryProvisioning(companyId, session.Id, session.InitiatingUserId,
            state.Selection.ProviderKind, state.Selection.TenantId, state.Selection.SiteId, state.Selection.DriveId,
            state.Selection.RootItemId, state.Selection.SourceDisplayName, state.Selection.RootDisplayName,
            state.Draft.EnableWrites, state.Draft.WritableFolderItemId, state.Draft.WritableFolderDisplayName,
            session.CorrelationId, now);
        foreach (var agentId in state.Draft.AgentIds) operation.Agents.Add(new(companyId, operation.Id, agentId, now));
        db.CompanyDocumentRepositoryProvisionings.Add(operation); session.MarkProvisioning(now);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var duplicate = await db.CompanyDocumentRepositoryProvisionings.AsNoTracking().SingleOrDefaultAsync(x => x.OnboardingSessionId == session.Id, ct);
            if (duplicate is not null) return MapProvisioning(duplicate);
            throw;
        }
        await WriteProvisioningAudit(session, AuditEventActions.DocumentRepositoryProvisioningConfirmed, AuditEventOutcomes.Started,
            "Confirmed and queued the reviewed Microsoft selected-resource permission provisioning.", ct);
        return MapProvisioning(operation);
    }

    public async Task<Microsoft365RepositoryProvisioningDto?> GetProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken ct)
    {
        EnsureCompany(companyId); var session = await Find(companyId, RequireUser(), sessionHandle, false, ct);
        if (session is null) return null;
        var operation = await db.CompanyDocumentRepositoryProvisionings.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.OnboardingSessionId == session.Id, ct);
        return operation is null ? null : MapProvisioning(operation);
    }

    public async Task<Microsoft365RepositoryProvisioningDto> RetryProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken ct)
    {
        EnsureCompany(companyId); var session = await Find(companyId, RequireUser(), sessionHandle, true, ct) ?? throw new KeyNotFoundException();
        await EnsureInitiatorIsStillAdmin(session, ct);
        var operation = await db.CompanyDocumentRepositoryProvisionings.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.OnboardingSessionId == session.Id, ct) ?? throw new KeyNotFoundException();
        operation.QueueRetry(clock.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
        await WriteProvisioningAudit(session, AuditEventActions.DocumentRepositoryProvisioningRetried, AuditEventOutcomes.Started, "Queued repository permission reconciliation.", ct);
        return MapProvisioning(operation);
    }

    public async Task<Microsoft365RepositoryProvisioningDto> CleanupProvisioningAsync(Guid companyId, string sessionHandle, CleanupMicrosoft365RepositoryProvisioningCommand command, CancellationToken ct)
    {
        EnsureCompany(companyId); if (!command.Confirmed) throw Failure("cleanup_confirmation_required", "Confirm removal of only the wizard-managed permissions before cleanup.");
        var session = await Find(companyId, RequireUser(), sessionHandle, true, ct) ?? throw new KeyNotFoundException();
        await EnsureInitiatorIsStillAdmin(session, ct);
        var operation = await db.CompanyDocumentRepositoryProvisionings.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.OnboardingSessionId == session.Id, ct) ?? throw new KeyNotFoundException();
        if (!operation.CanCleanup) throw Failure("cleanup_not_available", "There are no wizard-managed partial permissions to remove.");
        operation.QueueCleanup(clock.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
        await WriteProvisioningAudit(session, AuditEventActions.DocumentRepositoryProvisioningCleaned, AuditEventOutcomes.Started, "Queued removal of only wizard-managed partial Microsoft permissions.", ct);
        return MapProvisioning(operation);
    }

    public async Task ProcessPendingAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var candidate = await db.CompanyDocumentRepositoryProvisionings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == DocumentRepositoryProvisioningStatuses.Queued ||
                x.Status == DocumentRepositoryProvisioningStatuses.CleanupQueued ||
                x.Status == DocumentRepositoryProvisioningStatuses.ReconciliationRequired && x.AttemptCount < 8 && x.NextAttemptUtc <= now ||
                x.Status == DocumentRepositoryProvisioningStatuses.CleanupReconciliationRequired && x.AttemptCount < 8 && x.NextAttemptUtc <= now)
            .OrderBy(x => x.CreatedUtc).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (candidate == Guid.Empty) return;
        var claimed = await db.CompanyDocumentRepositoryProvisionings.IgnoreQueryFilters().Where(x => x.Id == candidate &&
            (x.Status == DocumentRepositoryProvisioningStatuses.Queued ||
             x.Status == DocumentRepositoryProvisioningStatuses.CleanupQueued ||
             x.Status == DocumentRepositoryProvisioningStatuses.ReconciliationRequired && x.AttemptCount < 8 && x.NextAttemptUtc <= now ||
             x.Status == DocumentRepositoryProvisioningStatuses.CleanupReconciliationRequired && x.AttemptCount < 8 && x.NextAttemptUtc <= now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status,
                    x => x.Status == DocumentRepositoryProvisioningStatuses.CleanupQueued ||
                         x.Status == DocumentRepositoryProvisioningStatuses.CleanupReconciliationRequired
                        ? DocumentRepositoryProvisioningStatuses.Cleaning
                        : DocumentRepositoryProvisioningStatuses.Provisioning)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1).SetProperty(x => x.UpdatedUtc, now)
                .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct);
        if (claimed != 1) return;
        db.ChangeTracker.Clear();
        var operation = await db.CompanyDocumentRepositoryProvisionings.IgnoreQueryFilters().Include(x => x.Agents).SingleAsync(x => x.Id == candidate, ct);
        var session = await db.CompanyDocumentRepositoryOnboardingSessions.IgnoreQueryFilters().SingleAsync(x => x.Id == operation.OnboardingSessionId, ct);
        var cleanup = operation.Status == DocumentRepositoryProvisioningStatuses.Cleaning;
        try
        {
            var stillAdmin = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == operation.CompanyId && x.UserId == operation.InitiatingUserId && x.Status == CompanyMembershipStatus.Active && (x.Role == CompanyMembershipRole.Owner || x.Role == CompanyMembershipRole.Admin), ct);
            if (!stillAdmin) throw Failure("administrator_authority_lost", "The confirming administrator no longer has authority for this company.");
            var state = await RefreshProvisioningStateAsync(session, ct);
            if (cleanup)
            {
                if (operation.WritePermissionManaged && operation.WritePermissionId is not null)
                    await permissionAdapter.DeleteAsync(state.Credential.AccessToken, operation.DriveId, operation.WritableFolderItemId!, operation.WritePermissionId, ct);
                if (operation.RootPermissionManaged && operation.RootPermissionId is not null)
                    await permissionAdapter.DeleteAsync(state.Credential.AccessToken, operation.DriveId, operation.RootItemId, operation.RootPermissionId, ct);
                operation.MarkCleaned(clock.GetUtcNow().UtcDateTime);
                session.Cancel(clock.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);
                await WriteProvisioningAudit(session, AuditEventActions.DocumentRepositoryProvisioningCleaned,
                    AuditEventOutcomes.Succeeded, "Removed only wizard-managed partial Microsoft permissions.", ct);
                ObserveProvisioning(operation, "cleaned");
                return;
            }
            var applicationId = Guid.Parse(ValidateOptions().PlatformClientId);
            var source = new GraphSetupSource(operation.ProviderKind, operation.SiteId, operation.DriveId,
                operation.RootItemId, operation.SourceDisplayName, null, null);
            _ = await setupAdapter.ValidateFolderAsync(state.Credential.AccessToken, source, operation.RootItemId, ct);
            if (operation.EnableWrites)
                _ = await setupAdapter.ValidateFolderAsync(state.Credential.AccessToken, source, operation.WritableFolderItemId!, ct);

            // Re-read the exact resource permission on every attempt. This reconciles a lost POST
            // response and prevents a persisted provider ID from being treated as current access.
            var root = await permissionAdapter.EnsureAsync(state.Credential.AccessToken, operation.DriveId,
                operation.RootItemId, applicationId, "read", ct);
            var rootManaged = string.Equals(operation.RootPermissionId, root.PermissionId, StringComparison.Ordinal)
                ? operation.RootPermissionManaged
                : root.Created;
            if (!string.Equals(operation.RootPermissionId, root.PermissionId, StringComparison.Ordinal) ||
                operation.RootPermissionManaged != rootManaged)
            {
                operation.RecordRootPermission(root.PermissionId, rootManaged, clock.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);
            }
            if (operation.EnableWrites)
            {
                var write = await permissionAdapter.EnsureAsync(state.Credential.AccessToken, operation.DriveId,
                    operation.WritableFolderItemId!, applicationId, "write", ct);
                var writeManaged = string.Equals(operation.WritePermissionId, write.PermissionId, StringComparison.Ordinal)
                    ? operation.WritePermissionManaged
                    : write.Created;
                if (!string.Equals(operation.WritePermissionId, write.PermissionId, StringComparison.Ordinal) ||
                    operation.WritePermissionManaged != writeManaged)
                {
                    operation.RecordWritePermission(write.PermissionId, writeManaged, clock.GetUtcNow().UtcDateTime);
                    await db.SaveChangesAsync(ct);
                }
            }

            var connection = operation.ConnectionId is { } connectionId
                ? await db.CompanyDocumentRepositoryConnections.IgnoreQueryFilters().Include(x => x.AgentGrants).SingleAsync(x => x.Id == connectionId, ct)
                : await db.CompanyDocumentRepositoryConnections.IgnoreQueryFilters().Include(x => x.AgentGrants).SingleOrDefaultAsync(x => x.CompanyId == operation.CompanyId && x.ProviderKind == operation.ProviderKind && x.DirectoryTenantId == operation.DirectoryTenantId && x.DriveId == operation.DriveId && x.RootItemId == operation.RootItemId, ct);
            if (connection is null)
            {
                connection = new CompanyDocumentRepositoryConnection(operation.CompanyId, operation.ProviderKind, operation.DirectoryTenantId,
                    applicationId, DocumentRepositoryCredentialModes.PlatformManagedReference, operation.DriveId, operation.RootItemId,
                    operation.SourceDisplayName, DocumentRepositoryAudiences.Company, clock.GetUtcNow().UtcDateTime);
                connection.UsePlatformManagedCredential(applicationId, clock.GetUtcNow().UtcDateTime);
                connection.ConfigureWrites(operation.EnableWrites, operation.WritableFolderItemId, clock.GetUtcNow().UtcDateTime);
                foreach (var selected in operation.Agents) connection.AgentGrants.Add(new(operation.CompanyId, connection.Id, selected.AgentId, clock.GetUtcNow().UtcDateTime));
                db.CompanyDocumentRepositoryConnections.Add(connection); operation.RecordConnection(connection.Id, clock.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
            }
            else if (connection.CredentialMode != DocumentRepositoryCredentialModes.PlatformManaged)
            {
                throw Failure("customer_managed_connection_exists", "A customer-managed connection already owns this repository root. It was not changed; clean up any wizard-created partial permission or choose another root.");
            }
            var validation = await graphAdapter.ValidateAsync(new(operation.ProviderKind, DocumentRepositoryCredentialModes.PlatformManaged, operation.DirectoryTenantId, applicationId, DocumentRepositoryCredentialModes.PlatformManagedReference, operation.DriveId, operation.RootItemId), ct);
            connection.MarkValidated(validation.RepositoryName, clock.GetUtcNow().UtcDateTime);
            var importKey = $"microsoft365-onboarding:{operation.Id:N}";
            if (!await db.CompanyDocumentRepositoryImportJobs.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == operation.CompanyId && x.ConnectionId == connection.Id && x.IdempotencyKey == importKey, ct))
                db.CompanyDocumentRepositoryImportJobs.Add(new(operation.CompanyId, connection.Id, importKey, operation.CorrelationId, clock.GetUtcNow().UtcDateTime));
            operation.Complete(connection.Id, clock.GetUtcNow().UtcDateTime); session.MarkConnected(clock.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
            await WriteProvisioningAudit(session, AuditEventActions.DocumentRepositoryProvisioningCompleted, AuditEventOutcomes.Succeeded, "Verified selected Microsoft permissions, activated the application-only connection, and queued initial import.", ct);
            ObserveProvisioning(operation, "connected");
        }
        catch (DocumentRepositoryOnboardingException exception)
        {
            if (cleanup) operation.FailCleanup(exception.Code, exception.SafeMessage, exception.Retryable && operation.AttemptCount < 8, clock.GetUtcNow().UtcDateTime);
            else operation.Fail(exception.Code, exception.SafeMessage, exception.Retryable && operation.AttemptCount < 8, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
            await WriteProvisioningAudit(session, AuditEventActions.DocumentRepositoryProvisioningFailed, AuditEventOutcomes.Failed, exception.SafeMessage, ct); ObserveProvisioning(operation, exception.Retryable ? "reconciliation_required" : "failed");
        }
        catch (DocumentRepositoryUnavailableException exception)
        {
            if (cleanup) operation.FailCleanup(exception.Code, exception.SafeMessage, operation.AttemptCount < 8, clock.GetUtcNow().UtcDateTime);
            else operation.Fail(exception.Code, exception.SafeMessage, operation.AttemptCount < 8, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
            await WriteProvisioningAudit(session, AuditEventActions.DocumentRepositoryProvisioningFailed, AuditEventOutcomes.Failed, exception.SafeMessage, ct); ObserveProvisioning(operation, "validation_pending");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or DbUpdateConcurrencyException)
        {
            logger.LogWarning(exception, "Repository provisioning {ProvisioningId} requires reconciliation.", operation.Id);
            if (cleanup) operation.FailCleanup("cleanup_outcome_ambiguous", "Repository permission cleanup has an ambiguous result and requires reconciliation.", operation.AttemptCount < 8, clock.GetUtcNow().UtcDateTime);
            else operation.Fail("provisioning_outcome_ambiguous", "Repository provisioning has an ambiguous result and requires reconciliation.", operation.AttemptCount < 8, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct); ObserveProvisioning(operation, cleanup ? "cleanup_reconciliation_required" : "reconciliation_required");
        }
    }

    private async Task<(CompanyDocumentRepositoryProvisioning? Existing, CompanyDocumentRepositoryOnboardingSession Session, Microsoft365OnboardingProtectedState State)> RequireDraftAsync(Guid companyId, string handle, CancellationToken ct)
    {
        var (session, state) = await RequireDiscoverySessionAsync(companyId, handle, true, ct);
        if (state.Selection is null || state.Draft is null) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "Complete repository, access, and agent selections before review.");
        var existing = await db.CompanyDocumentRepositoryProvisionings.AsNoTracking().SingleOrDefaultAsync(x => x.OnboardingSessionId == session.Id, ct);
        return (existing, session, state);
    }

    private async Task<Microsoft365OnboardingProtectedState> RefreshProvisioningStateAsync(CompanyDocumentRepositoryOnboardingSession session, CancellationToken ct)
    {
        if (session.ProtectedSetupMaterial is null || session.ExpiresUtc <= clock.GetUtcNow().UtcDateTime) throw Failure(DocumentRepositoryOnboardingFailureCodes.AccessLost, "Microsoft administrator authority expired. Restart authorization to reconcile safely.");
        var state = protector.UnprotectState(session.ProtectedSetupMaterial);
        if (state.Credential.AccessTokenExpiresUtc <= clock.GetUtcNow().UtcDateTime.AddMinutes(2))
        {
            state = state with { Credential = await RefreshCredentialAsync(session.ProviderTenantId!.Value, state.Credential, ct) };
            session.UpdateProtectedMaterial(protector.ProtectState(state), clock.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
        }
        return state;
    }

    private async Task WriteProvisioningAudit(CompanyDocumentRepositoryOnboardingSession session, string action, string outcome, string summary, CancellationToken ct)
    {
        var initiatedByUser = action is AuditEventActions.DocumentRepositoryOnboardingAccessConfigured
            or AuditEventActions.DocumentRepositoryProvisioningConfirmed
            or AuditEventActions.DocumentRepositoryProvisioningRetried
            or AuditEventActions.DocumentRepositoryProvisioningCleaned;

        await audit.WriteAsync(new(session.CompanyId, initiatedByUser ? AuditActorTypes.User : AuditActorTypes.System,
            initiatedByUser ? session.InitiatingUserId : null, action, AuditTargetTypes.DocumentRepositoryOnboardingSession,
            session.Id.ToString("D"), outcome, summary, ["microsoft_graph", "document_repository_provisioning"],
            new Dictionary<string, string?> { ["status"] = session.Status }, session.CorrelationId), ct);
    }
    private static Microsoft365RepositoryProvisioningDto MapProvisioning(CompanyDocumentRepositoryProvisioning x) => new(x.Id, x.Status, x.ConnectionId, x.FailureCode, x.FailureSummary, x.CanRetry, x.CanCleanup, x.AttemptCount, x.CreatedUtc, x.UpdatedUtc, x.CompletedUtc);
    private static void ObserveProvisioning(CompanyDocumentRepositoryProvisioning operation, string outcome) { TagList tags = default; tags.Add("outcome", outcome); ProvisioningOutcomes.Add(1, tags); ProvisioningAge.Record(Math.Max(0, (DateTime.UtcNow - operation.CreatedUtc).TotalSeconds), tags); }
}

internal sealed class DocumentRepositoryProvisioningWorker(IServiceScopeFactory scopes, IOptions<Microsoft365DocumentOnboardingOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(options.Value.ProvisioningPollIntervalSeconds, 2, 60)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDocumentRepositoryProvisioningProcessor>().ProcessPendingAsync(stoppingToken);
        }
    }
}
