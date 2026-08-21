using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxFinanceDocumentActionAdapter : IFinanceDocumentActionProviderAdapter
{
    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public FinanceDocumentProviderCommand Map(RequestFinanceDocumentActionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var line = new JsonObject
        {
            ["Description"] = command.Description,
            ["DeliveredQuantity"] = 1,
            ["Price"] = command.Amount,
            ["VAT"] = 0
        };
        var document = new JsonObject
        {
            ["CustomerName"] = command.CounterpartyName,
            ["Currency"] = command.Currency,
            ["YourReference"] = command.ContactReference,
            ["Remarks"] = $"Requested by Finance from {command.SourceType} {command.SourceId} version {command.SourceVersion}.",
            ["InvoiceRows"] = new JsonArray(line)
        };
        var isQuote = string.Equals(command.DocumentType, "quote", StringComparison.OrdinalIgnoreCase);
        var payload = isQuote
            ? new JsonObject { ["Offer"] = document }
            : new JsonObject { ["Invoice"] = document };

        return new FinanceDocumentProviderCommand(
            ProviderKey,
            FinanceIntegrationWriteCommandTypes.InvoiceExport,
            "POST",
            isQuote ? "offers" : "invoices",
            command.CounterpartyName,
            FortnoxWritePayloadSanitizer.CreateSummary(payload),
            FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
            FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload),
            "FinanceDocumentAction");
    }
}

public sealed class FortnoxFinanceCustomerDocumentAdapter : IFinanceCustomerDocumentProviderAdapter
{
    private readonly IFinanceCustomerInvoiceFortnoxActionService _actions;

    public FortnoxFinanceCustomerDocumentAdapter(IFinanceCustomerInvoiceFortnoxActionService actions)
    {
        _actions = actions;
    }

    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task<FinanceCustomerDocumentProviderResult> RequestExportAsync(
        RequestFinanceCustomerDocumentExportCommand command,
        CancellationToken cancellationToken)
    {
        var state = await _actions.RequestExportAsync(
            new RequestCustomerInvoiceFortnoxExportCommand(
                command.CompanyId,
                command.DocumentId,
                command.ActorUserId,
                command.ActorDisplayName,
                ProviderKey,
                command.AccountingDate,
                AccountingAuthorityOperationValues.ProviderAuthoritativeWrite),
            cancellationToken);
        var writeRequestId = state.CreateWriteRequestId ?? command.WriteRequestId;
        if (writeRequestId != command.WriteRequestId)
        {
            throw new InvalidOperationException("The provider adapter returned an unexpected write identity.");
        }

        return new FinanceCustomerDocumentProviderResult(
            writeRequestId,
            state.CreateApprovalId,
            state.CreateStatus,
            state.Message);
    }
}

public sealed class FinanceAccountingActionService : IFinanceAccountingActionService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceIntegrationWriteCommandService _writeCommands;
    private readonly IFinanceIntegrationProviderRegistry _providerRegistry;
    private readonly IFortnoxOutboundActionExecutor _fortnoxExecutor;
    private readonly IReadOnlyDictionary<string, IFinanceDocumentActionProviderAdapter> _adapters;
    private readonly IReadOnlyDictionary<string, IFinanceCustomerDocumentProviderAdapter> _customerDocumentAdapters;

    public FinanceAccountingActionService(
        VirtualCompanyDbContext dbContext,
        IFinanceIntegrationWriteCommandService writeCommands,
        IFinanceIntegrationProviderRegistry providerRegistry,
        IFortnoxOutboundActionExecutor fortnoxExecutor,
        IEnumerable<IFinanceDocumentActionProviderAdapter> adapters,
        IEnumerable<IFinanceCustomerDocumentProviderAdapter>? customerDocumentAdapters = null)
    {
        _dbContext = dbContext;
        _writeCommands = writeCommands;
        _providerRegistry = providerRegistry;
        _fortnoxExecutor = fortnoxExecutor;
        _adapters = adapters.ToDictionary(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase);
        _customerDocumentAdapters = (customerDocumentAdapters ?? []).ToDictionary(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<FinanceDocumentActionResult> RequestCustomerDocumentExportAsync(
        RequestFinanceCustomerDocumentExportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(command));
        if (command.DocumentId == Guid.Empty) throw new ArgumentException("DocumentId is required.", nameof(command));
        if (command.WriteRequestId == Guid.Empty) throw new ArgumentException("WriteRequestId is required.", nameof(command));

        var period = await _dbContext.AccountingAuthorityPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.EffectiveFrom <= command.AccountingDate &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= command.AccountingDate))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        var authority = period?.Authority ?? await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId)
            .Select(x => x.Authority)
            .SingleOrDefaultAsync(cancellationToken);

        if (authority == AccountingAuthorityValues.InternalLedger)
        {
            return new FinanceDocumentActionResult(
                authority,
                "virtual_company",
                "Virtual Company",
                FinanceAccountingActionStatuses.FinanceReviewRequired,
                command.WriteRequestId,
                null,
                "Virtual Company owns the books for this period. Finance should complete the native customer credit here.");
        }

        if (authority == AccountingAuthorityValues.Migration)
        {
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.MigrationOperationRequired,
                "Customer document export is paused until the accounting-authority cutover is reconciled.", true);
        }

        var providerKey = period?.ProviderKey;
        if (authority is null)
        {
            providerKey = await _dbContext.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.Status == FinanceIntegrationConnectionStatuses.Connected)
                .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
                .Select(x => x.ProviderKey)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(providerKey) || !_customerDocumentAdapters.TryGetValue(providerKey, out var adapter))
        {
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ProviderRequired,
                "The authoritative accounting provider does not support customer document exports.", true);
        }

        var result = await adapter.RequestExportAsync(command, cancellationToken);
        var provider = _providerRegistry.GetRequired(providerKey);
        return new FinanceDocumentActionResult(
            authority ?? AccountingAuthorityValues.ExternalProvider,
            providerKey,
            provider.DisplayName,
            result.Status,
            result.WriteRequestId,
            result.ApprovalId,
            result.Message);
    }

    public async Task<FinanceDocumentActionResult> RequestDocumentAsync(
        RequestFinanceDocumentActionCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        var period = await _dbContext.AccountingAuthorityPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.EffectiveFrom <= command.AccountingDate &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= command.AccountingDate))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        var configuredAuthority = period?.Authority ?? await _dbContext.AccountingConfigurations.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId)
            .Select(x => x.Authority)
            .SingleOrDefaultAsync(cancellationToken);

        if (configuredAuthority is null)
        {
            var legacyConnection = await _dbContext.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.Status == FinanceIntegrationConnectionStatuses.Connected)
                .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (legacyConnection is not null)
            {
                return await RequestProviderActionAsync(command, legacyConnection.ProviderKey, legacyConnection.Id,
                    AccountingAuthorityValues.ExternalProvider, enforceAuthority: false, cancellationToken);
            }

            return FinanceReview(command,
                "Finance needs to finish accounting setup and prepare this document in Virtual Company.");
        }

        if (configuredAuthority == AccountingAuthorityValues.InternalLedger)
        {
            return FinanceReview(command,
                "Virtual Company owns the books for this period. Finance should prepare the native customer document here rather than create an independent provider record.");
        }

        if (configuredAuthority == AccountingAuthorityValues.Migration)
        {
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.MigrationOperationRequired,
                "Finance document creation is paused until the accounting-authority cutover is reconciled.", true);
        }

        var providerKey = period?.ProviderKey;
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ProviderRequired,
                "The authoritative accounting provider is not configured for this period.", true);
        }

        var connectionId = await _dbContext.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.ProviderKey == providerKey &&
                        x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ProviderNotConnected,
                $"Connect {_providerRegistry.GetRequired(providerKey).DisplayName} before requesting this finance action.");

        return await RequestProviderActionAsync(command, providerKey, connectionId,
            AccountingAuthorityValues.ExternalProvider, enforceAuthority: true, cancellationToken);
    }

    public Task<FinanceIntegrationOutboundExecutionResult> RetryApprovedAsync(
        Guid companyId,
        Guid writeRequestId,
        CancellationToken cancellationToken) =>
        _fortnoxExecutor.ExecuteApprovedAsync(companyId, writeRequestId, cancellationToken);

    private async Task<FinanceDocumentActionResult> RequestProviderActionAsync(
        RequestFinanceDocumentActionCommand command,
        string providerKey,
        Guid connectionId,
        string authority,
        bool enforceAuthority,
        CancellationToken cancellationToken)
    {
        var provider = _providerRegistry.GetRequired(providerKey);
        if (!_adapters.TryGetValue(providerKey, out var adapter))
        {
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ProviderRequired,
                $"{provider.DisplayName} does not support Finance document actions.");
        }

        var providerCommand = adapter.Map(command);
        var result = await _writeCommands.RequestApprovalAsync(new FinanceIntegrationWriteCommand(
            providerCommand.ProviderKey,
            command.CompanyId,
            connectionId,
            command.ActorUserId,
            providerCommand.CommandType,
            providerCommand.HttpMethod,
            providerCommand.Path,
            providerCommand.TargetLabel,
            providerCommand.PayloadSummary,
            providerCommand.PayloadHash,
            new FinanceIntegrationWritePayload(providerCommand.SanitizedPayloadJson, providerCommand.ProviderPayloadType),
            command.WriteRequestId,
            command.CorrelationId,
            AccountingDate: enforceAuthority ? command.AccountingDate : null,
            AuthorityOperation: enforceAuthority ? AccountingAuthorityOperationValues.ProviderAuthoritativeWrite : null), cancellationToken);

        return new FinanceDocumentActionResult(
            authority,
            providerKey,
            provider.DisplayName,
            FinanceAccountingActionStatuses.AwaitingApproval,
            result.WriteRequestId,
            result.ApprovalId,
            $"Finance prepared an approval-backed {provider.DisplayName} document action.");
    }

    private static FinanceDocumentActionResult FinanceReview(
        RequestFinanceDocumentActionCommand command,
        string message) =>
        new(
            AccountingAuthorityValues.InternalLedger,
            "virtual_company",
            "Virtual Company",
            FinanceAccountingActionStatuses.FinanceReviewRequired,
            command.WriteRequestId,
            null,
            message);

    private static void Validate(RequestFinanceDocumentActionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(command));
        if (command.WriteRequestId == Guid.Empty) throw new ArgumentException("WriteRequestId is required.", nameof(command));
        if (command.Amount <= 0m) throw new ArgumentOutOfRangeException(nameof(command), "Amount must be positive.");
        if (string.IsNullOrWhiteSpace(command.CounterpartyName)) throw new ArgumentException("CounterpartyName is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.Currency)) throw new ArgumentException("Currency is required.", nameof(command));
        if (!string.Equals(command.DocumentType, "invoice", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command.DocumentType, "quote", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DocumentType must be invoice or quote.", nameof(command));
    }
}
