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
        var context = new FortnoxRequestContext(command.CompanyId, connection.Id, command.CorrelationId);
        var entityResults = new List<FortnoxEntitySyncResult>();
        _diagnostics?.SyncStarted(command.CompanyId, connection.Id, command.CorrelationId);

        _logger.LogInformation(
            "Starting Fortnox sync for company {CompanyId}, connection {ConnectionId}, correlation {CorrelationId}.",
            command.CompanyId,
            connection.Id,
            command.CorrelationId);

        entityResults.Add(await SyncEntityAsync(connection, "accounts", state => SyncAccountsAsync(context, state, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "customers", state => SyncCustomersAsync(context, state, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "suppliers", state => SyncSuppliersAsync(context, state, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "articles", state => SyncArticlesAsync(context, state, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "projects", state => SyncProjectsAsync(context, state, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "invoices", state => SyncInvoicesAsync(context, state, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "supplier_invoices", state => SyncSupplierInvoicesAsync(context, state, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "vouchers", state => SyncVouchersAsync(context, state, cancellationToken), cancellationToken));
        entityResults.Add(await SyncEntityAsync(connection, "payments", state => SyncPaymentActivityAsync(context, state, cancellationToken), cancellationToken));

        var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var errors = entityResults.Sum(x => x.Errors);
        var status = errors == 0 ? FinanceIntegrationSyncStatuses.Succeeded : FinanceIntegrationSyncStatuses.Failed;
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

        _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            command.CompanyId,
            connection.Id,
            FinanceIntegrationProviderKeys.Fortnox,
            "manual_sync",
            errors == 0 ? FinanceIntegrationAuditOutcomes.Succeeded : FinanceIntegrationAuditOutcomes.Failed,
            null,
            null,
            null,
            command.CorrelationId,
            BuildHistorySummary(entityResults),
            completedUtc,
            entityResults.Sum(x => x.Created),
            entityResults.Sum(x => x.Updated),
            entityResults.Sum(x => x.Skipped),
            errors));

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
            errorSummary);
    }

    public async Task<FortnoxSyncHistoryResult> GetHistoryAsync(GetFortnoxSyncHistoryQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit <= 0 ? 25 : Math.Min(query.Limit, 100);
        var events = await _dbContext.FinanceIntegrationAuditEvents
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == query.CompanyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.EventType == "manual_sync")
            .OrderByDescending(x => x.CreatedUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = events.Select(x => new FortnoxSyncHistoryItem(
                x.Id,
                x.ConnectionId,
                x.CreatedUtc,
                x.CreatedUtc,
                x.Outcome,
                x.CreatedCount,
                x.UpdatedCount,
                x.SkippedCount,
                x.ErrorCount,
                string.IsNullOrWhiteSpace(x.Summary) ? "Fortnox sync completed." : x.Summary!,
                x.Outcome == FinanceIntegrationAuditOutcomes.Failed ? x.Summary : null))
            .ToList();

        return new FortnoxSyncHistoryResult(query.CompanyId, items);
    }

    private async Task<FinanceIntegrationConnection> ResolveConnectionAsync(RunFortnoxSyncCommand command, CancellationToken cancellationToken)
    {
        var query = _dbContext.FinanceIntegrationConnections
            .Where(x => x.CompanyId == command.CompanyId && x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox);

        if (command.ConnectionId.HasValue)
        {
            query = query.Where(x => x.Id == command.ConnectionId.Value);
        }

        return await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No connected Fortnox integration was found for this company.");
    }

    private async Task<FortnoxEntitySyncResult> SyncEntityAsync(
        FinanceIntegrationConnection connection,
        string entityType,
        Func<FinanceIntegrationSyncState, Task<EntityCounters>> sync,
        CancellationToken cancellationToken)
    {
        var startedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var state = await GetOrCreateSyncStateAsync(connection, entityType, startedUtc, cancellationToken);
        state.MarkStarted(startedUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

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

            state.MarkFailed(safeMessage, completedUtc);
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
                exception,
                "Fortnox entity sync failed for company {CompanyId}, connection {ConnectionId}, entity {EntityType}.",
                state.CompanyId,
                state.ConnectionId,
                entityType);

            return new FortnoxEntitySyncResult(entityType, 0, 0, 0, 1, safeMessage);
        }
    }

    private async Task<EntityCounters> SyncAccountsAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetAccountsAsync(context, options, cancellationToken),
            async account => await UpsertAccountAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapAccount(account), cancellationToken),
            cancellationToken);

    private async Task<EntityCounters> SyncCustomersAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetCustomersAsync(context, options, cancellationToken),
            async customer => await UpsertCounterpartyAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapCustomer(customer), cancellationToken),
            cancellationToken);

    private async Task<EntityCounters> SyncSuppliersAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetSuppliersAsync(context, options, cancellationToken),
            async supplier => await UpsertCounterpartyAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapSupplier(supplier), cancellationToken),
            cancellationToken);

    private async Task<EntityCounters> SyncArticlesAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetArticlesAsync(context, options, cancellationToken),
            async article => await UpsertArticleAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapArticle(article), cancellationToken),
            cancellationToken);

    private async Task<EntityCounters> SyncProjectsAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetProjectsAsync(context, options, cancellationToken),
            async project => await UpsertProjectAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapProject(project), cancellationToken),
            cancellationToken);

    private async Task<EntityCounters> SyncInvoicesAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetInvoicesAsync(context, options, cancellationToken),
            async invoice => await UpsertInvoiceAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapInvoice(invoice), cancellationToken),
            cancellationToken);

    private async Task<EntityCounters> SyncSupplierInvoicesAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetSupplierInvoicesAsync(context, options, cancellationToken),
            async invoice => await UpsertSupplierInvoiceAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapSupplierInvoice(invoice), cancellationToken),
            cancellationToken);

    private async Task<EntityCounters> SyncVouchersAsync(FortnoxRequestContext context, FinanceIntegrationSyncState state, CancellationToken cancellationToken) =>
        await SyncPagedAsync(
            state,
            options => _apiClient.GetVouchersAsync(context, options, cancellationToken),
            async voucher => await UpsertVoucherAsync(context.CompanyId, context.ConnectionId!.Value, _mappingService.MapVoucher(voucher), cancellationToken),
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
        CancellationToken cancellationToken)
    {
        var counters = new EntityCounters();
        var cursor = ParseCursor(state.Cursor);
        var page = 1;
        DateTime? maxExternalUpdatedUtc = cursor;

        while (true)
        {
            var options = new FortnoxPageOptions(
                LastModified: cursor.HasValue ? new DateTimeOffset(cursor.Value, TimeSpan.Zero) : null,
                SortBy: "lastmodified",
                SortOrder: "ascending",
                Page: page,
                Limit: PageSize);

            var response = await fetchPage(options);
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
        }

        counters.NextCursor = maxExternalUpdatedUtc?.ToString("O") ?? state.Cursor ?? _timeProvider.GetUtcNow().UtcDateTime.ToString("O");
        return counters;
    }

    private async Task<SyncMutationResult> UpsertCounterpartyAsync(Guid companyId, Guid connectionId, FortnoxCounterpartySyncModel model, CancellationToken cancellationToken)
    {
        var existing = await FindReferenceAsync(companyId, connectionId, model.CounterpartyType, model.ExternalId, cancellationToken);
        if (existing?.IsCurrent(model.ExternalUpdatedUtc) == true) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        FinanceCounterparty counterparty;
        if (existing is null)
        {
            counterparty = new FinanceCounterparty(Guid.NewGuid(), companyId, model.Name, model.CounterpartyType, model.Email, taxId: model.TaxId);
            _dbContext.FinanceCounterparties.Add(counterparty);
            await AddReferenceAsync(companyId, connectionId, model.CounterpartyType, counterparty.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }

        counterparty = await _dbContext.FinanceCounterparties.SingleAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        counterparty.UpdateMasterData(model.Name, model.CounterpartyType, model.Email, taxId: model.TaxId);
        existing.Refresh(model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        AttachSource(counterparty, model.CounterpartyType, model.ExternalId, existing.Id);
        return SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
    }

    private async Task<SyncMutationResult> UpsertAccountAsync(Guid companyId, Guid connectionId, FortnoxAccountSyncModel model, CancellationToken cancellationToken)
    {
        var existing = await FindReferenceAsync(companyId, connectionId, "account", model.ExternalId, cancellationToken);
        if (existing?.IsCurrent(model.ExternalUpdatedUtc) == true) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        FinanceAccount account;
        if (existing is null)
        {
            account = new FinanceAccount(Guid.NewGuid(), companyId, model.Code, model.Name, model.AccountType, "SEK", 0m, _timeProvider.GetUtcNow().UtcDateTime);
            _dbContext.FinanceAccounts.Add(account);
            await AddReferenceAsync(companyId, connectionId, "account", account.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }

        account = await _dbContext.FinanceAccounts.SingleAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
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
        if (existing?.IsCurrent(model.ExternalUpdatedUtc) == true) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        var counterparty = await EnsureCounterpartyAsync(companyId, connectionId, "customer", model.CustomerNumber, model.CustomerName, cancellationToken);
        FinanceInvoice invoice;
        if (existing is null)
        {
            invoice = new FinanceInvoice(Guid.NewGuid(), companyId, counterparty.Id, model.ExternalNumber, model.IssuedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, settlementStatus: model.SettlementStatus);
            _dbContext.FinanceInvoices.Add(invoice);
            await AddReferenceAsync(companyId, connectionId, "invoice", invoice.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }

        invoice = await _dbContext.FinanceInvoices.SingleAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        invoice.ApplySyncedSnapshot(counterparty.Id, model.IssuedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, model.SettlementStatus);
        existing.Refresh(model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        AttachSource(invoice, "invoice", model.ExternalId, existing.Id);
        return SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
    }

    private async Task<SyncMutationResult> UpsertSupplierInvoiceAsync(Guid companyId, Guid connectionId, FortnoxSupplierInvoiceSyncModel model, CancellationToken cancellationToken)
    {
        var existing = await FindReferenceAsync(companyId, connectionId, "supplier_invoice", model.ExternalId, cancellationToken);
        if (existing?.IsCurrent(model.ExternalUpdatedUtc) == true) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        var counterparty = await EnsureCounterpartyAsync(companyId, connectionId, "supplier", model.SupplierNumber, model.SupplierName, cancellationToken);
        FinanceBill bill;
        if (existing is null)
        {
            bill = new FinanceBill(Guid.NewGuid(), companyId, counterparty.Id, model.ExternalNumber, model.ReceivedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, settlementStatus: model.SettlementStatus);
            _dbContext.FinanceBills.Add(bill);
            await AddReferenceAsync(companyId, connectionId, "supplier_invoice", bill.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }

        bill = await _dbContext.FinanceBills.SingleAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        bill.ApplySyncedSnapshot(counterparty.Id, model.ReceivedUtc, model.DueUtc, model.Amount, model.Currency, model.Status, model.SettlementStatus);
        existing.Refresh(model.ExternalNumber, model.ExternalUpdatedUtc, _timeProvider.GetUtcNow().UtcDateTime);
        AttachSource(bill, "supplier_invoice", model.ExternalId, existing.Id);
        return SyncMutationResult.FromUpdated(model.ExternalUpdatedUtc);
    }

    private async Task<SyncMutationResult> UpsertVoucherAsync(Guid companyId, Guid connectionId, FortnoxVoucherSyncModel model, CancellationToken cancellationToken)
    {
        var account = await _dbContext.FinanceAccounts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == "1930", cancellationToken)
            ?? await _dbContext.FinanceAccounts.FirstAsync(x => x.CompanyId == companyId, cancellationToken);
        var existing = await FindReferenceAsync(companyId, connectionId, "voucher", model.ExternalId, cancellationToken);
        if (existing?.IsCurrent(model.ExternalUpdatedUtc) == true) return SyncMutationResult.FromSkipped(model.ExternalUpdatedUtc);

        FinanceTransaction transaction;
        if (existing is null)
        {
            transaction = new FinanceTransaction(Guid.NewGuid(), companyId, account.Id, null, null, null, model.TransactionUtc, "voucher", model.Amount, "SEK", model.Description, model.ExternalNumber);
            _dbContext.FinanceTransactions.Add(transaction);
            await AddReferenceAsync(companyId, connectionId, "voucher", transaction.Id, model.ExternalId, model.ExternalNumber, model.ExternalUpdatedUtc, cancellationToken);
            return SyncMutationResult.FromCreated(model.ExternalUpdatedUtc);
        }

        transaction = await _dbContext.FinanceTransactions.SingleAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
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

        var payment = await _dbContext.Payments.SingleAsync(x => x.Id == existing.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        AttachSource(payment, "payment", externalId, existing.Id);
        return SyncMutationResult.FromSkipped(null);
    }

    private async Task<SyncMutationResult> UpsertReferenceOnlyAsync(Guid companyId, Guid connectionId, string entityType, string externalId, string externalNumber, DateTime? externalUpdatedUtc, CancellationToken cancellationToken)
    {
        var existing = await FindReferenceAsync(companyId, connectionId, entityType, externalId, cancellationToken);
        if (existing?.IsCurrent(externalUpdatedUtc) == true) return SyncMutationResult.FromSkipped(externalUpdatedUtc);

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
            return await _dbContext.FinanceCounterparties.SingleAsync(x => x.Id == reference.InternalRecordId && x.CompanyId == companyId, cancellationToken);
        }

        var counterparty = new FinanceCounterparty(Guid.NewGuid(), companyId, name, type);
        _dbContext.FinanceCounterparties.Add(counterparty);
        await AddReferenceAsync(companyId, connectionId, type, counterparty.Id, externalNumber, externalNumber, null, cancellationToken);
        return counterparty;
    }

    private async Task<FinanceAccount> EnsureSystemAccountAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.FinanceAccounts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == "FORTNOX", cancellationToken);
        if (account is not null) return account;

        account = new FinanceAccount(Guid.NewGuid(), companyId, "FORTNOX", "Fortnox synced reference", "integration", "SEK", 0m, _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.FinanceAccounts.Add(account);
        return account;
    }

    private async Task<FinanceExternalReference?> FindReferenceAsync(Guid companyId, Guid connectionId, string entityType, string externalId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceExternalReferences.SingleOrDefaultAsync(
            x => x.CompanyId == companyId &&
                 x.ConnectionId == connectionId &&
                 x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                 x.EntityType == entityType &&
                 x.ExternalId == externalId,
            cancellationToken);

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

    private static string BuildHistorySummary(IReadOnlyCollection<FortnoxEntitySyncResult> results) =>
        $"Created {results.Sum(x => x.Created)}, updated {results.Sum(x => x.Updated)}, skipped {results.Sum(x => x.Skipped)}, errors {results.Sum(x => x.Errors)}.";

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
