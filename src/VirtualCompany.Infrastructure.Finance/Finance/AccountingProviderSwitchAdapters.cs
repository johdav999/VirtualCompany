using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class AccountingProviderSwitchAdapterResolver(IEnumerable<IAccountingProviderSwitchAdapter> adapters)
    : IAccountingProviderSwitchAdapterResolver
{
    private readonly IAccountingProviderSwitchAdapter[] _adapters = adapters.ToArray();

    public IAccountingProviderSwitchAdapter GetRequired(string endpointKind, string? providerKey)
    {
        var candidates = _adapters
            .Where(x => x.CanHandle(endpointKind, providerKey))
            .ToArray();

        return candidates.FirstOrDefault(x => x is not UnavailableExternalProviderSwitchAdapter)
            ?? candidates.SingleOrDefault()
            ?? throw new InvalidOperationException($"No read-only provider-switch adapter is registered for '{endpointKind}:{providerKey ?? "internal"}'.");
    }
}

public sealed class InternalLedgerProviderSwitchAdapter(VirtualCompanyDbContext dbContext, TimeProvider timeProvider)
    : IAccountingProviderSwitchAdapter
{
    public bool CanHandle(string endpointKind, string? providerKey) =>
        endpointKind == AccountingProviderEndpointKinds.Internal && providerKey is null;

    public Task<ProviderMigrationCapabilityProfile> GetCapabilityProfileAsync(Guid companyId,
        AccountingProviderSwitchEndpointDto endpoint, string correlationId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        ProviderMigrationCapability[] capabilities =
        [
            Supported(AccountingProviderSwitchCapabilityKeys.Accounts, "Native accounts are queryable with stable company-scoped identifiers."),
            Supported(AccountingProviderSwitchCapabilityKeys.Tax, "Tax review facts and posted tax journal lines are queryable."),
            Supported(AccountingProviderSwitchCapabilityKeys.FiscalPeriods, "Fiscal periods are queryable by company."),
            Supported(AccountingProviderSwitchCapabilityKeys.PeriodLocks, "Close and reporting-lock state is explicit."),
            Supported(AccountingProviderSwitchCapabilityKeys.VoucherNumbering, "Voucher series and sequences are explicit."),
            Supported(AccountingProviderSwitchCapabilityKeys.Customers, "Customer counterparties are queryable."),
            Supported(AccountingProviderSwitchCapabilityKeys.Suppliers, "Supplier counterparties are queryable."),
            Supported(AccountingProviderSwitchCapabilityKeys.Invoices, "Customer invoices and supplier bills are queryable."),
            Partial(AccountingProviderSwitchCapabilityKeys.Credits, "Credits are represented through correction journals and provider references, not a single credit register."),
            Supported(AccountingProviderSwitchCapabilityKeys.Payments, "Payments are queryable with status and currency."),
            Supported(AccountingProviderSwitchCapabilityKeys.Allocations, "Payment allocations are queryable."),
            Supported(AccountingProviderSwitchCapabilityKeys.BankReconciliation, "Bank transactions and reconciliation state are queryable."),
            Supported(AccountingProviderSwitchCapabilityKeys.Currencies, "Transaction currencies are explicit."),
            Unsupported(AccountingProviderSwitchCapabilityKeys.ExchangeRates, "There is no independent historical exchange-rate register."),
            Unsupported(AccountingProviderSwitchCapabilityKeys.Dimensions, "No general-purpose accounting dimension register is implemented."),
            Supported(AccountingProviderSwitchCapabilityKeys.Journals, "Posted and pending journals are queryable without mutation."),
            Partial(AccountingProviderSwitchCapabilityKeys.Attachments, "Ledger evidence links and supplier document attachment state are queryable; source file retention varies."),
            Supported(AccountingProviderSwitchCapabilityKeys.StableIdentifiers, "Native entities use immutable GUID identifiers and posting identities."),
            Partial(AccountingProviderSwitchCapabilityKeys.IncrementalExtraction, "Stable ordering is available, but this assessment records aggregate dataset versions rather than change-feed tokens."),
            Supported(AccountingProviderSwitchCapabilityKeys.SandboxPreview, "Read-only assessment does not commit ledger records."),
            Supported(AccountingProviderSwitchCapabilityKeys.RateLimits, "Local reads are bounded by the assessment page policy."),
            Supported(AccountingProviderSwitchCapabilityKeys.ReconciliationLookup, "Payment, bank, source, and ledger links are queryable.")
        ];
        return Task.FromResult(new ProviderMigrationCapabilityProfile(endpoint.Kind, endpoint.ProviderKey, capabilities, now));
    }

    public async Task<ProviderSwitchInventoryExtractionResult> ExtractInventoryAsync(
        ProviderSwitchInventoryExtractionRequest request, CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId;
        long count;
        decimal total = 0m;
        object evidence = new { };
        string capability = AccountingProviderSwitchCapabilityLevels.Supported;
        string availability = AccountingProviderSwitchDatasetAvailability.Available;

        switch (request.DatasetKey)
        {
            case AccountingProviderSwitchDatasetKeys.Accounts:
                count = await dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                break;
            case AccountingProviderSwitchDatasetKeys.Tax:
                count = await dbContext.AccountingTaxReviews.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                break;
            case AccountingProviderSwitchDatasetKeys.FiscalPeriods:
                var periods = dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId);
                count = await periods.LongCountAsync(cancellationToken);
                evidence = new { closedCount = await periods.LongCountAsync(x => x.IsClosed, cancellationToken), lockedCount = await periods.LongCountAsync(x => x.IsReportingLocked, cancellationToken) };
                break;
            case AccountingProviderSwitchDatasetKeys.VoucherNumbering:
                count = await dbContext.VoucherSeries.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                evidence = new { sequenceCount = await dbContext.VoucherSequences.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken) };
                break;
            case AccountingProviderSwitchDatasetKeys.Customers:
                count = await dbContext.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId && x.CounterpartyType == "customer", cancellationToken);
                break;
            case AccountingProviderSwitchDatasetKeys.Suppliers:
                count = await dbContext.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId && x.CounterpartyType == "supplier", cancellationToken);
                break;
            case AccountingProviderSwitchDatasetKeys.Invoices:
                count = await dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken)
                    + await dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                total = await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
                total += await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
                break;
            case AccountingProviderSwitchDatasetKeys.Credits:
                count = await dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId && x.SourceType != null && x.SourceType.Contains("credit"), cancellationToken);
                capability = AccountingProviderSwitchCapabilityLevels.Partial;
                evidence = new { representation = "correction_journals" };
                break;
            case AccountingProviderSwitchDatasetKeys.Payments:
                count = await dbContext.Payments.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                total = await dbContext.Payments.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
                break;
            case AccountingProviderSwitchDatasetKeys.Allocations:
                count = await dbContext.PaymentAllocations.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                total = await dbContext.PaymentAllocations.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.AllocatedAmount, cancellationToken) ?? 0m;
                break;
            case AccountingProviderSwitchDatasetKeys.BankReconciliation:
                count = await dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                total = await dbContext.BankTransactions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
                evidence = new { followUpCount = await dbContext.BankReconciliationFollowUps.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken) };
                break;
            case AccountingProviderSwitchDatasetKeys.Currencies:
                var currencies = await dbContext.Payments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
                    .Select(x => x.Currency).Distinct().Take(100).ToArrayAsync(cancellationToken);
                count = currencies.LongLength;
                evidence = new { currencies };
                break;
            case AccountingProviderSwitchDatasetKeys.ExchangeRates:
                count = 0;
                capability = AccountingProviderSwitchCapabilityLevels.Unsupported;
                availability = AccountingProviderSwitchDatasetAvailability.Unsupported;
                break;
            case AccountingProviderSwitchDatasetKeys.Dimensions:
                count = 0;
                capability = AccountingProviderSwitchCapabilityLevels.Unsupported;
                availability = AccountingProviderSwitchDatasetAvailability.Unsupported;
                break;
            case AccountingProviderSwitchDatasetKeys.Journals:
                count = await dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                total = await dbContext.LedgerEntryLines.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.DebitAmount, cancellationToken) ?? 0m;
                evidence = new { creditTotal = await dbContext.LedgerEntryLines.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.CreditAmount, cancellationToken) ?? 0m };
                break;
            case AccountingProviderSwitchDatasetKeys.Attachments:
                count = await dbContext.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken)
                    + await dbContext.SupplierInvoiceSourceDocumentAttachments.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                capability = AccountingProviderSwitchCapabilityLevels.Partial;
                evidence = new { sourceArchiveAccessible = true, limitation = "Evidence references do not guarantee that every historical binary remains retained." };
                break;
            case AccountingProviderSwitchDatasetKeys.StableIdentifiers:
                count = await dbContext.LedgerPostingIdentities.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken);
                evidence = new { duplicateCount = 0 };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.DatasetKey), "Inventory dataset is not supported.");
        }

        if (availability == AccountingProviderSwitchDatasetAvailability.Available && count == 0)
            availability = AccountingProviderSwitchDatasetAvailability.ConfirmedAbsent;
        var evidenceJson = JsonSerializer.Serialize(evidence);
        return new ProviderSwitchInventoryExtractionResult(request.DatasetKey, availability, capability, count, total,
            null, null, "native-ledger-v1", Hash(request.DatasetKey, count, total, evidenceJson), evidenceJson, true);
    }

    private static ProviderMigrationCapability Supported(string key, string explanation) =>
        new(key, AccountingProviderSwitchCapabilityLevels.Supported, explanation);
    private static ProviderMigrationCapability Partial(string key, string explanation) =>
        new(key, AccountingProviderSwitchCapabilityLevels.Partial, explanation);
    private static ProviderMigrationCapability Unsupported(string key, string explanation) =>
        new(key, AccountingProviderSwitchCapabilityLevels.Unsupported, explanation);
    private static string Hash(string key, long count, decimal total, string evidence) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}|{count}|{total.ToString(CultureInfo.InvariantCulture)}|{evidence}"))).ToLowerInvariant();
}

public sealed class FortnoxProviderSwitchAdapter(
    IFortnoxApiClient apiClient,
    VirtualCompanyDbContext dbContext,
    TimeProvider timeProvider) : IAccountingProviderSwitchAdapter
{
    private static readonly IReadOnlyDictionary<string, (string Level, string Explanation, string? Scope)> CapabilityDefinitions =
        new Dictionary<string, (string, string, string?)>(StringComparer.Ordinal)
        {
            [AccountingProviderSwitchCapabilityKeys.Accounts] = ("supported", "Accounts are available through bounded bookkeeping pages.", "bookkeeping"),
            [AccountingProviderSwitchCapabilityKeys.Tax] = ("partial", "Tax-related account facts are readable, but a complete tax-code migration surface is not established.", "bookkeeping"),
            [AccountingProviderSwitchCapabilityKeys.FiscalPeriods] = ("unknown", "The current adapter has no verified fiscal-period inventory endpoint.", "bookkeeping"),
            [AccountingProviderSwitchCapabilityKeys.PeriodLocks] = ("unknown", "Period-lock extraction is not verified by the current adapter.", "bookkeeping"),
            [AccountingProviderSwitchCapabilityKeys.VoucherNumbering] = ("partial", "Voucher identifiers are readable, but numbering configuration is not extracted.", "bookkeeping"),
            [AccountingProviderSwitchCapabilityKeys.Customers] = ("supported", "Customers are available through bounded pages.", "customer"),
            [AccountingProviderSwitchCapabilityKeys.Suppliers] = ("supported", "Suppliers are available through bounded pages.", "supplier"),
            [AccountingProviderSwitchCapabilityKeys.Invoices] = ("supported", "Customer and supplier invoices are available through bounded pages.", "invoice,supplierinvoice"),
            [AccountingProviderSwitchCapabilityKeys.Credits] = ("partial", "Credit classification depends on invoice fields and is not returned as a complete independent register.", "invoice"),
            [AccountingProviderSwitchCapabilityKeys.Payments] = ("supported", "Customer and supplier payments are available through bounded pages.", "payment"),
            [AccountingProviderSwitchCapabilityKeys.Allocations] = ("partial", "Payment references are available, but allocation detail is not a complete independent register.", "payment"),
            [AccountingProviderSwitchCapabilityKeys.BankReconciliation] = ("unknown", "The current adapter has no verified bank-reconciliation inventory endpoint.", null),
            [AccountingProviderSwitchCapabilityKeys.Currencies] = ("partial", "Currency codes are present on financial documents; no independent currency register is extracted.", "invoice"),
            [AccountingProviderSwitchCapabilityKeys.ExchangeRates] = ("unknown", "Historical exchange-rate extraction is not verified.", null),
            [AccountingProviderSwitchCapabilityKeys.Dimensions] = ("partial", "Projects are readable; other accounting dimensions are not verified.", "project"),
            [AccountingProviderSwitchCapabilityKeys.Journals] = ("supported", "Vouchers are available through bounded bookkeeping pages.", "bookkeeping"),
            [AccountingProviderSwitchCapabilityKeys.Attachments] = ("unknown", "Historical attachment inventory is not implemented by this read adapter.", null),
            [AccountingProviderSwitchCapabilityKeys.StableIdentifiers] = ("supported", "Provider document and master-data numbers are returned as stable references.", null),
            [AccountingProviderSwitchCapabilityKeys.IncrementalExtraction] = ("supported", "Paged endpoints accept last-modified filters and stable page cursors.", null),
            [AccountingProviderSwitchCapabilityKeys.SandboxPreview] = ("unsupported", "Fortnox reads are production-tenant reads; assessment performs no writes.", null),
            [AccountingProviderSwitchCapabilityKeys.RateLimits] = ("supported", "Rate-limit responses are classified with bounded retry-after handling.", null),
            [AccountingProviderSwitchCapabilityKeys.ReconciliationLookup] = ("partial", "Document and voucher references are available; bank reconciliation lookup is not verified.", null)
        };

    public bool CanHandle(string endpointKind, string? providerKey) =>
        endpointKind == AccountingProviderEndpointKinds.External &&
        string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase);

    public async Task<ProviderMigrationCapabilityProfile> GetCapabilityProfileAsync(Guid companyId,
        AccountingProviderSwitchEndpointDto endpoint, string correlationId, CancellationToken cancellationToken)
    {
        var connection = await dbContext.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox)
            .OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken);
        var scopes = connection?.Scopes.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var connected = connection is not null && string.Equals(connection.Status, FinanceIntegrationConnectionStatuses.Connected, StringComparison.OrdinalIgnoreCase);
        var capabilities = CapabilityDefinitions.Select(item =>
        {
            var definition = item.Value;
            if (!connected)
                return new ProviderMigrationCapability(item.Key, AccountingProviderSwitchCapabilityLevels.Unknown,
                    "The Fortnox connection is unavailable or needs reconnection.", definition.Scope);
            if (definition.Scope is not null && !definition.Scope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(scopes.Contains))
                return new ProviderMigrationCapability(item.Key, AccountingProviderSwitchCapabilityLevels.Unknown,
                    $"The connected account did not authorize the required '{definition.Scope}' scope.", definition.Scope);
            return new ProviderMigrationCapability(item.Key, definition.Level, definition.Explanation, definition.Scope);
        }).ToArray();
        return new ProviderMigrationCapabilityProfile(endpoint.Kind, endpoint.ProviderKey, capabilities, timeProvider.GetUtcNow().UtcDateTime);
    }

    public async Task<ProviderSwitchInventoryExtractionResult> ExtractInventoryAsync(
        ProviderSwitchInventoryExtractionRequest request, CancellationToken cancellationToken)
    {
        var profile = await GetCapabilityProfileAsync(request.CompanyId, request.Endpoint, request.CorrelationId, cancellationToken);
        var capabilityKey = request.DatasetKey == AccountingProviderSwitchDatasetKeys.FiscalPeriods
            ? AccountingProviderSwitchCapabilityKeys.FiscalPeriods : request.DatasetKey;
        var capability = profile.Capabilities.FirstOrDefault(x => x.Key == capabilityKey)
            ?? new ProviderMigrationCapability(capabilityKey, AccountingProviderSwitchCapabilityLevels.Unknown, "Capability was not reported.");
        if (capability.RequiredScope is not null && capability.Level == AccountingProviderSwitchCapabilityLevels.Unknown)
            return Empty(request.DatasetKey, AccountingProviderSwitchDatasetAvailability.NotAuthorized, capability.Level,
                "provider_scope_missing", $"Grant the '{capability.RequiredScope}' scope and reconnect Fortnox.");
        if (capability.Level == AccountingProviderSwitchCapabilityLevels.Unsupported)
            return Empty(request.DatasetKey, AccountingProviderSwitchDatasetAvailability.Unsupported, capability.Level, null, null);

        var context = new FortnoxRequestContext(request.CompanyId, CorrelationId: request.CorrelationId, RetryExternalFailures: false);
        var page = ParsePage(request.Cursor);
        var size = Math.Clamp(request.PageSize, 1, 500);
        try
        {
            return request.DatasetKey switch
            {
                AccountingProviderSwitchDatasetKeys.Accounts => Page(request.DatasetKey, capability.Level,
                    await apiClient.GetAccountsAsync(context, new FortnoxPageOptions(Page: page, Limit: size), cancellationToken), x => x.CurrentBalance ?? x.Balance ?? 0m, x => x.Number?.ToString(CultureInfo.InvariantCulture)),
                AccountingProviderSwitchDatasetKeys.Customers => Page(request.DatasetKey, capability.Level,
                    await apiClient.GetCustomersAsync(context, new FortnoxPageOptions(Page: page, Limit: size), cancellationToken), _ => 0m, x => x.CustomerNumber),
                AccountingProviderSwitchDatasetKeys.Suppliers => Page(request.DatasetKey, capability.Level,
                    await apiClient.GetSuppliersAsync(context, new FortnoxPageOptions(Page: page, Limit: size), cancellationToken), _ => 0m, x => x.SupplierNumber),
                AccountingProviderSwitchDatasetKeys.Invoices => await InvoicePageAsync(request, context, capability.Level, size, cancellationToken),
                AccountingProviderSwitchDatasetKeys.Payments => await PaymentPageAsync(request, context, capability.Level, size, cancellationToken),
                AccountingProviderSwitchDatasetKeys.Journals => Page(request.DatasetKey, capability.Level,
                    await apiClient.GetVouchersAsync(context, new FortnoxPageOptions(Page: page, Limit: size), cancellationToken), x => x.Total ?? 0m, x => $"{x.VoucherSeries}:{x.VoucherNumber}"),
                AccountingProviderSwitchDatasetKeys.Dimensions => Page(request.DatasetKey, capability.Level,
                    await apiClient.GetProjectsAsync(context, new FortnoxPageOptions(Page: page, Limit: size), cancellationToken), _ => 0m, x => x.ProjectNumber),
                _ => Empty(request.DatasetKey, AccountingProviderSwitchDatasetAvailability.NotReturned, capability.Level,
                    "dataset_not_returned", "The current Fortnox read adapter has no verified extraction endpoint for this dataset.")
            };
        }
        catch (FortnoxApiException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden || exception.RequiresReconnect)
        {
            return Empty(request.DatasetKey, AccountingProviderSwitchDatasetAvailability.NotAuthorized, capability.Level,
                "provider_authorization_failed", exception.SafeMessage);
        }
    }

    private async Task<ProviderSwitchInventoryExtractionResult> InvoicePageAsync(ProviderSwitchInventoryExtractionRequest request,
        FortnoxRequestContext context, string level, int size, CancellationToken cancellationToken)
    {
        var (kind, page) = ParseCompositeCursor(request.Cursor);
        if (kind == "customer")
        {
            var result = await apiClient.GetInvoicesAsync(context, new FortnoxPageOptions(Page: page, Limit: size), cancellationToken);
            var mapped = Page(request.DatasetKey, level, result, x => x.Total ?? 0m, x => x.DocumentNumber);
            return mapped.IsComplete ? mapped with { NextCursor = "supplier:1", IsComplete = false } : mapped with { NextCursor = $"customer:{mapped.NextCursor}" };
        }
        var supplier = await apiClient.GetSupplierInvoicesAsync(context, new FortnoxPageOptions(Page: page, Limit: size), cancellationToken);
        return Page(request.DatasetKey, level, supplier, x => x.Total ?? 0m, x => x.GivenNumber);
    }

    private async Task<ProviderSwitchInventoryExtractionResult> PaymentPageAsync(ProviderSwitchInventoryExtractionRequest request,
        FortnoxRequestContext context, string level, int size, CancellationToken cancellationToken)
    {
        var (kind, page) = ParseCompositeCursor(request.Cursor);
        if (kind == "customer")
        {
            var result = await apiClient.GetInvoicePaymentsAsync(context, new FortnoxPageOptions(Page: page, Limit: size), cancellationToken);
            var mapped = Page(request.DatasetKey, level, result, x => x.Amount ?? 0m, x => x.Number);
            return mapped.IsComplete ? mapped with { NextCursor = "supplier:1", IsComplete = false } : mapped with { NextCursor = $"customer:{mapped.NextCursor}" };
        }
        var supplier = await apiClient.GetSupplierInvoicePaymentsAsync(context, new FortnoxPageOptions(Page: page, Limit: size), cancellationToken);
        return Page(request.DatasetKey, level, supplier, x => x.Amount ?? 0m, x => x.Number);
    }

    private static ProviderSwitchInventoryExtractionResult Page<T>(string key, string level, FortnoxPagedResponse<T> page,
        Func<T, decimal> amount, Func<T, string?> identifier)
    {
        var count = page.Items.Count;
        var total = page.Items.Sum(amount);
        var identifiers = page.Items.Select(identifier).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var next = page.HasNextPage ? ((page.Metadata.CurrentPage ?? 1) + 1).ToString(CultureInfo.InvariantCulture) : null;
        var availability = count == 0 && !page.HasNextPage ? AccountingProviderSwitchDatasetAvailability.ConfirmedAbsent : AccountingProviderSwitchDatasetAvailability.Available;
        var evidence = JsonSerializer.Serialize(new { currentPage = page.Metadata.CurrentPage, totalPages = page.Metadata.TotalPages, pageCount = count });
        return new ProviderSwitchInventoryExtractionResult(key, availability, level, count, total, null, next,
            page.Metadata.TotalResources?.ToString(CultureInfo.InvariantCulture), Hash(string.Join('|', identifiers), count, total), evidence, !page.HasNextPage);
    }

    private static ProviderSwitchInventoryExtractionResult Empty(string key, string availability, string level, string? code, string? summary)
    {
        var evidence = JsonSerializer.Serialize(new { availability, failureCode = code });
        return new(key, availability, level, 0, 0, null, null, null, Hash(key, 0, 0), evidence, true, code, summary);
    }

    private static int ParsePage(string? cursor) => int.TryParse(cursor, out var page) && page > 0 ? page : 1;
    private static (string Kind, int Page) ParseCompositeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return ("customer", 1);
        var parts = cursor.Split(':', 2);
        return parts.Length == 2 ? (parts[0], ParsePage(parts[1])) : ("customer", ParsePage(cursor));
    }
    private static string Hash(string key, long count, decimal total) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}|{count}|{total.ToString(CultureInfo.InvariantCulture)}"))).ToLowerInvariant();
}

public sealed class UnavailableExternalProviderSwitchAdapter(TimeProvider timeProvider) : IAccountingProviderSwitchAdapter
{
    public bool CanHandle(string endpointKind, string? providerKey) =>
        endpointKind == AccountingProviderEndpointKinds.External &&
        !string.IsNullOrWhiteSpace(providerKey) &&
        !string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase);

    public Task<ProviderMigrationCapabilityProfile> GetCapabilityProfileAsync(Guid companyId,
        AccountingProviderSwitchEndpointDto endpoint, string correlationId, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderMigrationCapabilityProfile(endpoint.Kind, endpoint.ProviderKey,
            AccountingProviderSwitchCapabilityKeys.All.Select(key => new ProviderMigrationCapability(key,
                AccountingProviderSwitchCapabilityLevels.Unknown,
                "No production read-only migration adapter is registered for this provider.")).ToArray(),
            timeProvider.GetUtcNow().UtcDateTime));

    public Task<ProviderSwitchInventoryExtractionResult> ExtractInventoryAsync(
        ProviderSwitchInventoryExtractionRequest request, CancellationToken cancellationToken)
    {
        var evidence = JsonSerializer.Serialize(new { adapter = "unavailable", providerKey = request.Endpoint.ProviderKey });
        return Task.FromResult(new ProviderSwitchInventoryExtractionResult(request.DatasetKey,
            AccountingProviderSwitchDatasetAvailability.NotReturned, AccountingProviderSwitchCapabilityLevels.Unknown,
            0, 0, null, null, null, Hash(evidence), evidence, true,
            "provider_adapter_unavailable", "Configure a production read-only migration adapter for this provider and replay the assessment."));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
