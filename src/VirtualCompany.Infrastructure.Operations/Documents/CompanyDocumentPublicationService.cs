using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Documents;

internal sealed class CompanyDocumentPublicationService(
    VirtualCompanyDbContext db,
    ICompanyDocumentStorage storage,
    IDocumentRepositoryGraphAdapter graph,
    ICompanyDocumentRepositorySynchronizationService synchronization,
    ICompanyOutboxEnqueuer outbox,
    IAuditEventWriter audit,
    IOptions<DocumentRepositoryOperationsOptions> operationsOptions,
    TimeProvider clock) : ICompanyDocumentPublicationService
{
    private const int MaximumBytes = 4 * 1024 * 1024;

    public async Task<DocumentPublicationRequestDto> PrepareAsync(Guid companyId, Guid agentId, string actorType,
        Guid actorId, PrepareDocumentPublicationCommand command, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty || actorId == Guid.Empty)
            throw new DocumentRepositoryValidationException("Company, agent, and requesting actor are required.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Trim().Length > 200)
            throw new DocumentRepositoryValidationException("A bounded idempotency key is required.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(command.ContentBase64 ?? string.Empty); }
        catch (FormatException) { throw new DocumentRepositoryValidationException("Publication content must be valid base64."); }
        if (bytes.Length is <= 0 or > MaximumBytes)
            throw new DocumentRepositoryValidationException($"Publication content must be between 1 byte and {MaximumBytes} bytes.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var key = command.IdempotencyKey.Trim();
        var existing = await db.CompanyDocumentPublicationRequests
            .Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.ContentSha256 != hash || existing.ConnectionId != command.ConnectionId ||
                !string.Equals(existing.FileName, command.FileName?.Trim(), StringComparison.Ordinal))
                throw new DocumentRepositoryConflictException("The publication idempotency key is already bound to different immutable content.");
            return Map(existing);
        }

        var connection = await WritableConnectionAsync(companyId, command.ConnectionId, agentId, cancellationToken);
        var id = Guid.NewGuid();
        var storageKey = $"companies/{companyId:N}/document-publications/{id:N}/{command.FileName}";
        await using var content = new MemoryStream(bytes, writable: false);
        await storage.WriteAsync(new DocumentStorageWriteRequest(companyId, id, storageKey, command.FileName,
            command.ContentType, content), cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var request = new CompanyDocumentPublicationRequest(id, companyId, connection.Id,
            connection.WritableFolderItemId!, command.FileName, command.ContentType, bytes.LongLength, hash,
            storageKey, agentId, actorType, actorId, key, now);
        db.CompanyDocumentPublicationRequests.Add(request);
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, actorType, actorId,
            AuditEventActions.DocumentPublicationPrepared, AuditTargetTypes.DocumentPublicationRequest,
            request.Id.ToString("N"), AuditEventOutcomes.Requested,
            "Agent output was staged for exact-file approval.", ["document_publication"],
            new Dictionary<string, string?> { ["connectionId"] = connection.Id.ToString("N"), ["fileName"] = request.FileName,
                ["contentSha256"] = request.ContentSha256, ["sizeBytes"] = request.SizeBytes.ToString() }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Map(request);
    }

    public async Task<DocumentPublicationRequestDto> PrepareUpdateAsync(Guid companyId, Guid agentId, string actorType,
        Guid actorId, PrepareDocumentUpdateCommand command, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty || actorId == Guid.Empty)
            throw new DocumentRepositoryValidationException("Company, agent, and requesting actor are required.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Trim().Length > 200)
            throw new DocumentRepositoryValidationException("A bounded idempotency key is required.");
        if (string.IsNullOrWhiteSpace(command.ItemId) || string.IsNullOrWhiteSpace(command.ExpectedRemoteVersion) ||
            string.IsNullOrWhiteSpace(command.OriginalEvidenceVersion))
            throw new DocumentRepositoryValidationException("Target item, expected remote version, and original evidence version are required.");
        byte[] replacement;
        try { replacement = Convert.FromBase64String(command.ContentBase64 ?? string.Empty); }
        catch (FormatException) { throw new DocumentRepositoryValidationException("Replacement content must be valid base64."); }
        if (replacement.Length is <= 0 or > MaximumBytes)
            throw new DocumentRepositoryValidationException($"Replacement content must be between 1 byte and {MaximumBytes} bytes.");

        var replacementHash = Convert.ToHexString(SHA256.HashData(replacement)).ToLowerInvariant();
        var key = command.IdempotencyKey.Trim();
        var existing = await db.CompanyDocumentPublicationRequests.Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.OperationKind != DocumentPublicationOperationKinds.Update || existing.ContentSha256 != replacementHash ||
                existing.ConnectionId != command.ConnectionId || existing.TargetItemId != command.ItemId ||
                existing.ExpectedRemoteVersion != command.ExpectedRemoteVersion)
                throw new DocumentRepositoryConflictException("The update idempotency key is already bound to a different immutable proposal.");
            return Map(existing);
        }

        var connection = await WritableConnectionAsync(companyId, command.ConnectionId, agentId, cancellationToken);
        var context = new GraphRepositoryContext(connection.ProviderKind, connection.CredentialMode, connection.DirectoryTenantId,
            connection.ApplicationClientId, connection.CredentialReference, connection.DriveId, connection.RootItemId);
        var target = await graph.ValidateItemAsync(context, command.ItemId.Trim(), cancellationToken);
        if (!target.IsAvailable || string.IsNullOrWhiteSpace(target.RemoteVersion))
            throw new DocumentRepositoryNotFoundException();
        if (!string.Equals(target.ParentItemId, connection.WritableFolderItemId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The target file is outside the explicitly writable folder.");
        if (!string.Equals(target.Name, command.FileName.Trim(), StringComparison.Ordinal))
            throw new DocumentRepositoryConflictException("The target filename no longer matches the reviewed destination.", target.RemoteVersion);
        if (!string.Equals(target.RemoteVersion, command.ExpectedRemoteVersion, StringComparison.Ordinal) ||
            !string.Equals(command.ExpectedRemoteVersion, command.OriginalEvidenceVersion, StringComparison.Ordinal))
            throw new DocumentRepositoryConflictException("The remote file changed before the proposal could be prepared.", target.RemoteVersion);
        if (target.SizeBytes is null or <= 0 or > MaximumBytes)
            throw new DocumentRepositoryValidationException("The original file is empty or too large for bounded update review.");

        CompanyDocumentPublicationRequest? stale = null;
        if (command.StalePublicationRequestId.HasValue)
        {
            stale = await db.CompanyDocumentPublicationRequests.Include(x => x.Connection)
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == command.StalePublicationRequestId.Value, cancellationToken)
                ?? throw new DocumentRepositoryNotFoundException();
            if (stale.OperationKind != DocumentPublicationOperationKinds.Update || stale.TargetItemId != command.ItemId ||
                stale.Status is not (DocumentPublicationStatuses.Conflict or DocumentPublicationStatuses.Unresolved))
                throw new DocumentRepositoryValidationException("Only a stale conflicting proposal for the same file can be replaced.");
        }

        await using var original = new MemoryStream((int)target.SizeBytes.Value);
        await graph.CopyContentVersionAsync(context, command.ItemId.Trim(), command.ExpectedRemoteVersion.Trim(),
            original, MaximumBytes, cancellationToken);
        var originalBytes = original.ToArray();
        var originalHash = Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant();
        var id = Guid.NewGuid();
        var storageKey = $"companies/{companyId:N}/document-publications/{id:N}/replacement/{command.FileName}";
        var originalStorageKey = $"companies/{companyId:N}/document-publications/{id:N}/original/{command.FileName}";
        await using (var content = new MemoryStream(replacement, writable: false))
            await storage.WriteAsync(new DocumentStorageWriteRequest(companyId, id, storageKey, command.FileName,
                command.ContentType ?? target.ContentType, content), cancellationToken);
        await using (var content = new MemoryStream(originalBytes, writable: false))
            await storage.WriteAsync(new DocumentStorageWriteRequest(companyId, id, originalStorageKey, command.FileName,
                target.ContentType, content), cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var request = new CompanyDocumentPublicationRequest(id, companyId, connection.Id,
            connection.WritableFolderItemId!, command.ItemId.Trim(), command.ExpectedRemoteVersion.Trim(),
            command.OriginalEvidenceVersion.Trim(), command.FileName, command.ContentType ?? target.ContentType,
            replacement.LongLength, replacementHash, storageKey, originalBytes.LongLength, originalHash,
            originalStorageKey, agentId, actorType, actorId, key, stale?.Id, now);
        db.CompanyDocumentPublicationRequests.Add(request);
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, actorType, actorId,
            AuditEventActions.DocumentPublicationUpdatePrepared, AuditTargetTypes.DocumentPublicationRequest,
            request.Id.ToString("N"), AuditEventOutcomes.Requested,
            "A whole-file replacement was staged against an exact Microsoft 365 version.",
            ["document_update", "optimistic_concurrency"],
            new Dictionary<string, string?> { ["connectionId"] = connection.Id.ToString("N"),
                ["targetItemId"] = request.TargetItemId, ["expectedRemoteVersion"] = request.ExpectedRemoteVersion,
                ["contentSha256"] = request.ContentSha256, ["originalContentSha256"] = request.OriginalContentSha256,
                ["stalePublicationRequestId"] = stale?.Id.ToString("N") }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Map(request);
    }

    public async Task<DocumentPublicationRequestDto> QueueApprovedAsync(Guid companyId, Guid agentId, Guid executionId,
        Guid publicationRequestId, string expectedSha256, CancellationToken cancellationToken)
    {
        var request = await db.CompanyDocumentPublicationRequests.Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == publicationRequestId, cancellationToken)
            ?? throw new DocumentRepositoryNotFoundException();
        if (request.RequestingAgentId != agentId)
            throw new UnauthorizedAccessException("The staged publication belongs to another agent.");
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(request.ContentSha256), Convert.FromHexString(expectedSha256)))
            throw new DocumentRepositoryConflictException("The staged file hash changed. A new approval is required.");
        var approval = await db.ApprovalRequests.SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.ToolExecutionAttemptId == executionId, cancellationToken);
        if (approval is null || approval.Status != ApprovalRequestStatus.Approved)
            throw new UnauthorizedAccessException("A current approval for the exact publication is required.");
        var connection = await WritableConnectionAsync(companyId, request.ConnectionId, agentId, cancellationToken);
        if (request.OperationKind == DocumentPublicationOperationKinds.Update)
        {
            var context = new GraphRepositoryContext(connection.ProviderKind, connection.CredentialMode, connection.DirectoryTenantId,
                connection.ApplicationClientId, connection.CredentialReference, connection.DriveId, connection.RootItemId);
            var target = await graph.ValidateItemAsync(context, request.TargetItemId!, cancellationToken);
            if (!target.IsAvailable || !string.Equals(target.RemoteVersion, request.ExpectedRemoteVersion, StringComparison.Ordinal))
            {
                request.MarkConflict(target.RemoteVersion ?? "unavailable", clock.GetUtcNow().UtcDateTime);
                approval.MarkStale("The target file changed after review. A new proposal and approval are required.");
                await db.SaveChangesAsync(cancellationToken);
                throw new DocumentRepositoryConflictException("A newer human edit was detected. The stale approval cannot be used.", target.RemoteVersion);
            }
        }
        request.Queue(approval.Id, executionId, JsonSerializer.Serialize(approval.PolicyDecision), approval.UpdatedUtc, clock.GetUtcNow().UtcDateTime);
        outbox.Enqueue(companyId, CompanyOutboxTopics.DocumentPublicationDeliveryRequested,
            new DocumentPublicationDeliveryRequestedMessage(companyId, request.Id, request.ContentSha256, null),
            idempotencyKey: $"document-publication:{companyId:N}:{request.Id:N}:{request.ContentSha256}",
            messageType: nameof(DocumentPublicationDeliveryRequestedMessage));
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, "agent", agentId,
            AuditEventActions.DocumentPublicationQueued, AuditTargetTypes.DocumentPublicationRequest,
            request.Id.ToString("N"), AuditEventOutcomes.Requested,
            "Approved document publication was queued for delivery.", ["approval", "company_outbox"],
            new Dictionary<string, string?> { ["approvalRequestId"] = approval.Id.ToString("N"),
                ["contentSha256"] = request.ContentSha256 }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Map(request);
    }

    public async Task<DocumentPublicationRequestDto?> GetAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken)
    {
        var request = await db.CompanyDocumentPublicationRequests.AsNoTracking().Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == publicationRequestId, cancellationToken);
        return request is null ? null : Map(request);
    }

    public async Task<Stream> OpenStagedAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken)
    {
        var request = await db.CompanyDocumentPublicationRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == publicationRequestId, cancellationToken)
            ?? throw new DocumentRepositoryNotFoundException();
        return await storage.OpenReadAsync(request.StorageKey, cancellationToken);
    }

    public async Task<Stream> OpenOriginalAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken)
    {
        var request = await db.CompanyDocumentPublicationRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == publicationRequestId, cancellationToken)
            ?? throw new DocumentRepositoryNotFoundException();
        if (request.OperationKind != DocumentPublicationOperationKinds.Update || string.IsNullOrWhiteSpace(request.OriginalStorageKey))
            throw new DocumentRepositoryValidationException("This publication has no reviewed original artifact.");
        return await storage.OpenReadAsync(request.OriginalStorageKey, cancellationToken);
    }

    public async Task<DocumentUpdateReviewDto> GetUpdateReviewAsync(Guid companyId, Guid publicationRequestId,
        CancellationToken cancellationToken)
    {
        var request = await db.CompanyDocumentPublicationRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == publicationRequestId, cancellationToken)
            ?? throw new DocumentRepositoryNotFoundException();
        if (request.OperationKind != DocumentPublicationOperationKinds.Update)
            throw new DocumentRepositoryValidationException("This publication is not a file update.");
        var textReview = IsTextReviewSupported(request.FileName, request.ContentType);
        string? diff = null;
        if (textReview)
        {
            await using var original = await storage.OpenReadAsync(request.OriginalStorageKey!, cancellationToken);
            await using var replacement = await storage.OpenReadAsync(request.StorageKey, cancellationToken);
            diff = await BuildBoundedDiffAsync(original, replacement, cancellationToken);
        }
        return new DocumentUpdateReviewDto(request.Id, request.FileName, request.ExpectedRemoteVersion!,
            request.OriginalEvidenceVersion!, request.ContentSha256, textReview, diff,
            textReview ? "Review the bounded text diff and both immutable artifacts before approval."
                : "This binary document is replaced as a whole file. Review both immutable downloads before approval.");
    }

    public async Task DispatchAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken)
    {
        var request = await db.CompanyDocumentPublicationRequests.Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == publicationRequestId, cancellationToken)
            ?? throw new CompanyOutboxPermanentException("The document publication request was not found.");
        if (request.Status == DocumentPublicationStatuses.Delivered)
        {
            await QueueSynchronizationAsync(request, cancellationToken);
            return;
        }
        var approval = request.ApprovalRequestId.HasValue
            ? await db.ApprovalRequests.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.ApprovalRequestId, cancellationToken)
            : null;
        if (approval?.Status != ApprovalRequestStatus.Approved || !request.ApprovalVersionUtc.HasValue ||
            approval.UpdatedUtc != request.ApprovalVersionUtc.Value)
            throw new CompanyOutboxPermanentException("The publication approval is no longer current.");
        var connection = await WritableConnectionAsync(companyId, request.ConnectionId, request.RequestingAgentId, cancellationToken);
        if (!string.Equals(connection.WritableFolderItemId, request.TargetFolderItemId, StringComparison.Ordinal))
            throw new CompanyOutboxPermanentException("The publication destination changed after approval.");

        var context = new GraphRepositoryContext(connection.ProviderKind, connection.CredentialMode, connection.DirectoryTenantId,
            connection.ApplicationClientId, connection.CredentialReference, connection.DriveId, connection.RootItemId);
        if (request.Status is DocumentPublicationStatuses.Sending or DocumentPublicationStatuses.ReconciliationRequired)
        {
            var reconciliation = request.OperationKind == DocumentPublicationOperationKinds.Update
                ? await graph.ReconcileUpdatedFileAsync(context, request.TargetItemId!, request.ContentSha256,
                    request.SizeBytes, cancellationToken)
                : await graph.ReconcileFileAsync(context, request.TargetFolderItemId, request.FileName,
                    request.ContentSha256, request.SizeBytes, cancellationToken);
            if (reconciliation.Found && reconciliation.ContentMatches)
            {
                request.MarkDelivered(reconciliation.File!.ItemId, reconciliation.File.RemoteVersion,
                    reconciliation.File.WebUrl, clock.GetUtcNow().UtcDateTime);
                await CompleteAsync(request, cancellationToken);
                return;
            }
            if (request.OperationKind == DocumentPublicationOperationKinds.Update)
            {
                request.MarkUnresolved(reconciliation.File?.RemoteVersion ?? "unavailable", clock.GetUtcNow().UtcDateTime);
                if (approval.Status == ApprovalRequestStatus.Approved)
                    approval.MarkStale("The conditional update outcome is unresolved after a later remote edit. No overwrite will be retried.");
                await audit.WriteAsync(new AuditEventWriteRequest(request.CompanyId, "agent", request.RequestingAgentId,
                    AuditEventActions.DocumentPublicationUpdateUnresolved, AuditTargetTypes.DocumentPublicationRequest,
                    request.Id.ToString("N"), AuditEventOutcomes.Failed,
                    "A conditional update could not be reconciled after the remote file changed again.",
                    ["microsoft_graph", "reconciliation", "document_update"],
                    new Dictionary<string, string?> { ["targetItemId"] = request.TargetItemId,
                        ["expectedRemoteVersion"] = request.ExpectedRemoteVersion,
                        ["currentRemoteVersion"] = request.ConflictRemoteVersion }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                throw new CompanyOutboxPermanentException("The conditional update outcome is unresolved and will not be retried automatically.");
            }
            if (reconciliation.Found)
                throw new CompanyOutboxPermanentException("A different file already exists at the approved destination.");
        }

        request.MarkSending(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await using var staged = await storage.OpenReadAsync(request.StorageKey, cancellationToken);
            await using var content = new MemoryStream((int)request.SizeBytes);
            await staged.CopyToAsync(content, cancellationToken);
            var actualHash = Convert.ToHexString(SHA256.HashData(content.ToArray())).ToLowerInvariant();
            if (content.Length != request.SizeBytes ||
                !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(request.ContentSha256)))
            {
                request.MarkFailed("staged_content_changed", "The staged artifact no longer matches the approved size and hash.", clock.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(cancellationToken);
                throw new CompanyOutboxPermanentException("The staged artifact changed after approval.");
            }
            content.Position = 0;
            var created = request.OperationKind == DocumentPublicationOperationKinds.Update
                ? await graph.UpdateFileAsync(context, request.TargetItemId!, request.ExpectedRemoteVersion!, content,
                    request.SizeBytes, request.ContentType, cancellationToken)
                : await graph.CreateFileAsync(context, request.TargetFolderItemId, request.FileName, content,
                    request.SizeBytes, request.ContentType, cancellationToken);
            request.MarkDelivered(created.ItemId, created.RemoteVersion, created.WebUrl, clock.GetUtcNow().UtcDateTime);
            await CompleteAsync(request, cancellationToken);
        }
        catch (DocumentRepositoryAmbiguousWriteException ex)
        {
            request.MarkReconciliationRequired("ambiguous_provider_outcome", ex.Message, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
        catch (DocumentRepositoryConflictException ex)
        {
            if (request.OperationKind == DocumentPublicationOperationKinds.Update)
            {
                request.MarkConflict(ex.CurrentRemoteVersion ?? "changed", clock.GetUtcNow().UtcDateTime);
                if (approval.Status == ApprovalRequestStatus.Approved)
                    approval.MarkStale("A newer human edit was detected. A new reviewed proposal is required.");
                await audit.WriteAsync(new AuditEventWriteRequest(request.CompanyId, "agent", request.RequestingAgentId,
                    AuditEventActions.DocumentPublicationUpdateConflict, AuditTargetTypes.DocumentPublicationRequest,
                    request.Id.ToString("N"), AuditEventOutcomes.Denied,
                    "The provider-enforced version condition rejected a stale whole-file replacement.",
                    ["microsoft_graph", "optimistic_concurrency", "document_update"],
                    new Dictionary<string, string?> { ["targetItemId"] = request.TargetItemId,
                        ["expectedRemoteVersion"] = request.ExpectedRemoteVersion,
                        ["currentRemoteVersion"] = request.ConflictRemoteVersion }), cancellationToken);
            }
            else request.MarkFailed("name_collision", ex.Message, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            throw new CompanyOutboxPermanentException(ex.Message);
        }
    }

    public async Task<DocumentPublicationRequestDto> RequestReconciliationAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken)
    {
        if (!operationsOptions.Value.Enabled)
            throw new DocumentRepositoryUnavailableException("feature_disabled", "Document repository operations are disabled for this deployment.");
        var request = await db.CompanyDocumentPublicationRequests.Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == publicationRequestId, cancellationToken)
            ?? throw new DocumentRepositoryNotFoundException();
        if (request.Connection.WritesPausedUtc.HasValue)
            throw new DocumentRepositoryUnavailableException("writes_paused", "Approved repository writes are paused by an administrator.");
        request.RequestReconciliation(clock.GetUtcNow().UtcDateTime);
        outbox.Enqueue(companyId, CompanyOutboxTopics.DocumentPublicationDeliveryRequested,
            new DocumentPublicationDeliveryRequestedMessage(companyId, request.Id, request.ContentSha256, null),
            idempotencyKey: $"document-publication-reconcile:{companyId:N}:{request.Id:N}:{request.UpdatedUtc.Ticks}",
            messageType: nameof(DocumentPublicationDeliveryRequestedMessage));
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, null,
            AuditEventActions.DocumentRepositoryRecoveryRequested, AuditTargetTypes.DocumentPublicationRequest,
            request.Id.ToString("N"), AuditEventOutcomes.Requested,
            "Requested safe reconciliation of an uncertain Microsoft 365 publication.",
            ["document_repository", "reconciliation"],
            new Dictionary<string, string?> { ["operationKind"] = request.OperationKind, ["attemptCount"] = request.AttemptCount.ToString() }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Map(request);
    }

    private async Task CompleteAsync(CompanyDocumentPublicationRequest request, CancellationToken cancellationToken)
    {
        if (request.OperationKind == DocumentPublicationOperationKinds.Update)
            await InvalidateOldEvidenceAsync(request, cancellationToken);
        await QueueSynchronizationAsync(request, cancellationToken);
        await audit.WriteAsync(new AuditEventWriteRequest(request.CompanyId, "agent", request.RequestingAgentId,
            request.OperationKind == DocumentPublicationOperationKinds.Update
                ? AuditEventActions.DocumentPublicationUpdateDelivered
                : AuditEventActions.DocumentPublicationDelivered,
            AuditTargetTypes.DocumentPublicationRequest, request.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            request.OperationKind == DocumentPublicationOperationKinds.Update
                ? "Approved whole-file replacement was conditionally uploaded and old indexed evidence was invalidated."
                : "Approved document was created without replacing an existing file.",
            ["microsoft_graph", "company_outbox"],
            new Dictionary<string, string?> { ["providerItemId"] = request.ProviderItemId,
                ["providerVersion"] = request.ProviderVersion, ["contentSha256"] = request.ContentSha256,
                ["expectedRemoteVersion"] = request.ExpectedRemoteVersion }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task InvalidateOldEvidenceAsync(CompanyDocumentPublicationRequest request, CancellationToken cancellationToken)
    {
        var sources = await db.CompanyKnowledgeDocumentRemoteSources.IgnoreQueryFilters()
            .Where(x => x.CompanyId == request.CompanyId && x.ConnectionId == request.ConnectionId &&
                x.ItemId == request.TargetItemId).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var source in sources)
        {
            source.MarkUnavailable("replacement_pending", now);
            var chunks = await db.CompanyKnowledgeChunks.IgnoreQueryFilters()
                .Where(x => x.CompanyId == request.CompanyId && x.DocumentId == source.DocumentId && x.IsActive)
                .ToListAsync(cancellationToken);
            foreach (var chunk in chunks) chunk.Deactivate();
        }
    }

    private Task<DocumentRepositorySynchronizationJobDto> QueueSynchronizationAsync(
        CompanyDocumentPublicationRequest request,
        CancellationToken cancellationToken) => synchronization.StartAsync(
            request.CompanyId,
            request.ConnectionId,
            new StartDocumentRepositorySynchronizationCommand(
                $"document-publication:{request.Id:N}:{request.ProviderVersion ?? request.ContentSha256}",
                ForceFullReconciliation: false),
            cancellationToken);

    private async Task<CompanyDocumentRepositoryConnection> WritableConnectionAsync(Guid companyId, Guid connectionId,
        Guid agentId, CancellationToken cancellationToken)
    {
        if (!operationsOptions.Value.Enabled)
            throw new DocumentRepositoryUnavailableException("feature_disabled", "Document repository operations are disabled for this deployment.");
        var connection = await db.CompanyDocumentRepositoryConnections.Include(x => x.AgentGrants)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == connectionId, cancellationToken)
            ?? throw new DocumentRepositoryNotFoundException();
        if (connection.LifecycleState != DocumentRepositoryLifecycleStates.Active || connection.IsReadOnly ||
            string.IsNullOrWhiteSpace(connection.WritableFolderItemId))
            throw new UnauthorizedAccessException("This repository is not active for explicitly configured writes.");
        if (connection.WritesPausedUtc.HasValue)
            throw new DocumentRepositoryUnavailableException("writes_paused", "Approved repository writes are paused by an administrator. Queued artifacts remain preserved.");
        if (!connection.AgentGrants.Any(x => x.AgentId == agentId))
            throw new UnauthorizedAccessException("This agent is not granted access to the repository.");
        return connection;
    }

    private static bool IsTextReviewSupported(string fileName, string? contentType) =>
        contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true ||
        contentType is "application/json" or "application/xml" ||
        Path.GetExtension(fileName).ToLowerInvariant() is ".md" or ".txt" or ".csv" or ".json" or ".xml";

    private static async Task<string> BuildBoundedDiffAsync(Stream original, Stream replacement, CancellationToken cancellationToken)
    {
        const int maxCharacters = 64 * 1024;
        const int maxLines = 400;
        using var originalReader = new StreamReader(original, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var replacementReader = new StreamReader(replacement, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var oldText = await originalReader.ReadToEndAsync(cancellationToken);
        var newText = await replacementReader.ReadToEndAsync(cancellationToken);
        if (oldText.Length > maxCharacters || newText.Length > maxCharacters)
            throw new DocumentRepositoryValidationException("The text is too large for inline diff review. Download both immutable artifacts instead.");
        var oldLines = oldText.Replace("\r\n", "\n").Split('\n');
        var newLines = newText.Replace("\r\n", "\n").Split('\n');
        var result = new List<string> { $"--- original ({oldLines.Length} lines)", $"+++ proposed ({newLines.Length} lines)" };
        for (var i = 0; i < Math.Max(oldLines.Length, newLines.Length) && result.Count < maxLines; i++)
        {
            var before = i < oldLines.Length ? oldLines[i] : null;
            var after = i < newLines.Length ? newLines[i] : null;
            if (string.Equals(before, after, StringComparison.Ordinal)) result.Add(" " + before);
            else
            {
                if (before is not null) result.Add("-" + before);
                if (after is not null && result.Count < maxLines) result.Add("+" + after);
            }
        }
        if (Math.Max(oldLines.Length, newLines.Length) + 2 > maxLines) result.Add("... diff truncated ...");
        return string.Join(Environment.NewLine, result);
    }

    private static DocumentPublicationRequestDto Map(CompanyDocumentPublicationRequest request) => new(
        request.Id, request.CompanyId, request.ConnectionId, request.Connection.DisplayName,
        request.TargetFolderItemId, request.FileName, request.ContentType, request.SizeBytes,
        request.ContentSha256, request.Status, request.RequestingAgentId, request.ApprovalRequestId,
        request.ApprovalVersionUtc, request.ProviderItemId, request.ProviderVersion, request.SourceWebUrl, request.AttemptCount,
        request.FailureCode, request.FailureMessage, request.CreatedUtc, request.UpdatedUtc, request.CompletedUtc,
        request.OperationKind, request.TargetItemId, request.ExpectedRemoteVersion, request.OriginalEvidenceVersion,
        request.OriginalContentSha256, request.OriginalSizeBytes, request.StalePublicationRequestId,
        request.ConflictRemoteVersion);
}
