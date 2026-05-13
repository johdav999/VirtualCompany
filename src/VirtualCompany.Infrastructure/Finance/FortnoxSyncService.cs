using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxSyncService : IFortnoxSyncService
{
    private const string ScopeKey = "default";
    private const int PageSize = 100;
    private const string FortnoxScopeArticle = "article";
    private const string FortnoxScopeBookkeeping = "bookkeeping";
    private const string FortnoxScopeCompanyInformation = "companyinformation";
    private const string FortnoxScopeCustomer = "customer";
    private const string FortnoxScopeInvoice = "invoice";
    private const string FortnoxScopeProject = "project";
    private const string FortnoxScopeSupplier = "supplier";
    private const string FortnoxScopeSupplierInvoice = "supplierinvoice";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFortnoxApiClient _apiClient;
    private readonly IFortnoxMappingService _mappingService;
    private readonly ILogger<FortnoxSyncService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IFortnoxIntegrationDiagnostics? _diagnostics;

    public FortnoxSyncService(
        VirtualCompanyDbContext dbContext,
        IFortnoxApiClient apiClient,
        IFortnoxMappingService mappingService,
        ILogger<FortnoxSyncService> logger,
        TimeProvider timeProvider,
        IFortnoxIntegrationDiagnostics? diagnostics = null)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
        _mappingService = mappingService;
        _logger = logger;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
    }

    public async Task<FortnoxSyncResult> SyncAsync(RunFortnoxSyncCommand command, CancellationToken cancellationToken)
    {
        var startedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var connection = await ResolveConnectionAsync(command, cancellationToken);
        var context = new FortnoxRequestContext(command.CompanyId, connection.Id, command.CorrelationId, ActorUserId: command.ActorUserId);
        var history = new FortnoxSyncHistory(Guid.NewGuid(), command.CompanyId, connection.Id, FortnoxSyncTypes.Manual, FortnoxSyncDirections.Import, startedUtc, command.ActorUserId, command.CorrelationId);
        var entityResults = new List<FortnoxEntitySyncResult>();
        _dbContext.FortnoxSyncHistories.Add(history);
        AddManualSyncAudit(command, connection.Id, FinanceIntegrationAuditOutcomes.Succeeded, "Fortnox manual sync started.", startedUtc, 0, 0, 0, 0, "manual_sync_started");
        _diagnostics?.SyncStarted(command.CompanyId, connection.Id, command.CorrelationId);
        var reconnectBlockReason = await GetFortnoxReconnectBlockReasonAsync(command.CompanyId, connection.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(reconnectBlockReason))
        {
            return await CompleteBlockedSyncAsync(command, connection, history, startedUtc, reconnectBlockReason, cancellationToken);
        }

        var grantedScopes = await ResolveGrantedScopesAsync(command.CompanyId, connection.Id, cancellationToken);

        _logger.LogInformation(
            "Starting Fortnox sync for company {CompanyId}, connection {ConnectionId}, correlation {CorrelationId}.",
            command.CompanyId,
            connection.Id,
            command.CorrelationId);

        entityResults.Add(await SyncEntityAsync(connection, "company_information", FortnoxScopeCompanyInformation, grantedScopes, state => SyncCompanyInformationAsync(context, state, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "accounts", FortnoxScopeBookkeeping, grantedScopes, state => SyncAccountsAsync(context, state, command.FullSync, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "customers", FortnoxScopeCustomer, grantedScopes, state => SyncCustomersAsync(context, state, command.FullSync, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "suppliers", FortnoxScopeSupplier, grantedScopes, state => SyncSuppliersAsync(context, state, command.FullSync, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "articles", FortnoxScopeArticle, grantedScopes, state => SyncArticlesAsync(context, state, command.FullSync, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "projects", FortnoxScopeProject, grantedScopes, state => SyncProjectsAsync(context, state, command.FullSync, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "invoices", FortnoxScopeInvoice, grantedScopes, state => SyncInvoicesAsync(context, state, command.FullSync, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "supplier_invoices", FortnoxScopeSupplierInvoice, grantedScopes, state => SyncSupplierInvoicesAsync(context, state, command.FullSync, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "vouchers", FortnoxScopeBookkeeping, grantedScopes, state => SyncVouchersAsync(context, state, command.FullSync, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "payments", state => SyncPaymentActivityAsync(context, state, cancellationToken), cancellationToken));

        var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var errors = entityResults.Sum(x => x.Errors);
        var status = errors == 0 ? FinanceIntegrationSyncStatuses.Succeeded : FinanceIntegrationSyncStatuses.Partial;
        var errorSummary = errors == 0
            ? null
            : string.Join("; ", entityResults.Where(x => !string.IsNullOrWhiteSpace(x.ErrorSummary)).Select(x => $"{x.EntityType}: {x.ErrorSummary}"));

        if (errors == 0)
        {
            connection.MarkSyncSucceeded(completedUtc);
        }
        else
        {
            connection.MarkSyncFailed(errorSummary ?? "Fortnox sync completed with errors.", completedUtc);
        }

        var created = entityResults.Sum(x => x.Created);
        var updated = entityResults.Sum(x => x.Updated);
        var skipped = entityResults.Sum(x => x.Skipped);
        var succeeded = created + updated + skipped;
        history.MarkCompleted(succeeded + errors, succeeded, errors, completedUtc, errorSummary);
        history.Metadata["created"] = created;
        history.Metadata["updated"] = updated;
        history.Metadata["skipped"] = skipped;
        history.Metadata["durationMilliseconds"] = (completedUtc - startedUtc).TotalMilliseconds;
        history.Metadata["entities"] = BuildEntityMetadata(entityResults);
        history.Metadata["retryAttempts"] = entityResults.Sum(x => x.RetryAttempts);
        history.Metadata["retryOutcome"] = BuildRetryOutcome(entityResults);
        AddManualSyncAudit(command, connection.Id, errors == 0 ? FinanceIntegrationAuditOutcomes.Succeeded : FinanceIntegrationAuditOutcomes.Failed, BuildHistorySummary(entityResults), completedUtc, created, updated, skipped, errors, "manual_sync_completed");

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Completed Fortnox sync for company {CompanyId}, connection {ConnectionId}. Created {Created}; updated {Updated}; skipped {Skipped}; errors {Errors}.",
            command.CompanyId,
            connection.Id,
            entityResults.Sum(x => x.Created),
            entityResults.Sum(x => x.Updated),
            entityResults.Sum(x => x.Skipped),
            errors);

        _diagnostics?.SyncCompleted(
            command.CompanyId,
            connection.Id,
            command.CorrelationId,
            status,
            entityResults.Sum(x => x.Created),
            entityResults.Sum(x => x.Updated),
            entityResults.Sum(x => x.Skipped),
            errors,
            completedUtc - startedUtc);

        return new FortnoxSyncResult(
            command.CompanyId,
            connection.Id,
            startedUtc,
            completedUtc,
            status,
            entityResults.Sum(x => x.Created),
            entityResults.Sum(x => x.Updated),
            entityResults.Sum(x => x.Skipped),
            errors,
            entityResults,
            errorSummary,
            entityResults.Sum(x => x.RetryAttempts),
            BuildRetryOutcome(entityResults));
    }

    public async Task<FortnoxSyncHistoryResult> GetHistoryAsync(GetFortnoxSyncHistoryQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit <= 0 ? 25 : Math.Min(query.Limit, 100);
        var histories = await _dbContext.FortnoxSyncHistories
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SyncType == FortnoxSyncTypes.Manual)
            .OrderByDescending(x => x.StartedUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = histories.Select(x => new FortnoxSyncHistoryItem(
                x.Id,
                x.FortnoxConnectionId,
                x.StartedUtc,
                x.CompletedUtc,
                x.Status,
                ReadInt(x.Metadata, "created"),
                ReadInt(x.Metadata, "updated"),
                ReadInt(x.Metadata, "skipped"),
                x.RecordsFailed,
                BuildHistoryItemSummary(x),
                x.ErrorSummary,
                ReadInt(x.Metadata, "retryAttempts"),
                ReadString(x.Metadata, "retryOutcome"),
                ReadEntityMetadata(x.Metadata)))
            .ToList();

        return new FortnoxSyncHistoryResult(query.CompanyId, items);
    }

    private void AddManualSyncAudit(RunFortnoxSyncCommand command, Guid connectionId, string outcome, string summary, DateTime createdUtc, int created, int updated, int skipped, int errors, string eventType) =>
        _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            command.CompanyId,
            connectionId,
            FinanceIntegrationProviderKeys.Fortnox,
            eventType,
            outcome,
            null,
            null,
            null,
            command.CorrelationId,
            summary,
            createdUtc,
            created, updated, skipped, errors));

    private async Task<FinanceIntegrationConnection> ResolveConnectionAsync(RunFortnoxSyncCommand command, CancellationToken cancellationToken)
    {
        var query = _dbContext.FinanceIntegrationConnections
            .Where(x => x.CompanyId == command.CompanyId && x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox);

        if (command.ConnectionId.HasValue)
        {
            query = query.Where(x => x.Id == command.ConnectionId.Value);
        }

        var connection = await query.SingleOrDefaultAsync(cancellationToken);
        if (connection is not null)
        {
            return connection;
        }

        var fortnoxQuery = _dbContext.FortnoxConnections
            .Where(x => x.CompanyId == command.CompanyId && x.Status == FortnoxConnectionStatus.Connected);

        if (command.ConnectionId.HasValue)
        {
            fortnoxQuery = fortnoxQuery.Where(x => x.Id == command.ConnectionId.Value);
        }

        var fortnoxConnection = await fortnoxQuery.SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No connected Fortnox integration was found for this company.");

        connection = new FinanceIntegrationConnection(
            fortnoxConnection.Id,
            fortnoxConnection.CompanyId,
            FinanceIntegrationProviderKeys.Fortnox,
            FinanceIntegrationConnectionStatuses.Connected,
            fortnoxConnection.ConnectedByUserId,
            fortnoxConnection.CreatedUtc);

        _dbContext.FinanceIntegrationConnections.Add(connection);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return connection;
    }

    private async Task<FortnoxEntitySyncResult> SyncEntityAsync(
        FinanceIntegrationConnection connection,
        string entityType,
        Func<FinanceIntegrationSyncState, Task<EntityCounters>> sync,
        CancellationToken cancellationToken)
    {
        return await SyncEntityAsync(connection, entityType, requiredScope: null, grantedScopes: null, sync, cancellationToken);
    }

    private async Task<FortnoxEntitySyncResult> SyncEntityAsync(
        FinanceIntegrationConnection connection,
        string entityType,
        string? requiredScope,
        IReadOnlySet<string>? grantedScopes,
        Func<FinanceIntegrationSyncState, Task<EntityCounters>> sync,
        CancellationToken cancellationToken)
    {
        var startedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var state = await GetOrCreateSyncStateAsync(connection, entityType, startedUtc, cancellationToken);
        state.MarkStarted(startedUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (IsMissingGrantedScope(requiredScope, grantedScopes))
        {
            return await MarkEntityBlockedByMissingScopeAsync(state, entityType, requiredScope!, cancellationToken);
        }

        try
        {
            var counters = await sync(state);
            var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var previousCursor = state.Cursor;
            state.MarkSucceeded(counters.NextCursor, completedUtc);
            _diagnostics?.CursorAdvanced(state.CompanyId, state.ConnectionId, entityType, previousCursor, counters.NextCursor);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new FortnoxEntitySyncResult(entityType, counters.Created, counters.Updated, counters.Skipped, counters.Errors);
        }
        catch (Exception exception) when (exception is FortnoxApiException or InvalidOperationException or ArgumentException or DbUpdateException)
        {
            var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var safeMessage = exception is FortnoxApiException apiException
                ? apiException.SafeMessage
                : "Fortnox data could not be synced.";
            var retrySummary = exception is FortnoxApiException { IsTransient: true }
                ? " Fortnox reported a temporary issue; retrying later may succeed."
                : string.Empty;

            state.MarkFailed($"{safeMessage}{retrySummary}", completedUtc);
            _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
                Guid.NewGuid(),
                state.CompanyId,
                state.ConnectionId,
                FinanceIntegrationProviderKeys.Fortnox,
                "entity_sync",
                FinanceIntegrationAuditOutcomes.Failed,
                entityType,
                null,
                null,
                null,
                $"{safeMessage}{retrySummary}",
                completedUtc,
                createdCount: 0,
                updatedCount: 0,
                skippedCount: 0,
                errorCount: 1));

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                exception,
                "Fortnox entity sync failed for company {CompanyId}, connection {ConnectionId}, entity {EntityType}.",
                state.CompanyId,
                state.ConnectionId,
                entityType);

            return new FortnoxEntitySyncResult(entityType, 0, 0, 0, 1, exception is FortnoxApiException { IsTransient: true } ? 1 : 0, string.IsNullOrWhiteSpace(retrySummary) ? null : retrySummary.Trim(), safeMessage);
        }
    }

    private async Task<IReadOnlySet<string>?> ResolveGrantedScopesAsync(
        Guid companyId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var scopes = await _dbContext.FortnoxConnections
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == connectionId)
            .Select(x => x.GrantedScopes)
            .SingleOrDefaultAsync(cancellationToken);

        return scopes is { Count: > 0 }
            ? scopes.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
    }

    private async Task<string?> GetFortnoxReconnectBlockReasonAsync(
        Guid companyId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var connection = await _dbContext.FortnoxConnections
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == connectionId)
            .Select(x => new { x.Status, x.LastErrorSummary })
            .SingleOrDefaultAsync(cancellationToken);

        return connection?.Status is FortnoxConnectionStatus.NeedsReconnect or FortnoxConnectionStatus.Revoked or FortnoxConnectionStatus.Disconnected
            ? connection.LastErrorSummary ?? "Fortnox needs to be reconnected."
            : null;
    }

    private async Task<FortnoxSyncResult> CompleteBlockedSyncAsync(
        RunFortnoxSyncCommand command,
        FinanceIntegrationConnection connection,
        FortnoxSyncHistory history,
        DateTime startedUtc,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var entityResults = new List<FortnoxEntitySyncResult>
        {
            new("connection", 0, 0, 0, 1, ErrorSummary: safeMessage)
        };

        connection.MarkSyncFailed(safeMessage, completedUtc);
        history.MarkCompleted(1, 0, 1, completedUtc, safeMessage);
        history.Metadata["created"] = 0;
        history.Metadata["updated"] = 0;
        history.Metadata["skipped"] = 0;
        history.Metadata["durationMilliseconds"] = (completedUtc - startedUtc).TotalMilliseconds;
        history.Metadata["entities"] = BuildEntityMetadata(entityResults);
        history.Metadata["retryAttempts"] = 0;
        history.Metadata["retryOutcome"] = BuildRetryOutcome(entityResults);
        AddManualSyncAudit(command, connection.Id, FinanceIntegrationAuditOutcomes.Failed, safeMessage, completedUtc, 0, 0, 0, 1, "manual_sync_completed");

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Fortnox sync skipped because the connection needs reconnect. Company {CompanyId}; connection {ConnectionId}.",
            command.CompanyId,
            connection.Id);

        _diagnostics?.SyncCompleted(
            command.CompanyId,
            connection.Id,
            command.CorrelationId,
            FinanceIntegrationSyncStatuses.Failed,
            created: 0,
            updated: 0,
            skipped: 0,
            errors: 1,
            completedUtc - startedUtc);

        return new FortnoxSyncResult(
            command.CompanyId,
            connection.Id,
            startedUtc,
            completedUtc,
            FinanceIntegrationSyncStatuses.Failed,
            0,
            0,
            0,
            1,
            entityResults,
            safeMessage,
            0,
            BuildRetryOutcome(entityResults));
    }

    private async Task<FortnoxEntitySyncResult> MarkEntityBlockedByMissingScopeAsync(
        FinanceIntegrationSyncState state,
        string entityType,
        string requiredScope,
        CancellationToken cancellationToken)
    {
        var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var safeMessage = $"Fortnox did not grant the {requiredScope} permission. Enable the scope in the Fortnox Developer Portal, reconnect Fortnox, and try again.";

        state.MarkFailed(safeMessage, completedUtc);
        state.Metadata["requiredScope"] = requiredScope;
        state.Metadata["scopeState"] = "missing";
        await MarkFortnoxConnectionNeedsReconnectAsync(
            state.CompanyId,
            state.ConnectionId,
            "Fortnox needs to be reconnected so the required sync permissions can be granted.",
            completedUtc,
            cancellationToken);

        _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            state.CompanyId,
            state.ConnectionId,
            FinanceIntegrationProviderKeys.Fortnox,
            "entity_sync",
            FinanceIntegrationAuditOutcomes.Failed,
            entityType,
            null,
            null,
            null,
            safeMessage,
            completedUtc,
            createdCount: 0,
            updatedCount: 0,
            skippedCount: 0,
            errorCount: 1));

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Fortnox entity sync skipped because the connected token is missing scope {RequiredScope}. Company {CompanyId}; connection {ConnectionId}; entity {EntityType}.",
            requiredScope,
            state.CompanyId,
            state.ConnectionId,
            entityType);

        return new FortnoxEntitySyncResult(entityType, 0, 0, 0, 1, ErrorSummary: safeMessage);
    }

    private async Task MarkFortnoxConnectionNeedsReconnectAsync(
        Guid companyId,
        Guid connectionId,
        string safeReason,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var connection = await _dbContext.FortnoxConnections
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == connectionId, cancellationToken);

        if (connection is null || connection.Status == FortnoxConnectionStatus.NeedsReconnect)
        {
            return;
        }

        connection.SetStatus(FortnoxConnectionStatus.NeedsReconnect, safeReason, nowUtc);
    }

    private static bool IsMissingGrantedScope(string? requiredScope, IReadOnlySet<string>? grantedScopes) =>
        !string.IsNullOrWhiteSpace(requiredScope) &&
        grantedScopes is { Count: > 0 } &&
        !grantedScopes.Contains(requiredScope);

    private async Task<EntityCounters> SyncCompanyInformationAsync(
        FortnoxRequestContext context,
        FinanceIntegrationSyncState state,
        CancellationToken cancellationToken)
    {
        var companyInformation = await _apiClient.GetCompanyInformationAsync(context, cancellationToken);
        var externalId =
            FirstNonEmpty(
                companyInformation.DatabaseNumber,
                companyInformation.OrganizationNumber,
                companyInformation.CompanyName)
            ?? "fortnox-company-information";

        var counters = new EntityCounters();
        counters.Add(await UpsertReferenceOnlyAsync(
            context.CompanyId,
            context.ConnectionId!.Value,
            "company_information",
            externalId,
            companyInformation.CompanyName ?? externalId,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken));
        counters.NextCursor = _timeProvider.GetUtcNow().UtcDateTime.ToString("O");
        return counters;
    }

    private async Task<EntityCounters> SyncAccountsAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, bool fullSync, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetAccountsAsync(context, options, cancellationToken),
            async account => await UpsertAccountAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapAccount(account), cancellationToken),
            fullSync,
            cancellationToken);

    private async Task<EntityCounters> SyncCustomersAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, bool fullSync, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetCustomersAsync(context, options, cancellationToken),
            async customer => await UpsertCounterpartyAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapCustomer(customer), cancellationToken),
            fullSync,
            cancellationToken);

    private async Task<EntityCounters> SyncSuppliersAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, bool fullSync, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetSuppliersAsync(context, options, cancellationToken),
            async supplier => await UpsertCounterpartyAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapSupplier(supplier), cancellationToken),
            fullSync,
            cancellationToken);

    private async Task<EntityCounters> SyncArticlesAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, bool fullSync, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetArticlesAsync(context, options, cancellationToken),
            async article => await UpsertArticleAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapArticle(article), cancellationToken),
            fullSync,
            cancellationToken);

    private async Task<EntityCounters> SyncProjectsAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, bool fullSync, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetProjectsAsync(context, options, cancellationToken),
            async project => await UpsertProjectAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapProject(project), cancellationToken),
            fullSync,
            cancellationToken);

    private async Task<EntityCounters> SyncInvoicesAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, bool fullSync, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetInvoicesAsync(context, options, cancellationToken),
            async invoice => await UpsertInvoiceAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapInvoice(invoice), cancellationToken),
            fullSync,
            cancellationToken);

    private async Task<EntityCounters> SyncSupplierInvoicesAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, bool fullSync, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetSupplierInvoicesAsync(context, options, cancellationToken),
            async invoice => await UpsertSupplierInvoiceAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapSupplierInvoice(invoice), cancellationToken),
            fullSync,
            cancellationToken);

    private async Task<EntityCounters> SyncVouchersAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, bool fullSync, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetVouchersAsync(context, options, cancellationToken),
            async voucher => await UpsertVoucherAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapVoucher(voucher), cancellationToken),
            fullSync,
            cancellationToken);

    private async Task<EntityCounters> SyncPaymentActivityAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, CancellationToken cancellationToken)
    {
        var counters = new EntityCounters { NextCursor = state.Cursor };
        var paidInvoices = await _dbContext.FinanceInvoices
            .Where(x => x.CompanyId == context.CompanyId && x.SettlementStatus == FinanceSettlementStatuses.Paid)
            .ToListAsync(cancellationToken);
        var paidBills = await _dbContext.FinanceBills
            .Where(x => x.CompanyId == context.CompanyId && x.SettlementStatus == FinanceSettlementStatuses.Paid)
            .ToListAsync(cancellationToken);

        foreach (var invoice in paidInvoices)
        {
            var payment = new Payment(
                Guid.NewGuid(),
                context.CompanyId,
                PaymentTypes.Incoming,
                Math.Abs(invoice.Amount),
                invoice.Currency,
                invoice.DueUtc,
                "bank_transfer",
                PaymentStatuses.Completed,
                invoice.InvoiceNumber,
                _timeProvider.GetUtcNow().UtcDateTime);

            counters.Add(await UpsertPaymentAsync(context.CompanyId, context.ConnectionId!.Value, $"invoice-payment-{invoice.InvoiceNumber}", invoice.InvoiceNumber, payment, cancellationToken));
        }

        foreach (var bill in paidBills)
        {
            var payment = new Payment(
                Guid.NewGuid(),
                context.CompanyId,
                PaymentTypes.Outgoing,
                Math.Abs(bill.Amount),
                bill.Currency,
                bill.DueUtc,
                "bank_transfer",
                PaymentStatuses.Completed,
                bill.BillNumber,
                _timeProvider.GetUtcNow().UtcDateTime);

            counters.Add(await UpsertPaymentAsync(context.CompanyId, context.ConnectionId!.Value, $"bill-payment-{bill.BillNumber}", bill.BillNumber, payment, cancellationToken));
        }

        counters.NextCursor = _timeProvider.GetUtcNow().UtcDateTime.ToString("O");
        return counters;
    }

    private async Task<EntityCounters> SyncPagedAsync<T>(
        FinanceIntegrationSyncState state,
        Func<FortnoxPageOptions, Task<FortnoxPagedResponse<T>>> fetchPage,
        Func<T, Task<SyncMutationResult>> upsert,
        bool fullSync,
        CancellationToken cancellationToken)
    {
        var counters = new EntityCounters();
        var cursor = fullSync ? null : ParseCursor(state.Cursor);
        var processedPages = new HashSet<int>();
        var page = 1;
        DateTime? maxExternalUpdatedUtc = cursor;

        while (true)
        {
            var options = new FortnoxPageOptions(
                LastModified: cursor.HasValue ? new DateTimeOffset(cursor.Value, TimeSpan.Zero) : null,
                Page: page,
                Limit: PageSize);

            var response = await fetchPage(options);
            var currentPage = response.Metadata.CurrentPage ?? page;
            if (!processedPages.Add(currentPage))
            {
                throw new FortnoxApiException("Fortnox returned duplicate pagination data.", HttpStatusCode.BadGateway, "invalid_response");
            }

            if (HasInvalidPageMetadata(response))
            {
                throw new FortnoxApiException("Fortnox returned invalid pagination data.", HttpStatusCode.BadGateway, "invalid_response");
            }

            foreach (var item in response.Items)
            {
                var result = await upsert(item);
                counters.Add(result);
                if (result.Skipped)
                {
                    _diagnostics?.DuplicateSkipped(state.CompanyId, state.ConnectionId, state.EntityType);
                }
                if (result.ExternalUpdatedUtc.HasValue && (!maxExternalUpdatedUtc.HasValue || result.ExternalUpdatedUtc.Value > maxExternalUpdatedUtc.Value))
                {
                    maxExternalUpdatedUtc = result.ExternalUpdatedUtc.Value;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (!response.HasNextPage)
            {
                break;
            }

            page++;
            if (page > 10000)
            {
                throw new FortnoxApiException("Fortnox returned too many pages to sync safely.", HttpStatusCode.BadGateway, "invalid_response");
            }
        }

        counters.NextCursor = maxExternalUpdatedUtc?.ToString("O") ?? state.Cursor ?? _timeProvider.GetUtcNow().UtcDateTime.ToString("O");
        return counters;
    }

    private async Task<SyncMutationResult> UpsertCounterpartyAsync(Guid companyId, Guid connectionId, FortnoxCounterpartySyncModel model, CancellationToken cancellationToken)
    {
        var existing = await FindReferenceAsync(companyId, connectionId, model.CounterpartyType, model.ExternalId, cancellationToken);
        if (MarkReferenceSyncedIfCurrent(existing, model.ExternalUpdatedUtc)) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        FinanceCounterparty counterparty;
        if (existing is null)
        {
            counterparty = new FinanceCounterparty(Guid.NewGuid(), companyId, model.Name, model.CounterpartyType, model.Email, taxId: model.TaxId);
            _dbContext.FinanceCounterparties.Add(counterparty);
            await AddReferenceAsync(companyId, connectionId, model.CounterpartyType, counterparty.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }

        counterparty = await _dbContext.FinanceCounterparties.SingleOrDefaultAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        if (counterparty is null)
        {
            counterparty = new FinanceCounterparty(Guid.NewGuid(), companyId, model.Name, model.CounterpartyType, model.Email, taxId: model.TaxId);
            _dbContext.FinanceCounterparties.Add(counterparty);
            existing.RepointToInternalRecord(counterparty.Id, model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
            AttachSource(counterparty, model.CounterpartyType, model.ExternalId, existing.Id);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }
        counterparty.UpdateMasterData(model.Name, model.CounterpartyType, model.Email, taxId: model.TaxId);
        existing.Refresh(model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        AttachSource(counterparty, model.CounterpartyType, model.ExternalId, existing.Id);
        return SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
    }

    private async Task<SyncMutationResult> UpsertAccountAsync(Guid companyId, Guid connectionId, FortnoxAccountSyncModel model, CancellationToken cancellationToken)
    {
        model = NormalizeAccountModel(model);
        var existing = await FindReferenceAsync(companyId, connectionId, "account", model.ExternalId, cancellationToken);
        if (MarkReferenceSyncedIfCurrent(existing, model.ExternalUpdatedUtc)) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        FinanceAccount account;
        if (existing is null)
        {
            var created = false;
            account = await FindFinanceAccountByCodeAsync(companyId, model.Code, cancellationToken);
            if (account is null)
            {
                account = new FinanceAccount(Guid.NewGuid(), companyId, model.Code, model.Name, model.AccountType, "SEK", 0m, _timeProvider.GetUtcNow().UtcDateTime);
                _dbContext.FinanceAccounts.Add(account);
                created = true;
            }
            else
            {
                account.ApplySyncedSnapshot(model.Code, model.Name, model.AccountType, "SEK", account.OpeningBalance, account.OpenedUtc, _timeProvider.GetUtcNow().UtcDateTime);
            }

            await AddReferenceAsync(companyId, connectionId, "account", account.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return created
                ? SyncMutationResult.FromCreated(model.ExternalUpdatedUtc)
                : SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
        }

        account = await _dbContext.FinanceAccounts.SingleOrDefaultAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        if (account is null)
        {
            account = await FindFinanceAccountByCodeAsync(companyId, model.Code, cancellationToken);
            if (account is null)
            {
                account = new FinanceAccount(Guid.NewGuid(), companyId, model.Code, model.Name, model.AccountType, "SEK", 0m, _timeProvider.GetUtcNow().UtcDateTime);
                _dbContext.FinanceAccounts.Add(account);
            }
            else
            {
                account.ApplySyncedSnapshot(model.Code, model.Name, model.AccountType, "SEK", account.OpeningBalance, account.OpenedUtc, _timeProvider.GetUtcNow().UtcDateTime);
            }

            existing.RepointToInternalRecord(account.Id, model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
            AttachSource(account, "account", model.ExternalId, existing.Id);
            return SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
        }
        account.ApplySyncedSnapshot(model.Code, model.Name, model.AccountType, "SEK", account.OpeningBalance, account.OpenedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        existing.Refresh(model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        AttachSource(account, "account", model.ExternalId, existing.Id);
        return SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
    }

    private async Task<SyncMutationResult> UpsertArticleAsync(Guid companyId, Guid connectionId, FortnoxArticleSyncModel model, CancellationToken cancellationToken) =>
        await UpsertReferenceOnlyAsync(companyId, connectionId, "article", model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);

    private async Task<SyncMutationResult> UpsertProjectAsync(Guid companyId, Guid connectionId, FortnoxProjectSyncModel model, CancellationToken cancellationToken) =>
        await UpsertReferenceOnlyAsync(companyId, connectionId, "project", model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);

    private async Task<SyncMutationResult> UpsertInvoiceAsync(Guid companyId, Guid connectionId, FortnoxInvoiceSyncModel model, CancellationToken cancellationToken)
    {
        var existing = await FindReferenceAsync(companyId, connectionId, "invoice", model.ExternalId, cancellationToken);
        if (MarkReferenceSyncedIfCurrent(existing, model.ExternalUpdatedUtc)) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        var counterparty = await EnsureCounterpartyAsync(companyId, connectionId, "customer", model.CustomerNumber, model.CustomerName, cancellationToken);
        FinanceInvoice invoice;
        if (existing is null)
        {
            invoice = new FinanceInvoice(Guid.NewGuid(), companyId, counterparty.Id, model.ExternalNumber, model.IssuedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, settlementStatus: model.SettlementStatus);
            _dbContext.FinanceInvoices.Add(invoice);
            await AddReferenceAsync(companyId, connectionId, "invoice", invoice.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }

        invoice = await _dbContext.FinanceInvoices.SingleOrDefaultAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        if (invoice is null)
        {
            invoice = new FinanceInvoice(Guid.NewGuid(), companyId, counterparty.Id, model.ExternalNumber, model.IssuedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, settlementStatus: model.SettlementStatus);
            _dbContext.FinanceInvoices.Add(invoice);
            existing.RepointToInternalRecord(invoice.Id, model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
            AttachSource(invoice, "invoice", model.ExternalId, existing.Id);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }
        invoice.ApplySyncedSnapshot(counterparty.Id, model.IssuedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, model.SettlementStatus);
        existing.Refresh(model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        AttachSource(invoice, "invoice", model.ExternalId, existing.Id);
        return SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
    }

    private async Task<SyncMutationResult> UpsertSupplierInvoiceAsync(Guid companyId, Guid connectionId, FortnoxSupplierInvoiceSyncModel model, CancellationToken cancellationToken)
    {
        var existing = await FindReferenceAsync(companyId, connectionId, "supplier_invoice", model.ExternalId, cancellationToken);
        if (MarkReferenceSyncedIfCurrent(existing, model.ExternalUpdatedUtc)) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        var counterparty = await EnsureCounterpartyAsync(companyId, connectionId, "supplier", model.SupplierNumber, model.SupplierName, cancellationToken);
        FinanceBill bill;
        if (existing is null)
        {
            bill = new FinanceBill(Guid.NewGuid(), companyId, counterparty.Id, model.ExternalNumber, model.ReceivedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, settlementStatus: model.SettlementStatus);
            _dbContext.FinanceBills.Add(bill);
            await AddReferenceAsync(companyId, connectionId, "supplier_invoice", bill.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }

        bill = await _dbContext.FinanceBills.SingleOrDefaultAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        if (bill is null)
        {
            bill = new FinanceBill(Guid.NewGuid(), companyId, counterparty.Id, model.ExternalNumber, model.ReceivedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, settlementStatus: model.SettlementStatus);
            _dbContext.FinanceBills.Add(bill);
            existing.RepointToInternalRecord(bill.Id, model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
            AttachSource(bill, "supplier_invoice", model.ExternalId, existing.Id);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }
        bill.ApplySyncedSnapshot(counterparty.Id, model.ReceivedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, model.SettlementStatus);
        existing.Refresh(model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        AttachSource(bill, "supplier_invoice", model.ExternalId, existing.Id);
        return SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
    }

    private async Task<SyncMutationResult> UpsertVoucherAsync(Guid companyId, Guid connectionId, FortnoxVoucherSyncModel model, CancellationToken cancellationToken)
    {
        var account = await _dbContext.FinanceAccounts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == "1930", cancellationToken) ?? await EnsureSystemAccountAsync(companyId, cancellationToken);
        var existing = await FindReferenceAsync(companyId, connectionId, "voucher", model.ExternalId, cancellationToken);
        if (MarkReferenceSyncedIfCurrent(existing, model.ExternalUpdatedUtc)) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        FinanceTransaction transaction;
        if (existing is null)
        {
            transaction = new FinanceTransaction(Guid.NewGuid(), companyId, account.Id, null, null, null, model.TransactionUtc, "voucher", model.Amount, "SEK", model.Description, model.ExternalNumber);
            _dbContext.FinanceTransactions.Add(transaction);
            await AddReferenceAsync(companyId, connectionId, "voucher", transaction.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }

        transaction = await _dbContext.FinanceTransactions.SingleOrDefaultAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        if (transaction is null)
        {
            transaction = new FinanceTransaction(Guid.NewGuid(), companyId, account.Id, null, null, null, model.TransactionUtc, "voucher", model.Amount, "SEK", model.Description, model.ExternalNumber);
            _dbContext.FinanceTransactions.Add(transaction);
            existing.RepointToInternalRecord(transaction.Id, model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
            AttachSource(transaction, "voucher", model.ExternalId, existing.Id);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }
        transaction.ChangeCategory("voucher");
        existing.Refresh(model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        AttachSource(transaction, "voucher", model.ExternalId, existing.Id);
        return SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
    }

    private async Task<SyncMutationResult> UpsertPaymentAsync(Guid companyId, Guid connectionId, string externalId, string externalNumber, Payment incoming, CancellationToken cancellationToken)
    {
        var existing = await FindReferenceAsync(companyId, connectionId, "payment", externalId, cancellationToken);
        if (existing is null)
        {
            _dbContext.Payments.Add(incoming);
            await AddReferenceAsync(companyId, connectionId, "payment", incoming.Id, externalId, externalNumber, null, cancellationToken);
            return SyncMutationResult.FromCreated(null);
        }

        var payment = await _dbContext.Payments.SingleOrDefaultAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        if (payment is null)
        {
            _dbContext.Payments.Add(incoming);
            existing.RepointToInternalRecord(incoming.Id, externalNumber, null, _timeProvider.GetUtcNow().UtcDateTime);
            return SyncMutationResult.FromCreated(null);
        }
        existing.MarkSynced(_timeProvider.GetUtcNow().UtcDateTime);
        AttachSource(payment, "payment", externalId, existing.Id);
        return SyncMutationResult.FromSkipped(null);
    }

    private async Task<SyncMutationResult> UpsertReferenceOnlyAsync(Guid companyId, Guid connectionId, string entityType, string externalId, string externalNumber, DateTime? externalUpdatedUtc, CancellationToken cancellationToken)
    {
        var existing = await FindReferenceAsync(companyId, connectionId, entityType, externalId, cancellationToken);
        if (MarkReferenceSyncedIfCurrent(existing, externalUpdatedUtc)) return SyncMutationResult.FromSkipped(externalUpdatedUtc);

        if (existing is null)
        {
            var placeholder = await EnsureSystemAccountAsync(companyId, cancellationToken);
            await AddReferenceAsync(companyId, connectionId, entityType, placeholder.Id, externalId, externalNumber, externalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(externalUpdatedUtc);
        }

        existing.Refresh(externalNumber, externalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        return SyncMutationResult.FromUpdated(externalUpdatedUtc);
    }

    private async Task<FinanceCounterparty> EnsureCounterpartyAsync(Guid companyId, Guid connectionId, string type, string externalNumber, string name, CancellationToken cancellationToken)
    {
        var reference = await FindReferenceAsync(companyId, connectionId, type, externalNumber, cancellationToken);
        if (reference is not null)
        {
            var existing = await _dbContext.FinanceCounterparties.SingleOrDefaultAsync(x => x.Id == reference.InternalRecordId && x.CompanyId == companyId, cancellationToken);
            if (existing is not null)
            {
                reference.MarkSynced(_timeProvider.GetUtcNow().UtcDateTime);
                return existing;
            }

            var repaired = new FinanceCounterparty(Guid.NewGuid(), companyId, name, type);
            _dbContext.FinanceCounterparties.Add(repaired);
            reference.RepointToInternalRecord(repaired.Id, externalNumber, null, _timeProvider.GetUtcNow().UtcDateTime);
            return repaired;
        }

        var counterparty = new FinanceCounterparty(Guid.NewGuid(), companyId, name, type);
        _dbContext.FinanceCounterparties.Add(counterparty);
        await AddReferenceAsync(companyId, connectionId, type, counterparty.Id, externalNumber, externalNumber, null, cancellationToken);
        return counterparty;
    }

    private bool MarkReferenceSyncedIfCurrent(FinanceExternalReference? reference, DateTime? externalUpdatedUtc)
    {
        if (reference?.IsCurrent(externalUpdatedUtc) != true)
        {
            return false;
        }

        reference.MarkSynced(_timeProvider.GetUtcNow().UtcDateTime);
        return true;
    }

    private async Task<FinanceAccount> EnsureSystemAccountAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.FinanceAccounts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == "FORTNOX", cancellationToken);
        if (account is not null) return account;

        account = new FinanceAccount(Guid.NewGuid(), companyId, "FORTNOX", "Fortnox synced reference", "integration", "SEK", 0m, _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.FinanceAccounts.Add(account);
        return account;
    }

    private async Task<FinanceExternalReference?> FindReferenceAsync(Guid companyId, Guid connectionId, string entityType, string externalId, CancellationToken cancellationToken)
    {
        var tracked = _dbContext.FinanceExternalReferences.Local.SingleOrDefault(
            x => x.CompanyId == companyId &&
                 x.ConnectionId == connectionId &&
                 x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                 x.EntityType == entityType &&
                 x.ExternalId == externalId);
        if (tracked is not null)
        {
            return tracked;
        }

        return await _dbContext.FinanceExternalReferences.SingleOrDefaultAsync(
            x => x.CompanyId == companyId &&
                 x.ConnectionId == connectionId &&
                 x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                 x.EntityType == entityType &&
                 x.ExternalId == externalId,
            cancellationToken);
    }

    private async Task<FinanceAccount?> FindFinanceAccountByCodeAsync(Guid companyId, string code, CancellationToken cancellationToken)
    {
        var tracked = _dbContext.FinanceAccounts.Local.SingleOrDefault(x => x.CompanyId == companyId && x.Code == code);
        return tracked ?? await _dbContext.FinanceAccounts.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Code == code, cancellationToken);
    }

    private static FortnoxAccountSyncModel NormalizeAccountModel(FortnoxAccountSyncModel model)
    {
        var code = string.IsNullOrWhiteSpace(model.Code) ? model.ExternalNumber : model.Code;
        code = string.IsNullOrWhiteSpace(code) ? model.ExternalId : code.Trim();
        var name = string.IsNullOrWhiteSpace(model.Name) ? $"Account {code}" : model.Name.Trim();
        var accountType = string.IsNullOrWhiteSpace(model.AccountType) ? InferAccountType(code) : model.AccountType.Trim();

        return model with
        {
            ExternalId = TrimTo(model.ExternalId, 256),
            ExternalNumber = TrimTo(string.IsNullOrWhiteSpace(model.ExternalNumber) ? code : model.ExternalNumber, 128),
            Code = TrimTo(code, 32),
            Name = TrimTo(name, 160),
            AccountType = TrimTo(accountType, 64)
        };
    }

    private static string InferAccountType(string code) =>
        int.TryParse(code, out var number)
            ? number switch
            {
                >= 1000 and < 2000 => "asset",
                >= 2000 and < 3000 => "liability",
                >= 3000 and < 4000 => "revenue",
                _ => "expense"
            }
            : "expense";

    private static string TrimTo(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private async Task AddReferenceAsync(Guid companyId, Guid connectionId, string entityType, Guid internalRecordId, string externalId, string? externalNumber, DateTime? externalUpdatedUtc, CancellationToken cancellationToken)
    {
        var reference = new FinanceExternalReference(Guid.NewGuid(), companyId, connectionId, FinanceIntegrationProviderKeys.Fortnox, entityType, internalRecordId, externalId, externalNumber, externalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.FinanceExternalReferences.Add(reference);
        AttachSource(internalRecordId, entityType, externalId, reference.Id);
        await Task.CompletedTask;
    }

    private void AttachSource(Guid internalRecordId, string entityType, string externalId, Guid referenceId)
    {
        var tracked = _dbContext.ChangeTracker.Entries()
            .FirstOrDefault(entry => entry.Metadata.FindProperty("FinanceExternalReferenceId") is not null &&
                                     entry.Properties.Any(property => property.Metadata.Name == "Id" && property.CurrentValue is Guid id && id == internalRecordId));
        if (tracked is not null) AttachSource(tracked.Entity, entityType, externalId, referenceId);
    }

    private void AttachSource(object entity, string entityType, string externalId, Guid referenceId)
    {
        var entry = _dbContext.Entry(entity);
        if (entry.Metadata.FindProperty("SourceType") is not null) entry.Property("SourceType").CurrentValue = FinanceRecordSourceTypes.Fortnox;
        if (entry.Metadata.FindProperty("ProviderKey") is not null) entry.Property("ProviderKey").CurrentValue = FinanceIntegrationProviderKeys.Fortnox;
        if (entry.Metadata.FindProperty("ProviderExternalId") is not null) entry.Property("ProviderExternalId").CurrentValue = externalId;
        if (entry.Metadata.FindProperty("FinanceExternalReferenceId") is not null) entry.Property("FinanceExternalReferenceId").CurrentValue = referenceId;
    }

    private async Task<FinanceIntegrationSyncState> GetOrCreateSyncStateAsync(FinanceIntegrationConnection connection, string entityType, DateTime now, CancellationToken cancellationToken)
    {
        var state = await _dbContext.FinanceIntegrationSyncStates.SingleOrDefaultAsync(
            x => x.CompanyId == connection.CompanyId &&
                 x.ConnectionId == connection.Id &&
                 x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                 x.EntityType == entityType &&
                 x.ScopeKey == ScopeKey,
            cancellationToken);

        if (state is not null) return state;

        state = new FinanceIntegrationSyncState(Guid.NewGuid(), connection.CompanyId, connection.Id, FinanceIntegrationProviderKeys.Fortnox, entityType, ScopeKey, now);
        _dbContext.FinanceIntegrationSyncStates.Add(state);
        return state;
    }

    private static DateTime? ParseCursor(string? cursor) =>
        DateTimeOffset.TryParse(cursor, out var parsed) ? parsed.UtcDateTime : null;

    private static bool HasInvalidPageMetadata<T>(FortnoxPagedResponse<T> response)
    {
        if (!response.Metadata.TotalPages.HasValue || !response.Metadata.CurrentPage.HasValue)
        {
            return false;
        }

        if (response.Metadata.CurrentPage.Value <= response.Metadata.TotalPages.Value)
        {
            return false;
        }

        return response.Items.Count > 0 ||
            response.Metadata.TotalPages.Value != 0 ||
            response.Metadata.TotalResources.GetValueOrDefault() != 0;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string BuildHistorySummary(IReadOnlyCollection<FortnoxEntitySyncResult> results) =>
        $"Created {results.Sum(x => x.Created)}, updated {results.Sum(x => x.Updated)}, skipped {results.Sum(x => x.Skipped)}, errors {results.Sum(x => x.Errors)}.";

    private static JsonArray BuildEntityMetadata(IEnumerable<FortnoxEntitySyncResult> results)
    {
        var array = new JsonArray();
        foreach (var result in results)
        {
            array.Add(new JsonObject
            {
                ["entityType"] = result.EntityType,
                ["created"] = result.Created,
                ["updated"] = result.Updated,
                ["skipped"] = result.Skipped,
                ["errors"] = result.Errors,
                ["retryAttempts"] = result.RetryAttempts,
                ["retryOutcome"] = result.RetryOutcome,
                ["errorSummary"] = result.ErrorSummary
            });
        }

        return array;
    }

    private static int ReadInt(JsonObject metadata, string key) =>
        metadata.TryGetPropertyValue(key, out var node) && node is not null && int.TryParse(node.ToString(), out var value)
            ? value
            : 0;

    private static string? ReadString(JsonObject metadata, string key) =>
        metadata.TryGetPropertyValue(key, out var node) && node is not null
            ? node.ToString()
            : null;

    private static IReadOnlyList<FortnoxEntitySyncResult> ReadEntityMetadata(JsonObject metadata)
    {
        if (!metadata.TryGetPropertyValue("entities", out var node) ||
            node is not JsonArray entities)
        {
            return [];
        }

        var results = new List<FortnoxEntitySyncResult>();
        foreach (var item in entities.OfType<JsonObject>())
        {
            var entityType = ReadString(item, "entityType");
            if (string.IsNullOrWhiteSpace(entityType))
            {
                continue;
            }

            results.Add(new FortnoxEntitySyncResult(
                entityType,
                ReadInt(item, "created"),
                ReadInt(item, "updated"),
                ReadInt(item, "skipped"),
                ReadInt(item, "errors"),
                ReadInt(item, "retryAttempts"),
                ReadString(item, "retryOutcome"),
                ReadString(item, "errorSummary")));
        }

        return results;
    }

    private static string BuildRetryOutcome(IReadOnlyCollection<FortnoxEntitySyncResult> results) =>
        results.Sum(x => x.RetryAttempts) == 0 ? "No retry was needed." : "One or more Fortnox requests needed retry handling.";

    private static string BuildHistoryItemSummary(FortnoxSyncHistory history)
    {
        var created = ReadInt(history.Metadata, "created");
        var updated = ReadInt(history.Metadata, "updated");
        var skipped = ReadInt(history.Metadata, "skipped");
        return $"Created {created}, updated {updated}, skipped {skipped}, errors {history.RecordsFailed}.";
    }

    private sealed class EntityCounters
    {
        public int Created { get; private set; }
        public int Updated { get; private set; }
        public int Skipped { get; private set; }
        public int Errors { get; private set; }
        public string? NextCursor { get; set; }

        public void Add(SyncMutationResult result)
        {
            Created += result.Created ? 1 : 0;
            Updated += result.Updated ? 1 : 0;
            Skipped += result.Skipped ? 1 : 0;
            Errors += result.Error ? 1 : 0;
        }
    }

    private sealed record SyncMutationResult(bool Created, bool Updated, bool Skipped, bool Error, DateTime? ExternalUpdatedUtc)
    {
        public static SyncMutationResult FromCreated(DateTime? externalUpdatedUtc) => new(true, false, false, false, externalUpdatedUtc);
        public static SyncMutationResult FromUpdated(DateTime? externalUpdatedUtc) => new(false, true, false, false, externalUpdatedUtc);
        public static SyncMutationResult FromSkipped(DateTime? externalUpdatedUtc) => new(false, false, true, false, externalUpdatedUtc);
    }
}
