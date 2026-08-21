using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingAuthorityService : IAccountingAuthorityService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingAuthorityPolicy _policy;
    private readonly IFinanceIntegrationProviderRegistry _providerRegistry;
    private readonly IAuditEventWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public AccountingAuthorityService(
        VirtualCompanyDbContext dbContext,
        IAccountingAuthorityPolicy policy,
        IFinanceIntegrationProviderRegistry providerRegistry,
        IAuditEventWriter auditWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _policy = policy;
        _providerRegistry = providerRegistry;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<AccountingAuthorityPolicyDecision> EvaluateAsync(
        EvaluateAccountingAuthorityQuery query,
        CancellationToken cancellationToken) =>
        await _policy.EvaluateAsync(query, cancellationToken);

    public async Task<AccountingAuthorityReadModel> GetAsync(
        GetAccountingAuthorityQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        var asOf = query.AsOf ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var periods = await _dbContext.AccountingAuthorityPeriods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .OrderByDescending(x => x.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var current = periods.FirstOrDefault(x => x.EffectiveFrom <= asOf && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= asOf))
                      ?? periods.FirstOrDefault(x => x.EffectiveFrom > asOf);
        var connections = await _dbContext.FinanceIntegrationConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .ToListAsync(cancellationToken);
        var exports = await _dbContext.AccountingProviderExports
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.LedgerEntry)
            .Where(x => x.CompanyId == query.CompanyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(Math.Clamp(query.ExportLimit, 1, 200))
            .ToListAsync(cancellationToken);

        var providerDtos = _providerRegistry.Providers
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(provider =>
            {
                var connection = connections.FirstOrDefault(x =>
                    string.Equals(x.ProviderKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase));
                return new AccountingAuthorityProviderDto(
                    provider.ProviderKey,
                    provider.DisplayName,
                    connection?.Status == FinanceIntegrationConnectionStatuses.Connected,
                    connection?.Status ?? "not_connected",
                    connection?.LastSyncUtc,
                    connection?.Scopes ?? [],
                    ProviderModeExplanation(current, provider.ProviderKey),
                    connection?.LastErrorSummary);
            })
            .ToArray();

        var periodDtos = periods.Select(ToPeriodDto).ToArray();
        var exportDtos = exports.Select(ToExportDto).ToArray();
        var pending = exportDtos.Count(x => x.Status is
            AccountingProviderExportStatuses.AwaitingApproval or
            AccountingProviderExportStatuses.Approved or
            AccountingProviderExportStatuses.Executing);
        var reconciliation = exportDtos.Count(x => x.Status == AccountingProviderExportStatuses.ReconciliationRequired);

        return new AccountingAuthorityReadModel(
            query.CompanyId,
            current is null ? null : ToPeriodDto(current),
            periodDtos,
            providerDtos,
            exportDtos,
            pending,
            reconciliation,
            AuthorityExplanation(current),
            current?.Authority != AccountingAuthorityValues.Migration);
    }

    public async Task<AccountingAuthorityChangePreview> PreviewChangeAsync(
        PreviewAccountingAuthorityChangeQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        var target = AccountingAuthorityValues.Normalize(query.TargetAuthority);
        if (target == AccountingAuthorityValues.Migration)
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.PeriodBoundaryRequired,
                "Choose Virtual Company or an external provider as the target authority.");

        var providerKey = NormalizeProvider(query.ProviderKey);
        var fiscalPeriod = await _dbContext.FiscalPeriods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.EffectiveFiscalPeriodId, cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.PeriodBoundaryRequired,
                "Select an existing accounting period boundary for the authority change.");
        var effectiveFrom = DateOnly.FromDateTime(fiscalPeriod.StartUtc);
        var effectiveTo = DateOnly.FromDateTime(fiscalPeriod.EndUtc).AddDays(-1);
        var current = await FindPeriodAsync(query.CompanyId, effectiveFrom, tracking: false, cancellationToken);
        var configuration = await _dbContext.AccountingConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.AuthorityNotConfigured,
                "Complete accounting setup before changing accounting authority.");

        var issues = new List<AccountingAuthorityIssueDto>();
        var warnings = new List<AccountingAuthorityIssueDto>();
        if (current is null)
            issues.Add(new(AccountingAuthorityReasonCodes.AuthorityPeriodNotFound,
                "No current authority period covers the selected boundary."));
        else if (current.Authority == AccountingAuthorityValues.Migration)
            issues.Add(new(AccountingAuthorityReasonCodes.ConflictingActivity,
                "Complete the active cutover before starting another authority change.", SubjectId: current.Id));
        else if (current.EffectiveFrom >= effectiveFrom)
            issues.Add(new(AccountingAuthorityReasonCodes.PeriodBoundaryRequired,
                "The new authority must begin at a later accounting-period boundary."));
        else if (current.Authority == target &&
                 string.Equals(current.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase))
            issues.Add(new(AccountingAuthorityReasonCodes.ConflictingActivity,
                "The selected system is already authoritative for this period."));

        if (target == AccountingAuthorityValues.ExternalProvider)
        {
            if (string.IsNullOrWhiteSpace(providerKey))
            {
                issues.Add(new(AccountingAuthorityReasonCodes.ProviderRequired,
                    "Select the external accounting provider that will own the books."));
            }
            else
            {
                _ = _providerRegistry.GetRequired(providerKey);
                var connected = await _dbContext.FinanceIntegrationConnections
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(x => x.CompanyId == query.CompanyId && x.ProviderKey == providerKey &&
                                   x.Status == FinanceIntegrationConnectionStatuses.Connected, cancellationToken);
                if (!connected)
                    issues.Add(new(AccountingAuthorityReasonCodes.ProviderNotConnected,
                        $"Connect {ProviderName(providerKey)} before starting this cutover."));
            }
        }

        var postedCount = await _dbContext.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == query.CompanyId && x.PostingDate >= effectiveFrom, cancellationToken);
        if (target == AccountingAuthorityValues.ExternalProvider && postedCount > 0)
            issues.Add(new(AccountingAuthorityReasonCodes.ConflictingActivity,
                $"{postedCount} committed local journal(s) already exist on or after this boundary. Choose a later period or reconcile them first."));

        var pendingExports = await _dbContext.AccountingProviderExports
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.LedgerEntry)
            .CountAsync(x => x.CompanyId == query.CompanyId && x.LedgerEntry.PostingDate >= effectiveFrom &&
                             x.Status != AccountingProviderExportStatuses.Exported &&
                             x.Status != AccountingProviderExportStatuses.Cancelled, cancellationToken);
        if (pendingExports > 0)
            warnings.Add(new(AccountingAuthorityReasonCodes.ConflictingActivity,
                $"{pendingExports} provider export(s) still need completion or reconciliation.", IsBlocking: false));

        var unmappedSources = target == AccountingAuthorityValues.ExternalProvider && !string.IsNullOrWhiteSpace(providerKey)
            ? await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(entry => entry.CompanyId == query.CompanyId && entry.PostingDate < effectiveFrom &&
                    !_dbContext.FinanceExternalReferences.IgnoreQueryFilters().Any(reference =>
                        reference.CompanyId == query.CompanyId && reference.ProviderKey == providerKey &&
                        reference.InternalRecordId == entry.Id), cancellationToken)
            : 0;
        if (unmappedSources > 0)
            warnings.Add(new(AccountingAuthorityReasonCodes.ConflictingActivity,
                $"{unmappedSources} historical journal(s) do not yet have a provider reference.", IsBlocking: false));

        var version = current?.Version ?? configuration.Version;
        var token = CreatePreviewToken(query.CompanyId, query.EffectiveFiscalPeriodId, target, providerKey,
            current?.Id, version, postedCount, pendingExports, unmappedSources);
        return new AccountingAuthorityChangePreview(
            query.CompanyId,
            current?.Authority ?? configuration.Authority,
            target,
            providerKey,
            query.EffectiveFiscalPeriodId,
            effectiveFrom,
            effectiveTo,
            postedCount,
            pendingExports,
            unmappedSources,
            token,
            version,
            issues.All(x => !x.IsBlocking),
            issues,
            warnings);
    }

    public async Task<AccountingAuthorityReadModel> StartChangeAsync(
        StartAccountingAuthorityChangeCommand command,
        CancellationToken cancellationToken)
    {
        ValidateActor(command.ActorUserId);
        var preview = await PreviewChangeAsync(new(
            command.CompanyId, command.EffectiveFiscalPeriodId, command.TargetAuthority, command.ProviderKey), cancellationToken);
        if (!preview.IsAllowed)
            throw new AccountingAuthorityException(preview.Issues[0].ReasonCode, preview.Issues[0].Explanation, true);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(preview.PreviewToken),
                Encoding.UTF8.GetBytes(command.PreviewToken ?? string.Empty)) ||
            preview.ExpectedCurrentVersion != command.ExpectedCurrentVersion)
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.PreviewStale,
                "The authority impact changed after preview. Review the latest impact before continuing.", true);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var current = await FindPeriodAsync(command.CompanyId, preview.EffectiveFrom, tracking: true, cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.AuthorityPeriodNotFound,
                "The current authority period could not be found.", true);
        if (current.Version != command.ExpectedCurrentVersion)
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ConcurrencyConflict,
                "The accounting authority changed while this request was being reviewed.", true);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        current.EndBefore(preview.EffectiveFrom, command.ActorUserId, now);
        var migration = new AccountingAuthorityPeriod(
            Guid.NewGuid(), command.CompanyId, preview.EffectiveFrom, effectiveTo: null,
            AccountingAuthorityValues.Migration, preview.ProviderKey, command.ActorUserId,
            command.Reason, now, preview.TargetAuthority);
        _dbContext.AccountingAuthorityPeriods.Add(migration);
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        configuration.SetAuthority(AccountingAuthorityValues.Migration, command.ActorUserId, now);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingAuthorityChangeStarted,
            migration.Id, "An accounting-authority cutover was started at an accounting-period boundary.", command.CorrelationId,
            new Dictionary<string, string?>
            {
                ["fromAuthority"] = preview.CurrentAuthority,
                ["targetAuthority"] = preview.TargetAuthority,
                ["providerKey"] = preview.ProviderKey,
                ["effectiveFrom"] = preview.EffectiveFrom.ToString("yyyy-MM-dd"),
                ["previewToken"] = preview.PreviewToken
            }, now, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ConcurrencyConflict,
                "The accounting authority changed while this request was being applied.", true);
        }

        return await GetAsync(new(command.CompanyId), cancellationToken);
    }

    public async Task<AccountingAuthorityReadModel> RecordCutoverValidationAsync(
        RecordAccountingCutoverValidationCommand command,
        CancellationToken cancellationToken)
    {
        ValidateActor(command.ActorUserId);
        var period = await _dbContext.AccountingAuthorityPeriods.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.AuthorityPeriodId, cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.AuthorityPeriodNotFound,
                "The authority cutover could not be found.");
        if (period.Version != command.ExpectedVersion)
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ConcurrencyConflict,
                "The cutover changed while it was being reviewed.", true);

        var nativeConflicts = period.TargetAuthority == AccountingAuthorityValues.ExternalProvider
            ? await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == command.CompanyId && x.PostingDate >= period.EffectiveFrom, cancellationToken)
            : 0;
        var conflicts = checked(command.ConflictCount + nativeConflicts);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        period.RecordCutoverValidation(command.OpeningBalancesReconciled, command.TrialBalanceReconciled,
            command.SourceMappingsReconciled, conflicts, command.Summary, command.ActorUserId, now);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingAuthorityCutoverValidated,
            period.Id, period.IsCutoverReady ? "The authority cutover passed its reconciliation checks." :
                "The authority cutover still has blocking reconciliation checks.", command.CorrelationId,
            new Dictionary<string, string?>
            {
                ["openingBalancesReconciled"] = period.OpeningBalancesReconciled.ToString(),
                ["trialBalanceReconciled"] = period.TrialBalanceReconciled.ToString(),
                ["sourceMappingsReconciled"] = period.SourceMappingsReconciled.ToString(),
                ["conflictCount"] = period.ConflictCount.ToString()
            }, now, cancellationToken);
        await SaveWithConcurrencyMappingAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId), cancellationToken);
    }

    public async Task<AccountingAuthorityReadModel> CompleteCutoverAsync(
        CompleteAccountingAuthorityCutoverCommand command,
        CancellationToken cancellationToken)
    {
        ValidateActor(command.ActorUserId);
        var period = await _dbContext.AccountingAuthorityPeriods.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.AuthorityPeriodId, cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.AuthorityPeriodNotFound,
                "The authority cutover could not be found.");
        if (period.Version != command.ExpectedVersion)
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ConcurrencyConflict,
                "The cutover changed while completion was being requested.", true);
        if (!period.IsCutoverReady || period.TargetAuthority is null)
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.CutoverIncomplete,
                "Reconcile opening balances, the trial balance, source mappings, and all conflicts before completing cutover.", true);

        var target = period.TargetAuthority;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        period.CompleteCutover(command.ActorUserId, now);
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        configuration.SetAuthority(target, command.ActorUserId, now);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingAuthorityCutoverCompleted,
            period.Id, "The accounting-authority cutover completed after all reconciliation checks passed.", command.CorrelationId,
            new Dictionary<string, string?>
            {
                ["authority"] = target,
                ["providerKey"] = period.ProviderKey,
                ["effectiveFrom"] = period.EffectiveFrom.ToString("yyyy-MM-dd")
            }, now, cancellationToken);
        await SaveWithConcurrencyMappingAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId), cancellationToken);
    }

    private async Task SaveWithConcurrencyMappingAsync(CancellationToken cancellationToken)
    {
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw new AccountingAuthorityException(AccountingAuthorityReasonCodes.ConcurrencyConflict,
                "The accounting authority changed while this request was being applied.", true);
        }
    }

    private Task<AccountingAuthorityPeriod?> FindPeriodAsync(
        Guid companyId, DateOnly date, bool tracking, CancellationToken cancellationToken)
    {
        var query = _dbContext.AccountingAuthorityPeriods.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.EffectiveFrom <= date &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date));
        if (!tracking) query = query.AsNoTracking();
        return query.OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(cancellationToken);
    }

    private AccountingAuthorityPeriodDto ToPeriodDto(AccountingAuthorityPeriod period) =>
        new(period.Id, period.EffectiveFrom, period.EffectiveTo, period.Authority, AuthorityLabel(period.Authority),
            period.TargetAuthority, period.TargetAuthority is null ? null : AuthorityLabel(period.TargetAuthority),
            period.ProviderKey, period.ProviderKey is null ? null : ProviderName(period.ProviderKey), period.ChangeReason,
            period.OpeningBalancesReconciled, period.TrialBalanceReconciled, period.SourceMappingsReconciled,
            period.ConflictCount, period.ValidationSummary, period.IsCutoverReady, period.Version, period.UpdatedUtc, period.CompletedUtc);

    private AccountingProviderExportDto ToExportDto(AccountingProviderExport export) =>
        new(export.Id, export.LedgerEntryId, export.LedgerEntry.EntryNumber,
            export.LedgerEntry.PostingDate ?? DateOnly.FromDateTime(export.LedgerEntry.EntryUtc),
            export.SourceType, export.SourceId, export.SourceVersion, export.ProviderKey, ProviderName(export.ProviderKey),
            export.Status, ExportStatusLabel(export.Status), export.WriteRequestId, export.ApprovalRequestId,
            export.FailureCategory, export.SafeSummary, export.ProviderExternalId, export.AttemptCount, export.Version, export.UpdatedUtc);

    private string ProviderName(string? providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerKey)) return "The external provider";
        try { return _providerRegistry.GetRequired(providerKey).DisplayName; }
        catch (FinanceIntegrationProviderNotFoundException) { return providerKey; }
    }

    private static string AuthorityLabel(string authority) => authority switch
    {
        AccountingAuthorityValues.InternalLedger => "Virtual Company",
        AccountingAuthorityValues.ExternalProvider => "External provider",
        AccountingAuthorityValues.Migration => "Cutover in progress",
        _ => "Not configured"
    };

    private string AuthorityExplanation(AccountingAuthorityPeriod? period) => period?.Authority switch
    {
        AccountingAuthorityValues.InternalLedger => "Virtual Company owns the books for this period. Approved provider actions are downstream exports and cannot change the committed local journal.",
        AccountingAuthorityValues.ExternalProvider => $"{ProviderName(period.ProviderKey)} owns the books for this period. Virtual Company imports accounting results for visibility without creating duplicate authoritative journals.",
        AccountingAuthorityValues.Migration => $"Accounting actions are limited while the cutover to {AuthorityLabel(period.TargetAuthority ?? string.Empty)} is reconciled.",
        _ => "Accounting authority has not been configured for the current period."
    };

    private string ProviderModeExplanation(AccountingAuthorityPeriod? period, string providerKey)
    {
        if (period?.Authority == AccountingAuthorityValues.ExternalProvider &&
            string.Equals(period.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase))
            return "Authoritative provider; imports remain read-only inside Virtual Company.";
        if (period?.Authority == AccountingAuthorityValues.Migration &&
            string.Equals(period.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase))
            return "Cutover reconciliation only; normal posting and export are paused.";
        return "Approved export destination; Virtual Company remains authoritative.";
    }

    private static string ExportStatusLabel(string status) => status switch
    {
        AccountingProviderExportStatuses.AwaitingApproval => "Waiting for approval",
        AccountingProviderExportStatuses.Approved => "Approved",
        AccountingProviderExportStatuses.Executing => "Sending",
        AccountingProviderExportStatuses.Exported => "Exported",
        AccountingProviderExportStatuses.ReconciliationRequired => "Needs reconciliation",
        AccountingProviderExportStatuses.Failed => "Failed",
        AccountingProviderExportStatuses.Cancelled => "Cancelled",
        _ => "Needs review"
    };

    private static string CreatePreviewToken(Guid companyId, Guid fiscalPeriodId, string target, string? provider,
        Guid? currentId, long version, int journals, int exports, int unmapped)
    {
        var value = $"{companyId:N}|{fiscalPeriodId:N}|{target}|{provider}|{currentId:N}|{version}|{journals}|{exports}|{unmapped}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private Task WriteAuditAsync(Guid companyId, Guid actorUserId, string action, Guid periodId, string summary,
        string? correlationId, IReadOnlyDictionary<string, string?> metadata, DateTime occurredUtc,
        CancellationToken cancellationToken) =>
        _auditWriter.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorUserId, action,
            AuditTargetTypes.AccountingAuthority, periodId.ToString("D"), AuditEventOutcomes.Succeeded, summary,
            ["accounting_authority", "fiscal_period", "finance_integration"], metadata, correlationId, occurredUtc),
            cancellationToken);

    private static void ValidateCompany(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
    }

    private static string? NormalizeProvider(string? providerKey) =>
        string.IsNullOrWhiteSpace(providerKey) ? null : providerKey.Trim().ToLowerInvariant();
}
