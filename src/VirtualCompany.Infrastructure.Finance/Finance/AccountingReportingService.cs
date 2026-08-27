using System.Globalization;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingReportingService : IAccountingReportingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipResolver;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ICompanyDocumentStorage _documentStorage;
    private readonly TimeProvider _timeProvider;
    private readonly AccountingOperationsTelemetry? _telemetry;

    public AccountingReportingService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipResolver,
        ICurrentUserAccessor currentUser,
        IAccountingPolicyPackResolver packResolver,
        IAuditEventWriter auditWriter,
        ICompanyDocumentStorage documentStorage,
        TimeProvider timeProvider,
        AccountingOperationsTelemetry? telemetry = null)
    {
        _dbContext = dbContext;
        _membershipResolver = membershipResolver;
        _currentUser = currentUser;
        _packResolver = packResolver;
        _auditWriter = auditWriter;
        _documentStorage = documentStorage;
        _timeProvider = timeProvider;
        _telemetry = telemetry;
    }

    public async Task<GeneralLedgerReportDto> GetGeneralLedgerAsync(GetGeneralLedgerQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "succeeded";
        try
        {
            await RequireViewAsync(query.CompanyId, cancellationToken);
            var period = await LoadPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 25, 1_000);
            var accountSeeds = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && (!query.FinanceAccountId.HasValue || x.Id == query.FinanceAccountId.Value))
                .OrderBy(x => x.Code)
                .Select(x => new AccountSeed(x.Id, x.Code, x.Name, x.AccountClass, x.Currency, x.OpeningBalance))
                .ToListAsync(cancellationToken);

            var priorMovements = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                    x.LedgerEntry.FiscalPeriod.StartUtc < period.StartUtc &&
                    (!query.FinanceAccountId.HasValue || x.FinanceAccountId == query.FinanceAccountId.Value))
                .GroupBy(x => x.FinanceAccountId)
                .Select(x => new AccountMovement(x.Key, x.Sum(y => y.DebitAmount), x.Sum(y => y.CreditAmount), x.Count()))
                .ToListAsync(cancellationToken);
            var periodQuery = _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                    x.LedgerEntry.FiscalPeriodId == period.Id &&
                    (!query.FinanceAccountId.HasValue || x.FinanceAccountId == query.FinanceAccountId.Value));
            var periodMovements = await periodQuery.GroupBy(x => x.FinanceAccountId)
                .Select(x => new AccountMovement(x.Key, x.Sum(y => y.DebitAmount), x.Sum(y => y.CreditAmount), x.Count()))
                .ToListAsync(cancellationToken);
            var totalLineCount = periodMovements.Sum(x => (long)x.Count);

            var skip = (page - 1) * pageSize;
            var ordered = query.FinanceAccountId.HasValue
                ? periodQuery.OrderBy(x => x.LedgerEntry.PostingDate)
                    .ThenBy(x => x.LedgerEntry.EntryNumber).ThenBy(x => x.Id)
                : periodQuery.OrderBy(x => x.FinanceAccount.Code)
                    .ThenBy(x => x.LedgerEntry.PostingDate)
                    .ThenBy(x => x.LedgerEntry.EntryNumber).ThenBy(x => x.Id);
            var priorPageMovements = skip == 0
                ? new Dictionary<Guid, decimal>()
                : (await ordered.Take(skip)
                    .GroupBy(x => x.FinanceAccountId)
                    .Select(x => new { AccountId = x.Key, Movement = x.Sum(y => y.DebitAmount - y.CreditAmount) })
                    .ToListAsync(cancellationToken))
                    .ToDictionary(x => x.AccountId, x => x.Movement);
            var rawRows = await ordered.Skip(skip).Take(pageSize)
                .Select(x => new RawLedgerRow(x.Id, x.LedgerEntryId, x.LedgerEntry.FiscalPeriodId,
                    x.FinanceAccountId, x.FinanceAccount.Code, x.FinanceAccount.Name,
                    x.FinanceAccount.AccountClass, x.FinanceAccount.Currency, x.FinanceAccount.OpeningBalance,
                    x.LedgerEntry.EntryNumber, x.LedgerEntry.PostingDate, x.LedgerEntry.EntryUtc,
                    x.Description ?? x.LedgerEntry.Description, x.DebitAmount, x.CreditAmount, x.Currency,
                    x.LedgerEntry.SourceType, x.LedgerEntry.SourceId, x.LedgerEntry.SourceVersion,
                    x.LedgerEntry.OriginalLedgerEntryId))
                .ToListAsync(cancellationToken);
            var rows = rawRows.Select(ToLedgerRow).ToList();

            var evidence = await LoadEvidenceAsync(query.CompanyId,
                rows.Select(x => x.EntryId).Distinct().ToArray(), cancellationToken);
            var priorLookup = priorMovements.ToDictionary(x => x.AccountId);
            var periodLookup = periodMovements.ToDictionary(x => x.AccountId);
            var accounts = accountSeeds.Select(account =>
            {
                var prior = priorLookup.GetValueOrDefault(account.Id);
                var movement = periodLookup.GetValueOrDefault(account.Id);
                var opening = account.OpeningBalance + (prior?.Debit ?? 0m) - (prior?.Credit ?? 0m);
                var running = opening + priorPageMovements.GetValueOrDefault(account.Id);
                var lines = rows.Where(x => x.AccountId == account.Id).Select(row =>
                {
                    running += row.Debit - row.Credit;
                    return new GeneralLedgerLineDto(row.LineId, row.EntryId, row.VoucherNumber, row.PostingDate,
                        row.Description, row.Debit, row.Credit, running, row.LineCurrency, row.SourceType,
                        row.SourceId, row.SourceVersion, row.OriginalEntryId,
                        evidence.GetValueOrDefault(row.EntryId, []));
                }).ToArray();
                var debit = movement?.Debit ?? 0m;
                var credit = movement?.Credit ?? 0m;
                return new GeneralLedgerAccountDto(account.Id, account.Code, account.Name,
                    account.AccountClass ?? "unclassified", account.Currency, opening, debit, credit,
                    opening + debit - credit, movement?.Count ?? 0, lines);
            }).ToArray();

            return new GeneralLedgerReportDto(query.CompanyId, period.Id, period.Name, period.StartUtc, period.EndUtc,
                period.IsClosed, period.IsReportingLocked, period.IsClosed ? "stored-period-evidence" : "posted-journals",
                accounts, page, pageSize, totalLineCount,
                query.FinanceAccountId.HasValue && (long)page * pageSize < totalLineCount);
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            _telemetry?.OperationCompleted(query.CompanyId, "general_ledger_page",
                Stopwatch.GetElapsedTime(started), 1_200, outcome);
        }
    }

    public async Task<TrialBalanceReportDto> GetTrialBalanceAsync(GetTrialBalanceQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "completed";
        try
        {
            var ledger = await GetGeneralLedgerAsync(new GetGeneralLedgerQuery(query.CompanyId, query.FiscalPeriodId), cancellationToken);
            var accounts = ledger.Accounts.Select(x => new TrialBalanceAccountDto(x.AccountId, x.AccountCode, x.AccountName,
                x.AccountClass, x.Currency, x.OpeningBalance, x.Debit, x.Credit, x.ClosingBalance, x.TotalLineCount)).ToArray();
            var checksum = Checksum(accounts.OrderBy(x => x.AccountCode).Select(x =>
                $"{x.AccountId:N}|{Amount(x.OpeningBalance)}|{Amount(x.Debit)}|{Amount(x.Credit)}|{Amount(x.ClosingBalance)}|{x.Currency}"));
            var openingDebits = accounts.Where(x => x.OpeningBalance >= 0).Sum(x => x.OpeningBalance);
            var openingCredits = accounts.Where(x => x.OpeningBalance < 0).Sum(x => -x.OpeningBalance);
            var closingDebits = accounts.Where(x => x.ClosingBalance >= 0).Sum(x => x.ClosingBalance);
            var closingCredits = accounts.Where(x => x.ClosingBalance < 0).Sum(x => -x.ClosingBalance);
            var debits = accounts.Sum(x => x.Debit);
            var credits = accounts.Sum(x => x.Credit);
            return new TrialBalanceReportDto(ledger.CompanyId, ledger.FiscalPeriodId, ledger.FiscalPeriodName,
                ledger.PeriodStartUtc, ledger.PeriodEndUtc, ledger.IsClosed, ledger.IsReportingLocked, ledger.SourceMode,
                checksum, openingDebits, openingCredits, debits, credits, closingDebits, closingCredits,
                decimal.Round(debits - credits, 2) == 0m && decimal.Round(closingDebits - closingCredits, 2) == 0m, accounts);
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            _telemetry?.OperationCompleted(query.CompanyId, "trial_balance",
                Stopwatch.GetElapsedTime(started), 1_500, outcome);
        }
    }

    public async Task<AccountingTaxSummaryDto> GetTaxSummaryAsync(GetAccountingTaxSummaryQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var period = await LoadPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
        var rows = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.LedgerEntry.FiscalPeriodId == period.Id &&
                        x.LedgerEntry.Status == LedgerEntryStatuses.Posted && x.TaxFactsJson != null)
            .Select(x => new TaxFactRow(x.LedgerEntryId, x.LedgerEntry.PolicyPackKey, x.LedgerEntry.PolicyPackVersion,
                x.Currency, x.DebitAmount, x.CreditAmount, x.TaxFactsJson!))
            .ToListAsync(cancellationToken);

        var parsed = rows.Select(ParseTaxFact).Where(x => x is not null).Cast<ParsedTaxFact>().ToArray();
        var lines = parsed.GroupBy(x => new { x.PolicyPackKey, x.PolicyPackVersion, x.TaxRuleKey, x.TaxTreatment, x.Currency })
            .OrderBy(x => x.Key.PolicyPackKey).ThenBy(x => x.Key.PolicyPackVersion).ThenBy(x => x.Key.TaxRuleKey)
            .Select(x => new AccountingTaxSummaryLineDto(x.Key.PolicyPackKey, x.Key.PolicyPackVersion,
                x.Key.TaxRuleKey, x.Key.TaxTreatment, x.Sum(y => y.TaxableAmount), x.Sum(y => y.TaxAmount),
                x.Key.Currency, x.Count(), x.Select(y => y.EntryId).Distinct().Order().ToArray())).ToArray();
        var checksum = Checksum(lines.Select(x =>
            $"{x.PolicyPackKey}|{x.PolicyPackVersion}|{x.TaxRuleKey}|{x.TaxTreatment}|{Amount(x.TaxableAmount)}|{Amount(x.TaxAmount)}|{x.Currency}"));

        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        var currentPack = configuration is null ? null : _packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out var pack) ? pack : null;
        var countryNeutral = currentPack?.Definition.IsCountryNeutral ?? true;
        var statutory = currentPack?.Definition.IsStatutoryComplianceValidated == true &&
                        currentPack.Definition.SupportedCapabilities.Contains(AccountingPolicyCapabilityKeys.CountrySpecificTax, StringComparer.OrdinalIgnoreCase);
        var review = await _dbContext.AccountingTaxReviews.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.FiscalPeriodId == period.Id, cancellationToken);
        var reviewed = review is not null && string.Equals(review.Checksum, checksum, StringComparison.OrdinalIgnoreCase);
        return new AccountingTaxSummaryDto(query.CompanyId, period.Id, period.Name, countryNeutral, statutory,
            statutory ? "Validated tax summary" : "Country-neutral bookkeeping tax summary",
            statutory ? currentPack!.Definition.ComplianceNotice : "Bookkeeping information only — not a statutory return.",
            checksum, reviewed, reviewed ? review!.ReviewedByUserId : null, reviewed ? review!.ReviewedUtc : null, lines);
    }

    public async Task<AccountingTaxSummaryDto> ReviewTaxSummaryAsync(ReviewAccountingTaxSummaryCommand command, CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var summary = await GetTaxSummaryAsync(new GetAccountingTaxSummaryQuery(command.CompanyId, command.FiscalPeriodId), cancellationToken);
        var review = await _dbContext.AccountingTaxReviews.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.FiscalPeriodId == command.FiscalPeriodId, cancellationToken);
        if (review is not null && string.Equals(review.Checksum, summary.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return summary with { IsReviewed = true, ReviewedByUserId = review.ReviewedByUserId, ReviewedUtc = review.ReviewedUtc };
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var json = JsonSerializer.Serialize(summary with { IsReviewed = true, ReviewedByUserId = command.ActorUserId, ReviewedUtc = now });
        if (review is null)
        {
            _dbContext.AccountingTaxReviews.Add(new AccountingTaxReview(Guid.NewGuid(), command.CompanyId,
                command.FiscalPeriodId, json, summary.Checksum, command.ActorUserId, now));
        }
        else
        {
            review.Replace(json, summary.Checksum, command.ActorUserId, now);
        }

        await _auditWriter.WriteAsync(new AuditEventWriteRequest(
            command.CompanyId,
            AuditActorTypes.User,
            command.ActorUserId,
            AuditEventActions.AccountingTaxSummaryReviewed,
            AuditTargetTypes.FiscalPeriod,
            command.FiscalPeriodId.ToString("D"),
            AuditEventOutcomes.Succeeded,
            "Reviewed the period tax summary against its current posted-journal checksum.",
            Metadata: new Dictionary<string, string?>
            {
                ["checksum"] = summary.Checksum,
                ["isCountryNeutral"] = summary.IsCountryNeutral ? "true" : "false",
                ["isStatutoryComplianceValidated"] = summary.IsStatutoryComplianceValidated ? "true" : "false"
            },
            OccurredUtc: now), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return summary with { IsReviewed = true, ReviewedByUserId = command.ActorUserId, ReviewedUtc = now };
    }

    public async Task<ControlAccountReconciliationDto> GetControlAccountReconciliationAsync(GetControlAccountReconciliationQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var period = await LoadPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
        var roles = await _dbContext.AccountingConfigurationAccountRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && (x.RoleKey == AccountingAccountRoleKeys.AccountsReceivable ||
                x.RoleKey == AccountingAccountRoleKeys.AccountsPayable || x.RoleKey == AccountingAccountRoleKeys.Bank))
            .Select(x => new { x.RoleKey, x.FinanceAccountId, x.FinanceAccount.Code, x.FinanceAccount.Name, x.FinanceAccount.Currency, x.FinanceAccount.OpeningBalance })
            .ToListAsync(cancellationToken);
        var accountIds = roles.Select(x => x.FinanceAccountId).ToArray();
        var postings = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && accountIds.Contains(x.FinanceAccountId) &&
                        x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                        x.LedgerEntry.FiscalPeriod.StartUtc < period.EndUtc)
            .Select(x => new { x.FinanceAccountId, x.LedgerEntryId, x.LedgerEntry.SourceType, Signed = x.DebitAmount - x.CreditAmount })
            .ToListAsync(cancellationToken);
        var result = roles.Select(role =>
        {
            var accountRows = postings.Where(x => x.FinanceAccountId == role.FinanceAccountId).ToArray();
            var acceptedSources = role.RoleKey switch
            {
                AccountingAccountRoleKeys.AccountsReceivable => new[] { "customer_invoice", "customer_credit_note", "payment", "payment_allocation", "payment_settlement" },
                AccountingAccountRoleKeys.AccountsPayable => new[] { "supplier_bill", "supplier_credit_note", "payment", "payment_allocation", "payment_settlement" },
                _ => new[] { "bank_transaction", "payment", "payment_allocation", "payment_settlement", "cash_settlement" }
            };
            var ledger = role.OpeningBalance + accountRows.Sum(x => x.Signed);
            var source = role.OpeningBalance + accountRows.Where(x => x.SourceType is not null && acceptedSources.Contains(x.SourceType, StringComparer.OrdinalIgnoreCase)).Sum(x => x.Signed);
            var difference = decimal.Round(ledger - source, 2);
            return new ControlAccountReconciliationLineDto(role.RoleKey, role.FinanceAccountId, role.Code, role.Name,
                role.Currency, ledger, source, difference, difference == 0m,
                accountRows.Where(x => x.SourceType is null || !acceptedSources.Contains(x.SourceType, StringComparer.OrdinalIgnoreCase)).Select(x => x.LedgerEntryId).Distinct().ToArray());
        }).ToArray();
        return new ControlAccountReconciliationDto(query.CompanyId, period.Id, result.All(x => x.IsReconciled), result);
    }

    public async Task<IReadOnlyList<AccountingPeriodHistoryDto>> GetPeriodHistoryAsync(Guid companyId, Guid fiscalPeriodId, CancellationToken cancellationToken)
    {
        await RequireViewAsync(companyId, cancellationToken);
        return await _dbContext.AccountingPeriodHistory.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId)
            .OrderByDescending(x => x.OccurredUtc)
            .Select(x => new AccountingPeriodHistoryDto(x.Id, x.Action, x.ActorUserId, x.Reason, x.SnapshotChecksum, x.OccurredUtc))
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountingExportJobDto> RequestExportAsync(RequestAccountingExportCommand command, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "completed";
        try
        {
            await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
            var period = await LoadPeriodAsync(command.CompanyId, command.FiscalPeriodId, cancellationToken);
            string exportType;
            try
            {
                exportType = AccountingExportTypeValues.Normalize(command.ExportType);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new AccountingExportException("accounting_export_type_unsupported",
                    "The requested accounting export type is not supported.");
            }
            if (AccountingExportTypeValues.IsSwedishStatutory(exportType))
            {
                var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken)
                    ?? throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePolicyHistory,
                        "An accounting configuration and selected Swedish policy pack are required for statutory export.");
                if (!_packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out var pack) || pack is null)
                    throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePolicyHistory,
                        "The selected accounting policy pack is unavailable for statutory export.");
                if (!pack.Definition.SupportedExports.Contains(exportType, StringComparer.OrdinalIgnoreCase))
                    throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePolicyHistory,
                        "The selected accounting policy pack does not support the requested Swedish statutory export.");
                if (!period.IsClosed || !period.IsReportingLocked)
                    throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePeriod,
                        "A Swedish statutory export requires a closed and reporting-locked fiscal period.");
            }
            var key = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 200);
            var existing = await _dbContext.AccountingExportJobs.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == key, cancellationToken);
            if (existing is not null)
            {
                if (existing.FiscalPeriodId != command.FiscalPeriodId ||
                    !string.Equals(existing.ExportType, exportType, StringComparison.Ordinal))
                    throw new AccountingExportException("accounting_export_idempotency_conflict",
                        "The idempotency key is already bound to different accounting export inputs.", isConflict: true);
                outcome = "replayed";
                return MapExport(existing);
            }
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var job = new AccountingExportJob(Guid.NewGuid(), command.CompanyId, command.FiscalPeriodId,
                command.ActorUserId, key, now, now.AddDays(30), exportType, command.CorrelationId);
            _dbContext.AccountingExportJobs.Add(job);

            await _auditWriter.WriteAsync(new AuditEventWriteRequest(
                command.CompanyId,
                AuditActorTypes.User,
                command.ActorUserId,
                AuditEventActions.AccountingExportRequested,
                AuditTargetTypes.AccountingExport,
                job.Id.ToString("D"),
                AuditEventOutcomes.Succeeded,
                AccountingExportTypeValues.IsSwedishStatutory(exportType)
                    ? "Requested a durable Swedish statutory accounting export."
                    : "Requested a durable country-neutral accounting export.",
                Metadata: new Dictionary<string, string?>
                {
                    ["fiscalPeriodId"] = command.FiscalPeriodId.ToString("D"),
                    ["exportType"] = exportType,
                    ["retentionExpiresUtc"] = job.ExpiresUtc.ToString("O")
                },
                CorrelationId: command.CorrelationId,
                OccurredUtc: now), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MapExport(job);
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            _telemetry?.OperationCompleted(command.CompanyId, "export_request",
                Stopwatch.GetElapsedTime(started), 500, outcome);
        }
    }

    public async Task<IReadOnlyList<AccountingExportJobDto>> ListExportsAsync(ListAccountingExportsQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var jobs = await _dbContext.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && (!query.FiscalPeriodId.HasValue || x.FiscalPeriodId == query.FiscalPeriodId))
            .OrderByDescending(x => x.RequestedUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return jobs.Select(MapExport).ToArray();
    }

    public async Task<AccountingExportDownloadDto> DownloadExportAsync(GetAccountingExportQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var job = await _dbContext.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.ExportId, cancellationToken)
            ?? throw new KeyNotFoundException("Accounting export was not found in the requested company.");
        if (job.Status != AccountingExportStatuses.Completed || (job.Content is null && job.StorageKey is null) || job.ExpiresUtc <= _timeProvider.GetUtcNow().UtcDateTime)
            throw new InvalidOperationException("The accounting export is not available for download.");
        byte[] content;
        if (job.Content is not null)
        {
            content = job.Content;
        }
        else
        {
            await using var stream = await _documentStorage.OpenReadAsync(job.StorageKey!, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }
        var actualChecksum = Sha256(content);
        if (!string.Equals(actualChecksum, job.Checksum, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The accounting export content failed checksum verification and cannot be downloaded.");
        return new AccountingExportDownloadDto(job.FileName!, job.MediaType!, content, job.Checksum!);
    }

    public async Task<int> RunDueExportsAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var jobs = await _dbContext.AccountingExportJobs.IgnoreQueryFilters()
            .Where(x => (x.Status == AccountingExportStatuses.Queued && (!x.NextAttemptUtc.HasValue || x.NextAttemptUtc <= now)) ||
                (x.Status == AccountingExportStatuses.Running && x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.RequestedUtc).Take(5).ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            var started = Stopwatch.GetTimestamp();
            var outcome = "completed";
            job.Start($"{Environment.MachineName}:{Guid.NewGuid():N}", now.AddMinutes(5), now);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                outcome = "claim_conflict";
                _dbContext.Entry(job).State = EntityState.Detached;
                _telemetry?.OperationCompleted(job.CompanyId, "export_claim",
                    Stopwatch.GetElapsedTime(started), 500, outcome);
                continue;
            }
            try
            {
                var artifact = await BuildExportAsync(job, cancellationToken);
                var checksum = Sha256(artifact.Content);
                string? storageKey = null;
                byte[]? databaseContent = artifact.Content;
                if (artifact.StoreInObjectStorage)
                {
                    storageKey = $"companies/{job.CompanyId:N}/finance/accounting-exports/{job.Id:N}/{artifact.FileName}";
                    await using var contentStream = new MemoryStream(artifact.Content, writable: false);
                    var write = await _documentStorage.WriteAsync(new DocumentStorageWriteRequest(job.CompanyId, job.Id,
                        storageKey, artifact.FileName, artifact.MediaType, contentStream), cancellationToken);
                    storageKey = write.StorageKey;
                    databaseContent = null;
                }
                job.Complete(databaseContent, checksum, artifact.FileName, artifact.MediaType, storageKey,
                    artifact.SpecificationVersion, artifact.InputChecksum, artifact.EncodingName,
                    artifact.AccountCount, artifact.JournalCount, artifact.LineCount,
                    artifact.DebitTotal, artifact.CreditTotal, artifact.ManifestJson,
                    _timeProvider.GetUtcNow().UtcDateTime);
                job.SetStoredContentLength(artifact.Content.LongLength);
            }
            catch (Sie4BValidationException ex)
            {
                outcome = "failed_permanent";
                job.Fail(ex.ReasonCode, ex.Message, _timeProvider.GetUtcNow().UtcDateTime);
            }
            catch (IOException)
            {
                outcome = job.AttemptCount < 3 ? "retry_scheduled" : "failed";
                var failedUtc = _timeProvider.GetUtcNow().UtcDateTime;
                if (job.AttemptCount < 3) job.Retry("export_storage_ambiguous", "The export object write could not be confirmed. The stable object key will be reconciled on retry.", failedUtc.AddMinutes(job.AttemptCount), failedUtc);
                else job.Fail("export_storage_ambiguous", "The export object write could not be confirmed after three attempts. Operator reconciliation is required.", failedUtc);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = job.AttemptCount < 3 ? "retry_scheduled" : "failed";
                var failedUtc = _timeProvider.GetUtcNow().UtcDateTime;
                if (job.AttemptCount < 3) job.Retry("export_generation_failed", "The export could not be generated. It will be retried.", failedUtc.AddMinutes(job.AttemptCount), failedUtc);
                else job.Fail("export_generation_failed", "The export could not be generated after three attempts.", failedUtc);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            _telemetry?.OperationCompleted(job.CompanyId, "export_completion",
                _timeProvider.GetUtcNow().UtcDateTime - job.RequestedUtc,
                TimeSpan.FromMinutes(5).TotalMilliseconds, outcome);
        }
        return jobs.Count;
    }

    private async Task<ExportArtifact> BuildExportAsync(AccountingExportJob job, CancellationToken cancellationToken)
    {
        if (job.ExportType == AccountingExportTypeValues.GenericJson)
        {
            var content = await BuildGenericJsonAsync(job.CompanyId, job.FiscalPeriodId, job.RequestedUtc, cancellationToken);
            return GenericArtifact(content, job, "json", "application/json", "utf-8");
        }
        if (job.ExportType == AccountingExportTypeValues.GenericCsv)
        {
            var content = await BuildGenericCsvAsync(job.CompanyId, job.FiscalPeriodId, cancellationToken);
            return GenericArtifact(content, job, "csv", "text/csv; charset=utf-8", "utf-8");
        }

        var source = await BuildSieSourceAsync(job.CompanyId, job.FiscalPeriodId, job.RequestedUtc, cancellationToken);
        var sourceManifest = await BuildStatutoryManifestAsync(job, source, cancellationToken);
        var inputChecksum = Sha256(JsonSerializer.SerializeToUtf8Bytes(new { source, sourceManifest }));
        var sie = new Sie4BSerializer().Serialize(source);
        if (job.ExportType == AccountingExportTypeValues.Sie4B)
        {
            var manifestJson = JsonSerializer.Serialize(sourceManifest, JsonOptions);
            return new ExportArtifact(sie.Content, $"accounting-{source.FinancialYearStart:yyyyMMdd}-{source.FinancialYearEnd:yyyyMMdd}.se",
                "application/x-sie", Sie4BSerializer.SpecificationVersion, inputChecksum, Sie4BSerializer.EncodingName,
                sie.AccountCount, sie.VoucherCount, sie.TransactionCount, sie.DebitTotal, sie.CreditTotal,
                manifestJson, true);
        }
        if (job.ExportType != AccountingExportTypeValues.SwedishStatutoryArchive)
            throw new ArgumentOutOfRangeException(nameof(job.ExportType));

        var archive = BuildStatutoryArchive(sie.Content, sourceManifest, source.FinancialYearEnd);
        return new ExportArtifact(archive.Content,
            $"statutory-accounting-archive-{source.FinancialYearStart:yyyyMMdd}-{source.FinancialYearEnd:yyyyMMdd}.zip",
            "application/zip", "Virtual Company Swedish statutory archive 1.0; SIE 4B 2008-09-30",
            inputChecksum, "zip (SIE entry uses IBM PC8/code page 437)", sie.AccountCount, sie.VoucherCount,
            sie.TransactionCount, sie.DebitTotal, sie.CreditTotal, archive.ManifestJson, true);
    }

    private async Task<byte[]> BuildGenericJsonAsync(Guid companyId, Guid fiscalPeriodId, DateTime generatedAtUtc, CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies.AsNoTracking().SingleAsync(x => x.Id == companyId, cancellationToken);
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == companyId, cancellationToken);
        var selections = await _dbContext.AccountingPolicyPackSelections.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.EffectiveFrom).ToListAsync(cancellationToken);
        var periods = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.StartUtc).ToListAsync(cancellationToken);
        var accounts = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(cancellationToken);
        var entries = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId && x.Status == LedgerEntryStatuses.Posted).OrderBy(x => x.EntryUtc).ThenBy(x => x.EntryNumber).ToListAsync(cancellationToken);
        var entryIds = entries.Select(x => x.Id).ToArray();
        var lines = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && entryIds.Contains(x.LedgerEntryId)).OrderBy(x => x.LedgerEntryId).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var parties = await _dbContext.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var attachments = await _dbContext.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && entryIds.Contains(x.LedgerEntryId)).OrderBy(x => x.LedgerEntryId).ThenBy(x => x.DocumentId).ToListAsync(cancellationToken);
        var export = new
        {
            schema = "virtual-company.country-neutral-accounting-export",
            version = "1.0",
            generatedAtUtc,
            company = new { company.Id, company.Name },
            configuration = new { configuration.BaseCurrency, configuration.Authority, configuration.PolicyPackKey, configuration.PolicyPackVersion, configuration.PolicyPackEffectiveFrom, configuration.RoundingPrecision, configuration.RoundingMode },
            policyPackHistory = selections.Select(x => new { x.PackKey, x.PackVersion, x.DefinitionHash, x.EffectiveFrom, x.EffectiveTo }),
            accounts = accounts.Select(x => new { x.Id, x.Code, x.Name, x.AccountClass, x.NormalBalance, x.Currency, x.ControlAccountRole, x.EffectiveFrom, x.EffectiveTo }),
            periods = periods.Select(x => new { x.Id, x.Name, x.StartUtc, x.EndUtc, x.IsClosed, x.IsReportingLocked }),
            vouchers = entries.Select(x => new { x.Id, x.EntryNumber, x.DocumentDate, x.PostingDate, x.BaseCurrency, x.PostingType, x.SourceType, x.SourceId, x.SourceVersion, x.PolicyPackKey, x.PolicyPackVersion, x.OriginalLedgerEntryId, x.CorrectionReason }),
            lines = lines.Select(x => new { x.Id, x.LedgerEntryId, x.FinanceAccountId, x.DebitAmount, x.CreditAmount, x.Currency, x.Description, taxFacts = ParseJson(x.TaxFactsJson), dimensionFacts = ParseJson(x.DimensionFactsJson) }),
            parties = parties.Select(x => new { x.Id, x.Name, x.CounterpartyType, x.TaxId }),
            attachmentManifest = attachments.Select(x => new { x.LedgerEntryId, x.DocumentId, x.Title, x.ContentHash })
        };
        return JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private async Task<byte[]> BuildGenericCsvAsync(Guid companyId, Guid fiscalPeriodId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LedgerEntry.FiscalPeriodId == fiscalPeriodId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted)
            .OrderBy(x => x.LedgerEntry.EntryUtc).ThenBy(x => x.LedgerEntry.EntryNumber).ThenBy(x => x.Id)
            .Select(x => new { x.LedgerEntry.EntryNumber, x.LedgerEntry.PostingDate, x.LedgerEntry.EntryUtc,
                AccountCode = x.FinanceAccount.Code, x.DebitAmount, x.CreditAmount, x.Currency, x.Description })
            .ToArrayAsync(cancellationToken);
        var csv = new StringBuilder("voucher,posting_date,account,debit,credit,currency,description\n");
        foreach (var row in rows)
            csv.Append(Csv(row.EntryNumber)).Append(',')
                .Append((row.PostingDate ?? DateOnly.FromDateTime(row.EntryUtc)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(row.AccountCode)).Append(',').Append(Amount(row.DebitAmount)).Append(',')
                .Append(Amount(row.CreditAmount)).Append(',').Append(Csv(row.Currency)).Append(',')
                .Append(Csv(row.Description ?? string.Empty)).Append('\n');
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private async Task<Sie4BSource> BuildSieSourceAsync(Guid companyId, Guid fiscalPeriodId,
        DateTime generatedAtUtc, CancellationToken cancellationToken)
    {
        var period = await LoadPeriodAsync(companyId, fiscalPeriodId, cancellationToken);
        if (!period.IsClosed || !period.IsReportingLocked)
            throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePeriod,
                "A Swedish statutory export requires a closed and reporting-locked fiscal period.");
        var profile = await _dbContext.CompanyStatutoryProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken)
            ?? throw new Sie4BValidationException(Sie4BReasonCodes.MissingStatutoryIdentity,
                "A Swedish statutory profile is required for SIE export.");
        if (!profile.IsFormatComplete || !profile.IsUserAttested)
            throw new Sie4BValidationException(Sie4BReasonCodes.MissingStatutoryIdentity,
                "The Swedish statutory profile must be format-complete and user-attested before SIE export.");
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken)
            ?? throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePolicyHistory,
                "An accounting configuration is required for SIE export.");
        if (!_packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out var currentPack) || currentPack is null)
            throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePolicyHistory,
                "The selected accounting policy pack is unavailable for SIE export.");
        if (!currentPack.Definition.SupportedExports.Contains(AccountingExportTypeValues.Sie4B, StringComparer.OrdinalIgnoreCase))
            throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePolicyHistory,
                "The selected policy pack does not support SIE 4B export.");

        var entries = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId && x.Status == LedgerEntryStatuses.Posted)
            .OrderBy(x => x.EntryUtc).ThenBy(x => x.EntryNumber).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
        var entryIds = entries.Select(x => x.Id).ToArray();
        var lines = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && entryIds.Contains(x.LedgerEntryId))
            .OrderBy(x => x.LedgerEntryId).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
        var accounts = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToArrayAsync(cancellationToken);
        var accountById = accounts.ToDictionary(x => x.Id);
        var series = await _dbContext.VoucherSeries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        var selections = await _dbContext.AccountingPolicyPackSelections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.EffectiveFrom).ToArrayAsync(cancellationToken);
        foreach (var entry in entries)
        {
            if (entry.PolicyPackKey is null || entry.PolicyPackVersion is null ||
                !_packResolver.TryResolve(entry.PolicyPackKey, entry.PolicyPackVersion, out var pack) || pack is null ||
                !selections.Any(x => x.PackKey == entry.PolicyPackKey && x.PackVersion == entry.PolicyPackVersion &&
                    x.DefinitionHash == pack.DefinitionHash && x.EffectiveFrom <= (entry.PostingDate ?? DateOnly.FromDateTime(entry.EntryUtc)) &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= (entry.PostingDate ?? DateOnly.FromDateTime(entry.EntryUtc)))))
                throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePolicyHistory,
                    "Every exported voucher must retain a resolvable effective-dated policy-pack selection and definition hash.");
        }

        var priorMovements = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                x.LedgerEntry.FiscalPeriod.StartUtc < period.StartUtc)
            .GroupBy(x => x.FinanceAccountId)
            .Select(x => new { AccountId = x.Key, Amount = x.Sum(y => y.DebitAmount - y.CreditAmount) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Amount, cancellationToken);
        var periodMovements = lines.GroupBy(x => x.FinanceAccountId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.DebitAmount - y.CreditAmount));
        var usedAccountIds = lines.Select(x => x.FinanceAccountId).ToHashSet();
        var sieAccounts = accounts.Where(x => usedAccountIds.Contains(x.Id) || x.OpeningBalance != 0m || priorMovements.ContainsKey(x.Id))
            .Select(account =>
            {
                var accountClass = account.AccountClass ?? throw new Sie4BValidationException(Sie4BReasonCodes.InvalidAccount,
                    $"Account {account.Code} is missing the account class required for SIE mapping.");
                var opening = accountClass is FinanceAccountClassValues.Asset or FinanceAccountClassValues.Liability or FinanceAccountClassValues.Equity
                    ? account.OpeningBalance + priorMovements.GetValueOrDefault(account.Id)
                    : 0m;
                var monthly = lines.Where(x => x.FinanceAccountId == account.Id)
                    .Join(entries, x => x.LedgerEntryId, x => x.Id, (line, entry) => new { line, entry })
                    .GroupBy(x => (x.entry.PostingDate ?? DateOnly.FromDateTime(x.entry.EntryUtc)).ToString("yyyyMM", CultureInfo.InvariantCulture))
                    .ToDictionary(x => x.Key, x => x.Sum(y => y.line.DebitAmount - y.line.CreditAmount));
                return new Sie4BAccount(account.Code, account.Name, accountClass, opening,
                    opening + periodMovements.GetValueOrDefault(account.Id), monthly);
            }).ToArray();

        var dimensionObjects = new Dictionary<int, HashSet<string>> { [1] = new(StringComparer.Ordinal), [6] = new(StringComparer.Ordinal) };
        var sieVouchers = new List<Sie4BVoucher>(entries.Length);
        foreach (var entry in entries)
        {
            if (!entry.VoucherSeriesId.HasValue || !entry.VoucherSequenceNumber.HasValue || !entry.VoucherFiscalYear.HasValue ||
                !series.TryGetValue(entry.VoucherSeriesId.Value, out var seriesCode))
                throw new Sie4BValidationException(Sie4BReasonCodes.MissingVoucherIdentity,
                    "Every exported journal requires a tenant-scoped voucher series, fiscal year, and sequence number.");
            var entryLines = lines.Where(x => x.LedgerEntryId == entry.Id).ToArray();
            var transactions = new List<Sie4BTransaction>(entryLines.Length);
            foreach (var line in entryLines)
            {
                if (!accountById.TryGetValue(line.FinanceAccountId, out var account))
                    throw new Sie4BValidationException(Sie4BReasonCodes.InvalidAccount, "A journal line references an unavailable company account.");
                if (!string.Equals(line.Currency, "SEK", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(entry.BaseCurrency ?? configuration.BaseCurrency, "SEK", StringComparison.OrdinalIgnoreCase))
                    throw new Sie4BValidationException(Sie4BReasonCodes.UnsupportedCurrency,
                        "Foreign-currency journal facts cannot be represented by the current SIE 4B export capability.");
                var objects = ParseSieObjects(line.DimensionFactsJson);
                foreach (var item in objects) dimensionObjects[item.Key].Add(item.Value);
                transactions.Add(new Sie4BTransaction(account.Code, line.DebitAmount - line.CreditAmount,
                    entry.PostingDate ?? DateOnly.FromDateTime(entry.EntryUtc), line.Description, objects));
            }
            sieVouchers.Add(new Sie4BVoucher(seriesCode, entry.VoucherSequenceNumber.Value,
                entry.PostingDate ?? DateOnly.FromDateTime(entry.EntryUtc), entry.Description,
                DateOnly.FromDateTime(entry.PostedAtUtc ?? entry.EntryUtc), transactions));
        }

        var dimensions = dimensionObjects.Where(x => x.Value.Count > 0)
            .Select(x => new Sie4BDimension(x.Key, x.Key == 1 ? "Cost centre" : "Project",
                x.Value.OrderBy(value => value, StringComparer.Ordinal).Select(value => new Sie4BObject(value, value)).ToArray()))
            .ToArray();
        return new Sie4BSource(new Sie4BCompany(profile.LegalName!, profile.SwedishOrganisationNumber!,
                string.Join(", ", new[] { profile.RegisteredAddressLine1, profile.RegisteredAddressLine2 }.Where(x => !string.IsNullOrWhiteSpace(x))),
                $"{profile.RegisteredPostalCode} {profile.RegisteredCity}", profile.CountryCode, profile.AccountingCurrency),
            DateOnly.FromDateTime(period.StartUtc), DateOnly.FromDateTime(period.EndUtc.AddTicks(-1)),
            DateOnly.FromDateTime(generatedAtUtc), dimensions, sieAccounts, sieVouchers);
    }

    private async Task<object> BuildStatutoryManifestAsync(AccountingExportJob job, Sie4BSource source, CancellationToken cancellationToken)
    {
        var period = await LoadPeriodAsync(job.CompanyId, job.FiscalPeriodId, cancellationToken);
        var selections = await _dbContext.AccountingPolicyPackSelections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == job.CompanyId && x.EffectiveFrom <= source.FinancialYearEnd && (!x.EffectiveTo.HasValue || x.EffectiveTo >= source.FinancialYearStart))
            .OrderBy(x => x.EffectiveFrom).ToArrayAsync(cancellationToken);
        var policyPacks = selections.Select(x =>
        {
            if (!_packResolver.TryResolve(x.PackKey, x.PackVersion, out var pack) || pack is null ||
                !string.Equals(x.DefinitionHash, pack.DefinitionHash, StringComparison.OrdinalIgnoreCase))
                throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePolicyHistory,
                    "Every effective policy-pack selection in the archive period must resolve to its retained definition hash.");
            return new { x.PackKey, x.PackVersion, x.DefinitionHash, x.EffectiveFrom, x.EffectiveTo,
                definition = pack.Definition };
        }).ToArray();
        var vatPackages = await _dbContext.VatReturns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == job.CompanyId && x.Status == VatReturnStatuses.Locked &&
                x.FilingPeriod.StartDate <= source.FinancialYearEnd && x.FilingPeriod.EndDate >= source.FinancialYearStart)
            .OrderBy(x => x.FilingPeriod.StartDate).ThenBy(x => x.Version)
            .Select(x => new { x.Id, x.FilingPeriod.PeriodCode, x.Version, x.PackageStorageKey, x.PackageChecksum,
                x.PackageFileName, x.PackageMediaType, x.PackageContentLength, x.FinalizedUtc })
            .ToArrayAsync(cancellationToken);
        var statements = await _dbContext.FinancialStatementSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == job.CompanyId && x.FiscalPeriodId == job.FiscalPeriodId)
            .OrderBy(x => x.StatementType).ThenBy(x => x.VersionNumber)
            .Select(x => new { x.Id, x.StatementType, x.VersionNumber, x.BalancesChecksum, x.GeneratedAtUtc, x.Currency,
                lines = x.Lines.OrderBy(line => line.LineOrder).Select(line => new { line.LineCode, line.LineName, line.LineOrder,
                    line.ReportSection, line.LineClassification, line.Amount, line.Currency }) })
            .ToArrayAsync(cancellationToken);
        var closeHistory = await _dbContext.AccountingPeriodHistory.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == job.CompanyId && x.FiscalPeriodId == job.FiscalPeriodId)
            .OrderBy(x => x.OccurredUtc)
            .Select(x => new { x.Id, x.Action, x.ActorUserId, x.Reason, x.SnapshotChecksum, x.OccurredUtc })
            .ToArrayAsync(cancellationToken);
        var evidence = await _dbContext.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == job.CompanyId && x.LedgerEntry.FiscalPeriodId == job.FiscalPeriodId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted)
            .OrderBy(x => x.LedgerEntryId).ThenBy(x => x.DocumentId)
            .Select(x => new { x.LedgerEntryId, x.DocumentId, x.Title, x.ContentHash, x.Document.StorageKey,
                x.Document.OriginalFileName, x.Document.FileSizeBytes })
            .ToArrayAsync(cancellationToken);
        return new
        {
            schema = "virtual-company.swedish-statutory-accounting-archive",
            version = "1.0",
            sieSpecification = Sie4BSerializer.SpecificationVersion,
            job = new { job.Id, job.CompanyId, job.FiscalPeriodId, job.ExportType, job.RequestedByUserId, job.RequestedUtc, job.CorrelationId },
            fiscalPeriod = new { period.Id, period.Name, period.StartUtc, period.EndUtc, period.IsClosed, period.IsReportingLocked },
            sourceCounts = new { accounts = source.Accounts.Count, journals = source.Vouchers.Count, lines = source.Vouchers.Sum(x => x.Transactions.Count) },
            sourceTotals = new { debit = source.Vouchers.SelectMany(x => x.Transactions).Where(x => x.Amount > 0).Sum(x => x.Amount),
                credit = -source.Vouchers.SelectMany(x => x.Transactions).Where(x => x.Amount < 0).Sum(x => x.Amount) },
            policyPacks,
            finalizedVatPackageReferences = vatPackages,
            financialStatements = statements,
            closeHistory,
            evidenceManifest = evidence
        };
    }

    private static ArchiveArtifact BuildStatutoryArchive(byte[] sieContent, object sourceManifest, DateOnly financialYearEnd)
    {
        var sieChecksum = Sha256(sieContent);
        var sourceManifestBytes = JsonSerializer.SerializeToUtf8Bytes(sourceManifest, JsonOptions);
        var archiveManifest = new
        {
            schema = "virtual-company.swedish-statutory-accounting-archive",
            version = "1.0",
            files = new[]
            {
                new { path = "accounting.sie", sha256 = sieChecksum, length = sieContent.LongLength, encoding = Sie4BSerializer.EncodingName },
                new { path = "source-manifest.json", sha256 = Sha256(sourceManifestBytes), length = sourceManifestBytes.LongLength, encoding = "utf-8" }
            },
            source = sourceManifest
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(archiveManifest, JsonOptions);
        var checksums = JsonSerializer.SerializeToUtf8Bytes(new
        {
            algorithm = "SHA-256",
            entries = new[]
            {
                new { path = "accounting.sie", sha256 = sieChecksum },
                new { path = "source-manifest.json", sha256 = Sha256(sourceManifestBytes) },
                new { path = "archive-manifest.json", sha256 = Sha256(manifestBytes) }
            }
        }, JsonOptions);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "accounting.sie", sieContent, financialYearEnd);
            WriteEntry(archive, "source-manifest.json", sourceManifestBytes, financialYearEnd);
            WriteEntry(archive, "archive-manifest.json", manifestBytes, financialYearEnd);
            WriteEntry(archive, "checksums.sha256.json", checksums, financialYearEnd);
        }
        return new ArchiveArtifact(buffer.ToArray(), Encoding.UTF8.GetString(manifestBytes));
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content, DateOnly date)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static IReadOnlyDictionary<int, string> ParseSieObjects(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<int, string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new Sie4BValidationException(Sie4BReasonCodes.UnsupportedDimension,
                    "Dimension facts must be a bounded object before they can be represented in SIE 4B.");
            var result = new Dictionary<int, string>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                    throw new Sie4BValidationException(Sie4BReasonCodes.UnsupportedDimension,
                        "Only string cost-centre and project dimension facts are supported in SIE 4B.");
                var dimension = property.Name.Replace('-', '_').ToLowerInvariant() switch
                {
                    "cost_center" or "cost_centre" or "costcenter" => 1,
                    "project" or "project_code" => 6,
                    _ => throw new Sie4BValidationException(Sie4BReasonCodes.UnsupportedDimension,
                        $"Dimension '{property.Name}' has no approved SIE 4B mapping.")
                };
                result[dimension] = property.Value.GetString()!.Trim();
            }
            return result;
        }
        catch (JsonException exception)
        {
            throw new Sie4BValidationException(Sie4BReasonCodes.UnsupportedDimension,
                $"Dimension facts are not valid JSON and cannot be exported: {exception.Message}");
        }
    }

    private static ExportArtifact GenericArtifact(byte[] content, AccountingExportJob job, string extension, string mediaType, string encoding) =>
        new(content, $"accounting-export-{job.FiscalPeriodId:N}.{extension}", mediaType, "virtual-company.accounting-export 1.0",
            Sha256(content), encoding, 0, 0, 0, 0m, 0m, "{}", false);

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static LedgerRow ToLedgerRow(RawLedgerRow row) =>
        new(row.LineId, row.EntryId, row.FiscalPeriodId, row.AccountId, row.AccountCode, row.AccountName,
            row.AccountClass, row.Currency, row.AccountOpeningBalance, row.VoucherNumber,
            row.PostingDate ?? DateOnly.FromDateTime(row.EntryUtc), row.EntryUtc, row.Description,
            row.Debit, row.Credit, row.LineCurrency, row.SourceType, row.SourceId, row.SourceVersion,
            row.OriginalEntryId);

    private async Task<Dictionary<Guid, IReadOnlyList<AccountingEvidenceReferenceDto>>> LoadEvidenceAsync(Guid companyId, Guid[] entryIds, CancellationToken cancellationToken) =>
        (await _dbContext.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && entryIds.Contains(x.LedgerEntryId))
            .Select(x => new { x.LedgerEntryId, Item = new AccountingEvidenceReferenceDto(x.DocumentId, x.Title, x.ContentHash) })
            .ToListAsync(cancellationToken)).GroupBy(x => x.LedgerEntryId).ToDictionary(x => x.Key, x => (IReadOnlyList<AccountingEvidenceReferenceDto>)x.Select(y => y.Item).ToArray());

    private async Task<FiscalPeriod> LoadPeriodAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken) =>
        await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == periodId, cancellationToken)
        ?? throw new KeyNotFoundException("Fiscal period was not found in the requested company.");

    private async Task RequireViewAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipResolver.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanViewAccounting(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }

    private async Task RequireManageAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId != actorUserId) throw new UnauthorizedAccessException();
        var membership = await _membershipResolver.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanManageAccounting(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }

    private static ParsedTaxFact? ParseTaxFact(TaxFactRow row)
    {
        try
        {
            using var document = JsonDocument.Parse(row.Json);
            var root = document.RootElement;
            var rule = Text(root, "taxRuleKey") ?? "unclassified";
            var treatment = Text(root, "taxTreatment") ?? "unclassified";
            var taxable = Number(root, "taxableAmount") ?? Number(root, "netAmount") ?? Math.Abs(row.Debit - row.Credit);
            var tax = Number(root, "documentTaxAmount") ?? Number(root, "taxAmount") ?? 0m;
            return new ParsedTaxFact(row.EntryId, row.PolicyPackKey ?? "unknown", row.PolicyPackVersion ?? "unknown", rule, treatment, taxable, tax, row.Currency);
        }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static decimal? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number) ? number : null;
    }
    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); } catch (JsonException) { return null; }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string Checksum(IEnumerable<string> lines) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();
    private static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    private static string Amount(decimal value) => value.ToString("0.00####", CultureInfo.InvariantCulture);
    private static string Required(string value, string name, int maxLength) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= maxLength ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private AccountingExportJobDto MapExport(AccountingExportJob x) => new(x.Id, x.CompanyId, x.FiscalPeriodId, x.Status, x.AttemptCount, x.RequestedUtc, x.StartedUtc, x.CompletedUtc, x.ExpiresUtc, x.Checksum, x.FileName, x.MediaType, x.ContentLength, x.FailureCode, x.FailureSummary, x.Status == AccountingExportStatuses.Completed && x.ExpiresUtc > _timeProvider.GetUtcNow().UtcDateTime && (x.Content is not null || x.StorageKey is not null), x.ExportType, x.SpecificationVersion, x.InputChecksum, x.EncodingName, x.SourceAccountCount, x.SourceJournalCount, x.SourceLineCount, x.SourceDebitTotal, x.SourceCreditTotal, x.CorrelationId);

    private sealed record RawLedgerRow(Guid LineId, Guid EntryId, Guid FiscalPeriodId, Guid AccountId, string AccountCode, string AccountName, string? AccountClass, string Currency, decimal AccountOpeningBalance, string VoucherNumber, DateOnly? PostingDate, DateTime EntryUtc, string? Description, decimal Debit, decimal Credit, string LineCurrency, string? SourceType, string? SourceId, string? SourceVersion, Guid? OriginalEntryId);
    private sealed record AccountSeed(Guid Id, string Code, string Name, string? AccountClass, string Currency, decimal OpeningBalance);
    private sealed record AccountMovement(Guid AccountId, decimal Debit, decimal Credit, int Count);
    private sealed record LedgerRow(Guid LineId, Guid EntryId, Guid FiscalPeriodId, Guid AccountId, string AccountCode, string AccountName, string? AccountClass, string Currency, decimal AccountOpeningBalance, string VoucherNumber, DateOnly PostingDate, DateTime EntryUtc, string? Description, decimal Debit, decimal Credit, string LineCurrency, string? SourceType, string? SourceId, string? SourceVersion, Guid? OriginalEntryId);
    private sealed record TaxFactRow(Guid EntryId, string? PolicyPackKey, string? PolicyPackVersion, string Currency, decimal Debit, decimal Credit, string Json);
    private sealed record ParsedTaxFact(Guid EntryId, string PolicyPackKey, string PolicyPackVersion, string TaxRuleKey, string TaxTreatment, decimal TaxableAmount, decimal TaxAmount, string Currency);
    private sealed record ExportArtifact(byte[] Content, string FileName, string MediaType, string SpecificationVersion,
        string InputChecksum, string EncodingName, int AccountCount, int JournalCount, int LineCount,
        decimal DebitTotal, decimal CreditTotal, string ManifestJson, bool StoreInObjectStorage);
    private sealed record ArchiveArtifact(byte[] Content, string ManifestJson);
}
