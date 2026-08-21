using System.Data;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxAccountingProviderExportAdapter : IAccountingProviderExportAdapter
{
    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public AccountingProviderCommand Map(AccountingProviderExportEnvelope export)
    {
        ArgumentNullException.ThrowIfNull(export);
        var rows = new JsonArray(export.Lines.Select(line =>
            (JsonNode)new JsonObject
            {
                ["Account"] = line.AccountCode,
                ["Description"] = line.Description ?? line.AccountName,
                ["Debit"] = line.DebitAmount,
                ["Credit"] = line.CreditAmount
            }).ToArray());
        var payload = new JsonObject
        {
            ["Voucher"] = new JsonObject
            {
                ["VoucherSeries"] = "A",
                ["VoucherDate"] = export.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Description"] = export.Description,
                ["ReferenceNumber"] = export.JournalNumber,
                ["VoucherRows"] = rows
            }
        };

        return new AccountingProviderCommand(
            ProviderKey,
            FinanceIntegrationWriteCommandTypes.VoucherCreate,
            "POST",
            "vouchers",
            export.JournalNumber,
            FortnoxWritePayloadSanitizer.CreateSummary(payload),
            FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
            FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload),
            "CommittedAccountingVoucher");
    }
}

public sealed class AccountingProviderExportService :
    IAccountingProviderExportService,
    IAccountingProviderExportExecutionTracker
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingAuthorityPolicy _authorityPolicy;
    private readonly IFinanceIntegrationWriteCommandService _writeCommands;
    private readonly IFinanceIntegrationProviderRegistry _providerRegistry;
    private readonly IReadOnlyDictionary<string, IAccountingProviderExportAdapter> _adapters;
    private readonly IAuditEventWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public AccountingProviderExportService(
        VirtualCompanyDbContext dbContext,
        IAccountingAuthorityPolicy authorityPolicy,
        IFinanceIntegrationWriteCommandService writeCommands,
        IFinanceIntegrationProviderRegistry providerRegistry,
        IEnumerable<IAccountingProviderExportAdapter> adapters,
        IAuditEventWriter auditWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _authorityPolicy = authorityPolicy;
        _writeCommands = writeCommands;
        _providerRegistry = providerRegistry;
        _adapters = adapters.ToDictionary(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase);
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<AccountingProviderExportDto> QueueAsync(
        QueueAccountingProviderExportCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId);
        var providerKey = NormalizeProvider(command.ProviderKey);
        var provider = _providerRegistry.GetRequired(providerKey);
        if (!_adapters.TryGetValue(providerKey, out var adapter))
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ProviderRequired,
                $"{provider.DisplayName} does not support committed-journal exports.");

        var journal = await _dbContext.LedgerEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Lines)
            .ThenInclude(x => x.FinanceAccount)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.LedgerEntryId, cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ExportNotFound,
                "The committed journal could not be found.");
        if (!LedgerEntryStatuses.IsPosted(journal.Status) || journal.PostingDate is not DateOnly postingDate)
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ExportBlocked,
                "Only a committed journal with a posting date can be exported.");

        var authority = await _authorityPolicy.EvaluateAsync(new(
            command.CompanyId, postingDate, AccountingAuthorityOperationValues.DownstreamExport, providerKey), cancellationToken);
        if (!authority.IsAllowed || authority.AuthorityPeriodId is not Guid authorityPeriodId)
            throw new AccountingAuthorityException(authority.ReasonCode ?? AccountingAuthorityReasonCodes.ExportBlocked,
                authority.Explanation, true);

        var connection = await _dbContext.FinanceIntegrationConnections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.ProviderKey == providerKey &&
                        x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ProviderNotConnected,
                $"Connect {provider.DisplayName} before exporting a committed journal.");

        var sourceType = journal.SourceType ?? "ledger_entry";
        var sourceId = journal.SourceId ?? journal.Id.ToString("N");
        var sourceVersion = journal.SourceVersion ?? journal.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        const string action = "export_committed_voucher";
        var stableIdentity = CreateStableIdentity(command.CompanyId, authorityPeriodId, sourceType, sourceId,
            sourceVersion, action, providerKey);
        var writeRequestId = DeterministicGuid(stableIdentity);
        var existing = await _dbContext.AccountingProviderExports.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.LedgerEntry)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.StableIdentity == stableIdentity,
                cancellationToken);
        if (existing is not null) return ToDto(existing, provider.DisplayName);

        var envelope = new AccountingProviderExportEnvelope(
            command.CompanyId, authorityPeriodId, journal.Id, journal.EntryNumber, postingDate,
            journal.Description ?? $"Journal {journal.EntryNumber}", sourceType, sourceId, sourceVersion,
            journal.BaseCurrency ?? journal.Lines.FirstOrDefault()?.Currency ?? string.Empty,
            journal.Lines.Select(line => new AccountingProviderExportLine(
                line.FinanceAccount.Code,
                line.FinanceAccount.Name,
                line.DebitAmount,
                line.CreditAmount,
                line.Currency,
                line.Description)).ToArray());
        var providerCommand = adapter.Map(envelope);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var export = new AccountingProviderExport(
            DeterministicGuid($"export|{stableIdentity}"), command.CompanyId, authorityPeriodId, journal.Id,
            providerKey, sourceType, sourceId, sourceVersion, action, stableIdentity, writeRequestId,
            command.ActorUserId, now);
        _dbContext.AccountingProviderExports.Add(export);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var approval = await _writeCommands.RequestApprovalAsync(new FinanceIntegrationWriteCommand(
            providerCommand.ProviderKey,
            command.CompanyId,
            connection.Id,
            command.ActorUserId,
            providerCommand.CommandType,
            providerCommand.HttpMethod,
            providerCommand.Path,
            providerCommand.TargetLabel,
            providerCommand.PayloadSummary,
            providerCommand.PayloadHash,
            new FinanceIntegrationWritePayload(providerCommand.SanitizedPayloadJson, providerCommand.ProviderPayloadType),
            writeRequestId,
            command.CorrelationId,
            AccountingDate: postingDate,
            AuthorityOperation: AccountingAuthorityOperationValues.DownstreamExport), cancellationToken);
        if (approval.ApprovalId is Guid approvalId) export.AttachApproval(approvalId, now);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingProviderExportQueued,
            export.Id, "A committed journal was queued for an approval-backed provider export.", command.CorrelationId,
            new Dictionary<string, string?>
            {
                ["providerKey"] = providerKey,
                ["journalId"] = journal.Id.ToString("D"),
                ["authorityPeriodId"] = authorityPeriodId.ToString("D"),
                ["writeRequestId"] = writeRequestId.ToString("D"),
                ["stableIdentity"] = stableIdentity
            }, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        export = await LoadAsync(command.CompanyId, export.Id, tracking: false, cancellationToken);
        return ToDto(export, provider.DisplayName);
    }

    public async Task<AccountingProviderExportDto> ReconcileAsync(
        ReconcileAccountingProviderExportCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId);
        var export = await LoadAsync(command.CompanyId, command.ExportId, tracking: true, cancellationToken);
        if (export.Version != command.ExpectedVersion)
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ConcurrencyConflict,
                "The export changed while it was being reconciled.", true);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (command.ProviderConfirmedSuccess)
            export.ReconcileAsExported(command.ProviderExternalId, command.Summary, command.ActorUserId, now);
        else
            export.ReconcileAsNotSent(command.Summary, command.ActorUserId, now);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingProviderExportReconciled,
            export.Id, command.ProviderConfirmedSuccess
                ? "The provider confirmed that the export succeeded."
                : "The provider confirmed that the export was not accepted.", command.CorrelationId,
            new Dictionary<string, string?>
            {
                ["providerKey"] = export.ProviderKey,
                ["providerConfirmedSuccess"] = command.ProviderConfirmedSuccess.ToString(),
                ["providerExternalId"] = command.ProviderExternalId,
                ["writeRequestId"] = export.WriteRequestId.ToString("D")
            }, now, cancellationToken);
        await SaveWithConcurrencyMappingAsync(cancellationToken);
        return ToDto(export, _providerRegistry.GetRequired(export.ProviderKey).DisplayName);
    }

    public async Task EnsureExecutionAllowedAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken)
    {
        var export = await TryLoadByWriteRequestAsync(companyId, writeRequestId, tracking: false, cancellationToken);
        if (export is null) return;
        var postingDate = export.LedgerEntry.PostingDate ?? DateOnly.FromDateTime(export.LedgerEntry.EntryUtc);
        var decision = await _authorityPolicy.EvaluateAsync(new(companyId, postingDate,
            AccountingAuthorityOperationValues.DownstreamExport, export.ProviderKey), cancellationToken);
        if (!decision.IsAllowed || decision.AuthorityPeriodId != export.AuthorityPeriodId)
            throw new AccountingAuthorityException(decision.ReasonCode ?? AccountingAuthorityReasonCodes.ExportBlocked,
                "The accounting authority changed after this export was approved. Review the export before sending it.", true);
    }

    public async Task MarkExecutionStartedAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken)
    {
        var export = await TryLoadByWriteRequestAsync(companyId, writeRequestId, tracking: true, cancellationToken);
        if (export is null) return;
        export.MarkExecuting(_timeProvider.GetUtcNow().UtcDateTime);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkExecutionSucceededAsync(Guid companyId, Guid writeRequestId, string? providerExternalId,
        string summary, CancellationToken cancellationToken)
    {
        var export = await TryLoadByWriteRequestAsync(companyId, writeRequestId, tracking: true, cancellationToken);
        if (export is null) return;
        export.MarkExported(providerExternalId, summary, _timeProvider.GetUtcNow().UtcDateTime);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkExecutionFailedAsync(Guid companyId, Guid writeRequestId, Exception exception,
        bool providerAcceptedRequest, CancellationToken cancellationToken)
    {
        var export = await TryLoadByWriteRequestAsync(companyId, writeRequestId, tracking: true, cancellationToken);
        if (export is null) return;
        var failure = Classify(exception, providerAcceptedRequest);
        export.MarkFailed(failure.Category, failure.Summary, failure.Ambiguous,
            _timeProvider.GetUtcNow().UtcDateTime);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static (string Category, string Summary, bool Ambiguous) Classify(Exception exception, bool providerAcceptedRequest)
    {
        if (providerAcceptedRequest)
            return (AccountingProviderExportFailureCategories.ProviderSuccessLocalFailure,
                "The provider accepted the request, but the local confirmation could not be saved. Reconcile the provider result before any further action.", true);
        if (exception is TaskCanceledException or HttpRequestException)
            return (AccountingProviderExportFailureCategories.UnknownOutcome,
                "The provider outcome is unknown. Check the provider before attempting another export.", true);
        if (exception is FortnoxApiException providerException)
        {
            if (providerException.RequiresReconnect || providerException.Category.Contains("authorization", StringComparison.OrdinalIgnoreCase))
                return (AccountingProviderExportFailureCategories.StaleCredentials, providerException.SafeMessage, false);
            if (providerException.StatusCode == HttpStatusCode.TooManyRequests ||
                providerException.Category.Contains("rate", StringComparison.OrdinalIgnoreCase))
                return (AccountingProviderExportFailureCategories.RateLimited, providerException.SafeMessage, false);
            if (providerException.Category.Contains("scope", StringComparison.OrdinalIgnoreCase) ||
                providerException.Category.Contains("permission", StringComparison.OrdinalIgnoreCase))
                return (AccountingProviderExportFailureCategories.MissingScope, providerException.SafeMessage, false);
            if (providerException.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity ||
                providerException.Category.Contains("validation", StringComparison.OrdinalIgnoreCase))
                return (AccountingProviderExportFailureCategories.Validation, providerException.SafeMessage, false);
            if (providerException.IsTransient)
                return (AccountingProviderExportFailureCategories.UnknownOutcome, providerException.SafeMessage, true);
            return (AccountingProviderExportFailureCategories.Permanent, providerException.SafeMessage, false);
        }
        return (AccountingProviderExportFailureCategories.Permanent,
            "The provider export failed safely. Review the connection and export details.", false);
    }

    private async Task<AccountingProviderExport> LoadAsync(Guid companyId, Guid exportId, bool tracking,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.AccountingProviderExports.IgnoreQueryFilters().Include(x => x.LedgerEntry)
            .Where(x => x.CompanyId == companyId && x.Id == exportId);
        if (!tracking) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ExportNotFound,
                "The provider export could not be found.");
    }

    private Task<AccountingProviderExport?> TryLoadByWriteRequestAsync(Guid companyId, Guid writeRequestId,
        bool tracking, CancellationToken cancellationToken)
    {
        var query = _dbContext.AccountingProviderExports.IgnoreQueryFilters().Include(x => x.LedgerEntry)
            .Where(x => x.CompanyId == companyId && x.WriteRequestId == writeRequestId);
        if (!tracking) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(cancellationToken);
    }

    private static AccountingProviderExportDto ToDto(AccountingProviderExport export, string providerName) =>
        new(export.Id, export.LedgerEntryId, export.LedgerEntry.EntryNumber,
            export.LedgerEntry.PostingDate ?? DateOnly.FromDateTime(export.LedgerEntry.EntryUtc), export.SourceType,
            export.SourceId, export.SourceVersion, export.ProviderKey, providerName, export.Status,
            export.Status switch
            {
                AccountingProviderExportStatuses.AwaitingApproval => "Waiting for approval",
                AccountingProviderExportStatuses.Approved => "Approved",
                AccountingProviderExportStatuses.Executing => "Sending",
                AccountingProviderExportStatuses.Exported => "Exported",
                AccountingProviderExportStatuses.ReconciliationRequired => "Needs reconciliation",
                AccountingProviderExportStatuses.Failed => "Failed",
                _ => "Needs review"
            }, export.WriteRequestId, export.ApprovalRequestId, export.FailureCategory, export.SafeSummary,
            export.ProviderExternalId, export.AttemptCount, export.Version, export.UpdatedUtc);

    private async Task SaveWithConcurrencyMappingAsync(CancellationToken cancellationToken)
    {
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ConcurrencyConflict,
                "The export changed while this request was being applied.", true);
        }
    }

    private Task WriteAuditAsync(Guid companyId, Guid actorUserId, string action, Guid exportId, string summary,
        string? correlationId, IReadOnlyDictionary<string, string?> metadata, DateTime occurredUtc,
        CancellationToken cancellationToken) =>
        _auditWriter.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorUserId, action,
            AuditTargetTypes.AccountingProviderExport, exportId.ToString("D"), AuditEventOutcomes.Succeeded, summary,
            ["accounting_journal", "accounting_authority", "finance_integration"], metadata, correlationId, occurredUtc),
            cancellationToken);

    private static string CreateStableIdentity(Guid companyId, Guid authorityPeriodId, string sourceType,
        string sourceId, string sourceVersion, string action, string providerKey)
    {
        var seed = $"{companyId:N}|{authorityPeriodId:N}|{sourceType}|{sourceId}|{sourceVersion}|{action}|{providerKey}".ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
    }

    private static Guid DeterministicGuid(string value)
    {
        Span<byte> bytes = stackalloc byte[16];
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    private static string NormalizeProvider(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("ProviderKey is required.", nameof(value))
            : value.Trim().ToLowerInvariant();

    private static void ValidateCommand(Guid companyId, Guid actorUserId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
    }
}
